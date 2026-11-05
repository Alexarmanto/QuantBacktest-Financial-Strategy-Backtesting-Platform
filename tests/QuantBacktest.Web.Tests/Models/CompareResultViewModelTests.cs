using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class CompareResultViewModelTests
{
    private const string Canonical =
        "symbol=AAPL&tf=weekly&strategies=s-one,s-two,s-three&capital=10000&fee=0.1&slip=0&rf=0";

    private static readonly PerformanceMetrics Full =
        new(11234.5m, 0.12346, -0.0456, 0.001, 0.2, 1.234, 2.346, -0.567, 0.2345, 12, 0.5, 1.5, 0.4567);

    private static readonly PerformanceMetrics Undefined =
        new(10000m, 0, 0, 0, 0, null, null, null, 0, 0, 0, null, 0);

    private static readonly PerformanceMetrics Benchmark =
        new(12000m, 0.2, 0.05, 0.001, 0.25, 0.8, 1.1, 0.3, 0.35, 1, 1, null, 1);

    private static ComparisonQuery Query()
    {
        BacktestQuery.TryParseComparison(new QueryCollection(QueryHelpers.ParseQuery(Canonical)), out var query, out _).Should().BeTrue();
        return query!;
    }

    private static ComparisonOutcome Success(ComparisonQuery query, string id, PerformanceMetrics metrics, PerformanceMetrics? benchmark = null)
    {
        var strategy = new StubStrategy
        {
            Id = id,
            Name = "Name " + id,
            Parameters = [new ParameterDefinition("n", "N", ParameterType.Integer, 3, 1, 10, 1)],
        };
        var single = query.ForStrategy(strategy);
        var result = CompareChartsViewModelTests.Result("Name " + id, [10000m, 10100m, 10200m], [10000m, 9900m, 9800m])
            with { Metrics = metrics, BenchmarkMetrics = benchmark ?? Benchmark };
        return ComparisonOutcome.Success(strategy, single, result);
    }

    private static string Html(MetricCell cell)
    {
        using var writer = new StringWriter();
        cell.Td().WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private static CompareResultViewModel Create(params ComparisonOutcome[] outcomes) =>
        CompareResultViewModel.Create(Query(), outcomes, "http://localhost:5080/Compare/Result?" + Canonical, "");

    [Fact] // C-4, P4-U14: the 10 metrics of P4-X1 with the formats of DisplayFormat, max drawdown positive (M8)
    public void Metrics_Formats_NullIsNa_DrawdownPositive()
    {
        var query = Query();
        var model = Create(Success(query, "s-one", Full), Success(query, "s-two", Undefined));

        CompareResultViewModel.Columns.Select(c => c.Label).Should().Equal(
            "Strategy", "Cumulative return", "Annualized return", "Annualized volatility", "Sharpe ratio", "Sortino ratio",
            "Calmar ratio", "Max drawdown", "Trades", "Profit factor", "Exposure");
        model.Rows[0].Cells.Select(c => c.Text).Should().Equal(
            "12.35%", "-4.56%", "20.00%", "1.23", "2.35", "-0.57", "23.45%", "12", "1.50", "45.67%");
        model.Rows[1].Cells.Select(c => c.Text).Should().Equal(
            "0.00%", "0.00%", "0.00%", "n/a", "n/a", "n/a", "0.00%", "0", "n/a", "0.00%");
    }

    [Fact] // C-4: raw invariant sort keys; an undefined ratio has no data-sort-value at all (table-sort.js: last)
    public void Metrics_SortValues_RawInvariant_NullHasNoAttribute()
    {
        var query = Query();
        var model = Create(Success(query, "s-one", Full), Success(query, "s-two", Undefined));

        model.Rows[0].Cells.Select(c => c.SortValue).Should().Equal(
            "0.12346", "-0.0456", "0.2", "1.234", "2.346", "-0.567", "0.2345", "12", "1.5", "0.4567");
        var sharpe = model.Rows[1].Cells[3];
        sharpe.SortValue.Should().BeNull();
        Html(sharpe).Should().Be("<td class=\"num text-nowrap\">n/a</td>");
        Html(model.Rows[0].Cells[3]).Should().Be("<td class=\"num text-nowrap\" data-sort-value=\"1.234\">1.23</td>");
    }

    [Fact] // P4-U4: only the returns are green or red; ratios, drawdown and counts stay neutral
    public void Metrics_OnlyReturnsAreColored()
    {
        var model = Create(Success(Query(), "s-one", Full));

        model.Rows[0].Cells.Select(c => c.Tone).Should().Equal(
            ValueTone.Gain, ValueTone.Loss, "", "", "", "", "", "", "", "");
        Html(model.Rows[0].Cells[1]).Should().Contain("class=\"num text-nowrap text-loss\"");
    }

    [Fact] // C1, point 6: one buy & hold row, from the BenchmarkMetrics of the result its curve comes from; no link
    public void BenchmarkRow_FromFirstSuccessfulResult()
    {
        var query = Query();
        var other = Benchmark with { CumulativeReturn = -0.99 };
        var model = Create(
            ComparisonOutcome.Failure("s-one", "Name s-one", "Refused."),
            Success(query, "s-two", Full),
            Success(query, "s-three", Full, other));

        model.Rows.Should().HaveCount(3);
        var benchmark = model.Rows[^1];
        benchmark.Name.Should().Be(BacktestResultViewModel.BenchmarkColumn);
        benchmark.DetailUrl.Should().BeNull();
        benchmark.SwatchClass.Should().Be(CompareResultViewModel.BenchmarkSwatchClass);
        benchmark.Cells[0].Text.Should().Be("20.00%");
        benchmark.Cells[8].Text.Should().Be("n/a");
    }

    [Fact] // C-6: each strategy links to the canonical URL of /Backtest/Result (its defaults made explicit), PathBase kept
    public void DetailLink_IsCanonicalResultUrl()
    {
        var query = Query();
        var model = CompareResultViewModel.Create(query, [Success(query, "s-one", Full)], "http://x/Compare/Result?" + Canonical, "/app");

        model.Rows[0].DetailUrl.Should().Be(
            "/app/Backtest/Result?symbol=AAPL&tf=weekly&strategy=s-one&capital=10000&fee=0.1&slip=0&rf=0&p.n=3");
        model.EditSettingsUrl.Should().Be("/app/Compare?" + Canonical);
    }

    [Fact] // C-3: the swatch of each row has the palette index of its curve (1, 2…: failures take no colour)
    public void Rows_SwatchesFollowTheSuccessfulOrder()
    {
        var query = Query();
        var model = Create(
            Success(query, "s-one", Full),
            ComparisonOutcome.Failure("s-two", "Name s-two", "Refused."),
            Success(query, "s-three", Full));

        model.Rows.Select(r => r.SwatchClass).Should().Equal("qb-swatch qb-swatch-1", "qb-swatch qb-swatch-2", "qb-swatch qb-swatch-benchmark");
        model.Rows.Select(r => r.Name).Should().Equal("Name s-one", "Name s-three", "Buy & hold");
        model.Errors.Should().Equal(new CompareError("Name s-two", "Refused."));
        model.Period.Should().Be("2024-01-05 to 2024-01-19");
    }

    [Fact] // decision D2: nothing run → errors only, no chart, no table, Edit settings kept
    public void AllFailed_NothingRun()
    {
        var model = Create(
            ComparisonOutcome.Failure("s-one", "Name s-one", "A."),
            ComparisonOutcome.Failure("s-two", "Name s-two", "B."));

        model.NothingRun.Should().BeTrue();
        model.Rows.Should().BeEmpty();
        model.Charts.Should().BeNull();
        model.Errors.Should().HaveCount(2);
        model.EditSettingsUrl.Should().Be("/Compare?" + Canonical);
    }

    [Fact] // P4-U3 (settings): the execution settings of the comparison, as on the result page
    public void Header_ShowsTheSharedSettings()
    {
        var model = Create(Success(Query(), "s-one", Full));

        model.Symbol.Should().Be("AAPL");
        model.Timeframe.Should().Be("Weekly");
        model.Execution.Should().Equal(ResultHeaderViewModel.ExecutionRows(ExecutionSettings.Default));
    }
}
