using System.Text.Json;

namespace Crap4Net.Tests;

public class CliApplicationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeCommandRunner _runner = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public void Dispose() => _temp.Dispose();

    private int Run(params string[] args) =>
        new CliApplication(_temp.Root, _out, _err, _runner).Execute(args);

    /// <summary>An app whose only method has CC 4 and is half covered: CRAP = 16 × 0.125 + 4 = 6.</summary>
    private string WriteHalfCoveredApp()
    {
        _temp.Write("App/App.csproj", Fixtures.AppProject);
        var source = _temp.Write("App/Calc.cs", """
            class Calc
            {
                int Sign(int x)
                {
                    if (x > 0 && x < 100 || x == -1)
                        return 1;
                    return 0;
                }
            }
            """);
        _temp.Write("coverage.xml", Fixtures.Cobertura(source, (4, 1), (5, 1), (6, 0), (7, 0)));
        return source;
    }

    [Fact]
    public void Help_prints_usage()
    {
        Assert.Equal(ExitCodes.Success, Run("--help"));
        Assert.Contains("Usage:", _out.ToString());
    }

    [Fact]
    public void Bad_usage_exits_1_with_usage_on_stderr()
    {
        Assert.Equal(ExitCodes.Usage, Run("--nope"));
        Assert.Contains("Unknown option: --nope", _err.ToString());
        Assert.Contains("Usage:", _err.ToString());
        Assert.Empty(_out.ToString());
    }

    [Fact]
    public void Missing_path_exits_1()
    {
        Assert.Equal(ExitCodes.Usage, Run("Missing.cs"));
        Assert.Contains("No such file or directory: Missing.cs", _err.ToString());
    }

    [Fact]
    public void Nothing_to_analyze_succeeds_without_running_tests()
    {
        Assert.Equal(ExitCodes.Success, Run());
        Assert.Equal("No C# files to analyze.\n", _out.ToString());

        _temp.Write("Models.cs", "record Point(int X, int Y);");
        _out.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, Run());
        Assert.Equal("No methods to analyze.\n", _out.ToString());
        Assert.Empty(_runner.Commands);
    }

    [Fact]
    public void Reports_and_passes_under_the_threshold()
    {
        WriteHalfCoveredApp();

        Assert.Equal(ExitCodes.Success, Run("--coverage-file", "coverage.xml"));
        Assert.Contains("Sign    Calc    4  50.0%   6.0  App/Calc.cs:3", _out.ToString());
        Assert.Empty(_err.ToString());
    }

    [Fact]
    public void Exits_2_when_the_threshold_is_exceeded()
    {
        WriteHalfCoveredApp();

        Assert.Equal(ExitCodes.ThresholdExceeded, Run("--coverage-file", "coverage.xml", "--threshold", "5.5"));
        Assert.Equal("CRAP threshold exceeded: 6.0 > 5.5" + Environment.NewLine, _err.ToString());
    }

    [Fact]
    public void Json_output_is_pure_json()
    {
        WriteHalfCoveredApp();

        Assert.Equal(ExitCodes.Success, Run("--json", "--coverage-file", "coverage.xml"));
        using var json = JsonDocument.Parse(_out.ToString());
        Assert.Equal(6, json.RootElement.GetProperty("maxCrap").GetDouble());
    }

    [Fact]
    public void Without_coverage_methods_report_na_and_pass()
    {
        WriteHalfCoveredApp();

        Assert.Equal(ExitCodes.Success, Run("--coverage-file", "missing.xml"));
        Assert.Contains("N/A   N/A", _out.ToString());
    }

    [Fact]
    public void Failing_tests_exit_3()
    {
        WriteHalfCoveredApp();
        _temp.Write("App.Tests/App.Tests.csproj", Fixtures.TestProject("../App/App.csproj"));
        _runner.OnStream = _ => 1;

        Assert.Equal(ExitCodes.Failure, Run());
        Assert.Contains("dotnet test failed for App.Tests", _err.ToString());
    }
}
