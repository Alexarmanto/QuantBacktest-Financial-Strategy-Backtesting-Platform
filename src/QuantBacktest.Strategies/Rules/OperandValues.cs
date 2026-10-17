using System.Globalization;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// Everything about one operand of a no-code rule: its values, its text and how to draw it.
// Example of operands: Close, SMA(200), RSI(2), the constant 10.
internal static class OperandValues
{
    // R5: value of the operand at every bar (NaN during the warm-up of an indicator)
    public static double[] Compute(Operand operand, double[] opens, double[] highs, double[] lows, double[] closes, double[] volumes)
    {
        switch (operand.Kind)
        {
            case OperandKind.Close:
                return closes;
            case OperandKind.Open:
                return opens;
            case OperandKind.High:
                return highs;
            case OperandKind.Low:
                return lows;
            case OperandKind.Volume:
                return volumes;
            case OperandKind.Sma:
                return Indicators.Sma(closes, PeriodOf(operand));
            case OperandKind.Ema:
                return Indicators.Ema(closes, PeriodOf(operand));
            case OperandKind.Rsi:
                return Indicators.Rsi(closes, PeriodOf(operand));
            case OperandKind.RateOfChange:
                return Indicators.Roc(closes, PeriodOf(operand));
            case OperandKind.BollingerUpper:
                return Indicators.Bollinger(closes, PeriodOf(operand), StdDevsOf(operand)).Upper;
            case OperandKind.BollingerMiddle:
                return Indicators.Bollinger(closes, PeriodOf(operand), StdDevsOf(operand)).Middle;
            case OperandKind.BollingerLower:
                return Indicators.Bollinger(closes, PeriodOf(operand), StdDevsOf(operand)).Lower;
            case OperandKind.MacdLine:
                return Indicators.Macd(closes, 12, 26, 9).Line;
            case OperandKind.MacdSignal:
                return Indicators.Macd(closes, 12, 26, 9).Signal;
            case OperandKind.HighestHigh:
                return Indicators.HighestHigh(highs, PeriodOf(operand));
            case OperandKind.LowestLow:
                return Indicators.LowestLow(lows, PeriodOf(operand));
            default:
                // Constant: the same value on every bar
                double[] result = new double[closes.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = ValueOf(operand);
                }
                return result;
        }
    }

    // text shown to the user (CONTRACT section 8): "SMA(200)", "BB lower(20, 2)", "10"
    public static string Label(Operand operand)
    {
        switch (operand.Kind)
        {
            case OperandKind.Close: return "Close";
            case OperandKind.Open: return "Open";
            case OperandKind.High: return "High";
            case OperandKind.Low: return "Low";
            case OperandKind.Volume: return "Volume";
            case OperandKind.Sma: return "SMA(" + operand.Period + ")";
            case OperandKind.Ema: return "EMA(" + operand.Period + ")";
            case OperandKind.Rsi: return "RSI(" + operand.Period + ")";
            case OperandKind.RateOfChange: return "ROC(" + operand.Period + ")";
            case OperandKind.BollingerUpper: return "BB upper(" + operand.Period + ", " + Format(StdDevsOf(operand)) + ")";
            case OperandKind.BollingerMiddle: return "BB middle(" + operand.Period + ", " + Format(StdDevsOf(operand)) + ")";
            case OperandKind.BollingerLower: return "BB lower(" + operand.Period + ", " + Format(StdDevsOf(operand)) + ")";
            case OperandKind.MacdLine: return "MACD line(12, 26, 9)";
            case OperandKind.MacdSignal: return "MACD signal(12, 26, 9)";
            case OperandKind.HighestHigh: return "Highest high(" + operand.Period + ")";
            case OperandKind.LowestLow: return "Lowest low(" + operand.Period + ")";
            default: return Format(ValueOf(operand));
        }
    }

    // P2-N3: key of the chart line, in lower case: "sma-200", "bb-lower-20-2", "macd-line"
    public static string Key(Operand operand)
    {
        switch (operand.Kind)
        {
            case OperandKind.Sma: return "sma-" + operand.Period;
            case OperandKind.Ema: return "ema-" + operand.Period;
            case OperandKind.Rsi: return "rsi-" + operand.Period;
            case OperandKind.RateOfChange: return "roc-" + operand.Period;
            case OperandKind.BollingerUpper: return "bb-upper-" + operand.Period + "-" + Format(StdDevsOf(operand));
            case OperandKind.BollingerMiddle: return "bb-middle-" + operand.Period + "-" + Format(StdDevsOf(operand));
            case OperandKind.BollingerLower: return "bb-lower-" + operand.Period + "-" + Format(StdDevsOf(operand));
            case OperandKind.MacdLine: return "macd-line";
            case OperandKind.MacdSignal: return "macd-signal";
            case OperandKind.HighestHigh: return "highest-high-" + operand.Period;
            case OperandKind.LowestLow: return "lowest-low-" + operand.Period;
            default: return Label(operand).ToLowerInvariant();
        }
    }

    // P2-N3: the prices and the constants are not drawn as an extra line, the indicators are
    public static bool IsDrawn(Operand operand)
    {
        return operand.Kind != OperandKind.Constant
            && operand.Kind != OperandKind.Close
            && operand.Kind != OperandKind.Open
            && operand.Kind != OperandKind.High
            && operand.Kind != OperandKind.Low
            && operand.Kind != OperandKind.Volume;
    }

    // P2-N3: RSI, ROC and MACD go in their own chart, the others on top of the price
    public static IndicatorPane Pane(Operand operand)
    {
        if (operand.Kind == OperandKind.Rsi || operand.Kind == OperandKind.RateOfChange
            || operand.Kind == OperandKind.MacdLine || operand.Kind == OperandKind.MacdSignal)
        {
            return IndicatorPane.Oscillator;
        }
        return IndicatorPane.Price;
    }

    // R3: the period is always given for an indicator (checked by RuleStrategyFactory)
    private static int PeriodOf(Operand operand)
    {
        if (operand.Period == null)
        {
            return 0;
        }
        return operand.Period.Value;
    }

    // R3: the value is always given for a constant (checked by RuleStrategyFactory)
    private static double ValueOf(Operand operand)
    {
        if (operand.Value == null)
        {
            return 0;
        }
        return operand.Value.Value;
    }

    // R3: 2 standard deviations when nothing is given
    private static double StdDevsOf(Operand operand)
    {
        if (operand.StdDevs == null)
        {
            return 2;
        }
        return operand.StdDevs.Value;
    }

    private static string Format(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
