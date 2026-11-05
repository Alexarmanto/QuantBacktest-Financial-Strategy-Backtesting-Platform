using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

/// <summary>Content types and file names of the downloads (docs/CONTRACT.md section 11, PART-4 section 7).</summary>
public class DownloadFileTests
{
    /// <summary>A fake daily backtest of AAPL ending on Thursday 2020-12-31 (fake bars: every weekday from 2018-01-01).</summary>
    private static async Task<BacktestResult> ResultAsync()
    {
        var marketData = new FakeMarketDataService();
        var catalog = new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());
        var request = new BacktestRequest(
            "AAPL",
            Timeframe.Daily,
            "fake-threshold",
            new Dictionary<string, double>(),
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 12, 31),
            ExecutionSettings.Default);
        return await new FakeBacktestService(marketData, catalog).RunAsync(request);
    }

    [Fact] // CONTRACT section 11: QuantBacktest_<SYMBOL>_<strategyId>_<yyyyMMdd>.pdf and .csv, date of the last bar
    public async Task FileName_FollowsContractSection11()
    {
        var result = await ResultAsync();

        result.Bars[^1].Date.Should().Be(new DateOnly(2020, 12, 31));
        DownloadFile.PdfFileName(result).Should().Be("QuantBacktest_AAPL_fake-threshold_20201231.pdf");
        DownloadFile.CsvFileName(result).Should().Be("QuantBacktest_AAPL_fake-threshold_20201231.csv");
    }

    [Fact] // CONTRACT section 11, Conventions.CustomStrategyIdPattern: a custom id is written as is
    public async Task FileName_WithACustomStrategyId()
    {
        var result = await ResultAsync();
        result = result with { Request = result.Request with { StrategyId = "custom-my-rsi-dip-2" } };

        DownloadFile.PdfFileName(result).Should().Be("QuantBacktest_AAPL_custom-my-rsi-dip-2_20201231.pdf");
    }

    [Fact] // CONTRACT section 11, Conventions.SymbolPattern allows '.' and '-'
    public async Task FileName_WithADottedSymbol()
    {
        var result = await ResultAsync();
        result = result with { Request = result.Request with { Symbol = "BRK.B" } };

        DownloadFile.CsvFileName(result).Should().Be("QuantBacktest_BRK.B_fake-threshold_20201231.csv");
    }

    [Fact] // PART-4 section 7 (downloads), decision D5: exact strings, no charset on the CSV
    public void ContentTypes_AreExact()
    {
        DownloadFile.PdfContentType.Should().Be("application/pdf");
        DownloadFile.CsvContentType.Should().Be("text/csv");
        DownloadFile.CsvEncoding.GetPreamble().Should().BeEmpty();
    }

    [Fact] // decision D6: rule X1 guarantees at least 2 bars; a result without bars is a bug, never a made-up name
    public async Task FileName_WithoutBars_Throws()
    {
        var result = await ResultAsync();
        result = result with { Bars = [] };

        var act = () => DownloadFile.PdfFileName(result);

        act.Should().Throw<InvalidOperationException>();
    }
}
