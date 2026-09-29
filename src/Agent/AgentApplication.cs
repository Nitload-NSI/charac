namespace WorkspaceAccessHost.Agent;

internal static class AgentApplication
{
    public static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Services.AddHostedService<AgentWorker>();
        await builder.Build().RunAsync();
    }
}
