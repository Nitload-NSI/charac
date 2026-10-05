using Charac.Server.Authentication;

namespace Charac.Server.Ssh;

/// <summary>
/// A request to open one remote shell after the API has authenticated its caller. The concrete SSH broker
/// resolves the target and SSH trust material by TargetId and verifies authorization again;
/// callers cannot supply an address or credentials.
/// </summary>
internal sealed record SshSessionRequest
{
    public SshSessionRequest(
        Guid workspaceId,
        Guid targetId,
        ExternalIdentity owner,
        string account,
        SshTerminalSize initialSize,
        string terminalType = "xterm-256color")
    {
        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException("A workspace ID is required.", nameof(workspaceId));
        }

        if (targetId == Guid.Empty)
        {
            throw new ArgumentException("A target ID is required.", nameof(targetId));
        }

        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalType);
        if (initialSize.Columns == 0 || initialSize.Rows == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialSize));
        }

        WorkspaceId = workspaceId;
        TargetId = targetId;
        Owner = owner;
        Account = account;
        InitialSize = initialSize;
        TerminalType = terminalType;
    }

    public Guid WorkspaceId { get; }
    public Guid TargetId { get; }
    public ExternalIdentity Owner { get; }
    public string Account { get; }
    public SshTerminalSize InitialSize { get; }
    public string TerminalType { get; }
}
