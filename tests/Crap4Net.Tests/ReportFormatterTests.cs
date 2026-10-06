using System.Globalization;
using System.Text.Json;

namespace Crap4Net.Tests;

public class ReportFormatterTests
{
    private const string Root = "/repo";

    private static MethodMetrics Metric(string name, int complexity, double? coverage, int line = 1) => new(
        new MethodDescriptor(name, "Cls", "Ns", "/repo/src/A.cs", line, line, line + 1, complexity),
        coverage * 100,
        coverage is { } c ? CrapScore.Compute(complexity, c) : null);

    private static readonly IReadOnlyList<MethodMetrics> Metrics =
    [
        Metric("Simple", 1, 1.0, line: 3),
        Metric("Unknown", 3, null, line: 9),
        Metric("Complex", 12, 0.45, line: 20),
    ];

    [Fact]
    public void Text_report_is_sorted_worst_first_with_unknown_coverage_last()
    {
        var expected = """
            CRAP Report
            ===========
            Method   Class  CC    Cov%  CRAP  Location
            ------------------------------------------
            Complex  Cls    12   45.0%  36.0  src/A.cs:20
            Simple   Cls     1  100.0%   1.0  src/A.cs:3
            Unknown  Cls     3     N/A   N/A  src/A.cs:9

            """.ReplaceLineEndings();

        Assert.Equal(expected, ReportFormatter.Text(Metrics, Root));
    }

    [Fact]
    public void Json_report_carries_the_threshold_verdict_and_every_method()
    {
        using var json = JsonDocument.Parse(ReportFormatter.Json(Metrics, Root, threshold: 8));
        var root = json.RootElement;

        Assert.Equal(8, root.GetProperty("threshold").GetDouble());
        Assert.Equal(35.96, root.GetProperty("maxCrap").GetDouble());
        Assert.True(root.GetProperty("exceeded").GetBoolean());
        var methods = root.GetProperty("methods").EnumerateArray().ToList();
        Assert.Equal(["Complex", "Simple", "Unknown"], methods.Select(m => m.GetProperty("method").GetString()));
        var first = methods[0];
        Assert.Equal(("Cls", "Ns", "src/A.cs", 20, 12), (
            first.GetProperty("class").GetString(),
            first.GetProperty("namespace").GetString(),
            first.GetProperty("file").GetString(),
            first.GetProperty("line").GetInt32(),
            first.GetProperty("complexity").GetInt32()));
        Assert.Equal(45, first.GetProperty("coverage").GetDouble());
        Assert.Equal(JsonValueKind.Null, methods[2].GetProperty("crap").ValueKind);
    }

    [Fact]
    public void Numbers_use_a_decimal_point_whatever_the_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Contains("45.0%  36.0", ReportFormatter.Text(Metrics, Root));
            Assert.Equal("8.5", ReportFormatter.FormatScore(8.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
