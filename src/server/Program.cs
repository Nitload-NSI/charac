using Charac.Server.Hosting;
using Charac.Server.Data;
using Charac.Server.Ssh;

namespace Charac.Server;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ServerCommandLine parsed;
        try
        {
            parsed = ServerCommandLine.Parse(args);
            if (parsed.ConfigPath is not null)
                WorkspaceAccessConfiguration.PathToLoad(parsed.ConfigPath);
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        args = parsed.Command;
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("Usage: char_rac_server [--config <path>] [serve --local-probe] | grant <user-name> <target-name> <account> [<login-key-name>] | status | sessions | reload | disconnect --subject <oidc-subject> --confirm | endpoint_regist <name> <ssh-address> <private-key-path> | database migrate | database target register <name> <address> <port> <host-key-file> | database target list | database grant register <target-name> <oidc-subject> <account> [<login-key-name>] | database grant pre-register <target-name> <user-name> <account> [<login-key-name>] | database grant pending | database grant list | database key register <name> <file-name> | database key assign <grant-id> <name> | database key unassign <grant-id> | database key list | database key grants | database enroll-probe <target-name> <oidc-subject> <key-name> | database grant-probe <target-id> <issuer> <subject> | database list-probes | database clear-probes | ssh probe [<address> <port> <account> <host-public-key>] | ssh broker-probe [<address> <port> <account> <host-public-key>] | ssh key-probe <target-id> <oidc-subject>");
            return 0;
        }
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Charac.Server supports Windows and Linux.");
            return 1;
        }
        if (args is ["endpoint_regist", var endpointName, var sshAddress, var privateKeyPath])
            return await EndpointRegistrationCommand.RunAsync(endpointName, sshAddress, privateKeyPath, parsed.ConfigPath);
        if (args is ["endpoint_regist", ..])
        {
            Console.Error.WriteLine("Use endpoint_regist <name> <ssh-address> <private-key-path>.");
            return 2;
        }
        if (args is ["database", "migrate"])
            return await DatabaseCommands.MigrateAsync(parsed.ConfigPath);
        if (args is ["grant", ..])
            return await AccessRegistrationCommand.RunAsync(args, parsed.ConfigPath);
        if (args is ["database", "key", ..])
            return await SshLoginKeyCommand.RunAsync(args, parsed.ConfigPath);
        if (args is ["database", "target" or "grant", ..])
            return await AccessRegistrationCommand.RunAsync(args, parsed.ConfigPath);
        if (args is ["database", "enroll-probe", var targetName, var enrolledSubject, var keyName])
            return await ProbeEnrollmentCommand.RunAsync(targetName, enrolledSubject, keyName, parsed.ConfigPath);
        if (args is ["status"] or ["sessions"] or ["reload"])
            return await ServerControlClient.RunAsync(args[0], parsed.ConfigPath);
        if (args is ["disconnect", "--subject", var disconnectSubject, "--confirm"] &&
            !string.IsNullOrWhiteSpace(disconnectSubject) && disconnectSubject.Length <= 256 &&
            !disconnectSubject.Any(char.IsControl))
            return await ServerControlClient.RunAsync("disconnect " + disconnectSubject, parsed.ConfigPath);
        if (args is ["disconnect", ..])
        {
            Console.Error.WriteLine("Use disconnect --subject <oidc-subject> --confirm.");
            return 2;
        }
        if (args is ["database", "list-probes"])
            return await ProbeRecordsCommand.RunAsync(clear: false, parsed.ConfigPath);
        if (args is ["database", "clear-probes"])
            return await ProbeRecordsCommand.RunAsync(clear: true, parsed.ConfigPath);
        if (args is ["database", "grant-probe", var targetText, var issuer, var subject] &&
            Guid.TryParse(targetText, out var targetId))
            return await ProbeGrantCommand.RunAsync(targetId, issuer, subject, parsed.ConfigPath);
        if (args is ["ssh", "probe", ..])
            return await SshProbeCommand.RunAsync(args, parsed.ConfigPath);
        if (args is ["ssh", "broker-probe", ..])
            return await SshBrokerProbeCommand.RunAsync(args, parsed.ConfigPath);
        if (args is ["ssh", "key-probe", var probeTarget, var probeSubject] &&
            Guid.TryParse(probeTarget, out var keyProbeTargetId))
            return await SshKeyProbeCommand.RunAsync(keyProbeTargetId, probeSubject, parsed.ConfigPath);
        if (args is ["serve", "--local-probe"])
        {
            await ServerApplication.RunAsync([], parsed.ConfigPath, localProbe: true);
            return 0;
        }

        await ServerApplication.RunAsync(args, parsed.ConfigPath);
        return 0;
    }
}
