namespace Crap4Net;

internal static class CrapScore
{
    /// <summary>CRAP = CC² × (1 − coverage)³ + CC, with <paramref name="coverage"/> as a fraction in 0..1.</summary>
    public static double Compute(int complexity, double coverage) =>
        complexity * complexity * Math.Pow(1 - coverage, 3) + complexity;
}
