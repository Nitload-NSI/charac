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
            Console.WriteLine("Usage: char_rac_server status | sessions | reload | disconnect --subject <oidc-subject> --confirm");
            Console.WriteLine("       char_rac_server grant | endpoint_regist | database  (running Server control channel)");
            Console.WriteLine("       char_rac_server [--config <path>] serve | ssh");
            return 0;
        }
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Charac.Server supports Windows and Linux.");
            return 1;
        }
        if (args is ["grant", ..] or ["database", ..] or ["endpoint_regist", ..])
            return await ServerControlClient.RunAsync(args);
        if (args is ["endpoint_regist", ..])
        {
            Console.Error.WriteLine("Use endpoint_regist <name> <ssh-address> <private-key-path>.");
            return 2;
        }
        if (args is ["database", "migrate"])
            return await DatabaseCommands.MigrateAsync(parsed.ConfigPath);
        if (args is ["status"] or ["sessions"] or ["reload"])
            return await ServerControlClient.RunAsync(args[0]);
        if (args is ["disconnect", "--subject", var disconnectSubject, "--confirm"] &&
            !string.IsNullOrWhiteSpace(disconnectSubject) && disconnectSubject.Length <= 256 &&
            !disconnectSubject.Any(char.IsControl))
            return await ServerControlClient.RunAsync("disconnect " + disconnectSubject);
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
