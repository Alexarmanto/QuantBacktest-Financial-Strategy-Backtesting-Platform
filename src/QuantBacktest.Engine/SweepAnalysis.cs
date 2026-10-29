using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine;

/// <summary>What the heatmap says about its grid: the best cell and the overfitting warning (rules O3 and O4).</summary>
internal sealed record SweepSummary(SweepCell? Best, double? BestNeighborhoodAverage, bool IsIsolatedPeak);

/// <summary>Pure functions on a finished grid, so they can be tested on grids written by hand.</summary>
internal static class SweepAnalysis
{
    /// <summary>
    /// cells are in row-major order: for each Y, every X (so the cell at column x and row y has the index y * xCount + x).
    /// </summary>
    public static SweepSummary Summarize(int xCount, int yCount, IReadOnlyList<SweepCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        // O3: highest non-null Value; a tie goes to the smallest Y, then the smallest X
        int bestIndex = -1;
        for (int i = 0; i < cells.Count; i++)
        {
            SweepCell cell = cells[i];
            if (cell.Value == null)
            {
                continue;
            }

            if (bestIndex < 0 || IsBetter(cell, cells[bestIndex]))
            {
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            return new SweepSummary(null, null, false);
        }

        SweepCell best = cells[bestIndex];

        // O4: average of the non-null Values of the (up to 8) neighbours of the best cell
        int bestColumn = bestIndex % xCount;
        int bestRow = bestIndex / xCount;
        double sum = 0;
        int count = 0;
        for (int row = bestRow - 1; row <= bestRow + 1; row++)
        {
            for (int column = bestColumn - 1; column <= bestColumn + 1; column++)
            {
                bool isTheBestCell = row == bestRow && column == bestColumn;
                bool isInsideTheGrid = row >= 0 && row < yCount && column >= 0 && column < xCount;
                if (isTheBestCell || !isInsideTheGrid)
                {
                    continue;
                }

                double? neighbourValue = cells[(row * xCount) + column].Value;
                if (neighbourValue != null)
                {
                    sum += neighbourValue.Value;
                    count++;
                }
            }
        }

        double? average = count == 0 ? null : sum / count;
        double bestValue = best.Value!.Value;
        bool isolated = bestValue > 0 && (average == null || average.Value < 0.5 * bestValue);

        return new SweepSummary(best, average, isolated);
    }

    private static bool IsBetter(SweepCell candidate, SweepCell current)
    {
        double candidateValue = candidate.Value!.Value;
        double currentValue = current.Value!.Value;
        if (candidateValue != currentValue)
        {
            return candidateValue > currentValue;
        }

        if (candidate.Y != current.Y)
        {
            return candidate.Y < current.Y;
        }

        return candidate.X < current.X;
    }
}
