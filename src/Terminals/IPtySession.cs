namespace WorkspaceAccessHost.Terminals;

public sealed record TerminalSize
{
    public TerminalSize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, short.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, short.MaxValue);
        Columns = columns;
        Rows = rows;
    }

    public int Columns { get; }
    public int Rows { get; }
}

/// <summary>Owned by a user agent for the lifetime of a terminal, independently of its transport.</summary>
public interface IPtySession : IAsyncDisposable
{
    Guid SessionId { get; }
    Stream Output { get; }
    Task<int> ExitCode { get; }
    ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken);
    ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken);
    ValueTask TerminateAsync(CancellationToken cancellationToken);
}

/// <summary>Invoked inside an already established system user context.</summary>
public interface IPtySessionFactory
{
    ValueTask<IPtySession> StartAsync(
        TerminalStartInfo startInfo, CancellationToken cancellationToken);
}

public sealed record TerminalStartInfo(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TerminalSize Size);
