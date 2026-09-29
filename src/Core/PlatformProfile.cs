namespace WorkspaceAccessHost.Core;

public enum UserEnvironmentMode
{
    ExistingLogonSession,
    SystemAccountSession
}

/// <summary>Platform design metadata; executable backend readiness is reported separately.</summary>
public sealed record PlatformProfile(
    string OperatingSystem,
    string ServiceManager,
    string TerminalBackend,
    UserEnvironmentMode UserEnvironmentMode);

public interface IPlatformProfileProvider
{
    PlatformProfile Profile { get; }
}
