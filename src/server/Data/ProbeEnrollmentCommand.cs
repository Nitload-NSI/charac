using Microsoft.EntityFrameworkCore;
using Npgsql;
using Charac.Server.Authentication;
using Charac.Server.Data.Entities;
using Charac.Server.Hosting;
using Charac.Server.Ssh;

namespace Charac.Server.Data;

/// <summary>Enrolls the configured SSH probe as a persistent, OIDC-bound test target.</summary>
internal static class ProbeEnrollmentCommand
{
    public static async Task<int> RunAsync(string name, string subject, string keyName, string? configPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 ||
                string.IsNullOrWhiteSpace(subject) || subject.Length > 256 ||
                string.IsNullOrWhiteSpace(keyName))
                throw new ArgumentException("Provide a target name, OIDC subject and SSH login key name.");

            var configuration = WorkspaceAccessConfiguration.Load(configPath);
            var oidc = OidcSettings.FromConfiguration(configuration)
                ?? throw new InvalidOperationException("Configure [oidc] before enrolling a target.");
            var probe = SshProbeSettings.FromConfiguration(configuration);
            var configured = DatabaseSettings.ConnectionString(configuration)
                ?? throw new InvalidOperationException("Configure [database] before enrolling a target.");
            var connection = new NpgsqlConnectionStringBuilder(configured);
            if (string.IsNullOrWhiteSpace(connection.Database) ||
                !connection.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Probe enrollment is restricted to a dedicated test database.");

            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connection.ConnectionString);
            await using var database = new AccessDbContext(options.Options);
            if ((await database.Database.GetPendingMigrationsAsync()).Any())
                throw new InvalidOperationException("Apply database migrations first.");
            var key = await database.LoginKeys.SingleOrDefaultAsync(x => x.Name == keyName && x.Enabled)
                ?? throw new InvalidOperationException("The enabled SSH login key does not exist.");
            using var privateKey = new SshKeyStore(configuration).Load(key.FileName);

            await using var transaction = await database.Database.BeginTransactionAsync();
            var target = await database.Targets.Include(x => x.HostKeys)
                .SingleOrDefaultAsync(x => x.Name == name);
            if (target is null)
            {
                target = new SshTarget
                {
                    Name = name, Address = probe.Address, Port = probe.Port, Enabled = true
                };
                target.HostKeys.Add(new SshHostKey
                {
                    Target = target, TargetId = target.Id,
                    PublicKey = probe.HostPublicKey, Enabled = true
                });
                database.Targets.Add(target);
            }
            else if (!target.Enabled || target.Address != probe.Address || target.Port != probe.Port ||
                target.HostKeys.Count != 1 || !target.HostKeys[0].Enabled ||
                target.HostKeys[0].PublicKey != probe.HostPublicKey)
            {
                throw new InvalidOperationException("The existing target differs from the configured SSH probe.");
            }

            var identity = await database.Identities.SingleOrDefaultAsync(x =>
                x.Issuer == oidc.Issuer && x.Subject == subject);
            if (identity is null)
            {
                identity = new AccessIdentity
                {
                    Issuer = oidc.Issuer, Subject = subject, Enabled = true
                };
                database.Identities.Add(identity);
            }
            else if (!identity.Enabled)
                throw new InvalidOperationException("The existing OIDC identity is disabled.");

            var grant = await database.Grants.SingleOrDefaultAsync(x =>
                x.IdentityId == identity.Id && x.TargetId == target.Id);
            if (grant is null)
            {
                grant = new AccessGrant
                {
                    Identity = identity, IdentityId = identity.Id,
                    Target = target, TargetId = target.Id,
                    Account = probe.Account, SshLoginKey = key,
                    SshLoginKeyId = key.Id, Enabled = true
                };
                database.Grants.Add(grant);
            }
            else if (!grant.Enabled || grant.Account != probe.Account || grant.SshLoginKeyId != key.Id)
                throw new InvalidOperationException("The existing grant differs from the configured account or login key.");

            await database.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine($"Probe target enrolled: target={target.Id}, identity={identity.Id}, grant={grant.Id}, key={key.Name}.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            IOException or UnauthorizedAccessException or System.Data.Common.DbException or DbUpdateException)
        {
            Console.Error.WriteLine($"Probe enrollment failed: {exception.Message}");
            return 1;
        }
    }
}
