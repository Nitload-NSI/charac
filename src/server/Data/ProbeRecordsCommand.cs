using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Charac.Server.Data;

/// <summary>Lists or removes records made by the local broker probe in a dedicated test database.</summary>
internal static class ProbeRecordsCommand
{
    private const string ProbeIssuer = "urn:workspace-access:local-broker-probe";
    private const string ProbeTargetPrefix = "local-broker-probe-";

    public static async Task<int> RunAsync(bool clear, string? configPath)
    {
        try
        {
            var configured = DatabaseSettings.ConnectionString(DatabaseSettings.LoadConfiguration(configPath));
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("Configure [database] before inspecting probe records.");
            var connection = new NpgsqlConnectionStringBuilder(configured);
            if (string.IsNullOrWhiteSpace(connection.Database) ||
                !connection.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Probe cleanup is restricted to a dedicated test database.");

            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connection.ConnectionString);
            await using var database = new AccessDbContext(options.Options);
            var targets = await database.Targets.Where(x => x.Name.StartsWith(ProbeTargetPrefix))
                .OrderBy(x => x.Name).ToListAsync();
            var targetIds = targets.Select(x => x.Id).ToArray();
            var grants = await database.Grants.Where(x => targetIds.Contains(x.TargetId)).ToListAsync();
            var keys = await database.HostKeys.Where(x => targetIds.Contains(x.TargetId)).ToListAsync();
            var identities = await database.Identities.Where(x => x.Issuer == ProbeIssuer).ToListAsync();

            Console.WriteLine($"Test database: {connection.Database}");
            foreach (var target in targets)
                Console.WriteLine($"Probe target: {target.Id} {target.Name}");
            Console.WriteLine($"Probe records: targets={targets.Count}, host_keys={keys.Count}, grants={grants.Count}, identities={identities.Count}.");
            if (!clear)
                return 0;

            var otherGrantIdentityIds = await database.Grants
                .Where(x => !targetIds.Contains(x.TargetId))
                .Select(x => x.IdentityId).ToListAsync();
            var orphanIdentities = identities.Where(x => !otherGrantIdentityIds.Contains(x.Id)).ToArray();
            await using var transaction = await database.Database.BeginTransactionAsync();
            database.Grants.RemoveRange(grants);
            database.HostKeys.RemoveRange(keys);
            await database.SaveChangesAsync();
            database.Targets.RemoveRange(targets);
            database.Identities.RemoveRange(orphanIdentities);
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine($"Removed probe records: targets={targets.Count}, host_keys={keys.Count}, grants={grants.Count}, identities={orphanIdentities.Length}. Migrations and other records remain.");
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or
            System.Data.Common.DbException or DbUpdateException or IOException)
        {
            Console.Error.WriteLine($"Probe records command failed ({exception.GetType().Name}): {exception.Message}");
            return 1;
        }
    }
}
