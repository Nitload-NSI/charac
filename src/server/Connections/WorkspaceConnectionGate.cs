using Charac.Server.Authentication;

namespace Charac.Server.Connections;

public enum AcquisitionStatus
{
    Acquired,
    TakeoverRequired,
    OwnedByAnotherIdentity
}

/// <summary>A process-local capability. A transport must not construct one from client data.</summary>
public sealed class ConnectionLease
{
    internal ConnectionLease(ExternalIdentity owner, long generation)
    {
        Owner = owner;
        Generation = generation;
        ConnectionId = Guid.NewGuid();
    }

    public ExternalIdentity Owner { get; }
    public long Generation { get; }
    public Guid ConnectionId { get; }
}

public sealed record AcquisitionResult(AcquisitionStatus Status, ConnectionLease? Lease);

/// <summary>
/// Serializes control of one workspace. Workspace ownership survives transport disconnection.
/// Authentication and workspace authorization must succeed before calling Acquire.
/// </summary>
public sealed class WorkspaceConnectionGate
{
    private readonly Lock _sync = new();
    private ExternalIdentity? _owner;
    private ConnectionLease? _active;
    private long _generation;

    public AcquisitionResult Acquire(ExternalIdentity identity, bool takeover = false)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (_sync)
        {
            if (_owner is not null && _owner != identity)
            {
                return new(AcquisitionStatus.OwnedByAnotherIdentity, null);
            }

            if (_active is not null && !takeover)
            {
                return new(AcquisitionStatus.TakeoverRequired, null);
            }

            var generation = checked(_generation + 1);
            _owner = identity;
            _generation = generation;
            _active = new ConnectionLease(identity, generation);
            return new(AcquisitionStatus.Acquired, _active);
        }
    }

    public bool IsCurrent(ConnectionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            return ReferenceEquals(_active, lease);
        }
    }

    /// <summary>
    /// Atomically validates and enqueues a command relative to takeover.
    /// The callback must be short, synchronous, and must not call back into this gate.
    /// Already accepted commands are ordered before a later takeover.
    /// </summary>
    public bool TryDispatch(ConnectionLease lease, Action enqueue)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(enqueue);
        lock (_sync)
        {
            if (!ReferenceEquals(_active, lease))
            {
                return false;
            }

            enqueue();
            return true;
        }
    }

    public bool Disconnect(ConnectionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            if (!ReferenceEquals(_active, lease))
            {
                return false;
            }

            _active = null;
            return true;
        }
    }

    /// <summary>Invalidate the current controller without releasing workspace ownership.</summary>
    public bool DisconnectCurrent()
    {
        lock (_sync)
        {
            if (_active is null)
                return false;
            _active = null;
            return true;
        }
    }

    /// <summary>Release ownership after the session manager has terminated the workspace.</summary>
    public bool CloseWorkspace(ConnectionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            if (!ReferenceEquals(_active, lease))
            {
                return false;
            }

            _active = null;
            _owner = null;
            return true;
        }
    }
}
