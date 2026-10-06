using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Crap4Net;

/// <summary>
/// Produces fresh coverage for the analyzed files: either by running every test project that
/// (transitively) references their projects, or by running --coverage-command, or by reading
/// existing --coverage-file reports.
/// </summary>
internal sealed class CoverageCollector(ICommandRunner runner, ProjectLocator projects, string root, TextWriter log)
{
    public const string ReportFileName = "coverage.cobertura.xml";

    public CoverageData Collect(CliOptions options, IReadOnlyList<string> sourceFiles)
    {
        if (options.CoverageCommand is { } command)
        {
            // A failed command must not leave last run's report looking current.
            foreach (var stale in options.CoverageFiles.SelectMany(ExpandReportPattern))
                File.Delete(stale);
            Log(command);
            var exitCode = runner.Stream(CommandSpec.Shell(command, root), log);
            if (exitCode != 0)
                throw new CrapFailureException($"Coverage command exited with code {exitCode}: {command}");
        }

        return options.CoverageFiles.Count > 0 ? LoadReports(options.CoverageFiles) : RunTests(sourceFiles);
    }

    internal CommandSpec TestCommand(ProjectInfo project, string resultsDirectory)
    {
        if (UsesTestingPlatform(project.Directory))
        {
            if (!project.References("Microsoft.Testing.Extensions.CodeCoverage"))
                throw new CrapFailureException(
                    $"{project.Name} runs on Microsoft.Testing.Platform but doesn't reference " +
                    "Microsoft.Testing.Extensions.CodeCoverage. Add that package, or produce a Cobertura " +
                    "report yourself and pass it with --coverage-file (and --coverage-command).");
            return new CommandSpec("dotnet",
            [
                "test", "--project", project.FilePath, "--results-directory", resultsDirectory,
                "--coverage", "--coverage-output-format", "cobertura", "--coverage-output", ReportFileName,
            ], project.Directory);
        }

        var collector = project.References("coverlet.collector") ? "XPlat Code Coverage" : "Code Coverage;Format=cobertura";
        return new CommandSpec("dotnet",
            ["test", project.FilePath, "--collect", collector, "--results-directory", resultsDirectory],
            project.Directory);
    }

    private CoverageData RunTests(IReadOnlyList<string> sourceFiles)
    {
        var targets = sourceFiles
            .Select(projects.OwnerOf)
            .OfType<ProjectInfo>()
            .DistinctBy(p => p.FilePath, CoverageData.PathComparer)
            .ToList();
        if (targets.Count == 0)
        {
            Log("warning: no .csproj contains the analyzed files, so coverage is N/A.");
            return new CoverageData();
        }

        var testProjects = TestProjectsCovering(targets);
        WarnAboutUntested(targets, testProjects);

        var data = new CoverageData();
        if (testProjects.Count == 0)
            return data;

        var output = Directory.CreateTempSubdirectory("crap4net-");
        try
        {
            foreach (var (project, index) in testProjects.Select((t, i) => (t.Project, i)))
                data.Merge(RunTestProject(project, Path.Combine(output.FullName, $"{index}-{project.Name}")));
            return data;
        }
        finally
        {
            DeleteQuietly(output);
        }
    }

    /// <summary>Test projects whose reference closure includes a target, with the targets each covers.</summary>
    private List<(ProjectInfo Project, IReadOnlySet<string> Covers)> TestProjectsCovering(IReadOnlyList<ProjectInfo> targets)
    {
        var targetPaths = targets.Select(t => t.FilePath).ToHashSet(CoverageData.PathComparer);
        return SearchRoots(targets)
            .SelectMany(projects.FindUnder)
            .DistinctBy(p => p.FilePath, CoverageData.PathComparer)
            .Where(p => p.IsTestProject)
            .Select(p => (Project: p, Covers: (IReadOnlySet<string>)projects.ReferenceClosure(p)
                .Intersect(targetPaths, CoverageData.PathComparer)
                .ToHashSet(CoverageData.PathComparer)))
            .Where(t => t.Covers.Count > 0)
            .ToList();
    }

    private void WarnAboutUntested(
        IReadOnlyList<ProjectInfo> targets, List<(ProjectInfo Project, IReadOnlySet<string> Covers)> testProjects)
    {
        var untested = targets
            .Where(t => !testProjects.Any(tp => tp.Covers.Contains(t.FilePath)))
            .Select(t => t.Name)
            .ToList();
        if (untested.Count > 0)
            Log($"warning: no test project references {string.Join(", ", untested)}, so its coverage is N/A.");
    }

