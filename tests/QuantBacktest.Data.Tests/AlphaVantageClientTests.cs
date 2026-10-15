using System.Net;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Options;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public class AlphaVantageClientTests
{
    private const string ApiKey = "SECRETKEY123";
    private const string DailyCsv = "timestamp,open,high,low,close,volume\r\n2026-01-06,11,12,10,11.5,200\r\n2026-01-05,10,11,9,10.5,100\r\n";

    // Texts sent by Alpha Vantage (the rate limit notice repeats the key, which is why it is never copied).
    private const string ErrorMessageJson = """{ "Error Message": "Invalid API call. Please retry or visit the documentation (https://www.alphavantage.co/documentation/) for TIME_SERIES_DAILY." }""";
    private const string NoteJson = """{ "Note": "Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute and 500 calls per day." }""";
    private const string InformationLimitJson = """{ "Information": "We have detected your API key as SECRETKEY123 and our standard API rate limit is 25 requests per day. Please subscribe to any of the premium plans at https://www.alphavantage.co/premium/ to instantly remove all daily rate limits." }""";
    private const string InformationPremiumJson = """{ "Information": "Thank you for using Alpha Vantage! This is a premium endpoint. You may subscribe to any of the premium plans at https://www.alphavantage.co/premium/ to instantly unlock all premium endpoints" }""";
    private const string InformationOtherJson = """{ "Information": "The demo API key is for demo purposes only." }""";

    private readonly RecordingLoggerProvider _logs = new();

    [Fact]
    public async Task FetchAsync_Daily_UsesTheCompactEndpointAndParsesTheCsv() // D1, P1-D6
    {
        var handler = CountingHttpMessageHandler.Returning(DailyCsv);

        var result = await CreateClient(handler).FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        handler.CallCount.Should().Be(1);
        handler.RequestedUris.Single().ToString().Should().Be(
            "https://example.test/query?function=TIME_SERIES_DAILY&symbol=MSFT&datatype=csv&apikey=SECRETKEY123&outputsize=compact");
        result.Bars.Should().HaveCount(2);
        result.IsAdjusted.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_Weekly_UsesTheAdjustedEndpoint() // D1, P1-D5, P1-D6
    {
        var handler = CountingHttpMessageHandler.Returning(
            "timestamp,open,high,low,close,adjusted close,volume,dividend amount\n2026-01-09,98,104,96,100,50,123456,0.0000\n");

        var result = await CreateClient(handler).FetchAsync("MSFT", Timeframe.Weekly, CancellationToken.None);

        handler.RequestedUris.Single().ToString().Should().Be(
            "https://example.test/query?function=TIME_SERIES_WEEKLY_ADJUSTED&symbol=MSFT&datatype=csv&apikey=SECRETKEY123");
        result.IsAdjusted.Should().BeTrue();
    }

    [Theory] // D4, P1-D2: no key, no request
    [InlineData("")]
    [InlineData("   ")]
    public async Task FetchAsync_WithoutApiKey_ThrowsMissingApiKeyWithoutAnyCall(string apiKey)
    {
        var handler = CountingHttpMessageHandler.Returning(DailyCsv);

        var act = () => CreateClient(handler, apiKey).FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.MissingApiKey);
        handler.CallCount.Should().Be(0);
    }

    [Theory] // D4, P1-D4: JSON errors received with HTTP 200
    [InlineData(ErrorMessageJson, MarketDataErrorKind.SymbolNotFound)]
    [InlineData(NoteJson, MarketDataErrorKind.RateLimitExceeded)]
    [InlineData(InformationLimitJson, MarketDataErrorKind.RateLimitExceeded)]
    [InlineData(InformationPremiumJson, MarketDataErrorKind.InvalidResponse)]
    [InlineData(InformationOtherJson, MarketDataErrorKind.InvalidResponse)]
    [InlineData("""{ "Meta Data": {} }""", MarketDataErrorKind.InvalidResponse)]
    [InlineData("{ not json", MarketDataErrorKind.InvalidResponse)]
    public async Task FetchAsync_JsonErrorWithStatus200_IsMappedToTheExpectedKind(string json, MarketDataErrorKind expected)
    {
        var handler = CountingHttpMessageHandler.Returning(json);

        var act = () => CreateClient(handler).FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(expected);
        handler.CallCount.Should().Be(1);
    }

    [Theory] // D4, P1-D2: status other than 200
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchAsync_StatusOtherThan200_ThrowsNetworkError(HttpStatusCode status)
    {
        var handler = CountingHttpMessageHandler.Returning(DailyCsv, status);

        var act = () => CreateClient(handler).FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.NetworkError);
    }

    [Fact]
    public async Task FetchAsync_Timeout_ThrowsNetworkError() // D4, P1-D2
    {
        // What HttpClient raises when its own timeout fires: a cancellation the caller did not ask for.
        var handler = CountingHttpMessageHandler.Throwing(new TaskCanceledException("timeout", new TimeoutException()));

        var act = () => CreateClient(handler).FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.NetworkError);
    }

    [Fact]
    public async Task FetchAsync_ConnectionFailure_ThrowsNetworkError() // D4, P1-D2
    {
        var handler = CountingHttpMessageHandler.Throwing(new HttpRequestException("No such host is known. (example.test:443)"));

        var act = () => CreateClient(handler).FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.NetworkError);
    }

    [Fact]
    public async Task FetchAsync_CancelledByTheCaller_ThrowsOperationCanceled() // P1-D2: a caller cancellation is not a network error
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new CountingHttpMessageHandler((_, token) => Task.FromCanceled<HttpResponseMessage>(token));

        var act = () => CreateClient(handler).FetchAsync("MSFT", Timeframe.Daily, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory] // D4, P1-D2, CONTRACT section 12: the key never appears in a message or a log
    [InlineData(ErrorMessageJson, HttpStatusCode.OK)]
    [InlineData(NoteJson, HttpStatusCode.OK)]
    [InlineData(InformationLimitJson, HttpStatusCode.OK)]
    [InlineData(InformationPremiumJson, HttpStatusCode.OK)]
    [InlineData("garbage", HttpStatusCode.OK)]
    [InlineData(DailyCsv, HttpStatusCode.InternalServerError)]
    public async Task FetchAsync_OnFailure_NeverExposesTheKey(string body, HttpStatusCode status)
    {
        var handler = CountingHttpMessageHandler.Returning(body, status);

        var act = () => CreateClient(handler).FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<MarketDataException>()).Which;
        exception.ToString().Should().NotContain(ApiKey);
        _logs.AllText.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task FetchAsync_OnTimeout_NeverExposesTheKey() // D4, P1-D2
    {
        var handler = CountingHttpMessageHandler.Throwing(new TaskCanceledException($"https://example.test/query?apikey={ApiKey}"));

        var act = () => CreateClient(handler).FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.ToString().Should().NotContain(ApiKey);
        _logs.AllText.Should().NotContain(ApiKey);
    }

    private AlphaVantageClient CreateClient(CountingHttpMessageHandler handler, string apiKey = ApiKey)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AlphaVantageOptions
        {
            ApiKey = apiKey,
            BaseUrl = "https://example.test/query",
        });
        var parser = new AlphaVantageCsvParser(_logs.CreateLogger<AlphaVantageCsvParser>());
        return new AlphaVantageClient(new HttpClient(handler), options, parser, _logs.CreateLogger<AlphaVantageClient>());
    }
}
