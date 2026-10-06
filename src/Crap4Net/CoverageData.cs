namespace Crap4Net;

/// <summary>
/// Line hits per source file, merged across any number of reports. Coverage is attributed to a
/// method by line range rather than by name, because the compiler moves async/iterator bodies
/// and lambdas into generated classes whose names don't match the source.
/// </summary>
internal sealed class CoverageData
{
    // Linux file systems are case-sensitive; the macOS and Windows defaults are not.
    public static StringComparison PathComparison { get; } =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static StringComparer PathComparer { get; } = StringComparer.FromComparison(PathComparison);

    private readonly Dictionary<string, Dictionary<int, long>> _hitsByFile = new(PathComparer);

    public bool IsEmpty => _hitsByFile.Count == 0;

    public void AddLine(string filePath, int line, long hits)
    {
        var path = Path.GetFullPath(filePath);
        if (!_hitsByFile.TryGetValue(path, out var lines))
            _hitsByFile[path] = lines = [];
        lines[line] = Math.Max(lines.GetValueOrDefault(line), hits);
    }

    public void Merge(CoverageData other)
    {
        foreach (var (file, lines) in other._hitsByFile)
            foreach (var (line, hits) in lines)
                AddLine(file, line, hits);
    }

    /// <summary>
    /// Fraction (0..1) of coverable lines in startLine..endLine that ran, or null when the report
    /// doesn't know the file or has no coverable lines in that range.
    /// </summary>
    public double? CoverageFor(string filePath, int startLine, int endLine)
    {
        if (!_hitsByFile.TryGetValue(Path.GetFullPath(filePath), out var lines))
            return null;
        int coverable = 0, covered = 0;
        foreach (var (line, hits) in lines)
        {
            if (line < startLine || line > endLine)
                continue;
            coverable++;
            if (hits > 0)
                covered++;
        }
        return coverable == 0 ? null : (double)covered / coverable;
    }
}
