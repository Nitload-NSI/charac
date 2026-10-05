using Microsoft.EntityFrameworkCore;
using Renci.SshNet.Common;
using Charac.Server.Authentication;
using Charac.Server.Hosting;
using Charac.Server.Ssh;

namespace Charac.Server.Data;

/// <summary>Interactive, local-only registration of a target and its managed SSH login key.</summary>
internal static class EndpointRegistrationCommand
{
    public static async Task<int> RunAsync(string name, string address, string sourceKeyPath, string? configPath)
    {
        try
        {
            var configuration = WorkspaceAccessConfiguration.Load(configPath);
            var connection = DatabaseSettings.ConnectionString(configuration)
                ?? throw new InvalidOperationException("Configure [database] before registering an endpoint.");
            var options = new DbContextOptionsBuilder<AccessDbContext>();
            DatabaseSettings.Configure(options, connection);
            await using var database = new AccessDbContext(options.Options);
            if ((await database.Database.GetPendingMigrationsAsync()).Any())
                throw new InvalidOperationException("Apply database migrations first.");
            if (Console.IsInputRedirected)
                throw new InvalidOperationException("Endpoint registration requires an interactive local terminal.");

            var portText = Prompt("SSH port [22]", optional: true);
            if (portText.Length != 0 && !int.TryParse(portText, out _))
                throw new ArgumentException("Provide a valid SSH port.");
            var port = portText.Length == 0 ? 22 : int.Parse(portText);
            var hostKeyPath = Prompt("Trusted SSH host public key file");
            var hostPublicKey = AccessRegistrationCommand.ReadHostKey(hostKeyPath);
            var fingerprint = AccessRegistrationCommand.Fingerprint(hostPublicKey);
            Console.WriteLine($"Host key fingerprint: {fingerprint}");
            Console.WriteLine("Compare this fingerprint with the target's console or another trusted channel.");
            if (Prompt("Paste the fingerprint to confirm") != fingerprint)
                throw new InvalidOperationException("The SSH host key fingerprint was not confirmed.");

            var subject = Prompt("OIDC subject (blank to register without an access grant)", optional: true);
            var account = subject.Length == 0 ? null : Prompt("Target OS account");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
                throw new ArgumentException("The endpoint name is invalid.");
            if (address.Length > 253 || Uri.CheckHostName(address) == UriHostNameType.Unknown ||
                port is < 1 or > 65535)
                throw new ArgumentException("Provide a valid SSH address and port.");
            if (subject.Length > 256 || subject.Any(char.IsControl) ||
                account is { Length: > 256 } || account is not null && account.Any(char.IsControl))
                throw new ArgumentException("The OIDC subject or OS account is invalid.");
            if (account is not null && OidcSettings.FromConfiguration(configuration) is null)
                throw new InvalidOperationException("Configure [oidc] before registering a grant.");

            var existingTarget = await database.Targets.Include(x => x.HostKeys)
                .SingleOrDefaultAsync(x => x.Name == name);
            if (existingTarget is not null && (!existingTarget.Enabled || existingTarget.Address != address ||
                existingTarget.Port != port ||
                !existingTarget.HostKeys.Any(x => x.Enabled && x.PublicKey == hostPublicKey)))
                throw new InvalidOperationException("This endpoint name is already registered with different or disabled connection details.");

            var existingKey = await database.LoginKeys.AsNoTracking().SingleOrDefaultAsync(x => x.Name == name);
            if (existingKey is { Enabled: false })
                throw new InvalidOperationException("The existing SSH login key is disabled.");
            var store = new SshKeyStore(configuration);
            var fileName = new SshKeyImporter(store).Import(name, sourceKeyPath, existingKey?.FileName);

            await using var transaction = await database.Database.BeginTransactionAsync();
            await AccessRegistrationCommand.RegisterTargetAsync(database, name, address, port, hostPublicKey, announce: false);
            await SshLoginKeyCommand.RegisterAsync(database, store, name, fileName, announce: false);
            if (account is not null)
                await AccessRegistrationCommand.RegisterGrantAsync(database, configuration, name, subject, account, name, announce: false);
            await transaction.CommitAsync();
            Console.WriteLine(account is null
                ? "Endpoint registered. Add an OIDC grant before connecting from Client."
                : "Endpoint and OIDC grant registered. Client can now select the target.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            IOException or UnauthorizedAccessException or System.Data.Common.DbException or DbUpdateException or
            SshException or FormatException)
        {
            Console.Error.WriteLine($"Endpoint registration failed: {exception.Message}");
            return 1;
        }
    }

    private static string Prompt(string label, bool optional = false)
    {
        Console.Write($"{label}: ");
        var value = Console.ReadLine()?.Trim();
        if (value is null || !optional && value.Length == 0)
            throw new InvalidOperationException($"{label} is required.");
        return value;
    }
}
