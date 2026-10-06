using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet.Common;
using Charac.Server.Authentication;
using Charac.Server.Data.Entities;
using Charac.Server.Hosting;
using Charac.Server.Ssh;

namespace Charac.Server.Data;

/// <summary>Local administrator commands for persistent SSH targets and OIDC grants.</summary>
internal static class AccessRegistrationCommand
{
    public static async Task<int> RunAsync(string[] args, string? configPath)
    {
        try
        {
            var configuration = WorkspaceAccessConfiguration.Load(configPath);
            var connection = DatabaseSettings.ConnectionString(configuration)
                ?? throw new InvalidOperationException("Configure [database] before registering targets or grants.");
            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connection);
            await using var database = new AccessDbContext(options.Options);
            if ((await database.Database.GetPendingMigrationsAsync()).Any())
                throw new InvalidOperationException("Apply database migrations first.");

            return args switch
            {
                ["database", "target", "register", var name, var address, var portText, var hostKeyFile]
                    when int.TryParse(portText, out var port) =>
                    await RegisterTargetAsync(database, name, address, port, ReadHostKey(hostKeyFile)),
                ["database", "target", "list"] => await ListTargetsAsync(database),
                ["database", "grant", "register", var target, var subject, var account] =>
                    await RegisterGrantAsync(database, configuration, target, subject, account, null),
                ["database", "grant", "register", var target, var subject, var account, var keyName] =>
                    await RegisterGrantAsync(database, configuration, target, subject, account, keyName),
                ["database", "grant", "pre-register", var target, var userName, var account] =>
                    await RegisterPreRegisteredGrantAsync(database, configuration, target, userName, account, null),
                ["database", "grant", "pre-register", var target, var userName, var account, var keyName] =>
                    await RegisterPreRegisteredGrantAsync(database, configuration, target, userName, account, keyName),
                ["database", "grant", "pending"] => await ListPendingGrantsAsync(database),
                ["database", "grant", "list"] => await ListGrantsAsync(database),
                ["grant", var userName, var target, var account] =>
                    await RegisterPreRegisteredGrantAsync(database, configuration, target, userName, account, null),
                ["grant", var userName, var target, var account, var keyName] =>
                    await RegisterPreRegisteredGrantAsync(database, configuration, target, userName, account, keyName),
                _ => throw new ArgumentException("Use grant <user-name> <target-name> <account> [<login-key-name>], database target register <name> <address> <port> <host-key-file>, target list, database grant register <target-name> <oidc-subject> <account> [<login-key-name>], database grant pending, or database grant list.")
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            IOException or UnauthorizedAccessException or System.Data.Common.DbException or DbUpdateException or
            SshException)
        {
            Console.Error.WriteLine($"Access registration failed: {exception.Message}");
            return 1;
        }
    }

    internal static async Task<int> RegisterTargetAsync(AccessDbContext database, string name,
        string address, int port, string hostPublicKey, bool announce = true)
    {
        RequireText(name, 128, "target name");
        RequireText(address, 253, "SSH address");
        if (Uri.CheckHostName(address) == UriHostNameType.Unknown || port is < 1 or > 65535)
            throw new ArgumentException("Provide a valid SSH address and port.");
        _ = new SshHostKeyTrust([hostPublicKey]);

        var existing = await database.Targets.Include(x => x.HostKeys)
            .SingleOrDefaultAsync(x => x.Name == name);
        if (existing is not null)
        {
            if (!existing.Enabled || existing.Address != address || existing.Port != port ||
                !existing.HostKeys.Any(key => key.Enabled && key.PublicKey == hostPublicKey))
                throw new InvalidOperationException("The target name is already registered with different or disabled connection details.");
            if (announce) Console.WriteLine($"SSH target already registered: {existing.Id} {existing.Name}.");
            return 0;
        }

        var target = new SshTarget { Name = name, Address = address, Port = port, Enabled = true };
        target.HostKeys.Add(new SshHostKey
        {
            Target = target, TargetId = target.Id, PublicKey = hostPublicKey, Enabled = true
        });
        database.Targets.Add(target);
        await database.SaveChangesAsync();
        if (announce) Console.WriteLine($"SSH target registered: {target.Id} {target.Name} host_key={Fingerprint(hostPublicKey)}.");
        return 0;
    }

