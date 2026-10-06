using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Crap4Net;

internal static class ReportFormatter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <param name="root">Locations are shown relative to this directory.</param>
    public static string Text(IReadOnlyList<MethodMetrics> metrics, string root)
    {
        string[] headers = ["Method", "Class", "CC", "Cov%", "CRAP", "Location"];
        var rows = CrapAnalyzer.Sort(metrics).Select(m => new[]
        {
            m.Method.Name,
            m.Method.ClassName,
            m.Method.Complexity.ToString(Invariant),
            m.CoveragePercent is { } c ? c.ToString("0.0", Invariant) + "%" : "N/A",
            m.Crap is { } s ? s.ToString("0.0", Invariant) : "N/A",
            Location(m.Method, root),
        }).ToList();

        var widths = headers.Select((h, i) => rows.Select(r => r[i].Length).Prepend(h.Length).Max()).ToArray();
        // Text columns align left, numbers right; the last column isn't padded.
        string Format(string[] cells) => string.Join("  ", cells.Select((cell, i) =>
            i == cells.Length - 1 ? cell
            : i is >= 2 and <= 4 ? cell.PadLeft(widths[i])
            : cell.PadRight(widths[i])));

        var header = Format(headers);
        var builder = new StringBuilder();
        builder.AppendLine("CRAP Report");
        builder.AppendLine("===========");
        builder.AppendLine(header);
        builder.AppendLine(new string('-', header.Length));
        foreach (var row in rows)
            builder.AppendLine(Format(row));
        return builder.ToString();
    }

    public static string Json(IReadOnlyList<MethodMetrics> metrics, string root, double threshold)
    {
        var max = CrapAnalyzer.MaxCrap(metrics);
        var report = new
        {
            threshold,
            maxCrap = Math.Round(max, 2),
            exceeded = max > threshold,
            methods = CrapAnalyzer.Sort(metrics).Select(m => new
            {
                method = m.Method.Name,
                @class = m.Method.ClassName,
                @namespace = m.Method.Namespace,
                file = RelativePath(m.Method.FilePath, root),
                line = m.Method.Line,
                complexity = m.Method.Complexity,
                coverage = m.CoveragePercent is { } c ? Math.Round(c, 2) : (double?)null,
                crap = m.Crap is { } s ? Math.Round(s, 2) : (double?)null,
            }),
        };
        return JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;
    }

    public static string FormatScore(double score) => score.ToString("0.0#", Invariant);

    private static string Location(MethodDescriptor method, string root) =>
        $"{RelativePath(method.FilePath, root)}:{method.Line}";

    // Forward slashes on every OS, so reports and JSON compare equal across machines.
    private static string RelativePath(string path, string root) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
