using System.Collections.Concurrent;
using Charac.Server.Connections;
using Charac.Server.Authentication;

namespace Charac.Server.Tests;

public sealed class WorkspaceConnectionGateTests
{
    [Fact]
    public void OperatorDisconnectInvalidatesControllerButKeepsOwner()
    {
        var gate = new WorkspaceConnectionGate();
        var first = Assert.IsType<ConnectionLease>(gate.Acquire(Alice).Lease);
        Assert.True(gate.DisconnectCurrent());
        Assert.False(gate.DisconnectCurrent());
        Assert.False(gate.IsCurrent(first));
        Assert.False(gate.TryDispatch(first, () => throw new Exception("Stale input was dispatched.")));
        Assert.Equal(AcquisitionStatus.OwnedByAnotherIdentity,
            gate.Acquire(new(Alice.Issuer, "bob-id")).Status);
        Assert.Equal(AcquisitionStatus.Acquired, gate.Acquire(Alice).Status);
    }

    private static readonly ExternalIdentity Alice = new("https://auth.example/application/o/workspace/", "alice-id");

    [Fact]
    public void TakeoverInvalidatesOldInputAndDisconnect()
    {
        var gate = new WorkspaceConnectionGate();
        var first = Assert.IsType<ConnectionLease>(gate.Acquire(Alice).Lease);
        Assert.Equal(AcquisitionStatus.TakeoverRequired, gate.Acquire(Alice).Status);
        var replacement = Assert.IsType<ConnectionLease>(gate.Acquire(Alice, takeover: true).Lease);
        var inputs = new List<string>();

        Assert.False(gate.TryDispatch(first, () => inputs.Add("stale")));
        Assert.False(gate.Disconnect(first));
        Assert.False(gate.CloseWorkspace(first));
        Assert.True(gate.TryDispatch(replacement, () => inputs.Add("current")));
        Assert.Equal(new[] { "current" }, inputs);
        Assert.True(replacement.Generation > first.Generation);
    }

    [Fact]
    public void DisconnectionPreservesOwnerAndAllowsOwnerToReconnect()
    {
        var gate = new WorkspaceConnectionGate();
        var lease = Assert.IsType<ConnectionLease>(gate.Acquire(Alice).Lease);
        Assert.True(gate.Disconnect(lease));
        Assert.False(gate.IsCurrent(lease));
        Assert.Equal(AcquisitionStatus.OwnedByAnotherIdentity,
            gate.Acquire(new(Alice.Issuer, "bob-id"), takeover: true).Status);
        Assert.Equal(AcquisitionStatus.Acquired, gate.Acquire(Alice).Status);
    }

    [Fact]
    public void IssuerIsPartOfIdentity()
    {
        var gate = new WorkspaceConnectionGate();
        gate.Acquire(Alice);
        Assert.Equal(AcquisitionStatus.OwnedByAnotherIdentity,
            gate.Acquire(new("https://other.example/", Alice.Subject), takeover: true).Status);
    }

    [Fact]
    public void ClosedWorkspaceCanBeAssignedToAnotherOwner()
    {
        var gate = new WorkspaceConnectionGate();
        var lease = Assert.IsType<ConnectionLease>(gate.Acquire(Alice).Lease);
        Assert.True(gate.CloseWorkspace(lease));
        Assert.Equal(AcquisitionStatus.Acquired, gate.Acquire(new(Alice.Issuer, "bob-id")).Status);
        Assert.False(gate.IsCurrent(lease));
    }

    [Fact]
    public void OnlyOneConcurrentConnectionAcquiresControl()
    {
        var gate = new WorkspaceConnectionGate();
        var results = new ConcurrentBag<AcquisitionResult>();
        Parallel.For(0, 64, _ => results.Add(gate.Acquire(Alice)));
        Assert.Single(results, result => result.Status == AcquisitionStatus.Acquired);
        Assert.Equal(63, results.Count(result => result.Status == AcquisitionStatus.TakeoverRequired));
    }

    [Fact]
    public void LeaseFromAnotherGateCannotDispatch()
    {
        var gate = new WorkspaceConnectionGate();
        var other = new WorkspaceConnectionGate();
        gate.Acquire(Alice);
        var foreign = Assert.IsType<ConnectionLease>(other.Acquire(Alice).Lease);
        Assert.False(gate.TryDispatch(foreign, () => Assert.Fail("Foreign lease dispatched input.")));
    }
}
