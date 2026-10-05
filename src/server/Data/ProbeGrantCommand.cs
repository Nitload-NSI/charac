using Microsoft.EntityFrameworkCore;
using Npgsql;
using Charac.Server.Authentication;
using Charac.Server.Data.Entities;
using Charac.Server.Hosting;
using Charac.Server.Ssh;

namespace Charac.Server.Data;

/// <summary>Local operator command for binding a real OIDC identity to an existing probe target.</summary>
internal static class ProbeGrantCommand
{
    public static async Task<int> RunAsync(Guid targetId, string issuer, string subject, string? configPath)
    {
        try
        {
            var configuration = WorkspaceAccessConfiguration.Load(configPath);
            var oidc = OidcSettings.FromConfiguration(configuration)
                ?? throw new InvalidOperationException("Configure [oidc] before enrolling a probe identity.");
            var probe = SshProbeSettings.FromConfiguration(configuration);
            if (targetId == Guid.Empty || issuer != oidc.Issuer || string.IsNullOrWhiteSpace(subject))
                throw new InvalidOperationException("Provide a probe target, configured issuer, subject and [ssh_probe] account.");
            var connectionString = DatabaseSettings.ConnectionString(configuration)
                ?? throw new InvalidOperationException("Configure the test database.");
            var connection = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(connection.Database) ||
                !connection.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The database name must identify a dedicated test database.");

            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connection.ConnectionString);
            await using var database = new AccessDbContext(options.Options);
            var target = await database.Targets.Include(x => x.HostKeys)
                .SingleOrDefaultAsync(x => x.Id == targetId);
            if (target is null || !target.Enabled ||
                !target.Name.StartsWith("local-broker-probe-", StringComparison.Ordinal) ||
                target.Address != probe.Address || target.Port != probe.Port ||
                !target.HostKeys.Any(key => key.Enabled && key.PublicKey == probe.HostPublicKey))
                throw new InvalidOperationException("The target must be an enabled broker-probe target.");
            var identity = await database.Identities.SingleOrDefaultAsync(x =>
                x.Issuer == issuer && x.Subject == subject);
            if (identity is { Enabled: false })
                throw new InvalidOperationException("The existing OIDC identity is disabled.");
            if (identity is null)
            {
                identity = new AccessIdentity { Issuer = issuer, Subject = subject, Enabled = true };
                database.Identities.Add(identity);
            }
            var existing = await database.Grants.SingleOrDefaultAsync(x =>
                x.IdentityId == identity.Id && x.TargetId == targetId);
            if (existing is not null)
                throw new InvalidOperationException("The identity already has a grant for this target.");
            var grant = new AccessGrant
            {
                Identity = identity,
                IdentityId = identity.Id,
                Target = target,
                TargetId = target.Id,
                Account = probe.Account,
                Enabled = true
            };
            database.Grants.Add(grant);
            await database.SaveChangesAsync();
            Console.WriteLine($"Probe grant saved: identity={identity.Id}, target={target.Id}, grant={grant.Id}.");
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Probe grant failed: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or DbUpdateException)
        {
            Console.Error.WriteLine($"Probe grant failed ({exception.GetType().Name}). Check the test database.");
            return 1;
        }
    }
}
