namespace Crap4Net;

internal static class CrapAnalyzer
{
    public static IReadOnlyList<MethodMetrics> Analyze(IEnumerable<MethodDescriptor> methods, CoverageData coverage) =>
        methods.Select(method =>
        {
            var fraction = coverage.CoverageFor(method.FilePath, method.StartLine, method.EndLine);
            return new MethodMetrics(
                method,
                fraction * 100,
                fraction is { } f ? CrapScore.Compute(method.Complexity, f) : null);
        }).ToList();

    /// <summary>The highest numeric CRAP score, or 0 when none has coverage.</summary>
    public static double MaxCrap(IEnumerable<MethodMetrics> metrics) =>
        metrics.Select(m => m.Crap).OfType<double>().DefaultIfEmpty(0).Max();

    /// <summary>Worst first; methods without coverage go last.</summary>
    public static IReadOnlyList<MethodMetrics> Sort(IEnumerable<MethodMetrics> metrics) => metrics
        .OrderBy(m => m.Crap is null)
        .ThenByDescending(m => m.Crap ?? 0)
        .ThenByDescending(m => m.Method.Complexity)
        .ThenBy(m => m.Method.FilePath, StringComparer.Ordinal)
        .ThenBy(m => m.Method.Line)
        .ToList();
}
