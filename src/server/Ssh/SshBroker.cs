using Charac.Server.Authentication;

namespace Charac.Server.Ssh;

/// <summary>Opens one password-authenticated SSH shell for an already validated identity.</summary>
internal sealed class SshBroker(SshAccessResolver access, SshSessionRegistry sessions, SshKeyStore keys)
{
    public async Task<SshSession> OpenWithPasswordAsync(
        SshSessionRequest request, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var decision = await ResolveAsync(request, cancellationToken);

        var session = await SshPasswordConnection.OpenAsync(
            request.WorkspaceId, request.TargetId, decision.Address, decision.Port, decision.Account,
            decision.HostPublicKeys, password, request.InitialSize, request.TerminalType,
            sessions.Remove, cancellationToken);
        return Register(session);
    }

    public async Task<SshSession> OpenWithKeyAsync(
        SshSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var decision = await ResolveAsync(request, cancellationToken);
        if (decision.SshLoginKeyId is null || decision.SshLoginKeyFileName is null)
            throw new InvalidOperationException("No SSH login key is registered for this grant.");
        var privateKey = keys.Load(decision.SshLoginKeyFileName);
        try
        {
            var session = await SshKeyConnection.OpenAsync(request.WorkspaceId, request.TargetId,
                decision.Address, decision.Port, decision.Account, decision.HostPublicKeys,
                privateKey, request.InitialSize, request.TerminalType, sessions.Remove, cancellationToken);
            return Register(session);
        }
        catch
        {
            privateKey.Dispose();
            throw;
        }
    }

    private async Task<SshAccessDecision> ResolveAsync(SshSessionRequest request,
        CancellationToken cancellationToken)
    {
        var decision = await access.ResolveAsync(request.Owner, request.TargetId, cancellationToken)
            ?? throw new UnauthorizedAccessException("SSH target access was denied.");
        if (!string.Equals(decision.Account, request.Account, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The requested SSH account is not authorized.");
        return decision;
    }

    private SshSession Register(SshSession session)
    {
        try
        {
            sessions.Add(session);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }
}
