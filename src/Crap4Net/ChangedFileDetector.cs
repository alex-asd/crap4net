namespace Crap4Net;

/// <summary>Lists modified, added, renamed and untracked files according to git status.</summary>
internal sealed class ChangedFileDetector(ICommandRunner runner)
{
    public IReadOnlyList<string> Detect(string directory)
    {
        var topLevel = runner.Capture(new CommandSpec("git", ["rev-parse", "--show-toplevel"], directory));
        if (topLevel.ExitCode != 0)
            throw new CliUsageException($"--changed needs a git repository, but {directory} is not inside one.");
        var repositoryRoot = topLevel.StandardOutput.Trim();

        var status = runner.Capture(new CommandSpec(
            "git", ["status", "--porcelain=v1", "-z", "--untracked-files=all"], directory));
        if (status.ExitCode != 0)
            throw new CrapFailureException($"git status failed: {status.StandardError.Trim()}");

        return ParsePorcelain(status.StandardOutput)
            .Select(relative => Path.GetFullPath(Path.Combine(repositoryRoot, relative)))
            .ToList();
    }

    /// <summary>
    /// Parses `git status --porcelain=v1 -z`: NUL-separated "XY path" entries, where a rename or
    /// copy is followed by an extra entry holding the original path. Deleted files are dropped.
    /// </summary>
    internal static IEnumerable<string> ParsePorcelain(string output)
    {
        var entries = output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4)
                continue;
            var (index, worktree) = (entry[0], entry[1]);
            if (index is 'R' or 'C')
                i++; // skip the original path
            if (index == 'D' || worktree == 'D')
                continue;
            yield return entry[3..];
        }
    }
}
