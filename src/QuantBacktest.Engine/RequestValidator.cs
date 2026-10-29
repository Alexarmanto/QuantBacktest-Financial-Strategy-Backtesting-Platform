using System.Globalization;
using System.Text.RegularExpressions;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine;

/// <summary>
/// Checks a request before it is simulated (docs/CONTRACT.md section 4, "Validation d'une requête").
/// Shared by the engine, the backtest service and the heatmap.
/// </summary>
internal static class RequestValidator
{
    /// <summary>Trim and upper-case, like the data service does.</summary>
    public static string NormalizeSymbol(string symbol)
    {
        return (symbol ?? string.Empty).Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Checks the whole request against the strategy and returns it completed with the default value of every
    /// parameter that was left out (P3-E4). The strategy must already be resolved.
    /// </summary>
    public static BacktestRequest ValidateAndComplete(BacktestRequest request, IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(strategy);

        if (!Regex.IsMatch(NormalizeSymbol(request.Symbol), Conventions.SymbolPattern))
        {
            throw new BacktestValidationException("Symbol must be 1 to 10 characters: letters, digits, '.' or '-'.");
        }

        if (!strategy.SupportedTimeframes.Contains(request.Timeframe))
        {
            throw new BacktestValidationException(
                $"Strategy '{strategy.Name}' does not support the {request.Timeframe} timeframe.");
        }

        Dictionary<string, double> completed = CompleteParameters(request.Parameters, strategy);
        ValidateExecution(request.Execution);

        return request with { Parameters = completed };
    }

    /// <summary>Unknown key, value outside [Min, Max] or not whole for an Integer parameter: rejected. Missing keys get their default.</summary>
    private static Dictionary<string, double> CompleteParameters(
        IReadOnlyDictionary<string, double> given, IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(given);

        foreach (string key in given.Keys)
        {
            if (!strategy.Parameters.Any(definition => definition.Key == key))
            {
                throw new BacktestValidationException($"Unknown parameter '{key}' for strategy '{strategy.Name}'.");
            }
        }

        Dictionary<string, double> completed = new();
        foreach (ParameterDefinition definition in strategy.Parameters)
        {
            double value = given.TryGetValue(definition.Key, out double givenValue) ? givenValue : definition.DefaultValue;
            string range = $"{definition.Min.ToString(CultureInfo.InvariantCulture)} and {definition.Max.ToString(CultureInfo.InvariantCulture)}";

            // written so that NaN is rejected too
            if (!(value >= definition.Min && value <= definition.Max))
            {
                throw new BacktestValidationException($"Parameter '{definition.Key}' must be between {range}.");
            }

            if (definition.Type == ParameterType.Integer && value != Math.Floor(value))
            {
                throw new BacktestValidationException($"Parameter '{definition.Key}' must be a whole number between {range}.");
            }

            completed[definition.Key] = value;
        }

        return completed;
    }
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
