namespace Crap4Net.Tests;

public class ChangedFileDetectorTests
{
    [Fact]
    public void Parses_modified_added_untracked_and_renamed_entries_but_not_deletions()
    {
        const string output = " M src/A.cs\0A  src/B.cs\0?? new dir/C.cs\0R  src/Renamed.cs\0src/Old.cs\0D  src/Gone.cs\0 D src/Gone2.cs\0MM src/D.cs\0";

        Assert.Equal(
            ["src/A.cs", "src/B.cs", "new dir/C.cs", "src/Renamed.cs", "src/D.cs"],
            ChangedFileDetector.ParsePorcelain(output));
    }

    [Fact]
    public void Resolves_paths_against_the_repository_root()
    {
        var runner = new FakeCommandRunner
        {
            OnCapture = c => c.Arguments[0] == "rev-parse"
                ? new CommandResult(0, "/repo\n", "")
                : new CommandResult(0, " M app/A.cs\0", ""),
        };

        var files = new ChangedFileDetector(runner).Detect("/repo/app");

        Assert.Equal([Path.GetFullPath("/repo/app/A.cs")], files);
        Assert.All(runner.Commands, c => Assert.Equal("/repo/app", c.WorkingDirectory));
        Assert.Equal(["status", "--porcelain=v1", "-z", "--untracked-files=all"], runner.Commands[1].Arguments);
    }

    [Fact]
    public void Outside_a_repository_is_a_usage_error()
    {
        var runner = new FakeCommandRunner { OnCapture = _ => new CommandResult(128, "", "fatal: not a git repository") };

        Assert.Throws<CliUsageException>(() => new ChangedFileDetector(runner).Detect("/tmp"));
    }
}
