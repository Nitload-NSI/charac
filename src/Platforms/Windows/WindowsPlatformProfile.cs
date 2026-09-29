using WorkspaceAccessHost.Core;

namespace WorkspaceAccessHost.Platforms.Windows;

public sealed class WindowsPlatformProfile : IPlatformProfileProvider
{
    public PlatformProfile Profile { get; } = new(
        "windows", "Windows Service", "ConPTY", UserEnvironmentMode.ExistingLogonSession);
}
