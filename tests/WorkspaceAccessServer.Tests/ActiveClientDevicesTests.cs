using System.Security.Cryptography;
using Charac.Server.Authentication;
using Charac.Server.Connections;
using Charac.Client;

namespace Charac.Server.Tests;

public sealed class ActiveClientDevicesTests
{
    private static readonly ExternalIdentity Alice = new("https://auth.example/", "alice");
    private static readonly ExternalIdentity Bob = new("https://auth.example/", "bob");

    [Fact]
    public void SameInstallationCanHoldSeveralConnectionsWhileAnotherIsRejected()
    {
        var devices = new ActiveClientDevices();
        var first = Credential();
        var second = Credential();
        Assert.Equal(ClientDeviceStatus.Invalid, devices.Check(Alice, "spoofed"));
        Assert.Equal(ClientDeviceStatus.Allowed, devices.Acquire(Alice, first, out var one));
        Assert.Equal(ClientDeviceStatus.Allowed, devices.Acquire(Alice, first, out var two));
        Assert.Equal(ClientDeviceStatus.Occupied, devices.Check(Alice, second));
        Assert.Equal(ClientDeviceStatus.Occupied, devices.Acquire(Alice, second, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(ClientDeviceStatus.Allowed, devices.Acquire(Bob, second, out var bob));
        one!.Dispose();
        Assert.Equal(ClientDeviceStatus.Occupied, devices.Check(Alice, second));
        two!.Dispose();
        Assert.Equal(ClientDeviceStatus.Allowed, devices.Acquire(Alice, second, out var replacement));
        replacement!.Dispose();
        bob!.Dispose();
    }

    [Fact]
    public void OperatorDisconnectReleasesDeviceAndStaleLeaseCannotReleaseReplacement()
    {
        var devices = new ActiveClientDevices();
        var first = Credential();
        var second = Credential();
        Assert.Equal(ClientDeviceStatus.Allowed, devices.Acquire(Alice, first, out var old));
        using (devices.BeginDisconnect(Alice))
        {
            Assert.Equal(ClientDeviceStatus.Occupied, devices.Check(Alice, second));
            Assert.False(devices.TryUse(old!, () => new object(), out _));
        }
        Assert.Equal(ClientDeviceStatus.Allowed, devices.Acquire(Alice, second, out var current));
        old!.Dispose();
        Assert.Equal(ClientDeviceStatus.Occupied, devices.Check(Alice, first));
        current!.Dispose();
    }

    [Fact]
    public async Task InstallationCredentialPersistsAndIsPrivate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rac-device-" + Guid.NewGuid().ToString("N"));
        try
        {
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => ClientDeviceCredential.LoadOrCreate(directory))));
            var first = concurrent[0];
            Assert.Equal(43, first.Length);
            Assert.Equal(first, ClientDeviceCredential.LoadOrCreate(directory));
            Assert.All(concurrent, value => Assert.Equal(first, value));
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(Path.Combine(directory, "device-key"));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode &
                    (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead |
                     UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static string Credential() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
