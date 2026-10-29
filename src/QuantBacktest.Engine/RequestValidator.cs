using System.Globalization;
using QuantBacktest.Core;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine;

/// <summary>
/// Checks a request before it is simulated (docs/CONTRACT.md section 4, "Validation d'une requête").
/// Shared by the engine, the backtest service and the heatmap.
/// </summary>
internal static class RequestValidator
{
    /// <summary>Checks every execution setting. Each error names the field and its range.</summary>
    public static void ValidateExecution(ExecutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.InitialCapital <= 0)
        {
            throw new BacktestValidationException("Initial capital must be greater than 0.");
        }

        CheckRange("Fee rate", settings.FeeRate, 0, Conventions.MaxFeeRate, zeroAllowed: true);
        CheckRange("Slippage rate", settings.SlippageRate, 0, Conventions.MaxSlippageRate, zeroAllowed: true);

        if (settings.StopLossFraction != null)
        {
            CheckRange("Stop-loss", settings.StopLossFraction.Value, 0, Conventions.MaxStopLossFraction, zeroAllowed: false);
        }

        if (settings.TakeProfitFraction != null)
        {
            CheckRange("Take-profit", settings.TakeProfitFraction.Value, 0, Conventions.MaxTakeProfitFraction, zeroAllowed: false);
        }

        if (settings.TrailingStopFraction != null)
        {
            CheckRange("Trailing stop", settings.TrailingStopFraction.Value, 0, Conventions.MaxTrailingStopFraction, zeroAllowed: false);
        }

        // written so that NaN is rejected too
        if (!(settings.RiskFreeRate >= 0 && settings.RiskFreeRate <= Conventions.MaxRiskFreeRate))
        {
            throw new BacktestValidationException(
                $"Risk-free rate must be between 0 and {Conventions.MaxRiskFreeRate.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void CheckRange(string name, decimal value, decimal min, decimal max, bool zeroAllowed)
    {
        bool tooLow = zeroAllowed ? value < min : value <= min;
        if (tooLow || value > max)
        {
            string open = zeroAllowed ? "[" : "]";
            throw new BacktestValidationException(
                $"{name} must be in {open}{min.ToString(CultureInfo.InvariantCulture)}, {max.ToString(CultureInfo.InvariantCulture)}].");
        }
    }
}
