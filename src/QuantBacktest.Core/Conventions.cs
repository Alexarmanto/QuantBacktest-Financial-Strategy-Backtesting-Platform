using System.Text;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Core;

/// <summary>
/// Shared constants and tiny shared helpers. Defined once here so no part can diverge from the others.
/// Semantics are specified in docs/CONTRACT.md.
/// </summary>
public static class Conventions
{
    // ---- Execution defaults and limits (docs/CONTRACT.md section 4) ----

    public const decimal DefaultInitialCapital = 10_000m;

    /// <summary>Fee rate applied to the notional of every fill (0.001 = 0.1 %).</summary>
    public const decimal DefaultFeeRate = 0.001m;

    public const decimal DefaultSlippageRate = 0m;

    /// <summary>Annual risk-free rate used by Sharpe and Sortino (0.02 = 2 %).</summary>
    public const double DefaultRiskFreeRate = 0.0;

    public const decimal MaxFeeRate = 0.05m;
    public const decimal MaxSlippageRate = 0.02m;
    public const decimal MaxStopLossFraction = 0.5m;
    public const decimal MaxTakeProfitFraction = 5m;
    public const decimal MaxTrailingStopFraction = 0.5m;
    public const double MaxRiskFreeRate = 0.2;

    // ---- Feature limits ----

    /// <summary>Maximum number of cells of an optimisation heatmap (25 x 25).</summary>
    public const int MaxSweepCells = 625;

    /// <summary>Maximum number of strategies in the comparator.</summary>
    public const int MaxComparedStrategies = 5;

    /// <summary>Default share of the selected period used for training in a train/test split.</summary>
    public const double DefaultTrainFraction = 0.7;

    public const int DefaultMonteCarloSimulations = 1000;
    public const int MaxMonteCarloSimulations = 10_000;
    public const int DefaultMonteCarloSeed = 42;
    public const int MinTradesForMonteCarlo = 5;
    public const int HistogramBinCount = 20;

    /// <summary>Maximum number of conditions in the entry rule or in the exit rule of a custom strategy.</summary>
    public const int MaxConditionsPerRule = 5;

    // ---- Identifiers ----

    /// <summary>Valid market symbol after normalisation (trim + upper case).</summary>
    public const string SymbolPattern = "^[A-Z0-9.\\-]{1,10}$";

    public const string CustomStrategyIdPrefix = "custom-";

    /// <summary>Valid identifier of a custom (no-code) strategy.</summary>
    public const string CustomStrategyIdPattern = "^custom-[a-z0-9]+(-[a-z0-9]+)*$";

    public const int CustomStrategySlugMaxLength = 40;

    /// <summary>Number of periods per year used to annualise volatility, Sharpe and Sortino.</summary>
    public static int PeriodsPerYear(Timeframe timeframe) => timeframe switch
    {
        Timeframe.Daily => 252,
        Timeframe.Weekly => 52,
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
    };

    /// <summary>
    /// The one and only way to turn a custom strategy name into its identifier.
    /// "My RSI dip #2" becomes "custom-my-rsi-dip-2". Throws when the name contains no letter or digit.
    /// </summary>
    public static string CreateCustomStrategyId(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder();
        var previousWasDash = false;
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(character);
                previousWasDash = false;
            }
            else if (!previousWasDash && builder.Length > 0)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > CustomStrategySlugMaxLength)
        {
            slug = slug[..CustomStrategySlugMaxLength].Trim('-');
        }

        if (slug.Length == 0)
        {
            throw new ArgumentException("The strategy name must contain at least one letter or digit.", nameof(name));
        }

        return CustomStrategyIdPrefix + slug;
    }
}
