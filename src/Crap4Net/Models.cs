namespace Crap4Net;

/// <param name="Line">Line of the member's name, for the report's location column.</param>
/// <param name="StartLine">First line of the member's span; coverage is attributed over StartLine..EndLine.</param>
internal sealed record MethodDescriptor(
    string Name,
    string ClassName,
    string Namespace,
    string FilePath,
    int Line,
    int StartLine,
    int EndLine,
    int Complexity);

/// <param name="CoveragePercent">0..100, or null when no coverage data covers the method.</param>
/// <param name="Crap">Null exactly when <paramref name="CoveragePercent"/> is.</param>
internal sealed record MethodMetrics(MethodDescriptor Method, double? CoveragePercent, double? Crap);

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Usage = 1;
    public const int ThresholdExceeded = 2;
    public const int Failure = 3;
}

/// <summary>Bad command line or path argument: exit 1 with usage.</summary>
internal sealed class CliUsageException(string message) : Exception(message);

/// <summary>The analysis could not complete, e.g. tests failed: exit 3.</summary>
internal sealed class CrapFailureException(string message) : Exception(message);