    internal static async Task<int> RegisterGrantAsync(AccessDbContext database,
        Microsoft.Extensions.Configuration.IConfiguration configuration, string targetName,
        string subject, string account, string? keyName, bool announce = true)
    {
        var oidc = OidcSettings.FromConfiguration(configuration)
            ?? throw new InvalidOperationException("Configure [oidc] before registering a grant.");
        RequireText(targetName, 128, "target name");
        RequireText(subject, 256, "OIDC subject");
        RequireText(account, 256, "SSH account");
        if (keyName is not null)
            RequireText(keyName, 128, "SSH login key name");

        var target = await database.Targets.Include(x => x.HostKeys)
            .SingleOrDefaultAsync(x => x.Name == targetName)
            ?? throw new InvalidOperationException("Register the SSH target first.");
        if (!target.Enabled || !target.HostKeys.Any(key => key.Enabled))
            throw new InvalidOperationException("The SSH target needs an enabled host key.");
        SshLoginKey? loginKey = null;
        if (keyName is not null)
        {
            loginKey = await database.LoginKeys.SingleOrDefaultAsync(x => x.Name == keyName && x.Enabled)
                ?? throw new InvalidOperationException("Register and enable the SSH login key first.");
            using var privateKey = new SshKeyStore(configuration).Load(loginKey.FileName);
        }
        var endpointAccount = await GetEndpointAccountAsync(database, target, account, loginKey);

        var identity = await database.Identities.SingleOrDefaultAsync(x =>
            x.Issuer == oidc.Issuer && x.Subject == subject);
        if (identity is { Enabled: false })
            throw new InvalidOperationException("The OIDC identity is disabled.");
        identity ??= new AccessIdentity { Issuer = oidc.Issuer, Subject = subject, Enabled = true };

        var existing = await database.Grants.SingleOrDefaultAsync(x =>
            x.IdentityId == identity.Id && x.TargetId == target.Id);
        if (existing is not null)
        {
            if (!existing.Enabled || existing.Account != account || existing.EndpointAccountId != endpointAccount.Id)
                throw new InvalidOperationException("The identity already has a different or disabled grant for this target.");
            if (announce) Console.WriteLine($"SSH grant already registered: {existing.Id} target={target.Name} account={account}.");
            return 0;
        }

        if (database.Entry(identity).State == EntityState.Detached)
            database.Identities.Add(identity);
        var grant = new AccessGrant
        {
            Identity = identity, IdentityId = identity.Id,
            Target = target, TargetId = target.Id,
            EndpointAccount = endpointAccount, EndpointAccountId = endpointAccount.Id,
            Account = account, SshLoginKey = loginKey, SshLoginKeyId = loginKey?.Id,
            Enabled = true
        };
        database.Grants.Add(grant);
        await database.SaveChangesAsync();
        if (announce) Console.WriteLine($"SSH grant registered: {grant.Id} target={target.Name} account={account} key={keyName ?? "(password)"}.");
        return 0;
    }

