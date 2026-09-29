using WorkspaceAccessHost.Agent;
using WorkspaceAccessHost.Hosting;

const string usage = "Usage: WorkspaceAccessHost <host|agent> [options]";

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine(usage);
    return 0;
}

if (args[0] is not ("host" or "agent"))
{
    Console.Error.WriteLine(usage);
    return 2;
}

if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("WorkspaceAccessHost supports Windows and Linux.");
    return 1;
}

var roleArguments = args[1..];
if (args[0] == "host")
{
    await HostApplication.RunAsync(roleArguments);
}
else
{
    await AgentApplication.RunAsync(roleArguments);
}

return 0;
