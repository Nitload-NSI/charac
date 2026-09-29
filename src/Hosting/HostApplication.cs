using WorkspaceAccessHost.Connections;
using WorkspaceAccessHost.Core;
using WorkspaceAccessHost.Platforms.Linux;
using WorkspaceAccessHost.Platforms.Windows;

namespace WorkspaceAccessHost.Hosting;

internal static class HostApplication
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        IPlatformProfileProvider platform;
        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddWindowsService(options => options.ServiceName = "WorkspaceAccessHost");
            platform = new WindowsPlatformProfile();
        }
        else if (OperatingSystem.IsLinux())
        {
            builder.Services.AddSystemd();
            platform = new LinuxPlatformProfile();
        }
        else
        {
            throw new PlatformNotSupportedException("WorkspaceAccessHost supports Windows and Linux.");
        }

        builder.Services.AddSingleton(platform);
        builder.Services.AddSingleton<HostConnectionGate>();

        var app = builder.Build();
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        app.MapGet("/health/ready", () => Results.Json(
            new { status = "initializing", stage = "scaffold", pending = new[] { "oidc", "agent-ipc", "pty", "websocket" } },
            statusCode: StatusCodes.Status503ServiceUnavailable));

        app.Logger.LogInformation(
            "Host scaffold started for {OperatingSystem}; remote workspace readiness is pending backend integration.",
            platform.Profile.OperatingSystem);
        await app.RunAsync();
    }
}
