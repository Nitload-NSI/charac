using Charac.Server.Authentication;
using Charac.Server.Data;

namespace Charac.Server.Ssh;

/// <summary>Owns live workspaces until explicit close, SSH EOF, or Server shutdown.</summary>
internal sealed class SshWorkspaceManager : IHostedService, IDisposable
{
    private readonly WorkspaceRecordStore? _records;
    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, SshWorkspace> _workspaces = [];
    private bool _stopping;

    public SshWorkspaceManager(WorkspaceRecordStore? records = null) => _records = records;

    public int Count
    {
        get
        {
            lock (_sync)
                return _workspaces.Count;
        }
    }

    public SshWorkspace Add(SshSession session, ExternalIdentity owner)
    {
        SshWorkspace workspace;
        lock (_sync)
        {
            if (_stopping)
                throw new InvalidOperationException("The session manager is stopping.");
            workspace = new SshWorkspace(session, owner, Remove);
            if (!_workspaces.TryAdd(session.WorkspaceId, workspace))
                throw new InvalidOperationException("The workspace already exists.");
        }
        workspace.Start();
        return workspace;
    }

    public SshWorkspace? Find(Guid id)
    {
        lock (_sync)
            return _workspaces.GetValueOrDefault(id);
    }

    public SshWorkspaceSnapshot[] GetSnapshots()
    {
        SshWorkspace[] current;
        lock (_sync)
            current = _workspaces.Values.ToArray();
        // Do not hold the manager lock while taking individual workspace locks.
        return current.Select(workspace => workspace.GetSnapshot())
            .OfType<SshWorkspaceSnapshot>().ToArray();
    }
    public int DisconnectUser(ExternalIdentity owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        SshWorkspace[] current;
        lock (_sync)
            current = _workspaces.Values.Where(workspace => workspace.Owner == owner).ToArray();
        return current.Count(workspace => workspace.DisconnectViewer());
    }
    private void Remove(SshWorkspace workspace, string reason)
    {
        lock (_sync)
        {
            if (_workspaces.TryGetValue(workspace.Id, out var current) && ReferenceEquals(current, workspace))
                _workspaces.Remove(workspace.Id);
        }
        _records?.Closed(workspace.Id, reason);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        SshWorkspace[] closing;
        lock (_sync)
        {
            _stopping = true;
            closing = _workspaces.Values.ToArray();
            _workspaces.Clear();
        }
        foreach (var workspace in closing)
            workspace.Stop();
        if (_records is not null)
            await _records.FlushAsync().WaitAsync(cancellationToken);
    }

    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();
}