    private CoverageData RunTestProject(ProjectInfo project, string resultsDirectory)
    {
        var command = TestCommand(project, resultsDirectory);
        Log(command.ToString());
        var exitCode = runner.Stream(command, log);
        if (exitCode != 0)
            throw new CrapFailureException(
                $"dotnet test failed for {project.Name} (exit code {exitCode}); CRAP needs a passing test run.");

        var reports = Directory.Exists(resultsDirectory)
            ? Directory.GetFiles(resultsDirectory, "*.cobertura.xml", SearchOption.AllDirectories)
            : [];
        if (reports.Length == 0)
            Log($"warning: {project.Name} produced no Cobertura report, so its coverage is N/A.");

        var data = new CoverageData();
        foreach (var report in reports)
            data.Merge(CoberturaParser.ParseFile(report));
        return data;
    }

    private static void DeleteQuietly(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Leftover temp files are harmless.
        }
    }

    private CoverageData LoadReports(IReadOnlyList<string> patterns)
    {
        var data = new CoverageData();
        foreach (var pattern in patterns)
        {
            var reports = ExpandReportPattern(pattern).ToList();
            if (reports.Count == 0)
                Log($"warning: no coverage report matches {pattern}.");
            foreach (var report in reports)
                data.Merge(CoberturaParser.ParseFile(report));
        }
        if (data.IsEmpty)
            Log("warning: no coverage data was read, so coverage is N/A.");
        return data;
    }

    /// <summary>Resolves a report path, which may contain * and ** globs, against the root.</summary>
    private IEnumerable<string> ExpandReportPattern(string pattern)
    {
        var full = Path.GetFullPath(pattern, root);
        var wildcard = full.IndexOfAny(['*', '?']);
        if (wildcard < 0)
            return File.Exists(full) ? [full] : [];

        var baseEnd = full.LastIndexOf(Path.DirectorySeparatorChar, wildcard);
        var baseDirectory = full[..Math.Max(baseEnd, 1)];
        if (!Directory.Exists(baseDirectory))
            return [];
        var matcher = new Matcher(CoverageData.PathComparison);
        matcher.AddInclude(full[(baseEnd + 1)..]);
        return matcher.GetResultsInFullPath(baseDirectory).Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// Where to look for test projects: the directory crap4net runs in, plus the nearest directory
    /// above each target that holds a solution or is a git root (never climbing to the home directory).
    /// </summary>
    private IEnumerable<string> SearchRoots(IEnumerable<ProjectInfo> targets)
    {
        var roots = targets.Select(t => SearchRoot(t.Directory))
            .Append(Path.GetFullPath(root))
            .Distinct(CoverageData.PathComparer)
            .ToList();
        return roots.Where(r => !roots.Any(other => !string.Equals(other, r, CoverageData.PathComparison) && IsInside(r, other)));
    }

    private static string SearchRoot(string start)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (var directory = start; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (string.Equals(directory, home, CoverageData.PathComparison))
                break;
            if (Directory.EnumerateFiles(directory, "*.sln").Any()
                || Directory.EnumerateFiles(directory, "*.slnx").Any()
                || Path.Exists(Path.Combine(directory, ".git")))
                return directory;
        }
        return start;
    }

    private static bool IsInside(string path, string directory) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, CoverageData.PathComparison);

    /// <summary>
    /// Since the .NET 10 SDK, a global.json with "test": { "runner": "Microsoft.Testing.Platform" }
    /// switches dotnet test from VSTest to MTP, which takes different arguments.
    /// </summary>
    private static bool UsesTestingPlatform(string projectDirectory) =>
        NearestGlobalJson(projectDirectory) is { } globalJson && SelectsTestingPlatform(globalJson);

    // The SDK only reads the nearest global.json, so a closer one without a test section means VSTest.
    private static string? NearestGlobalJson(string directory)
    {
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
        {
            var candidate = Path.Combine(current, "global.json");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static bool SelectsTestingPlatform(string globalJson)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(globalJson), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return document.RootElement.TryGetProperty("test", out var test)
                   && test.ValueKind == JsonValueKind.Object
                   && test.TryGetProperty("runner", out var runnerName)
                   && string.Equals(runnerName.ToString(), "Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void Log(string message) => log.WriteLine($"crap4net: {message}");
}