    internal static async Task<int> RegisterPreRegisteredGrantAsync(AccessDbContext database,
        Microsoft.Extensions.Configuration.IConfiguration configuration, string targetName,
        string userName, string account, string? keyName, bool announce = true)
    {
        var oidc = OidcSettings.FromConfiguration(configuration)
            ?? throw new InvalidOperationException("Configure [oidc] before registering a grant.");
        RequireText(targetName, 128, "target name");
        RequireText(userName, 256, "OIDC user name");
        RequireText(account, 256, "SSH account");
        if (keyName is not null)
            RequireText(keyName, 128, "SSH login key name");

        var target = await database.Targets.Include(x => x.HostKeys)
            .SingleOrDefaultAsync(x => x.Name == targetName)
            ?? throw new InvalidOperationException("Register the SSH target first.");
        if (!target.Enabled || !target.HostKeys.Any(key => key.Enabled))
            throw new InvalidOperationException("The SSH target needs an enabled host key.");
        SshLoginKey? loginKey = null;
        if (keyName is not null)
        {
            loginKey = await database.LoginKeys.SingleOrDefaultAsync(x => x.Name == keyName && x.Enabled)
                ?? throw new InvalidOperationException("Register and enable the SSH login key first.");
            using var privateKey = new SshKeyStore(configuration).Load(loginKey.FileName);
        }
        var endpointAccount = await GetEndpointAccountAsync(database, target, account, loginKey);

        var identity = await database.Identities.SingleOrDefaultAsync(x =>
            x.Issuer == oidc.Issuer && x.UserName == userName);
        if (identity is { Enabled: false })
            throw new InvalidOperationException("The OIDC identity is disabled.");
        if (identity is not null)
        {
            var existing = await database.Grants.SingleOrDefaultAsync(x =>
                x.IdentityId == identity.Id && x.TargetId == target.Id);
            if (existing is not null)
            {
                if (!existing.Enabled || existing.Account != account || existing.EndpointAccountId != endpointAccount.Id)
                    throw new InvalidOperationException("The identity already has a different or disabled grant for this target.");
                if (announce) Console.WriteLine($"SSH grant already registered: {existing.Id} target={target.Name} account={account}.");
                return 0;
            }

            database.Grants.Add(new AccessGrant
            {
                Identity = identity, IdentityId = identity.Id,
                Target = target, TargetId = target.Id,
                EndpointAccount = endpointAccount, EndpointAccountId = endpointAccount.Id,
                Account = account, SshLoginKey = loginKey, SshLoginKeyId = loginKey?.Id,
                Enabled = true
            });
            await database.SaveChangesAsync();
            if (announce) Console.WriteLine($"SSH grant registered: user={userName} target={target.Name} account={account} key={keyName ?? "(password)"}.");
            return 0;
        }

        var pending = await database.PreRegisteredGrants.SingleOrDefaultAsync(x =>
            x.Issuer == oidc.Issuer && x.UserName == userName && x.TargetId == target.Id);
        if (pending is not null)
        {
            if (!pending.Enabled || pending.Account != account || pending.EndpointAccountId != endpointAccount.Id)
                throw new InvalidOperationException("The user already has a different or disabled pre-registered grant for this target.");
            if (announce) Console.WriteLine($"Pre-registered grant already exists: {pending.Id} user={userName} target={target.Name}.");
            return 0;
        }

        database.PreRegisteredGrants.Add(new PreRegisteredGrant
        {
            Issuer = oidc.Issuer, UserName = userName,
            Target = target, TargetId = target.Id,
            EndpointAccount = endpointAccount, EndpointAccountId = endpointAccount.Id,
            Account = account, SshLoginKey = loginKey, SshLoginKeyId = loginKey?.Id,
            Enabled = true
        });
        await database.SaveChangesAsync();
        if (announce) Console.WriteLine($"Pre-registered grant saved: user={userName} target={target.Name} account={account} key={keyName ?? "(password)"}.");
        return 0;
    }

