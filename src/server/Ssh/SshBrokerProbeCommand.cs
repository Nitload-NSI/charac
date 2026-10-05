using Microsoft.EntityFrameworkCore;
using Npgsql;
using Renci.SshNet.Common;
using System.Net.Sockets;
using System.Text;
using Charac.Server.Authentication;
using Charac.Server.Data;
using Charac.Server.Data.Entities;

namespace Charac.Server.Ssh;

/// <summary>Local integration probe through authorization, broker and SSH transport.</summary>
internal static class SshBrokerProbeCommand
{
    public static async Task<int> RunAsync(string[] args, string? configPath = null)
    {
        SshProbeSettings settings;
        try
        {
            settings = SshProbeSettings.FromCommand(args, "broker-probe", configPath);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FileNotFoundException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("An interactive terminal is required to enter the SSH password.");
            return 2;
        }

        var configured = DatabaseSettings.ConnectionString(DatabaseSettings.LoadConfiguration(configPath));
        if (string.IsNullOrWhiteSpace(configured))
        {
            Console.Error.WriteLine("Configure [database] host, name and username in workspace-access.config.");
            return 2;
        }
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(configured);
            if (string.IsNullOrWhiteSpace(connection.Database) ||
                !connection.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The database name must identify a dedicated test database.");

            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connection.ConnectionString);
            await using var database = new AccessDbContext(options.Options);
            if ((await database.Database.GetPendingMigrationsAsync()).Any())
                throw new InvalidOperationException("Apply database migrations before running broker-probe.");

            var identity = new AccessIdentity
            {
                Issuer = "urn:workspace-access:local-broker-probe",
                Subject = Guid.NewGuid().ToString("N"), Enabled = true
            };
            var target = new SshTarget
            {
                Name = "local-broker-probe-" + Guid.NewGuid().ToString("N"),
                Address = settings.Address, Port = settings.Port, Enabled = true
            };
            var hostKey = new SshHostKey
            {
                TargetId = target.Id, PublicKey = settings.HostPublicKey, Enabled = true
            };
            target.HostKeys.Add(hostKey);
            var grant = new AccessGrant
            {
                Identity = identity, IdentityId = identity.Id,
                Target = target, TargetId = target.Id,
                Account = settings.Account, Enabled = true
            };
            database.Grants.Add(grant);
            await database.SaveChangesAsync();
            Console.WriteLine($"Probe records saved: identity={identity.Id}, target={target.Id}, host_key={hostKey.Id}, grant={grant.Id}.");

            Console.Write("SSH password: ");
            var password = SshProbeCommand.ReadPassword();
            Console.WriteLine();
            using var sessions = new SshSessionRegistry();
            using var manager = new SshWorkspaceManager();
            var broker = new SshBroker(new SshAccessResolver(database, TimeProvider.System), sessions,
                new SshKeyStore(DatabaseSettings.LoadConfiguration(configPath)));
            var request = new SshSessionRequest(Guid.CreateVersion7(), target.Id,
                new ExternalIdentity(identity.Issuer, identity.Subject), settings.Account, new SshTerminalSize(80, 24));
            var session = await broker.OpenWithPasswordAsync(request, password);
            var workspace = manager.Add(session, request.Owner);
            var attached = workspace.Attach(request.Owner, takeover: false);
            if (attached.Lease is null || attached.Output is null)
                throw new InvalidOperationException("Could not attach to the managed SSH workspace.");
            try
            {
                await VerifyLinuxLsAsync(workspace, attached.Lease, attached.Output);
                Console.WriteLine("Database authorization, host key, password, managed session and Linux ls verified.");
            }
            finally
            {
                workspace.Detach(attached.Lease);
            }
            return 0;
        }
        catch (Exception exception) when (exception is SshException or SocketException or
            InvalidOperationException or ArgumentException or TimeoutException or IOException or
            System.Data.Common.DbException or DbUpdateException or UnauthorizedAccessException or OperationCanceledException)
        {
            Console.Error.WriteLine($"SSH broker probe failed ({exception.GetType().Name}). Check test database, target, host key, account, password and sshd policy.");
            return 1;
        }
    }

    internal static async Task VerifyLinuxLsAsync(SshWorkspace workspace,
        Charac.Server.Connections.ConnectionLease lease,
        System.Threading.Channels.ChannelReader<SshOutput> output)
    {
        var marker = Guid.NewGuid().ToString("N");
        var begin = "RAC_BEGIN_" + marker;
        var end = "RAC_END_" + marker + ":";
        if (!workspace.TryInput(lease, Encoding.UTF8.GetBytes("stty -echo\n")) ||
            !workspace.TryInput(lease, Encoding.UTF8.GetBytes(
                $"printf '\\n{begin}\\n'; ls; result=$?; printf '\\n{end}%s\\n' \"$result\"\n")))
            throw new InvalidOperationException("Could not queue the Linux ls probe.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var received = new MemoryStream();
        await foreach (var chunk in output.ReadAllAsync(timeout.Token))
        {
            received.Write(chunk.Data);
            if (received.Length > 256 * 1024)
                throw new InvalidOperationException("Linux ls output exceeded the probe limit.");
            var text = Encoding.UTF8.GetString(received.GetBuffer().AsSpan(0, (int)received.Length));
            var finish = text.IndexOf(end, StringComparison.Ordinal);
            while (finish >= 0 && (text.Length <= finish + end.Length ||
                !char.IsAsciiDigit(text[finish + end.Length])))
                finish = text.IndexOf(end, finish + end.Length, StringComparison.Ordinal);
            if (finish < 0)
                continue;
            var start = text.LastIndexOf(begin, finish, StringComparison.Ordinal);
            if (start < 0)
                continue;
            var exitCode = text[finish + end.Length];
            var listing = text[(start + begin.Length)..finish].Trim();
            Console.WriteLine("Remote ls output:");
            Console.WriteLine(listing.Length == 0 ? "(empty directory)" : listing);
            if (exitCode != '0')
                throw new InvalidOperationException("Linux ls returned a nonzero exit code.");
            return;
        }
        throw new InvalidOperationException("The SSH shell closed before the Linux ls result arrived.");
    }
}
