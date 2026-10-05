using System.Threading.Channels;
using Charac.Server.Authentication;
using Charac.Server.Connections;

namespace Charac.Server.Ssh;

internal sealed record SshWorkspaceSnapshot(Guid Id, Guid TargetId, ExternalIdentity Owner,
    string Account, DateTimeOffset CreatedAt, string State)
{
    public string? ConnectionPeer { get; init; }
    public DateTimeOffset? AttachedAt { get; init; }
}
internal sealed record SshOutput(long Sequence, byte[] Data);
internal sealed record SshWorkspaceAttachment(AcquisitionStatus Status, ConnectionLease? Lease,
    ChannelReader<SshOutput>? Output, Task<string>? Ended)
{
    public Task? Disconnected { get; init; }
}

/// <summary>Keeps one SSH channel alive and drains its output independently of attached clients.</summary>
internal sealed class SshWorkspace : IDisposable
{
    private const int HistoryLimit = 256 * 1024;
    private const int HistoryChunkLimit = 256;
    private readonly Lock _sync = new();
    private readonly WorkspaceConnectionGate _gate = new();
    private readonly Channel<WorkspaceCommand> _commands = Channel.CreateBounded<WorkspaceCommand>(
        new BoundedChannelOptions(128) { SingleReader = true, SingleWriter = false });
    private readonly Queue<SshOutput> _history = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly TaskCompletionSource<string> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<SshWorkspace, string> _onClosed;
    private Channel<SshOutput>? _viewer;
    private TaskCompletionSource? _viewerDisconnected;
    private string? _connectionPeer;
    private DateTimeOffset? _attachedAt;
    private int _historyBytes;
    private long _nextSequence;
    private int _disposed;

    public SshWorkspace(SshSession session, ExternalIdentity owner, Action<SshWorkspace, string> onClosed)
    {
        Session = session;
        Owner = owner;
        _onClosed = onClosed;
    }

    public SshSession Session { get; }
    public ExternalIdentity Owner { get; }
    public Guid Id => Session.WorkspaceId;
    public Guid TargetId => Session.TargetId;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public SshWorkspaceSnapshot? GetSnapshot()
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return null;
            return new SshWorkspaceSnapshot(Id, TargetId, Owner, Session.Account, CreatedAt,
                _viewer is null ? "detached" : "attached")
            {
                ConnectionPeer = _connectionPeer,
                AttachedAt = _attachedAt
            };
        }
    }
    public void Start()
    {
        _ = PumpOutputAsync();
        _ = DispatchCommandsAsync();
    }

    public SshWorkspaceAttachment Attach(ExternalIdentity identity, bool takeover,
        string? connectionPeer = null)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0 || identity != Owner)
                return new SshWorkspaceAttachment(AcquisitionStatus.OwnedByAnotherIdentity, null, null, null);
            var acquired = _gate.Acquire(identity, takeover);
            if (acquired.Lease is null)
                return new SshWorkspaceAttachment(acquired.Status, null, null, null);

            _viewerDisconnected?.TrySetResult();
            _viewer?.Writer.TryComplete();
            var viewer = Channel.CreateBounded<SshOutput>(new BoundedChannelOptions(512)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            foreach (var chunk in _history)
                if (!viewer.Writer.TryWrite(chunk))
                    throw new InvalidOperationException("SSH output history exceeds the viewer queue.");
            _viewer = viewer;
            _viewerDisconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectionPeer = connectionPeer;
            _attachedAt = DateTimeOffset.UtcNow;
            return new SshWorkspaceAttachment(AcquisitionStatus.Acquired, acquired.Lease, viewer.Reader, _ended.Task)
            {
                Disconnected = _viewerDisconnected.Task
            };
        }
    }

    public bool TryInput(ConnectionLease lease, byte[] input)
    {
        if (input.Length is 0 or > 8192 || Volatile.Read(ref _disposed) != 0)
            return false;
        var accepted = false;
        return _gate.TryDispatch(lease, () => accepted = _commands.Writer.TryWrite(
            new WorkspaceCommand(input, null))) && accepted;
    }

    public bool TryResize(ConnectionLease lease, SshTerminalSize size)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;
        var accepted = false;
        return _gate.TryDispatch(lease, () => accepted = _commands.Writer.TryWrite(
            new WorkspaceCommand(null, size))) && accepted;
    }

    public void Detach(ConnectionLease lease)
    {
        lock (_sync)
        {
            if (!_gate.Disconnect(lease))
                return;
            _viewerDisconnected?.TrySetResult();
            _viewerDisconnected = null;
            _viewer?.Writer.TryComplete();
            _viewer = null;
            _connectionPeer = null;
            _attachedAt = null;
        }
    }

    private async Task PumpOutputAsync()
    {
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var count = await Session.ReadAsync(buffer, _closing.Token);
                if (count == 0)
                    break;
                var chunk = new SshOutput(Interlocked.Increment(ref _nextSequence), buffer[..count]);
                Channel<SshOutput>? viewer;
                bool queued;
                lock (_sync)
                {
                    _history.Enqueue(chunk);
                    _historyBytes += count;
                    while (_historyBytes > HistoryLimit || _history.Count > HistoryChunkLimit)
                        _historyBytes -= _history.Dequeue().Data.Length;
                    viewer = _viewer;
                    queued = viewer is null || viewer.Writer.TryWrite(chunk);
                }
                if (!queued)
                {
                    try { await viewer!.Writer.WriteAsync(chunk, _closing.Token); }
                    catch (ChannelClosedException) { }
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            End("ssh_closed");
        }
    }

    private async Task DispatchCommandsAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_closing.Token))
            {
                if (command.Input is { } input)
                    await Session.WriteAsync(input, _closing.Token);
                else if (command.Size is { } size)
                    Session.Resize(size);
            }
        }
        catch (Exception)
        {
            End("ssh_closed");
        }
    }

    public bool DisconnectViewer()
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0 || !_gate.DisconnectCurrent())
                return false;
            _viewerDisconnected?.TrySetResult();
            _viewerDisconnected = null;
            _viewer?.Writer.TryComplete();
            _viewer = null;
            _connectionPeer = null;
            _attachedAt = null;
            return true;
        }
    }

    public void Dispose() => End("workspace_closed");

    public void Stop() => End("server_stopped");

    private void End(string reason)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _closing.Cancel();
        _commands.Writer.TryComplete();
        _ended.TrySetResult(reason);
        lock (_sync)
        {
            _viewerDisconnected?.TrySetResult();
            _viewerDisconnected = null;
            _viewer?.Writer.TryComplete();
            _viewer = null;
        }
        try
        {
            Session.Dispose();
        }
        finally
        {
            _onClosed(this, reason);
        }
    }

    private sealed record WorkspaceCommand(byte[]? Input, SshTerminalSize? Size);
}
