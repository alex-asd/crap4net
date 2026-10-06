namespace Crap4Net.Tests;

/// <summary>Runs real processes through the platform shell.</summary>
public class ProcessCommandRunnerTests
{
    private static readonly string WorkingDirectory = Path.GetTempPath();

    private static CommandSpec Shell(string command) => CommandSpec.Shell(command, WorkingDirectory);

    [Fact]
    public void Capture_returns_the_exit_code_and_both_streams()
    {
        var result = new ProcessCommandRunner().Capture(Shell("echo out&& echo err 1>&2&& exit 3"));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out", result.StandardOutput.Trim());
        Assert.Equal("err", result.StandardError.Trim());
    }

    [Fact]
    public void Stream_copies_both_streams_as_lines()
    {
        var output = new StringWriter();

        var exitCode = new ProcessCommandRunner().Stream(Shell("echo one&& echo two 1>&2"), output);

        Assert.Equal(0, exitCode);
        Assert.Equal(["one", "two"], output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Order());
    }

    [Fact]
    public void A_missing_program_is_a_failure()
    {
        var command = new CommandSpec("crap4net-no-such-program", [], WorkingDirectory);

        var error = Assert.Throws<CrapFailureException>(() => new ProcessCommandRunner().Capture(command));
        Assert.Contains("crap4net-no-such-program", error.Message);
    }

    [Fact]
    public void Display_form_quotes_arguments_with_spaces() =>
        Assert.Equal(
            "dotnet test --collect \"XPlat Code Coverage\"",
            new CommandSpec("dotnet", ["test", "--collect", "XPlat Code Coverage"], WorkingDirectory).ToString());
}
