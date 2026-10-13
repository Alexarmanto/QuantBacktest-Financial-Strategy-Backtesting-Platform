using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>Part 1. Turns a finished backtest into downloadable documents. Never recomputes a metric.</summary>
public interface IReportGenerator
{
    /// <summary>A complete PDF report. When monteCarlo is given, a Monte Carlo section is added.</summary>
    byte[] GeneratePdf(BacktestResult result, MonteCarloResult? monteCarlo = null);

    /// <summary>The trade log as CSV text (format: docs/CONTRACT.md section 11).</summary>
    string GenerateTradesCsv(BacktestResult result);
}
