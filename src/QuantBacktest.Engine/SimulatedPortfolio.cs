using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine;

/// <summary>
/// The cash, the position and the list of trades during one simulation. Long only, 100 % of the cash (rule X5).
/// Fills, fees and quantities follow rule X6.
/// </summary>
internal sealed class SimulatedPortfolio
{
    private readonly decimal _feeRate;
    private readonly decimal _slippageRate;
    private readonly List<Trade> _trades = new();

    private DateOnly _entryDate;
    private decimal _entryCost;
    private decimal _entryFees;

    public SimulatedPortfolio(decimal initialCapital, decimal feeRate, decimal slippageRate)
    {
        Cash = initialCapital;
        _feeRate = feeRate;
        _slippageRate = slippageRate;
    }

    public decimal Cash { get; private set; }

    public decimal Quantity { get; private set; }

    public bool IsInvested { get; private set; }

    /// <summary>Fill price of the current position (slippage included).</summary>
    public decimal EntryPrice { get; private set; }

    /// <summary>Index of the bar where the current position was opened.</summary>
    public int EntryIndex { get; private set; }

    public IReadOnlyList<Trade> Trades => _trades;

    /// <summary>X6: buy with all the cash at price * (1 + slippage).</summary>
    public void Buy(DateOnly date, int barIndex, decimal price)
    {
        decimal fill = price * (1 + _slippageRate);
        Quantity = Cash / (fill * (1 + _feeRate));
        _entryCost = Quantity * fill * (1 + _feeRate);
        _entryFees = Quantity * fill * _feeRate;
        Cash -= _entryCost;

        EntryPrice = fill;
        EntryIndex = barIndex;
        _entryDate = date;
        IsInvested = true;
    }

    /// <summary>X6 and X13: sell everything at price * (1 - slippage) and record the trade.</summary>
    public void Sell(DateOnly date, decimal price, ExitReason reason, int holdingBars)
    {
        decimal fill = price * (1 - _slippageRate);
        decimal proceeds = Quantity * fill * (1 - _feeRate);
        decimal exitFees = Quantity * fill * _feeRate;
        decimal netProfit = proceeds - _entryCost;

        _trades.Add(new Trade(
            EntryDate: _entryDate,
            EntryPrice: EntryPrice,
            ExitDate: date,
            ExitPrice: fill,
            Quantity: Quantity,
            Fees: _entryFees + exitFees,
            NetProfit: netProfit,
            ReturnFraction: (double)(netProfit / _entryCost),
            ExitReason: reason,
            HoldingBars: holdingBars));

        Cash += proceeds;
        Quantity = 0;
        IsInvested = false;
    }
}
