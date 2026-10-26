using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine.Tests;

/// <summary>O3 and O4 on grids written by hand. Cells are in row-major order (for each Y, every X).</summary>
public class SweepAnalysisTests
{
    // builds a grid from rows of values (null = empty cell); the first row is Y = 0
    private static List<SweepCell> Grid(params double?[][] rows)
    {
        List<SweepCell> cells = new();
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                cells.Add(new SweepCell(x, y, rows[y][x], rows[y][x] == null ? 0 : 5, null));
            }
        }

        return cells;
    }

    private static SweepSummary Summarize(params double?[][] rows)
    {
        return SweepAnalysis.Summarize(rows[0].Length, rows.Length, Grid(rows));
    }

    [Fact]
    public void Best_IsTheHighestValue() // O3
    {
        SweepSummary summary = Summarize(
            new double?[] { 1, 2, 3 },
            new double?[] { 4, 9, 5 },
            new double?[] { 6, 7, 8 });

        summary.Best!.Value.Should().Be(9);
        summary.Best.X.Should().Be(1);
        summary.Best.Y.Should().Be(1);
    }

    [Fact]
    public void Tie_GoesToTheSmallestY_ThenTheSmallestX() // O3
    {
        SweepSummary summary = Summarize(
            new double?[] { 1, 5, 1 },
            new double?[] { 5, 1, 5 },
            new double?[] { 5, 1, 1 });

        // the cells with 5 are (1,0) (0,1) (2,1) (0,2): the smallest Y is 0, so (1,0)
        summary.Best!.X.Should().Be(1);
        summary.Best.Y.Should().Be(0);
    }

    [Fact]
    public void Tie_OnTheSameRow_GoesToTheSmallestX() // O3
    {
        SweepSummary summary = Summarize(new double?[] { 1, 7, 7 }, new double?[] { 1, 1, 1 });

        summary.Best!.X.Should().Be(1);
    }

    [Fact]
    public void NoValue_GivesNoBest() // O3
    {
        SweepSummary summary = Summarize(new double?[] { null, null }, new double?[] { null, null });

        summary.Best.Should().BeNull();
        summary.BestNeighborhoodAverage.Should().BeNull();
        summary.IsIsolatedPeak.Should().BeFalse();
    }

    [Fact]
    public void IsolatedPeak_IsDetected() // O4
    {
        // best = 10, its 8 neighbours average 1 < 0.5 * 10
        SweepSummary summary = Summarize(
            new double?[] { 1, 1, 1 },
            new double?[] { 1, 10, 1 },
            new double?[] { 1, 1, 1 });

        summary.BestNeighborhoodAverage.Should().BeApproximately(1.0, 1e-12);
        summary.IsIsolatedPeak.Should().BeTrue();
    }

    [Fact]
    public void Plateau_IsNotAnIsolatedPeak() // O4
    {
        // neighbours average 9 >= 0.5 * 10
        SweepSummary summary = Summarize(
            new double?[] { 9, 9, 9 },
            new double?[] { 9, 10, 9 },
            new double?[] { 9, 9, 9 });

        summary.BestNeighborhoodAverage.Should().BeApproximately(9.0, 1e-12);
        summary.IsIsolatedPeak.Should().BeFalse();
    }

    [Fact]
    public void NeighbourExactlyHalf_IsNotIsolated() // O4: the test is strictly "< 0.5 * best"
    {
        SweepSummary summary = Summarize(new double?[] { 5, 10 });

        summary.BestNeighborhoodAverage.Should().Be(5);
        summary.IsIsolatedPeak.Should().BeFalse();
    }

    [Fact]
    public void NullNeighbours_AreIgnored() // O4
    {
        // the best (10) is in the corner: only 3 neighbours, one of them null; average of 2 and 4 = 3
        SweepSummary summary = Summarize(
            new double?[] { 10, 2 },
            new double?[] { null, 4 });

        summary.BestNeighborhoodAverage.Should().BeApproximately(3.0, 1e-12);
        summary.IsIsolatedPeak.Should().BeTrue(); // 3 < 5
    }

    [Fact]
    public void BestWithoutAnyNeighbourValue_IsAnIsolatedPeak() // O4
    {
        SweepSummary summary = Summarize(new double?[] { 10, null }, new double?[] { null, null });

        summary.BestNeighborhoodAverage.Should().BeNull();
        summary.IsIsolatedPeak.Should().BeTrue();
    }

    [Fact]
    public void NegativeBest_IsNeverAnIsolatedPeak() // O4
    {
        SweepSummary summary = Summarize(
            new double?[] { -5, -5, -5 },
            new double?[] { -5, -1, -5 },
            new double?[] { -5, -5, -5 });

        summary.Best!.Value.Should().Be(-1);
        summary.IsIsolatedPeak.Should().BeFalse();
    }

    [Fact]
    public void BestOnAnEdge_UsesOnlyTheNeighboursInsideTheGrid() // O4
    {
        // best 8 at the bottom-right corner: neighbours 4, 4, 4
        SweepSummary summary = Summarize(
            new double?[] { 0, 0, 0 },
            new double?[] { 0, 4, 4 },
            new double?[] { 0, 4, 8 });

        summary.Best!.X.Should().Be(2);
        summary.Best.Y.Should().Be(2);
        summary.BestNeighborhoodAverage.Should().BeApproximately(4.0, 1e-12);
        summary.IsIsolatedPeak.Should().BeFalse(); // 4 is not under 0.5 * 8
    }
}
