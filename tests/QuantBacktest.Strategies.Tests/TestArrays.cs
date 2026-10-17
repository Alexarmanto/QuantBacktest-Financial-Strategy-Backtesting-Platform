namespace QuantBacktest.Strategies.Tests;

// Small helper to compare two arrays of doubles in the tests
internal static class TestArrays
{
    // each value must be NaN in both arrays, or equal with a small tolerance
    public static void ShouldBeClose(double[] actual, double[] expected, double tolerance)
    {
        actual.Length.Should().Be(expected.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            if (double.IsNaN(expected[i]))
            {
                double.IsNaN(actual[i]).Should().BeTrue($"index {i} should be NaN");
            }
            else
            {
                actual[i].Should().BeApproximately(expected[i], tolerance, $"index {i} is wrong");
            }
        }
    }
}
