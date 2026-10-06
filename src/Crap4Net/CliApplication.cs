using System.Diagnostics.CodeAnalysis;

namespace Crap4Net;

internal sealed class CliApplication(string root, TextWriter output, TextWriter error, ICommandRunner runner)
{
    public int Execute(IReadOnlyList<string> args)
    {
        if (!TryParse(args, out var options))
            return ExitCodes.Usage;
        if (options.Mode == CliMode.Help)
        {
            output.Write(CliArgumentsParser.Usage);
            return ExitCodes.Success;
        }

        try
        {
            return Run(options);
        }
        catch (CliUsageException e)
        {
            error.WriteLine(e.Message);
            return ExitCodes.Usage;
        }
        catch (Exception e) when (IsAnalysisFailure(e))
        {
            error.WriteLine($"crap4net: {e.Message}");
            return ExitCodes.Failure;
        }
    }

    private bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out CliOptions? options)
    {
        try
        {
            options = CliArgumentsParser.Parse(args);
            return true;
        }
        catch (CliUsageException e)
        {
            error.WriteLine(e.Message);
            error.Write(CliArgumentsParser.Usage);
            options = null;
            return false;
        }
    }

    private static bool IsAnalysisFailure(Exception e) =>
        e is CrapFailureException or IOException or UnauthorizedAccessException;

    private int Run(CliOptions options)
    {
        var projects = new ProjectLocator();
        var files = SelectFiles(options, projects);
        var methods = files.SelectMany(CSharpMethodParser.ParseFile).ToList();
        if (methods.Count == 0)
            return ReportNothingToAnalyze(options, files);

        var filesWithMethods = methods.Select(m => m.FilePath).Distinct().ToList();
        var coverage = new CoverageCollector(runner, projects, root, error).Collect(options, filesWithMethods);
        var metrics = CrapAnalyzer.Analyze(methods, coverage);

        output.Write(options.Json
            ? ReportFormatter.Json(metrics, root, options.Threshold)
            : ReportFormatter.Text(metrics, root));
        return CheckThreshold(metrics, options.Threshold);
    }

    private IReadOnlyList<string> SelectFiles(CliOptions options, ProjectLocator projects)
    {
        var finder = new SourceFileFinder(root, projects, options.Excludes);
        return options.Mode switch
        {
            CliMode.Changed => finder.FilterChanged(new ChangedFileDetector(runner).Detect(root)),
            CliMode.Paths => finder.Select(options.Paths),
            _ => finder.FindAll(),
        };
    }

    // Nothing to measure means no reason to run the tests.
    private int ReportNothingToAnalyze(CliOptions options, IReadOnlyList<string> files)
    {
        output.Write(options.Json
            ? ReportFormatter.Json([], root, options.Threshold)
            : files.Count == 0 ? "No C# files to analyze.\n" : "No methods to analyze.\n");
        return ExitCodes.Success;
    }

    private int CheckThreshold(IReadOnlyList<MethodMetrics> metrics, double threshold)
    {
        var max = CrapAnalyzer.MaxCrap(metrics);
        if (max <= threshold)
            return ExitCodes.Success;
        error.WriteLine(
            $"CRAP threshold exceeded: {ReportFormatter.FormatScore(max)} > {ReportFormatter.FormatScore(threshold)}");
        return ExitCodes.ThresholdExceeded;
    }
}
