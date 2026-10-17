using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// ============================================================================================
// P2-C3: TEMPLATE to write your own strategy in C#.
//
// How to use it:
//   1. Copy this file into the Strategies folder and rename it (for example MyStrategy.cs).
//   2. Rename the class (for example MyStrategy) and its constructor.
//   3. Remove the [HiddenStrategy] line just below: without it the catalog shows the strategy.
//   4. Change the id (step A): it must be unique, in lower case with dashes ("my-strategy").
//   5. Write the documentation (B), the parameters (C) and the rules (D and E).
//   6. Build and run the application: the strategy is in the list of the backtest form.
//
// Rules to respect (docs/CONTRACT.md section 8):
//   - one signal per bar, Hold while the indicators are not ready (warm-up);
//   - only look at bars[0..i] to decide the signal of bar i (never the future);
//   - a strategy only gives signals: no money, no fees, no stop-loss (the engine does that).
// ============================================================================================
[HiddenStrategy]
internal class TemplateStrategy : StrategyBase
{
    // Constructor
    public TemplateStrategy()
        : base(
            // (A) id, name shown in the website, and one or two sentences of description
            "template-strategy",
            "Template strategy",
            "Example strategy: buys when the close goes above its moving average and sells when it goes below.",

            // family of the strategy: TrendFollowing, MeanReversion, Momentum, Breakout or Calendar
            StrategyCategory.TrendFollowing,

            // (B) the explanation card: idea, entry rule, exit rule, risks (plain English text)
            new StrategyDocumentation(
                "When the price is above its average, the trend is up. This is only an example to copy.",
                "Buy when the close crosses above SMA(period). Default: period = 20.",
                "Sell when the close crosses below SMA(period).",
                "Many false signals when the market goes sideways."),

            // timeframes accepted: Daily and/or Weekly
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },

            // (C) the parameters the user can change: key, label, type, default, min, max, step, help text
            new ParameterDefinition[]
            {
                new ParameterDefinition("period", "SMA period (bars)", ParameterType.Integer, 20, 2, 200, 1, "Period of the moving average."),
            })
    {
    }

    // (D) the rules: one signal per bar
    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        // read the parameters (the default value is used when one is missing)
        int period = ReadIntParameter(parameters, "period");

        // prices as arrays, then the indicators you need (see Indicators/Indicators.cs)
        double[] closes = GetCloses(bars);
        double[] sma = Indicators.Sma(closes, period);

        // all signals are Hold at the start
        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            // if buy and sell are both true, Sell must win (S4): test the sale first
            if (Indicators.CrossesBelow(closes, sma, i))
            {
                signals[i] = Signal.Sell;
            }
            else if (Indicators.CrossesAbove(closes, sma, i))
            {
                signals[i] = Signal.Buy;
            }
        }
        return signals;
    }

    // (E) the lines to draw on the charts (return new IndicatorSeries[0] if there is none)
    public override IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int period = ReadIntParameter(parameters, "period");
        double[] sma = Indicators.Sma(GetCloses(bars), period);

        return new IndicatorSeries[]
        {
            MakeSeries("sma-" + period, "SMA(" + period + ")", IndicatorPane.Price, sma, new double[0]),
        };
    }
}
