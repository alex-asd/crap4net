using System.Globalization;

namespace Crap4Net;

internal enum CliMode
{
    All,
    Changed,
    Paths,
    Help,
}

internal sealed record CliOptions(
    CliMode Mode,
    IReadOnlyList<string> Paths,
    double Threshold,
    IReadOnlyList<string> Excludes,
    IReadOnlyList<string> CoverageFiles,
    string? CoverageCommand,
    bool Json)
{
    public const double DefaultThreshold = 8.0;
}

internal static class CliArgumentsParser
{
    public const string Usage = """
        Usage:
          crap4net [options]            Analyze all C# files under the current directory
          crap4net [options] --changed  Analyze C# files that git status reports as changed
          crap4net [options] <path...>  Analyze these files, and all C# files under these directories
          crap4net --help               Print this help message

        Options:
          --threshold <n>           Exit 2 when any CRAP score exceeds n (default 8)
          --exclude <glob>          Skip files matching the glob, relative to the current directory (repeatable)
          --coverage-file <path>    Read this Cobertura report instead of running the tests; globs allowed (repeatable)
          --coverage-command <cmd>  Run cmd to produce the --coverage-file report(s) instead of running dotnet test
          --json                    Print the report as JSON

        Test projects (ones that reference the analyzed projects) run under dotnet test with coverage on.
        Test projects themselves, bin/, obj/ and generated code are never analyzed.

        Exit codes: 0 ok, 1 usage error, 2 threshold exceeded, 3 analysis failed (e.g. a test failed)

        """;

    private static readonly Dictionary<string, Action<Builder>> Flags = new()
    {
        ["--changed"] = b => b.Changed = true,
        ["--json"] = b => b.Json = true,
    };

    private static readonly Dictionary<string, Action<Builder, string>> ValueOptions = new()
    {
        ["--threshold"] = (b, value) => b.Threshold = ParseThreshold(value),
        ["--exclude"] = (b, value) => b.Excludes.Add(value),
        ["--coverage-file"] = (b, value) => b.CoverageFiles.Add(value),
        ["--coverage-command"] = (b, value) => b.CoverageCommand = value,
    };

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        if (WantsHelp(args))
            return new CliOptions(CliMode.Help, [], CliOptions.DefaultThreshold, [], [], null, false);

        var builder = new Builder();
        for (var i = 0; i < args.Count; i++)
            i = Apply(builder, args, i);
        return builder.Build();
    }

    private static bool WantsHelp(IReadOnlyList<string> args) =>
        args.TakeWhile(a => a != "--").Any(a => a is "--help" or "-h");

    /// <summary>Applies the argument at <paramref name="index"/>; returns the index of the last argument it used.</summary>
    private static int Apply(Builder builder, IReadOnlyList<string> args, int index)
    {
        var (name, inlineValue) = SplitOption(args[index]);
        if (name == "--")
        {
            builder.Paths.AddRange(args.Skip(index + 1));
            return args.Count;
        }

        if (Flags.TryGetValue(name, out var flag))
            flag(builder);
        else if (ValueOptions.TryGetValue(name, out var option))
            option(builder, inlineValue ?? ValueAt(args, ++index, name));
        else if (name.StartsWith('-'))
            throw new CliUsageException($"Unknown option: {name}");
        else
            builder.Paths.Add(args[index]);
        return index;
    }

    private static string ValueAt(IReadOnlyList<string> args, int index, string option) =>
        index < args.Count ? args[index] : throw new CliUsageException($"{option} needs a value.");

    private static (string Name, string? InlineValue) SplitOption(string arg)
    {
        var equals = arg.IndexOf('=');
        return arg.StartsWith("--") && equals > 0 ? (arg[..equals], arg[(equals + 1)..]) : (arg, null);
    }

    private static double ParseThreshold(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) && threshold >= 0
            ? threshold
            : throw new CliUsageException($"--threshold must be a non-negative number, got '{value}'.");

    private sealed class Builder
    {
        public List<string> Paths { get; } = [];
        public List<string> Excludes { get; } = [];
        public List<string> CoverageFiles { get; } = [];
        public string? CoverageCommand { get; set; }
        public double Threshold { get; set; } = CliOptions.DefaultThreshold;
        public bool Changed { get; set; }
        public bool Json { get; set; }

        public CliOptions Build()
        {
            if (Changed && Paths.Count > 0)
                throw new CliUsageException("--changed cannot be combined with paths.");
            if (CoverageCommand is not null && CoverageFiles.Count == 0)
                throw new CliUsageException("--coverage-command needs --coverage-file to say where the report lands.");

            var mode = Changed ? CliMode.Changed : Paths.Count > 0 ? CliMode.Paths : CliMode.All;
            return new CliOptions(mode, Paths, Threshold, Excludes, CoverageFiles, CoverageCommand, Json);
        }
    }
}
