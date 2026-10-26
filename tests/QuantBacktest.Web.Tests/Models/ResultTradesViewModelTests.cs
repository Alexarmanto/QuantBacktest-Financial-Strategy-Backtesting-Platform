using System.Globalization;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class ResultTradesViewModelTests
{
    private static Trade Trade(decimal netProfit = 98.765m, double returnFraction = 0.0987654, ExitReason reason = ExitReason.TakeProfit) =>
        new(
            new DateOnly(2024, 2, 2),
            1234.5m,
            new DateOnly(2024, 3, 1),
            1300.25m,
            12.345678m,
            3.5m,
            netProfit,
            returnFraction,
            reason,
            19);

    [Fact] // P4-U6: every column of Trade, in the order of the record
    public void Columns_AreEveryFieldOfTrade()
    {
        var model = ResultTradesViewModel.Create([]);

        model.Columns.Select(c => c.Label).Should().Equal(
            "Entry date", "Entry price", "Exit date", "Exit price", "Quantity", "Fees", "Net profit", "Return", "Exit reason", "Holding bars");
        model.Columns.Select(c => c.SortType).Should().Equal(
            "text", "number", "text", "number", "number", "number", "number", "number", "number", "number");
    }

    [Fact] // P4-U6, P4-U14: formatted texts
    public void Row_IsFormatted()
    {
        var row = ResultTradesViewModel.Create([Trade()]).Rows.Single();

        row.EntryDate.Text.Should().Be("2024-02-02");
        row.EntryPrice.Text.Should().Be("$1,234.50");
        row.ExitDate.Text.Should().Be("2024-03-01");
        row.ExitPrice.Text.Should().Be("$1,300.25");
        row.Quantity.Text.Should().Be("12.3457");
        row.Fees.Text.Should().Be("$3.50");
        row.NetProfit.Text.Should().Be("$98.77");
        row.Return.Text.Should().Be("9.88%");
        row.ExitReason.Text.Should().Be("Take-profit");
        row.HoldingBars.Text.Should().Be("19");
    }

    [Fact] // P4-U6: the sort keys are raw invariant values, never the formatted text
    public void Row_SortValues_AreRawInvariantValues()
    {
        var row = ResultTradesViewModel.Create([Trade(netProfit: -1234.5m, returnFraction: -0.0123)]).Rows.Single();

        row.EntryDate.SortValue.Should().Be("2024-02-02");
        row.EntryPrice.SortValue.Should().Be("1234.5");
        row.ExitDate.SortValue.Should().Be("2024-03-01");
        row.ExitPrice.SortValue.Should().Be("1300.25");
        row.Quantity.SortValue.Should().Be("12.345678");
        row.Fees.SortValue.Should().Be("3.5");
        row.NetProfit.SortValue.Should().Be("-1234.5");
        row.Return.SortValue.Should().Be("-0.0123");
        row.ExitReason.SortValue.Should().Be("2");
        row.HoldingBars.SortValue.Should().Be("19");
    }

    [Fact] // P4-U6: sort keys do not depend on the current culture
    public void SortValues_DoNotDependOnTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var row = ResultTradesViewModel.Create([Trade()]).Rows.Single();

            row.EntryPrice.SortValue.Should().Be("1234.5");
            row.Return.SortValue.Should().Be("0.0987654");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory] // P4-U6: gain / loss rows
    [InlineData("10", ResultTradesViewModel.GainRow)]
    [InlineData("-10", ResultTradesViewModel.LossRow)]
    [InlineData("0", "")]
    public void Row_IsColouredByNetProfit(string netProfit, string rowClass)
    {
        var trade = Trade(netProfit: decimal.Parse(netProfit, CultureInfo.InvariantCulture));

        ResultTradesViewModel.Create([trade]).Rows.Single().RowClass.Should().Be(rowClass);
    }

    [Theory] // P4-U6: one label and one badge per exit reason
    [InlineData(ExitReason.Signal, "Signal", "text-bg-primary")]
    [InlineData(ExitReason.StopLoss, "Stop-loss", "text-bg-warning")]
    [InlineData(ExitReason.TakeProfit, "Take-profit", "text-bg-info")]
    [InlineData(ExitReason.TrailingStop, "Trailing stop", "qb-exit-trailing")]
    [InlineData(ExitReason.EndOfData, "End of data", "text-bg-secondary")]
    public void ExitReason_HasItsLabelAndBadge(ExitReason reason, string label, string badgeClass)
    {
        var row = ResultTradesViewModel.Create([Trade(reason: reason)]).Rows.Single();

        row.ExitReason.Text.Should().Be(label);
        row.ExitReasonBadgeClass.Should().Be(badgeClass);
    }

    [Fact] // P4-U6: the five badges are distinct, and none uses the gain / loss colours
    public void ExitReasonBadges_AreDistinct_AndNeutral()
    {
        var classes = Enum.GetValues<ExitReason>().Select(ResultTradesViewModel.BadgeClass).ToList();

        classes.Should().OnlyHaveUniqueItems().And.HaveCount(5);
        classes.Should().NotContain(["text-bg-success", "text-bg-danger"]);
    }

    [Fact] // P4-U6: zero trade → the message instead of the table
    public void NoTrade_IsEmpty()
    {
        var model = ResultTradesViewModel.Create([]);

        model.IsEmpty.Should().BeTrue();
        ResultTradesViewModel.EmptyMessage.Should().Be("No trades were executed in this period.");
    }

    [Fact] // P4-U6: the order of the result is kept (the initial order of the table)
    public void Rows_KeepTheOrderOfTheResult()
    {
        var later = Trade() with { EntryDate = new DateOnly(2024, 6, 3) };

        var model = ResultTradesViewModel.Create([later, Trade()]);

        model.IsEmpty.Should().BeFalse();
        model.Rows.Select(r => r.EntryDate.Text).Should().Equal("2024-06-03", "2024-02-02");
    }
}
