using System.Diagnostics;

namespace Charac.Client;

internal sealed class UnixTerminalInputMode : IDisposable
{
    private readonly string _originalMode;
    private bool _disposed;

    private UnixTerminalInputMode(string originalMode)
    {
        _originalMode = originalMode;
    }

    public static UnixTerminalInputMode? TryEnable()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        try
        {
            var originalMode = RunStty("-g");
            RunStty("-icanon", "-echo", "min", "1", "time", "0");
            return new UnixTerminalInputMode(originalMode);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { RunStty(_originalMode); }
        catch (Exception exception) when (exception is InvalidOperationException or IOException) { }
    }

    private static string RunStty(params string[] arguments)
    {
        var start = new ProcessStartInfo("stty")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start stty.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"stty failed: {error.Trim()}");
        return output.Trim();
    }
}
