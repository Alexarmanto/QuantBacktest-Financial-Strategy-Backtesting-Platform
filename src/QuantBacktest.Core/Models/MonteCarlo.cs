namespace QuantBacktest.Core.Models;

public sealed record MonteCarloRequest(
    int Simulations = Conventions.DefaultMonteCarloSimulations,
    int Seed = Conventions.DefaultMonteCarloSeed);

/// <summary>Percentiles of a simulated distribution (fractions). Method: docs/CONTRACT.md section 9.</summary>
public sealed record PercentileSummary(double P5, double P25, double P50, double P75, double P95);

/// <summary>Histogram bin [Lower, Upper) ; the last bin also includes Upper.</summary>
public sealed record HistogramBin(double Lower, double Upper, int Count);

/// <summary>
/// Result of resampling the trades of one backtest. "Actual" values are computed on the real trade order
/// with the same trade-level method, so they are directly comparable with the percentiles.
/// </summary>
public sealed record MonteCarloResult(
    MonteCarloRequest Request,
    int TradeCount,
    PercentileSummary FinalReturn,
    PercentileSummary MaxDrawdown,
    double ProbabilityOfLoss,
    double ActualFinalReturn,
    double ActualMaxDrawdown,
    IReadOnlyList<HistogramBin> FinalReturnHistogram,
    IReadOnlyList<HistogramBin> MaxDrawdownHistogram);
