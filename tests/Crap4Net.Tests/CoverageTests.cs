using System.Xml.Linq;

namespace Crap4Net.Tests;

public class CoverageDataTests
{
    private const string File = "/src/App/C.cs";

    [Fact]
    public void Fraction_of_coverable_lines_in_the_range_that_ran()
    {
        var data = new CoverageData();
        data.AddLine(File, 9, 0);
        data.AddLine(File, 10, 3);
        data.AddLine(File, 11, 0);
        data.AddLine(File, 13, 1);
        data.AddLine(File, 14, 0);
        data.AddLine(File, 20, 0);

        Assert.Equal(2.0 / 4, data.CoverageFor(File, 10, 14));
    }

    [Fact]
    public void Unknown_file_or_no_coverable_lines_means_no_coverage()
    {
        var data = new CoverageData();
        data.AddLine(File, 10, 1);

        Assert.Null(data.CoverageFor("/src/App/Other.cs", 1, 100));
        Assert.Null(data.CoverageFor(File, 11, 20));
    }

    [Fact]
    public void A_line_hit_in_any_report_counts_as_covered()
    {
        var first = new CoverageData();
        first.AddLine(File, 1, 0);
        first.AddLine(File, 2, 5);
        var second = new CoverageData();
        second.AddLine(File, 1, 2);
        second.AddLine(File, 2, 0);

        first.Merge(second);

        Assert.Equal(1.0, first.CoverageFor(File, 1, 2));
    }

    [Fact]
    public void Normalizes_paths()
    {
        var data = new CoverageData();
        data.AddLine("/src/App/../App/C.cs", 1, 1);

        Assert.Equal(1.0, data.CoverageFor(File, 1, 1));
    }
}

public class CoberturaParserTests
{
    private static CoverageData Parse(string xml, string reportDirectory = "/reports") =>
        CoberturaParser.Parse(XDocument.Parse(xml), reportDirectory);

    [Fact]
    public void Reads_line_hits_for_absolute_filenames()
    {
        var data = Parse(Fixtures.Cobertura("/src/App/C.cs", (1, 1), (2, 0), (3, 0), (4, 7)));

        Assert.Equal(0.5, data.CoverageFor("/src/App/C.cs", 1, 4));
    }

    [Fact]
    public void Resolves_relative_filenames_against_sources()
    {
        using var temp = new TempDirectory();
        var existing = temp.Write("b/C.cs");
        var data = Parse($"""
            <coverage>
              <sources><source>/nowhere/a</source><source>{temp["b"]}</source></sources>
              <packages><package><classes>
                <class name="C" filename="C.cs"><lines><line number="1" hits="1" /></lines></class>
              </classes></package></packages>
            </coverage>
            """);

        Assert.Equal(1.0, data.CoverageFor(existing, 1, 1));
    }

    [Fact]
    public void Without_sources_relative_filenames_resolve_against_the_report()
    {
        var data = Parse(Fixtures.Cobertura("src/C.cs", (1, 1)), reportDirectory: "/reports");

        Assert.Equal(1.0, data.CoverageFor("/reports/src/C.cs", 1, 1));
    }

    [Fact]
    public void Merges_lines_listed_under_methods_and_under_generated_classes()
    {
        var data = Parse("""
            <coverage><packages><package><classes>
              <class name="App.C" filename="/src/C.cs">
                <methods><method name="RunAsync"><lines><line number="5" hits="0" /></lines></method></methods>
                <lines><line number="5" hits="0" /></lines>
              </class>
              <class name="App.C.&lt;RunAsync&gt;d__3" filename="/src/C.cs">
                <methods><method name="MoveNext"><lines><line number="5" hits="2" /><line number="6" hits="0" /></lines></method></methods>
              </class>
            </classes></package></packages></coverage>
            """);

        Assert.Equal(0.5, data.CoverageFor("/src/C.cs", 5, 6));
    }

    [Fact]
    public void Malformed_report_is_a_failure()
    {
        using var temp = new TempDirectory();
        var report = temp.Write("bad.xml", "<coverage><oops>");

        var error = Assert.Throws<CrapFailureException>(() => CoberturaParser.ParseFile(report));
        Assert.Contains("bad.xml", error.Message);
    }
}
