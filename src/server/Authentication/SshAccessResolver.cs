using Microsoft.EntityFrameworkCore;
using Charac.Server.Data;
using Charac.Server.Data.Entities;

namespace Charac.Server.Authentication;

internal sealed record SshTargetResource(Guid Id, string Name, string Account);

/// <summary>Queries current grants only after OIDC validation.</summary>
internal sealed class SshAccessResolver(AccessDbContext database, TimeProvider clock)
{
    public Task<SshTargetResource[]> ListTargetsAsync(ExternalIdentity identity,
        CancellationToken cancellationToken = default) =>
        AuthorizedGrants(identity).OrderBy(x => x.Target.Name).ThenBy(x => x.TargetId)
            .Select(x => new SshTargetResource(x.TargetId, x.Target.Name, x.EndpointAccount.Account))
            .ToArrayAsync(cancellationToken);

    public async Task<SshAccessDecision?> ResolveAsync(
        ExternalIdentity identity, Guid targetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (targetId == Guid.Empty)
            return null;

        var grant = await AuthorizedGrants(identity).AsSingleQuery()
            .Include(x => x.EndpointAccount).ThenInclude(x => x.SshLoginKey)
            .Include(x => x.Target).ThenInclude(x => x.UserCertificateAuthority)
            .Include(x => x.Target).ThenInclude(x => x.HostKeys.Where(key => key.Enabled))
            .Where(x => x.TargetId == targetId)
            .SingleOrDefaultAsync(cancellationToken);

        if (grant is null)
            return null;

        var target = grant.Target;
        return new SshAccessDecision(grant.Id, target.Id, target.Address, target.Port,
            grant.EndpointAccount.Account, grant.EndpointAccount.CertificatePrincipal, target.UserCertificateAuthorityId,
            target.UserCertificateAuthority?.PublicKey, target.UserCertificateAuthority?.SigningKeyReference,
            target.HostKeys.Select(x => x.PublicKey).ToArray(), grant.ExpiresAt,
            grant.EndpointAccount.SshLoginKeyId, grant.EndpointAccount.SshLoginKey?.FileName);
    }

    private IQueryable<AccessGrant> AuthorizedGrants(ExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var now = clock.GetUtcNow();
        return database.Grants.AsNoTracking().Where(x =>
            x.Identity.Issuer == identity.Issuer && x.Identity.Subject == identity.Subject &&
            x.Enabled && x.Identity.Enabled && x.Target.Enabled &&
            x.EndpointAccount.Enabled &&
            (x.EndpointAccount.SshLoginKeyId == null || x.EndpointAccount.SshLoginKey!.Enabled) &&
            (x.ExpiresAt == null || x.ExpiresAt > now) &&
            x.Target.HostKeys.Any(key => key.Enabled));
    }
}
