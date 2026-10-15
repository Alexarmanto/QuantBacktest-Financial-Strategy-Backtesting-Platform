using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.Options;

namespace QuantBacktest.Data.AlphaVantage;

/// <summary>
/// Downloads one series from Alpha Vantage (D1, D4, P1-D2, P1-D6). One call = at most one HTTP request.
/// The quota (D4, P1-D10) is checked by the caller before calling this client.
/// The request URL contains the key: it is never logged and never put in an exception message.
/// </summary>
internal sealed class AlphaVantageClient(
    HttpClient httpClient,
    IOptions<AlphaVantageOptions> options,
    AlphaVantageCsvParser parser,
    ILogger<AlphaVantageClient> logger)
{
    public const string HttpClientName = "AlphaVantage";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>P1-D6: daily stays on the free compact endpoint; weekly uses the adjusted endpoint, free per the P1-D5 spike.</summary>
    public const string DailyFunction = "TIME_SERIES_DAILY";
    public const string WeeklyFunction = "TIME_SERIES_WEEKLY_ADJUSTED";

    private readonly AlphaVantageOptions _options = options.Value;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <param name="symbol">An already normalised symbol (trimmed, upper case).</param>
    public async Task<ParsedPrices> FetchAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        // D4, P1-D2: no key, no request.
        if (!HasApiKey)
        {
            throw MissingApiKey();
        }

        var body = await DownloadAsync(BuildUri(symbol, timeframe), symbol, timeframe, cancellationToken);

        // D4: Alpha Vantage reports errors as JSON with HTTP 200, even when CSV was asked for.
        if (body.TrimStart().StartsWith('{'))
        {
            var error = AlphaVantageErrorClassifier.Classify(body, symbol);
            logger.LogWarning("Alpha Vantage refused {Symbol} {Timeframe}: {Kind}.", symbol, timeframe, error.Kind);
            throw error;
        }

        return parser.Parse(body, $"{symbol} {timeframe} (Alpha Vantage)");
    }

    public static MarketDataException MissingApiKey() => new(
        MarketDataErrorKind.MissingApiKey,
        "The Alpha Vantage API key is not configured. Set AlphaVantage:ApiKey in user-secrets or the AlphaVantage__ApiKey environment variable.");

    private async Task<string> DownloadAsync(Uri uri, string symbol, Timeframe timeframe, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(uri, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // The HttpClient timeout. Inner exceptions are not attached: they may describe the request.
            throw NetworkError(symbol, timeframe, "the request timed out");
        }
        catch (HttpRequestException)
        {
            throw NetworkError(symbol, timeframe, "the server could not be reached");
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw NetworkError(symbol, timeframe, $"HTTP status {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
            {
                throw NetworkError(symbol, timeframe, "the response could not be read");
            }
        }
    }

    private MarketDataException NetworkError(string symbol, Timeframe timeframe, string reason)
    {
        logger.LogWarning("Alpha Vantage request for {Symbol} {Timeframe} failed: {Reason}.", symbol, timeframe, reason);
        return new MarketDataException(
            MarketDataErrorKind.NetworkError,
            $"Could not download {symbol} ({timeframe}) from Alpha Vantage: {reason}.");
    }

    /// <summary>P1-D6: ?function=&lt;F&gt;&amp;symbol=&lt;S&gt;&amp;datatype=csv&amp;apikey=&lt;K&gt; (+ outputsize=compact for daily).</summary>
    private Uri BuildUri(string symbol, Timeframe timeframe)
    {
        var function = timeframe switch
        {
            Timeframe.Daily => DailyFunction,
            Timeframe.Weekly => WeeklyFunction,
            _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
        };

        var query = $"?function={function}&symbol={Uri.EscapeDataString(symbol)}&datatype=csv&apikey={Uri.EscapeDataString(_options.ApiKey.Trim())}";
        if (timeframe == Timeframe.Daily)
        {
            query += "&outputsize=compact";
        }

        return new Uri(_options.BaseUrl.TrimEnd('?') + query);
    }
}
