using System.Security.Cryptography;
using Charac.Server.Authentication;

namespace Charac.Server.Connections;

internal enum ClientDeviceStatus { Allowed, Invalid, Occupied }

/// <summary>One active client installation per OIDC identity; several workspaces may share it.</summary>
internal sealed class ActiveClientDevices
{
    public const string HeaderName = "X-RAC-Device";
    private readonly Lock _sync = new();
    private readonly Dictionary<ExternalIdentity, ActiveDevice> _active = [];
    private readonly HashSet<ExternalIdentity> _disconnecting = [];

    public ClientDeviceStatus Check(ExternalIdentity owner, string? credential)
    {
        if (!TryFingerprint(credential, out var fingerprint))
            return ClientDeviceStatus.Invalid;
        lock (_sync)
            return _disconnecting.Contains(owner) ||
                _active.TryGetValue(owner, out var active) && active.Fingerprint != fingerprint
                ? ClientDeviceStatus.Occupied : ClientDeviceStatus.Allowed;
    }

    public ClientDeviceStatus Acquire(ExternalIdentity owner, string? credential, out ClientDeviceLease? lease)
    {
        lease = null;
        if (!TryFingerprint(credential, out var fingerprint))
            return ClientDeviceStatus.Invalid;
        lock (_sync)
        {
            if (_disconnecting.Contains(owner))
                return ClientDeviceStatus.Occupied;
            if (_active.TryGetValue(owner, out var active))
            {
                if (active.Fingerprint != fingerprint)
                    return ClientDeviceStatus.Occupied;
                active.Count++;
            }
            else
            {
                active = new ActiveDevice(fingerprint);
                _active.Add(owner, active);
            }
            lease = new ClientDeviceLease(this, owner, active);
            return ClientDeviceStatus.Allowed;
        }
    }

    public bool TryUse<T>(ClientDeviceLease lease, Func<T> action, out T? result) where T : class
    {
        lock (_sync)
        {
            if (lease.IsDisposed || _disconnecting.Contains(lease.Owner) ||
                !_active.TryGetValue(lease.Owner, out var current) ||
                !ReferenceEquals(current, lease.Device))
            {
                result = null;
                return false;
            }
            result = action();
            return true;
        }
    }

    public IDisposable BeginDisconnect(ExternalIdentity owner)
    {
        lock (_sync)
        {
            if (!_disconnecting.Add(owner))
                throw new InvalidOperationException("A disconnect operation is already running for this identity.");
            _active.Remove(owner);
        }
        return new OperatorDisconnectLease(this, owner);
    }

    private void FinishDisconnect(ExternalIdentity owner)
    {
        lock (_sync)
            _disconnecting.Remove(owner);
    }

    private void Release(ExternalIdentity owner, ActiveDevice active)
    {
        lock (_sync)
        {
            if (!_active.TryGetValue(owner, out var current) || !ReferenceEquals(current, active))
                return;
            if (--current.Count == 0)
                _active.Remove(owner);
        }
    }

    private static bool TryFingerprint(string? credential, out string fingerprint)
    {
        fingerprint = "";
        if (credential is not { Length: 43 } || credential.Any(ch =>
            !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            return false;
        try
        {
            var bytes = Convert.FromBase64String(credential.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != 32)
                return false;
            fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal sealed class ActiveDevice(string fingerprint)
    {
        public string Fingerprint { get; } = fingerprint;
        public int Count { get; set; } = 1;
    }

    internal sealed class ClientDeviceLease(ActiveClientDevices gate, ExternalIdentity owner,
        ActiveDevice device) : IDisposable
    {
        private int _disposed;
        internal ExternalIdentity Owner => owner;
        internal ActiveDevice Device => device;
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Release(owner, device);
        }
    }

    private sealed class OperatorDisconnectLease(ActiveClientDevices gate, ExternalIdentity owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.FinishDisconnect(owner);
        }
    }
}
