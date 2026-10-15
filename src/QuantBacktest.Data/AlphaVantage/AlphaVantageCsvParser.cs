using System.Globalization;
using Microsoft.Extensions.Logging;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Data.AlphaVantage;

/// <summary>
/// Reads the CSV format of Alpha Vantage (D5, P1-D3), for API responses and seed files alike.
/// Two headers are accepted: the raw one and the one of the adjusted endpoints (P1-D6, P1-D7).
/// </summary>
internal sealed class AlphaVantageCsvParser(ILogger<AlphaVantageCsvParser> logger)
{
    public const string RawHeader = "timestamp,open,high,low,close,volume";
    public const string AdjustedHeader = "timestamp,open,high,low,close,adjusted close,volume,dividend amount";

    /// <summary>Decimals kept after applying the adjustment factor (rounding is monotonic, so OHLC ordering survives).</summary>
    private const int AdjustedPriceDecimals = 6;

    private const NumberStyles PriceStyle = NumberStyles.AllowDecimalPoint;
    private const NumberStyles VolumeStyle = NumberStyles.None;

    /// <param name="csv">The CSV text.</param>
    /// <param name="source">What is parsed (for example "MSFT Weekly"), used in log messages only.</param>
    public ParsedPrices Parse(string csv, string source)
    {
        ArgumentNullException.ThrowIfNull(csv);

        // D5: "\r\n" or "\n" line endings.
        var lines = csv.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var header = lines.Count > 0 ? lines[0].Trim().TrimStart('﻿') : string.Empty;
        var isAdjusted = header switch
        {
            RawHeader => false,
            AdjustedHeader => true,
            _ => throw new MarketDataException(
                MarketDataErrorKind.InvalidResponse,
                "The price data has an unexpected format."),
        };
        var fieldCount = isAdjusted ? 8 : 6;

        var barsByDate = new Dictionary<DateOnly, PriceBar>();
        for (var index = 1; index < lines.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var lineNumber = index + 1;
            if (!TryParseLine(lines[index], fieldCount, isAdjusted, out var bar, out var reason))
            {
                logger.LogWarning("Skipped line {LineNumber} of {Source}: {Reason}.", lineNumber, source, reason);
                continue;
            }

            // The file is sorted most recent first; the first occurrence of a date wins.
            if (!barsByDate.TryAdd(bar.Date, bar))
            {
                logger.LogWarning("Skipped line {LineNumber} of {Source}: duplicate date {Date}.", lineNumber, source, bar.Date);
            }
        }

        if (barsByDate.Count == 0)
        {
            throw new MarketDataException(
                MarketDataErrorKind.InvalidResponse,
                "The price data contains no valid bar.");
        }

        var bars = barsByDate.Values.OrderBy(bar => bar.Date).ToList();
        return new ParsedPrices(bars, isAdjusted);
    }

    private static bool TryParseLine(string line, int fieldCount, bool isAdjusted, out PriceBar bar, out string reason)
    {
        bar = null!;
        var fields = line.Split(',');
        if (fields.Length != fieldCount)
        {
            reason = $"expected {fieldCount} fields, found {fields.Length}";
            return false;
        }

        if (!DateOnly.TryParseExact(fields[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            reason = "invalid date";
            return false;
        }

        if (!TryParsePrice(fields[1], out var open)
            || !TryParsePrice(fields[2], out var high)
            || !TryParsePrice(fields[3], out var low)
            || !TryParsePrice(fields[4], out var close))
        {
            reason = "invalid or non-positive price";
            return false;
        }

        var volumeField = isAdjusted ? fields[6] : fields[5];
        if (!long.TryParse(volumeField.Trim(), VolumeStyle, CultureInfo.InvariantCulture, out var volume))
        {
            reason = "invalid volume";
            return false;
        }

        if (isAdjusted)
        {
            if (!TryParsePrice(fields[5], out var adjustedClose))
            {
                reason = "invalid or non-positive adjusted close";
                return false;
            }

            // P1-D7: the factor adjusted close / close applies to the four prices; the volume stays raw.
            var factor = adjustedClose / close;
            open = Adjust(open, factor);
            high = Adjust(high, factor);
            low = Adjust(low, factor);
            close = Adjust(close, factor);
            if (low <= 0m)
            {
                reason = "non-positive adjusted price";
                return false;
            }
        }

        // PriceSeries invariants (CONTRACT section 3): a bar that breaks them is dropped, never repaired.
        if (high < low)
        {
            reason = "high below low";
            return false;
        }

        if (high < Math.Max(open, close) || low > Math.Min(open, close))
        {
            reason = "open or close outside the high-low range";
            return false;
        }

        bar = new PriceBar(date, open, high, low, close, volume);
        reason = string.Empty;
        return true;
    }

    private static bool TryParsePrice(string field, out decimal price)
        => decimal.TryParse(field.Trim(), PriceStyle, CultureInfo.InvariantCulture, out price) && price > 0m;

    private static decimal Adjust(decimal price, decimal factor)
        => Math.Round(price * factor, AdjustedPriceDecimals, MidpointRounding.AwayFromZero);
}
