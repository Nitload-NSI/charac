using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Charac.Client;

/// <summary>Passes VT output through the Windows console and reads VT keyboard and mouse input.</summary>
internal sealed class WindowsTerminalInputMode : IDisposable
{
    private const int StandardInput = -10;
    private const int StandardOutput = -11;
    private const uint ProcessedInput = 0x0001;
    private const uint LineInput = 0x0002;
    private const uint EchoInput = 0x0004;
    private const uint MouseInput = 0x0010;
    private const uint QuickEdit = 0x0040;
    private const uint ExtendedFlags = 0x0080;
    private const uint VirtualTerminalInput = 0x0200;
    private const uint ProcessedOutput = 0x0001;
    private const uint VirtualTerminalOutput = 0x0004;
    private const uint DisableNewlineAutoReturn = 0x0008;

    private readonly nint _inputHandle;
    private readonly uint _originalInputMode;
    private readonly nint _outputHandle;
    private readonly uint _originalOutputMode;
    private bool _disposed;

    private WindowsTerminalInputMode(nint inputHandle, uint originalInputMode,
        nint outputHandle, uint originalOutputMode)
    {
        _inputHandle = inputHandle;
        _originalInputMode = originalInputMode;
        _outputHandle = outputHandle;
        _originalOutputMode = originalOutputMode;
    }

    public static WindowsTerminalInputMode? TryEnable()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var inputHandle = GetStdHandle(StandardInput);
        var outputHandle = GetStdHandle(StandardOutput);
        if (inputHandle == 0 || inputHandle == -1 || outputHandle == 0 || outputHandle == -1 ||
            !GetConsoleMode(inputHandle, out var inputMode) ||
            !GetConsoleMode(outputHandle, out var outputMode))
            return null;

        var virtualTerminalOutput = outputMode | ProcessedOutput | VirtualTerminalOutput |
            DisableNewlineAutoReturn;
        if (!SetConsoleMode(outputHandle, virtualTerminalOutput))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enable terminal VT output.");
        var raw = (inputMode | MouseInput | ExtendedFlags | VirtualTerminalInput) &
            ~(ProcessedInput | LineInput | EchoInput | QuickEdit);
        if (!SetConsoleMode(inputHandle, raw))
        {
            var error = Marshal.GetLastWin32Error();
            _ = SetConsoleMode(outputHandle, outputMode);
            throw new Win32Exception(error, "Could not enable terminal mouse input.");
        }
        return new WindowsTerminalInputMode(inputHandle, inputMode, outputHandle, outputMode);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ = SetConsoleMode(_inputHandle, _originalInputMode);
        _ = SetConsoleMode(_outputHandle, _originalOutputMode);
    }

    public static void ResetTerminalState(bool leaveAlternateScreen = false)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var outputHandle = GetStdHandle(StandardOutput);
        if (outputHandle == 0 || outputHandle == -1 || !GetConsoleMode(outputHandle, out var originalMode))
            return;
        var inputHandle = GetStdHandle(StandardInput);
        if (!SetConsoleMode(outputHandle, originalMode | ProcessedOutput | VirtualTerminalOutput))
            return;
        try
        {
            var output = Console.OpenStandardOutput();
            output.Write(Encoding.ASCII.GetBytes(
                "\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l\u001b[?1015l\u001b[?1004l\u001b[?2004l\u001b[?2026l" +
                (leaveAlternateScreen ? "\u001b[?1049l" : "") + "\u001b[?25h"));
            output.Flush();
            if (inputHandle != 0 && inputHandle != -1)
                _ = FlushConsoleInputBuffer(inputHandle);
        }
        finally
        {
            _ = SetConsoleMode(outputHandle, originalMode);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint consoleHandle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint consoleHandle, uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushConsoleInputBuffer(nint consoleHandle);
}
