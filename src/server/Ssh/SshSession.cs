using Renci.SshNet;

namespace Charac.Server.Ssh;

internal sealed class SshSession : IDisposable
{
    private readonly SshClient _client;
    private readonly ShellStream _stream;
    private readonly Action<SshSession> _onClosed;
    private readonly IDisposable? _credential;
    private int _disposed;

    public SshSession(Guid workspaceId, Guid targetId, string account, SshClient client,
        ShellStream stream, Action<SshSession> onClosed, IDisposable? credential = null)
    {
        WorkspaceId = workspaceId;
        TargetId = targetId;
        Account = account;
        _client = client;
        _stream = stream;
        _onClosed = onClosed;
        _credential = credential;
    }

    public Guid WorkspaceId { get; }
    public Guid TargetId { get; }
    public string Account { get; }
    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _client.IsConnected;

    public ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken = default) =>
        _stream.ReadAsync(output, cancellationToken);

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        await _stream.WriteAsync(input, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    public void Resize(SshTerminalSize size)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _stream.ChangeWindowSize(size.Columns, size.Rows, 0, 0);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _onClosed(this);
        try
        {
            _stream.Dispose();
        }
        finally
        {
            try { _client.Dispose(); }
            finally { _credential?.Dispose(); }
        }
    }
}
