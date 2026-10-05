namespace Charac.Server.Ssh;

/// <summary>Owns live SSH connections independently of HTTP/WebSocket attachments.</summary>
internal sealed class SshSessionRegistry : IHostedService, IDisposable
{
    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, SshSession> _sessions = [];
    private bool _stopping;

    public void Add(SshSession session)
    {
        lock (_sync)
        {
            if (_stopping)
                throw new InvalidOperationException("The SSH broker is stopping.");
            if (!_sessions.TryAdd(session.WorkspaceId, session))
                throw new InvalidOperationException("The workspace already has an SSH session.");
        }
    }

    public SshSession? Find(Guid workspaceId)
    {
        lock (_sync)
            return _sessions.GetValueOrDefault(workspaceId);
    }

    public void Remove(SshSession session)
    {
        lock (_sync)
        {
            if (_sessions.TryGetValue(session.WorkspaceId, out var current) && ReferenceEquals(current, session))
                _sessions.Remove(session.WorkspaceId);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        SshSession[] closing;
        lock (_sync)
        {
            _stopping = true;
            closing = _sessions.Values.ToArray();
            _sessions.Clear();
        }
        foreach (var session in closing)
            session.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();
}
