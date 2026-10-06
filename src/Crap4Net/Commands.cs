using System.ComponentModel;
using System.Diagnostics;

namespace Crap4Net;

internal sealed record CommandSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    public static CommandSpec Shell(string command, string workingDirectory) => OperatingSystem.IsWindows()
        ? new CommandSpec("cmd.exe", ["/c", command], workingDirectory)
        : new CommandSpec("/bin/sh", ["-c", command], workingDirectory);

    public override string ToString() =>
        string.Join(' ', Arguments.Prepend(FileName).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

internal interface ICommandRunner
{
    /// <summary>Runs to completion and returns everything it printed.</summary>
    CommandResult Capture(CommandSpec command);

    /// <summary>Runs to completion, copying stdout and stderr to <paramref name="output"/> as they arrive.</summary>
    int Stream(CommandSpec command, TextWriter output);
}

internal sealed class ProcessCommandRunner : ICommandRunner
{
    public CommandResult Capture(CommandSpec command)
    {
        using var process = Start(command);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new CommandResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    public int Stream(CommandSpec command, TextWriter output)
    {
        var gate = new object();
        void Copy(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null)
                return;
            lock (gate)
                output.WriteLine(e.Data);
        }

        using var process = Start(command);
        process.OutputDataReceived += Copy;
        process.ErrorDataReceived += Copy;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static Process Start(CommandSpec command)
    {
        var info = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in command.Arguments)
            info.ArgumentList.Add(argument);
        try
        {
            return Process.Start(info) ?? throw new CrapFailureException($"Could not start {command.FileName}");
        }
        catch (Win32Exception e)
        {
            throw new CrapFailureException($"Could not start {command.FileName}: {e.Message}");
        }
    }
}
