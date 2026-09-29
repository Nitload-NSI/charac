using WorkspaceAccessHost.Core;

namespace WorkspaceAccessHost.Platforms.Linux;

public sealed class LinuxPlatformProfile : IPlatformProfileProvider
{
    public PlatformProfile Profile { get; } = new(
        "linux", "systemd", "POSIX PTY", UserEnvironmentMode.SystemAccountSession);
}
