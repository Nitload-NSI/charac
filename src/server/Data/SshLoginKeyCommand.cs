using Microsoft.EntityFrameworkCore;
using Renci.SshNet.Common;
using Charac.Server.Data.Entities;
using Charac.Server.Hosting;
using Charac.Server.Ssh;

namespace Charac.Server.Data;

/// <summary>Local-only administration of key metadata and grant bindings.</summary>
internal static class SshLoginKeyCommand
{
    public static async Task<int> RunAsync(string[] args, string? configPath)
    {
        try
        {
            var configuration = WorkspaceAccessConfiguration.Load(configPath);
            var connection = DatabaseSettings.ConnectionString(configuration)
                ?? throw new InvalidOperationException("Configure [database] before managing SSH login keys.");
            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connection);
            await using var database = new AccessDbContext(options.Options);
            if ((await database.Database.GetPendingMigrationsAsync()).Any())
                throw new InvalidOperationException("Apply database migrations first.");
            var store = new SshKeyStore(configuration);
            return args switch
            {
                ["database", "key", "register", var name, var fileName] =>
                    await RegisterAsync(database, store, name, fileName),
                ["database", "key", "assign", var grantText, var name]
                    when Guid.TryParse(grantText, out var grantId) && grantId != Guid.Empty =>
                    await AssignAsync(database, grantId, name),
                ["database", "key", "unassign", var grantText]
                    when Guid.TryParse(grantText, out var grantId) && grantId != Guid.Empty =>
                    await UnassignAsync(database, grantId),
                ["database", "key", "list"] => await ListAsync(database),
                ["database", "key", "grants"] => await GrantsAsync(database),
                _ => throw new ArgumentException("Use database key register <name> <file-name>, assign <grant-id> <name>, unassign <grant-id>, list, or grants.")
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or
            IOException or UnauthorizedAccessException or System.Data.Common.DbException or DbUpdateException or
            SshException or FormatException)
        {
            Console.Error.WriteLine($"SSH key management failed: {exception.Message}");
            return 1;
        }
    }

    internal static async Task<int> RegisterAsync(AccessDbContext database, SshKeyStore store,
        string name, string fileName, bool announce = true)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
            throw new ArgumentException("The SSH key name is invalid.");
        using var key = store.Load(fileName);
        var existing = await database.LoginKeys.SingleOrDefaultAsync(x => x.Name == name);
        if (existing is not null)
        {
            if (existing.FileName != fileName)
                throw new InvalidOperationException("This SSH key name is already registered with another file name.");
            if (announce) Console.WriteLine($"SSH login key already registered: {existing.Id} {existing.Name}.");
            return 0;
        }
        var record = new SshLoginKey
        {
            Name = name, FileName = fileName, Enabled = true
        };
        database.LoginKeys.Add(record);
        await database.SaveChangesAsync();
        if (announce) Console.WriteLine($"SSH login key registered: {record.Id} {record.Name}.");
        return 0;
    }

    private static async Task<int> AssignAsync(AccessDbContext database, Guid grantId, string name)
    {
        var grant = await database.Grants.Include(x => x.EndpointAccount).SingleOrDefaultAsync(x => x.Id == grantId)
            ?? throw new InvalidOperationException("The grant does not exist.");
        var key = await database.LoginKeys.SingleOrDefaultAsync(x => x.Name == name && x.Enabled)
            ?? throw new InvalidOperationException("The enabled SSH login key does not exist.");
        grant.EndpointAccount.SshLoginKeyId = key.Id;
        grant.EndpointAccount.SshLoginKey = key;
        grant.SshLoginKeyId = key.Id;
        await database.SaveChangesAsync();
        Console.WriteLine($"SSH login key {key.Name} assigned to grant {grantId}.");
        return 0;
    }

    private static async Task<int> UnassignAsync(AccessDbContext database, Guid grantId)
    {
        var grant = await database.Grants.Include(x => x.EndpointAccount).SingleOrDefaultAsync(x => x.Id == grantId)
            ?? throw new InvalidOperationException("The grant does not exist.");
        grant.EndpointAccount.SshLoginKeyId = null;
        grant.EndpointAccount.SshLoginKey = null;
        grant.SshLoginKeyId = null;
        await database.SaveChangesAsync();
        Console.WriteLine($"SSH login key unassigned from grant {grantId}.");
        return 0;
    }

    private static async Task<int> ListAsync(AccessDbContext database)
    {
        var keys = await database.LoginKeys.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync();
        foreach (var key in keys)
            Console.WriteLine($"{key.Id} {key.Name} file={key.FileName} enabled={key.Enabled}");
        return 0;
    }

    private static async Task<int> GrantsAsync(AccessDbContext database)
    {
        var grants = await database.Grants.AsNoTracking().OrderBy(x => x.Target.Name)
            .ThenBy(x => x.Account)
            .Select(x => new
            {
                x.Id, TargetName = x.Target.Name, x.Target.Address, x.Account,
                x.Identity.Issuer, x.Identity.Subject, x.Enabled,
                LoginKey = x.EndpointAccount.SshLoginKey == null ? null : x.EndpointAccount.SshLoginKey.Name
            }).ToArrayAsync();
        foreach (var grant in grants)
            Console.WriteLine($"{grant.Id} target={grant.TargetName} address={grant.Address} account={grant.Account} issuer={grant.Issuer} subject={grant.Subject} enabled={grant.Enabled} key={grant.LoginKey ?? "(password)"}");
        Console.WriteLine($"Grants: {grants.Length}.");
        return 0;
    }
}
