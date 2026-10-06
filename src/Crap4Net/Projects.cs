using System.Xml.Linq;

namespace Crap4Net;

internal sealed record ProjectInfo(
    string FilePath,
    bool IsTestProject,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlySet<string> PackageReferences)
{
    // Packages that only a test project references; a library referencing xunit.v3.assert alone doesn't count.
    private static readonly HashSet<string> TestPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Test.Sdk", "xunit", "xunit.core", "xunit.v3", "xunit.v3.core",
        "NUnit", "MSTest", "MSTest.TestFramework", "TUnit", "TUnit.Engine",
    };

    public string Directory => Path.GetDirectoryName(FilePath)!;

    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    public bool References(string package) => PackageReferences.Contains(package);

    public static ProjectInfo Load(string csprojPath)
    {
        var path = Path.GetFullPath(csprojPath);
        var xml = ReadXml(path);
        var packages = Includes(xml, "PackageReference").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directory = Path.GetDirectoryName(path)!;
        var references = Includes(xml, "ProjectReference")
            .Where(include => !include.Contains("$("))
            .Select(include => Path.GetFullPath(Path.Combine(directory, include.Replace('\\', Path.DirectorySeparatorChar))))
            .ToList();
        return new ProjectInfo(path, IsTest(xml, packages), references, packages);
    }

    private static XDocument ReadXml(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch (System.Xml.XmlException e)
        {
            throw new CrapFailureException($"Cannot read project {path}: {e.Message}");
        }
    }

    /// <summary>An explicit IsTestProject wins; otherwise the MSTest SDK or a test framework package decides.</summary>
    private static bool IsTest(XDocument xml, IReadOnlySet<string> packages)
    {
        if (bool.TryParse(Elements(xml, "IsTestProject").LastOrDefault()?.Value.Trim(), out var flag))
            return flag;
        var sdk = xml.Root?.Attribute("Sdk")?.Value ?? "";
        return sdk.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase) || packages.Overlaps(TestPackages);
    }

    private static IEnumerable<string> Includes(XDocument xml, string itemType) =>
        Elements(xml, itemType).Select(e => e.Attribute("Include")?.Value).OfType<string>();

    // Old-style projects put everything in the MSBuild namespace, so match on local names.
    private static IEnumerable<XElement> Elements(XDocument xml, string name) =>
        xml.Descendants().Where(e => e.Name.LocalName == name);
}

/// <summary>Finds and caches .csproj files on disk.</summary>
internal sealed class ProjectLocator
{
    private readonly Dictionary<string, ProjectInfo?> _ownerByDirectory = new(CoverageData.PathComparer);
    private readonly Dictionary<string, ProjectInfo> _byPath = new(CoverageData.PathComparer);

    public ProjectInfo Get(string csprojPath)
    {
        var path = Path.GetFullPath(csprojPath);
        if (!_byPath.TryGetValue(path, out var project))
            _byPath[path] = project = ProjectInfo.Load(path);
        return project;
    }

    /// <summary>The project in the nearest directory at or above the file, or null if there is none.</summary>
    public ProjectInfo? OwnerOf(string filePath) => OwnerOfDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)));

    private ProjectInfo? OwnerOfDirectory(string? directory)
    {
        if (directory is null)
            return null;
        if (_ownerByDirectory.TryGetValue(directory, out var cached))
            return cached;
        var csproj = System.IO.Directory.EnumerateFiles(directory, "*.csproj").Order(StringComparer.Ordinal).FirstOrDefault();
        var owner = csproj is not null ? Get(csproj) : OwnerOfDirectory(Path.GetDirectoryName(directory));
        _ownerByDirectory[directory] = owner;
        return owner;
    }

    public IReadOnlyList<ProjectInfo> FindUnder(string root) =>
        SourceTree.EnumerateFiles(root, "*.csproj").Select(Get).ToList();

    /// <summary>The project plus everything it references, directly or transitively.</summary>
    public IReadOnlySet<string> ReferenceClosure(ProjectInfo project)
    {
        var seen = new HashSet<string>(CoverageData.PathComparer);
        var pending = new Stack<string>([project.FilePath]);
        while (pending.TryPop(out var path))
        {
            if (!seen.Add(path) || !File.Exists(path))
                continue;
            foreach (var reference in Get(path).ProjectReferences)
                pending.Push(reference);
        }
        return seen;
    }
}

/// <summary>Recursive file enumeration that skips build output, tool caches and hidden directories.</summary>
internal static class SourceTree
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "TestResults", "packages",
    };

    public static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        var pending = new Stack<string>([Path.GetFullPath(root)]);
        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern).Order(StringComparer.Ordinal))
                yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory).OrderDescending(StringComparer.Ordinal))
            {
                if (!IsSkipped(Path.GetFileName(child)))
                    pending.Push(child);
            }
        }
    }

    public static bool IsSkipped(string directoryName) =>
        directoryName.StartsWith('.') || SkippedDirectories.Contains(directoryName);
}
