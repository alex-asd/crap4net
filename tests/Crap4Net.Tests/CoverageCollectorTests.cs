namespace Crap4Net.Tests;

public class CoverageCollectorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeCommandRunner _runner = new();
    private readonly StringWriter _log = new();
    private readonly string _source;

    public CoverageCollectorTests()
    {
        _temp.Write("All.slnx", "<Solution />");
        _temp.Write("src/App/App.csproj", Fixtures.AppProject);
        _source = _temp.Write("src/App/C.cs");
    }

    public void Dispose() => _temp.Dispose();

    private CoverageCollector Collector() => new(_runner, new ProjectLocator(), _temp.Root, _log);

    private static CliOptions Options(string[]? coverageFiles = null, string? command = null) =>
        new(CliMode.All, [], 8, [], coverageFiles ?? [], command, false);

    /// <summary>Makes every dotnet test run drop a report covering lines 1-2 of the source, half hit.</summary>
    private void TestsWriteReport() => _runner.OnStream = command =>
    {
        var directory = Fixtures.ArgumentAfter(command, "--results-directory");
        Directory.CreateDirectory(Path.Combine(directory, "guid"));
        File.WriteAllText(Path.Combine(directory, "guid", "x.cobertura.xml"), Fixtures.Cobertura(_source, (1, 1), (2, 0)));
        return 0;
    };

    [Fact]
    public void Runs_test_projects_that_reference_the_code_and_reads_their_reports()
    {
        _temp.Write("tests/App.Tests/App.Tests.csproj", Fixtures.TestProject(@"..\..\src\App\App.csproj"));
        _temp.Write("tests/Other.Tests/Other.Tests.csproj", Fixtures.TestProject("../../src/Other/Other.csproj"));
        TestsWriteReport();

        var data = Collector().Collect(Options(), [_source]);

        var command = Assert.Single(_runner.Commands);
        Assert.Equal(_temp["tests/App.Tests/App.Tests.csproj"], command.Arguments[1]);
        Assert.Equal(0.5, data.CoverageFor(_source, 1, 2));
        Assert.False(Directory.Exists(Fixtures.ArgumentAfter(command, "--results-directory")), "temp results are cleaned up");
    }

    [Fact]
    public void Follows_references_transitively()
    {
        _temp.Write("src/Api/Api.csproj", """<Project><ItemGroup><ProjectReference Include="../App/App.csproj" /></ItemGroup></Project>""");
        _temp.Write("tests/Api.Tests/Api.Tests.csproj", Fixtures.TestProject("../../src/Api/Api.csproj"));
        TestsWriteReport();

        Collector().Collect(Options(), [_source]);

        Assert.Equal(_temp["tests/Api.Tests/Api.Tests.csproj"], Assert.Single(_runner.Commands).Arguments[1]);
    }

    [Fact]
    public void Uses_microsoft_testing_platform_arguments_when_global_json_opts_in()
    {
        _temp.Write("global.json", Fixtures.MtpGlobalJson);
        var project = ProjectInfo.Load(_temp.Write("tests/T/T.csproj",
            Fixtures.TestProject("../../src/App/App.csproj", "xunit.v3", "Microsoft.Testing.Extensions.CodeCoverage")));

        var command = Collector().TestCommand(project, "/out");

        Assert.Equal("dotnet", command.FileName);
        Assert.Equal(_temp["tests/T"], command.WorkingDirectory);
        Assert.Equal(
        [
            "test", "--project", project.FilePath, "--results-directory", "/out",
            "--coverage", "--coverage-output-format", "cobertura", "--coverage-output", "coverage.cobertura.xml",
        ], command.Arguments);
    }

    [Fact]
    public void Microsoft_testing_platform_without_the_coverage_extension_is_a_failure()
    {
        _temp.Write("global.json", "// comment\n" + Fixtures.MtpGlobalJson);
        var project = ProjectInfo.Load(_temp.Write("tests/T/T.csproj", Fixtures.TestProject("../../src/App/App.csproj")));

        var error = Assert.Throws<CrapFailureException>(() => Collector().TestCommand(project, "/out"));
        Assert.Contains("Microsoft.Testing.Extensions.CodeCoverage", error.Message);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "test": "Microsoft.Testing.Platform" }""")]
    [InlineData("""{ "test": { "runner": "VSTest" } }""")]
    public void Anything_but_a_testing_platform_runner_in_global_json_means_vstest(string globalJson)
    {
        _temp.Write("global.json", globalJson);
        var project = ProjectInfo.Load(_temp.Write("tests/T/T.csproj", Fixtures.TestProject("../../src/App/App.csproj")));

        Assert.Equal(["test", project.FilePath], Collector().TestCommand(project, "/out").Arguments.Take(2));
    }

    [Theory]
    [InlineData("coverlet.collector", "XPlat Code Coverage")]
    [InlineData("Microsoft.NET.Test.Sdk", "Code Coverage;Format=cobertura")]
    public void Uses_vstest_collectors_otherwise(string package, string collector)
    {
        _temp.Write("global.json", """{ "sdk": { "version": "10.0.100" } }""");
        var project = ProjectInfo.Load(_temp.Write("tests/T/T.csproj", Fixtures.TestProject("../../src/App/App.csproj", package)));

        var command = Collector().TestCommand(project, "/out");

        Assert.Equal(["test", project.FilePath, "--collect", collector, "--results-directory", "/out"], command.Arguments);
    }

    [Fact]
    public void Failing_tests_are_a_failure()
    {
        _temp.Write("tests/T/T.csproj", Fixtures.TestProject("../../src/App/App.csproj"));
        _runner.OnStream = _ => 1;

        var error = Assert.Throws<CrapFailureException>(() => Collector().Collect(Options(), [_source]));
        Assert.Contains("dotnet test failed for T ", error.Message);
    }

    [Fact]
    public void Without_tests_coverage_is_unknown_and_says_why()
    {
        var data = Collector().Collect(Options(), [_source]);

        Assert.Empty(_runner.Commands);
        Assert.Null(data.CoverageFor(_source, 1, 2));
        Assert.Contains("no test project references App", _log.ToString());
    }

    [Fact]
    public void Reads_existing_reports_including_globs_without_running_anything()
    {
        _temp.Write("TestResults/a1/coverage.cobertura.xml", Fixtures.Cobertura(_source, (1, 1)));
        _temp.Write("TestResults/b2/coverage.cobertura.xml", Fixtures.Cobertura(_source, (2, 1)));
        _temp.Write("single.xml", Fixtures.Cobertura(_source, (3, 0)));

        var data = Collector().Collect(Options(["TestResults/**/coverage.cobertura.xml", "single.xml"]), [_source]);

        Assert.Empty(_runner.Commands);
        Assert.Equal(2.0 / 3, data.CoverageFor(_source, 1, 3));
    }

    [Fact]
    public void Missing_reports_are_a_warning()
    {
        var data = Collector().Collect(Options(["nope.xml"]), [_source]);

        Assert.True(data.IsEmpty);
        Assert.Contains("no coverage report matches nope.xml", _log.ToString());
    }

    [Fact]
    public void Coverage_command_deletes_the_stale_report_then_runs_in_a_shell()
    {
        var report = _temp.Write("out/coverage.xml", Fixtures.Cobertura(_source, (1, 0)));
        _runner.OnStream = command =>
        {
            Assert.False(File.Exists(report), "stale report is deleted before the command runs");
            File.WriteAllText(report, Fixtures.Cobertura(_source, (1, 4)));
            return 0;
        };

        var data = Collector().Collect(Options(["out/coverage.xml"], "make coverage"), [_source]);

        var command = Assert.Single(_runner.Commands);
        Assert.Equal("make coverage", command.Arguments[^1]);
        Assert.Equal(_temp.Root, command.WorkingDirectory);
        Assert.Equal(1.0, data.CoverageFor(_source, 1, 1));
    }

    [Fact]
    public void Failing_coverage_command_is_a_failure()
    {
        _runner.OnStream = _ => 2;

        Assert.Throws<CrapFailureException>(() => Collector().Collect(Options(["c.xml"], "false"), [_source]));
    }
}