    private static async Task<SshEndpointAccount> GetEndpointAccountAsync(
        AccessDbContext database, SshTarget target, string account, SshLoginKey? requestedKey)
    {
        var existing = await database.EndpointAccounts.SingleOrDefaultAsync(x =>
            x.TargetId == target.Id && x.Account == account);
        if (existing is not null)
        {
            if (requestedKey is not null && existing.SshLoginKeyId != requestedKey.Id)
                throw new InvalidOperationException("The endpoint account already uses a different SSH login key.");
            if (!existing.Enabled)
                throw new InvalidOperationException("The endpoint account is disabled.");
            return existing;
        }

        var endpointAccount = new SshEndpointAccount
        {
            Target = target,
            TargetId = target.Id,
            Account = account,
            SshLoginKey = requestedKey,
            SshLoginKeyId = requestedKey?.Id,
            Enabled = true
        };
        database.EndpointAccounts.Add(endpointAccount);
        return endpointAccount;
    }

    internal static string ReadHostKey(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Provide a trusted host public key file.");
        var file = new FileInfo(Path.GetFullPath(fileName));
        if (!file.Exists || file.Length is < 1 or > 8192 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The SSH host public key file is missing or unsafe.");
        var lines = File.ReadAllLines(file.FullName).Select(line => line.Trim())
            .Where(line => line.Length > 0).ToArray();
        if (lines.Length != 1)
            throw new InvalidOperationException("The SSH host public key file must contain exactly one key line.");
        var fields = lines[0].Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
            throw new InvalidOperationException("The SSH host public key line is incomplete.");
        var publicKey = $"{fields[0]} {fields[1]}";
        if (publicKey.Length > 2048)
            throw new InvalidOperationException("The SSH host public key is too long.");
        _ = new SshHostKeyTrust([publicKey]);
        return publicKey;
    }

    private static async Task<int> ListTargetsAsync(AccessDbContext database)
    {
        var targets = await database.Targets.AsNoTracking().Include(x => x.HostKeys)
            .OrderBy(x => x.Name).ToArrayAsync();
        foreach (var target in targets)
        {
            Console.WriteLine($"{target.Id} {target.Name} address={target.Address} port={target.Port} enabled={target.Enabled}");
            foreach (var key in target.HostKeys.OrderBy(x => x.Id))
                Console.WriteLine($"  host_key={Fingerprint(key.PublicKey)} enabled={key.Enabled}");
        }
        Console.WriteLine($"Targets: {targets.Length}.");
        return 0;
    }

    private static async Task<int> ListGrantsAsync(AccessDbContext database)
    {
        var grants = await database.Grants.AsNoTracking().Include(x => x.Target)
            .Include(x => x.Identity).Include(x => x.SshLoginKey)
            .OrderBy(x => x.Target.Name).ThenBy(x => x.Identity.Subject).ToArrayAsync();
        foreach (var grant in grants)
            Console.WriteLine($"{grant.Id} target={grant.Target.Name} user={grant.Identity.UserName ?? "(unbound)"} issuer={grant.Identity.Issuer} subject={grant.Identity.Subject} account={grant.Account} key={grant.SshLoginKey?.Name ?? "(password)"} enabled={grant.Enabled}");
        Console.WriteLine($"Grants: {grants.Length}.");
        return 0;
    }

    private static async Task<int> ListPendingGrantsAsync(AccessDbContext database)
    {
        var pending = await database.PreRegisteredGrants.AsNoTracking().Include(x => x.Target)
            .Include(x => x.SshLoginKey).OrderBy(x => x.UserName).ThenBy(x => x.Target.Name).ToArrayAsync();
        foreach (var grant in pending)
            Console.WriteLine($"{grant.Id} target={grant.Target.Name} user={grant.UserName} issuer={grant.Issuer} account={grant.Account} key={grant.SshLoginKey?.Name ?? "(password)"} enabled={grant.Enabled}");
        Console.WriteLine($"Pending grants: {pending.Length}.");
        return 0;
    }

    internal static string Fingerprint(string publicKey)
    {
        var base64 = publicKey.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        return "SHA256:" + Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(base64))).TrimEnd('=');
    }

    private static void RequireText(string value, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl))
            throw new ArgumentException($"The {field} must be nonempty and at most {maximum} characters.");
    }
}
