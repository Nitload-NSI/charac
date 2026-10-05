using Microsoft.Extensions.Configuration;
using Charac.Server.Hosting;

namespace Charac.Server.Ssh;

internal sealed record SshProbeSettings(string Address, int Port, string Account, string HostPublicKey)
{
    public static SshProbeSettings FromConfiguration(IConfiguration configuration)
    {
        var address = configuration["ssh_probe:address"]?.Trim();
        var portText = configuration["ssh_probe:port"]?.Trim();
        var account = configuration["ssh_probe:account"]?.Trim();
        var hostPublicKey = configuration["ssh_probe:host_public_key"]?.Trim();
        if (hostPublicKey is { Length: >= 2 } && hostPublicKey[0] == '"' && hostPublicKey[^1] == '"')
            hostPublicKey = hostPublicKey[1..^1];
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(account) ||
            string.IsNullOrWhiteSpace(hostPublicKey) || !int.TryParse(portText, out var port) ||
            port is < 1 or > 65535)
            throw new InvalidOperationException("Configure [ssh_probe] address, port, account and host_public_key.");
        _ = new SshHostKeyTrust([hostPublicKey]);
        return new SshProbeSettings(address, port, account, hostPublicKey);
    }

    public static SshProbeSettings FromCommand(string[] args, string command, string? configPath = null)
    {
        if (args is ["ssh", _, var address, var portText, var account, var hostPublicKey] &&
            int.TryParse(portText, out var port) && port is >= 1 and <= 65535 &&
            !string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(account) &&
            !string.IsNullOrWhiteSpace(hostPublicKey))
        {
            _ = new SshHostKeyTrust([hostPublicKey]);
            return new SshProbeSettings(address, port, account, hostPublicKey);
        }
        if (args is ["ssh", _])
            return FromConfiguration(WorkspaceAccessConfiguration.Load(configPath));
        throw new ArgumentException($"Usage: Charac.Server ssh {command} [<address> <port> <account> <host-public-key>]");
    }
}
