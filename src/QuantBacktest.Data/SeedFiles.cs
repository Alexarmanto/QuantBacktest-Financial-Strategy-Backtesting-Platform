using QuantBacktest.Core.Models;
using QuantBacktest.Data.AlphaVantage;

namespace QuantBacktest.Data;

/// <summary>Reads data/seed/&lt;SYMBOL&gt;_daily.csv and _weekly.csv, in the Alpha Vantage CSV format (D1, P1-D13).</summary>
internal sealed class SeedFiles(string directory, AlphaVantageCsvParser parser)
{
    public string Directory { get; } = directory;

    /// <summary>The parsed seed file, or null when there is none for this symbol and timeframe.</summary>
    public async Task<ParsedPrices?> TryReadAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Directory, FileName(symbol, timeframe));
        if (!File.Exists(path))
        {
            return null;
        }

        var csv = await File.ReadAllTextAsync(path, cancellationToken);
        return parser.Parse(csv, $"seed file {Path.GetFileName(path)}");
    }

    public static string FileName(string symbol, Timeframe timeframe) => timeframe switch
    {
        Timeframe.Daily => $"{symbol}_daily.csv",
        Timeframe.Weekly => $"{symbol}_weekly.csv",
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
    };
}
