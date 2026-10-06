using Microsoft.Extensions.FileSystemGlobbing;

namespace Crap4Net;

/// <summary>
/// Picks the .cs files to analyze. Directory expansion and --changed both skip build output,
/// test projects, and --exclude matches; a file named explicitly is always analyzed.
/// </summary>
internal sealed class SourceFileFinder
{
    private readonly string _root;
    private readonly ProjectLocator _projects;
    private readonly Matcher? _excludes;

    public SourceFileFinder(string root, ProjectLocator projects, IReadOnlyList<string> excludes)
    {
        _root = Path.GetFullPath(root);
        _projects = projects;
        if (excludes.Count > 0)
        {
            _excludes = new Matcher(CoverageData.PathComparison);
            _excludes.AddIncludePatterns(excludes);
        }
    }

    public IReadOnlyList<string> FindAll() => Sorted(Expand(_root));

    public IReadOnlyList<string> Select(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var argument in paths)
        {
            var path = Path.GetFullPath(argument, _root);
            if (File.Exists(path))
                files.Add(path);
            else if (Directory.Exists(path))
                files.AddRange(Expand(path));
            else
                throw new CliUsageException($"No such file or directory: {argument}");
        }
        return Sorted(files);
    }

    /// <summary>Keeps the changed files that expanding the root directory would have picked.</summary>
    public IReadOnlyList<string> FilterChanged(IEnumerable<string> changedFiles) => Sorted(changedFiles
        .Select(f => Path.GetFullPath(f))
        .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(f))
        .Where(f => IsUnderRoot(f) && !IsInSkippedDirectory(f) && Accept(f)));

    private IEnumerable<string> Expand(string directory) =>
        SourceTree.EnumerateFiles(directory, "*.cs").Where(Accept);

    private bool Accept(string file) =>
        _projects.OwnerOf(file) is not { IsTestProject: true } && !IsExcluded(file);

    private bool IsExcluded(string file) =>
        _excludes is not null && IsUnderRoot(file) && _excludes.Match(_root, file).HasMatches;

    private bool IsUnderRoot(string file) =>
        file.StartsWith(Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar, CoverageData.PathComparison);

    private bool IsInSkippedDirectory(string file) =>
        Path.GetRelativePath(_root, Path.GetDirectoryName(file)!)
            .Split(Path.DirectorySeparatorChar)
            .Any(segment => segment != "." && SourceTree.IsSkipped(segment));

    private static IReadOnlyList<string> Sorted(IEnumerable<string> files) =>
        files.Distinct(CoverageData.PathComparer).Order(StringComparer.Ordinal).ToList();
}
