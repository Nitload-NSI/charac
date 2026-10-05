using Microsoft.EntityFrameworkCore;
using Npgsql;
using Renci.SshNet.Common;
using System.Net.Sockets;
using Charac.Server.Authentication;
using Charac.Server.Data;
using Charac.Server.Hosting;

namespace Charac.Server.Ssh;

/// <summary>Local diagnostic for a registered SSH key and its OIDC-bound test grant.</summary>
internal static class SshKeyProbeCommand
{
    public static async Task<int> RunAsync(Guid targetId, string subject, string? configPath)
    {
        try
        {
            if (targetId == Guid.Empty || string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Provide a target ID and OIDC subject.");
            var configuration = WorkspaceAccessConfiguration.Load(configPath);
            var oidc = OidcSettings.FromConfiguration(configuration)
                ?? throw new InvalidOperationException("Configure [oidc] before running key-probe.");
            var configured = DatabaseSettings.ConnectionString(configuration)
                ?? throw new InvalidOperationException("Configure [database] before running key-probe.");
            var connection = new NpgsqlConnectionStringBuilder(configured);
            if (string.IsNullOrWhiteSpace(connection.Database) ||
                !connection.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Key probe is restricted to a dedicated test database.");
            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connection.ConnectionString);
            await using var database = new AccessDbContext(options.Options);
            if ((await database.Database.GetPendingMigrationsAsync()).Any())
                throw new InvalidOperationException("Apply database migrations first.");

            var owner = new ExternalIdentity(oidc.Issuer, subject);
            var resolver = new SshAccessResolver(database, TimeProvider.System);
            var decision = await resolver.ResolveAsync(owner, targetId)
                ?? throw new InvalidOperationException("The identity is not authorized for this target.");
            if (decision.SshLoginKeyId is null)
                throw new InvalidOperationException("The grant has no SSH login key assigned.");

            using var sessions = new SshSessionRegistry();
            using var manager = new SshWorkspaceManager();
            var broker = new SshBroker(resolver, sessions, new SshKeyStore(configuration));
            var request = new SshSessionRequest(Guid.CreateVersion7(), targetId, owner,
                decision.Account, new SshTerminalSize(80, 24));
            var session = await broker.OpenWithKeyAsync(request);
            var workspace = manager.Add(session, owner);
            var attached = workspace.Attach(owner, takeover: false);
            if (attached.Lease is null || attached.Output is null)
                throw new InvalidOperationException("Could not attach to the SSH workspace.");
            try
            {
                await SshBrokerProbeCommand.VerifyLinuxLsAsync(workspace, attached.Lease, attached.Output);
                Console.WriteLine("Database authorization, registered login key, host key and remote Linux ls verified.");
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
            Console.Error.WriteLine($"SSH key probe failed ({exception.GetType().Name}): {exception.Message}");
            return 1;
        }
    }
}
