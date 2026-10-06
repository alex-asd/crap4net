namespace Crap4Net.Tests;

public class ProjectInfoTests
{
    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit.v3" /></ItemGroup></Project>""", true)]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" /></ItemGroup></Project>""", true)]
    [InlineData("""<Project Sdk="MSTest.Sdk/3.6.0" />""", true)]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>""", true)]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup><ItemGroup><PackageReference Include="NUnit" /></ItemGroup></Project>""", false)]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit.v3.assert" /></ItemGroup></Project>""", false)]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk.Web" />""", false)]
    public void Detects_test_projects(string xml, bool expected)
    {
        using var temp = new TempDirectory();

        Assert.Equal(expected, ProjectInfo.Load(temp.Write("P/P.csproj", xml)).IsTestProject);
    }

    [Fact]
    public void Resolves_project_references_and_reads_old_style_projects()
    {
        using var temp = new TempDirectory();
        var project = ProjectInfo.Load(temp.Write("tests/T/T.csproj", """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <PackageReference Include="NUnit" Version="4.0.0" />
                <ProjectReference Include="..\..\src\App\App.csproj" />
                <ProjectReference Include="$(RepoRoot)\Generated.csproj" />
              </ItemGroup>
            </Project>
            """));

        Assert.True(project.IsTestProject);
        Assert.True(project.References("nunit"));
        Assert.Equal([temp["src/App/App.csproj"]], project.ProjectReferences);
    }

    [Fact]
    public void Owner_is_the_nearest_project_above_the_file()
    {
        using var temp = new TempDirectory();
        temp.Write("App/App.csproj", Fixtures.AppProject);
        temp.Write("App/Tests/Tests.csproj", Fixtures.TestProject("../App.csproj"));
        var locator = new ProjectLocator();

        Assert.Equal(temp["App/App.csproj"], locator.OwnerOf(temp.Write("App/Deep/Er/A.cs"))?.FilePath);
        Assert.Equal(temp["App/Tests/Tests.csproj"], locator.OwnerOf(temp.Write("App/Tests/T.cs"))?.FilePath);
        Assert.Null(locator.OwnerOf(temp.Write("Loose/L.cs")));
    }

    [Fact]
    public void Reference_closure_is_transitive_and_survives_cycles()
    {
        using var temp = new TempDirectory();
        temp.Write("A/A.csproj", """<Project><ItemGroup><ProjectReference Include="../B/B.csproj" /></ItemGroup></Project>""");
        temp.Write("B/B.csproj", """<Project><ItemGroup><ProjectReference Include="../C/C.csproj" /></ItemGroup></Project>""");
        temp.Write("C/C.csproj", """<Project><ItemGroup><ProjectReference Include="../A/A.csproj" /></ItemGroup></Project>""");
        var locator = new ProjectLocator();

        var closure = locator.ReferenceClosure(locator.Get(temp["A/A.csproj"]));

        Assert.Equal(
            new[] { "A/A.csproj", "B/B.csproj", "C/C.csproj" }.Select(p => temp[p]).Order(),
            closure.Order());
    }
}

public class SourceFileFinderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public SourceFileFinderTests()
    {
        _temp.Write("App/App.csproj", Fixtures.AppProject);
        _temp.Write("App/Foo.cs");
        _temp.Write("App/Migrations/M.cs");
        _temp.Write("App/bin/Debug/X.cs");
        _temp.Write("App/obj/Y.cs");
        _temp.Write("App/.hidden/Z.cs");
        _temp.Write("App/notes.txt");
        _temp.Write("App.Tests/App.Tests.csproj", Fixtures.TestProject("../App/App.csproj"));
        _temp.Write("App.Tests/FooTests.cs");
    }

    public void Dispose() => _temp.Dispose();

    private SourceFileFinder Finder(params string[] excludes) => new(_temp.Root, new ProjectLocator(), excludes);

    private string[] Paths(params string[] relative) => relative.Select(r => _temp[r]).ToArray();

    [Fact]
    public void Finds_sources_but_not_build_output_hidden_folders_or_tests() =>
        Assert.Equal(Paths("App/Foo.cs", "App/Migrations/M.cs"), Finder().FindAll());

    [Fact]
    public void Honors_exclude_globs() =>
        Assert.Equal(Paths("App/Foo.cs"), Finder("App/Migrations/**").FindAll());

    [Fact]
    public void Expands_directories_and_keeps_explicit_files_even_in_test_projects() =>
        Assert.Equal(
            Paths("App.Tests/FooTests.cs", "App/Foo.cs", "App/Migrations/M.cs"),
            Finder().Select(["App", "App.Tests/FooTests.cs", "App/Foo.cs"]));

    [Fact]
    public void A_test_project_directory_expands_to_nothing() =>
        Assert.Empty(Finder().Select(["App.Tests"]));

    [Fact]
    public void Missing_path_is_a_usage_error() =>
        Assert.Throws<CliUsageException>(() => Finder().Select(["Nope"]));

    [Fact]
    public void Filters_changed_files_like_directory_expansion()
    {
        var changed = Paths("App/Foo.cs", "App/bin/Debug/X.cs", "App.Tests/FooTests.cs", "App/notes.txt", "App/Deleted.cs")
            .Append(Path.Combine(Path.GetTempPath(), "Elsewhere.cs"));

        Assert.Equal(Paths("App/Foo.cs"), Finder().FilterChanged(changed));
    }
}
