using Renci.SshNet;

namespace Charac.Server.Ssh;

/// <summary>Shared SSH transport used by the authorized broker and local diagnostic command.</summary>
internal static class SshPasswordConnection
{
    public static async Task<SshSession> OpenAsync(
        Guid workspaceId, Guid targetId, string address, int port, string account,
        IReadOnlyList<string> hostPublicKeys, string password, SshTerminalSize size,
        string terminalType, Action<SshSession> onClosed, CancellationToken cancellationToken = default)
    {
        var trust = new SshHostKeyTrust(hostPublicKeys);
        var connection = new Renci.SshNet.ConnectionInfo(address, port, account,
            new PasswordAuthenticationMethod(account, password))
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var client = new SshClient(connection);
        client.HostKeyReceived += (_, eventArgs) => eventArgs.CanTrust = trust.IsTrusted(eventArgs.HostKey);
        try
        {
            await client.ConnectAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var stream = client.CreateShellStream(terminalType, size.Columns, size.Rows, 0, 0, 8192);
            return new SshSession(workspaceId, targetId, account, client, stream, onClosed);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
