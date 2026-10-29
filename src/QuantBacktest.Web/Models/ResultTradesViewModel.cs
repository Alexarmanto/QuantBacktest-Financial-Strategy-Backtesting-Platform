using System.Globalization;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One table cell: the formatted text, and the raw invariant value table-sort.js sorts on (ISO date, number with
/// "." as decimal point). The formatted text ($1,234.00) is never sorted.
/// </summary>
internal sealed record SortableCell(string Text, string SortValue);

/// <summary>
/// Header of a sortable column. SortType is "number" or "text" (read by table-sort.js); CssClass aligns numbers and
/// keeps dates and numbers on one line (at 360 px the table scrolls inside .table-responsive instead).
/// </summary>
internal sealed record TradeColumn(string Label, string SortType, string? CssClass);

internal sealed record TradeRow(
    string RowClass,
    SortableCell EntryDate,
    SortableCell EntryPrice,
    SortableCell ExitDate,
    SortableCell ExitPrice,
    SortableCell Quantity,
    SortableCell Fees,
    SortableCell NetProfit,
    SortableCell Return,
    SortableCell ExitReason,
    string ExitReasonBadgeClass,
    SortableCell HoldingBars);

/// <summary>Trades table of the result page (P4-U6): every field of Trade, in the order of the result.</summary>
internal sealed record ResultTradesViewModel(IReadOnlyList<TradeColumn> Columns, IReadOnlyList<TradeRow> Rows)
{
    public const string EmptyMessage = "No trades were executed in this period.";

    public const string NumberSort = "number";
    public const string TextSort = "text";

    public const string DateCell = "text-nowrap";
    public const string NumberCell = "num text-nowrap";

    public const string GainRow = "qb-row-gain";
    public const string LossRow = "qb-row-loss";

    public static IReadOnlyList<TradeColumn> TradeColumns { get; } =
    [
        new("Entry date", TextSort, DateCell),
        new("Entry price", NumberSort, NumberCell),
        new("Exit date", TextSort, DateCell),
        new("Exit price", NumberSort, NumberCell),
        new("Quantity", NumberSort, NumberCell),
        new("Fees", NumberSort, NumberCell),
        new("Net profit", NumberSort, NumberCell),
        new("Return", NumberSort, NumberCell),
        new("Exit reason", NumberSort, null),
        new("Holding bars", NumberSort, NumberCell),
    ];

    public bool IsEmpty => Rows.Count == 0;

    public static ResultTradesViewModel Create(IReadOnlyList<Trade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return new ResultTradesViewModel(TradeColumns, trades.Select(Row).ToList());
    }

    /// <summary>Display label of an exit reason (P4-U6).</summary>
    public static string Label(ExitReason reason) => reason switch
    {
        ExitReason.Signal => "Signal",
        ExitReason.StopLoss => "Stop-loss",
        ExitReason.TakeProfit => "Take-profit",
        ExitReason.TrailingStop => "Trailing stop",
        ExitReason.EndOfData => "End of data",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown exit reason."),
    };

    /// <summary>
    /// One distinct badge per exit reason. Neutral hues only: green and red mean gain and loss (site.css), and an
    /// exit reason says nothing about the outcome. Bootstrap has no purple, hence qb-exit-trailing.
    /// </summary>
    public static string BadgeClass(ExitReason reason) => reason switch
    {
        ExitReason.Signal => "text-bg-primary",
        ExitReason.StopLoss => "text-bg-warning",
        ExitReason.TakeProfit => "text-bg-info",
        ExitReason.TrailingStop => "qb-exit-trailing",
        ExitReason.EndOfData => "text-bg-secondary",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown exit reason."),
    };

    /// <summary>Shortest round-trip invariant form of a double (sort key only, never shown): -0.0123, 1E-05.</summary>
    public static string SortNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static TradeRow Row(Trade trade) => new(
        trade.NetProfit > 0 ? GainRow : trade.NetProfit < 0 ? LossRow : string.Empty,
        Date(trade.EntryDate),
        Money(trade.EntryPrice),
        Date(trade.ExitDate),
        Money(trade.ExitPrice),
        new SortableCell(DisplayFormat.Quantity(trade.Quantity), SortNumber(trade.Quantity)),
        Money(trade.Fees),
        Money(trade.NetProfit),
        new SortableCell(DisplayFormat.Percent(trade.ReturnFraction), SortNumber(trade.ReturnFraction)),
        new SortableCell(Label(trade.ExitReason), ((int)trade.ExitReason).ToString(CultureInfo.InvariantCulture)),
        BadgeClass(trade.ExitReason),
        new SortableCell(
            trade.HoldingBars.ToString(CultureInfo.InvariantCulture),
            trade.HoldingBars.ToString(CultureInfo.InvariantCulture)));

    private static SortableCell Date(DateOnly date)
    {
        var text = date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);
        return new SortableCell(text, text);
    }

    private static SortableCell Money(decimal amount) => new(DisplayFormat.Money(amount), SortNumber(amount));

    private static string SortNumber(decimal value) => BacktestQuery.FormatNumber(value)!;
}
