namespace Crap4Net.Tests;

public class CrapScoreTests
{
    [Theory]
    [InlineData(1, 1.0, 1.0)]
    [InlineData(1, 0.0, 2.0)]
    [InlineData(5, 1.0, 5.0)]
    [InlineData(5, 0.0, 30.0)]
    [InlineData(10, 0.5, 22.5)]
    [InlineData(12, 0.45, 12 + 144 * 0.166375)]
    public void Computes_cc_squared_times_uncovered_cubed_plus_cc(int complexity, double coverage, double expected) =>
        Assert.Equal(expected, CrapScore.Compute(complexity, coverage), precision: 9);
}
