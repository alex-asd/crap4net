namespace Crap4Net.Tests;

internal sealed class TempDirectory : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("crap4net-tests-").FullName;

    public string this[string relative] => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public string Write(string relative, string content = "")
    {
        var path = this[relative];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal sealed class FakeCommandRunner : ICommandRunner
{
    public List<CommandSpec> Commands { get; } = [];

    public Func<CommandSpec, CommandResult> OnCapture { get; set; } = _ => new CommandResult(0, "", "");

    public Func<CommandSpec, int> OnStream { get; set; } = _ => 0;

    public CommandResult Capture(CommandSpec command)
    {
        Commands.Add(command);
        return OnCapture(command);
    }

    public int Stream(CommandSpec command, TextWriter output)
    {
        Commands.Add(command);
        return OnStream(command);
    }
}

internal static class Fixtures
{
    public const string AppProject = """<Project Sdk="Microsoft.NET.Sdk" />""";

    public static string TestProject(string reference, params string[] packages) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            {string.Join("\n    ", packages.DefaultIfEmpty("xunit.v3").Select(p => $"<PackageReference Include=\"{p}\" Version=\"1.0.0\" />"))}
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="{reference}" />
          </ItemGroup>
        </Project>
        """;

    public const string MtpGlobalJson = """{ "test": { "runner": "Microsoft.Testing.Platform" } }""";

    public static string Cobertura(string filename, params (int Line, int Hits)[] lines) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <coverage>
          <packages>
            <package name="App">
              <classes>
                <class name="App.C" filename="{filename}">
                  <lines>{string.Concat(lines.Select(l => $"<line number=\"{l.Line}\" hits=\"{l.Hits}\" />"))}</lines>
                </class>
              </classes>
            </package>
          </packages>
        </coverage>
        """;

    /// <summary>The value following <paramref name="option"/> in a command's arguments.</summary>
    public static string ArgumentAfter(CommandSpec command, string option) =>
        command.Arguments[command.Arguments.ToList().IndexOf(option) + 1];
}
