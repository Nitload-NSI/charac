using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Charac.Server.Hosting;
using Charac.Server.Connections;
using Charac.Server.Ssh;

namespace Charac.Server.Tests;

public sealed class ServerControlChannelTests
{
    [Fact]
    public async Task RunningProcessAcceptsStatusAndLoggingReloadButRejectsServerChange()
    {
        var directory = Path.Combine(Path.GetTempPath(), "workspace-access-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "workspace-access.config");
        var original = "[server]\ndomain=127.0.0.1\nlisten=http://127.0.0.1:5080\n[Logging:LogLevel]\nDefault=Information\n";
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var live = new ConfigurationBuilder().AddIniFile(path, optional: false, reloadOnChange: false).Build();
        using var workspaces = new SshWorkspaceManager();
        using var control = new ServerControlChannel(path, live, workspaces, new ActiveClientDevices(),
            NullLogger<ServerControlChannel>.Instance);
        try
        {
            await control.StartAsync(TestContext.Current.CancellationToken);
            var status = await ServerControlClient.SendAsync("status", path);
            Assert.True(status.Ok);
            Assert.Equal(Environment.ProcessId, status.ProcessId);
            Assert.Equal(0, status.Workspaces);

            var sessions = await ServerControlClient.SendAsync("sessions", path);
            Assert.True(sessions.Ok);
            Assert.Empty(Assert.IsType<ServerControlSession[]>(sessions.Sessions));

            var disabled = await ServerControlClient.SendAsync("disconnect alice", path);
            Assert.False(disabled.Ok);
            Assert.Null(disabled.Disconnected);

            var oversized = await ServerControlClient.SendAsync(new string('x', 513), path);
            Assert.False(oversized.Ok);
            Assert.True((await ServerControlClient.SendAsync("status", path)).Ok);

            await File.WriteAllTextAsync(path, original.Replace("Default=Information", "Default=Warning"),
                TestContext.Current.CancellationToken);
            var reload = await ServerControlClient.SendAsync("reload", path);
            Assert.True(reload.Ok);
            Assert.Equal("Warning", live["Logging:LogLevel:Default"]);

            await File.WriteAllTextAsync(path, original.Replace("5080", "5081"),
                TestContext.Current.CancellationToken);
            var rejected = await ServerControlClient.SendAsync("reload", path);
            Assert.False(rejected.Ok);
            Assert.Equal("Warning", live["Logging:LogLevel:Default"]);
            Assert.Equal("http://127.0.0.1:5080", live["server:listen"]);
        }
        finally
        {
            await control.StopAsync(CancellationToken.None);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitlyEnabledDisconnectUsesConfiguredIssuerAndValidatesSubject()
    {
        var directory = Path.Combine(Path.GetTempPath(), "workspace-access-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "workspace-access.config");
        await File.WriteAllTextAsync(path,
            "[management]\nallow_disconnect=true\n[oidc]\nissuer=https://auth.example/workspace/\n",
            TestContext.Current.CancellationToken);
        var live = new ConfigurationBuilder().AddIniFile(path, optional: false).Build();
        using var workspaces = new SshWorkspaceManager();
        var devices = new ActiveClientDevices();
        using var control = new ServerControlChannel(path, live, workspaces, devices,
            NullLogger<ServerControlChannel>.Instance);
        try
        {
            await control.StartAsync(TestContext.Current.CancellationToken);
            var identity = new Charac.Server.Authentication.ExternalIdentity(
                "https://auth.example/workspace/", "alice");
            var credential = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(ClientDeviceStatus.Allowed, devices.Acquire(identity, credential, out var old));
            var accepted = await ServerControlClient.SendAsync("disconnect alice", path);
            Assert.True(accepted.Ok);
            Assert.Equal(0, accepted.Disconnected);
            Assert.False(devices.TryUse(old!, () => new object(), out _));
            old!.Dispose();
            var rejected = await ServerControlClient.SendAsync("disconnect ", path);
            Assert.False(rejected.Ok);
        }
        finally
        {
            await control.StopAsync(CancellationToken.None);
            Directory.Delete(directory, recursive: true);
        }
    }
}
