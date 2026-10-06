using Microsoft.EntityFrameworkCore;
using Charac.Server.Data;
using Charac.Server.Data.Entities;

namespace Charac.Server.Authentication;

/// <summary>Persists the readable OIDC username and consumes matching pre-registrations.</summary>
internal sealed class OidcIdentityBinding(AccessDbContext database)
{
    public async Task BindAsync(ExternalIdentity external, CancellationToken cancellationToken)
    {
        var identity = await database.Identities.SingleOrDefaultAsync(x =>
            x.Issuer == external.Issuer && x.Subject == external.Subject, cancellationToken);
        if (identity is { Enabled: false })
            return;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (identity is null)
        {
            identity = new AccessIdentity
            {
                Issuer = external.Issuer,
                Subject = external.Subject,
                UserName = external.UserName,
                Enabled = true
            };
            database.Identities.Add(identity);
        }
        else if (identity.UserName is null && external.UserName is not null)
        {
            identity.UserName = external.UserName;
        }

        if (external.UserName is not null)
        {
            var pending = await database.PreRegisteredGrants
                .Where(x => x.Issuer == external.Issuer && x.UserName == external.UserName && x.Enabled)
                .Include(x => x.EndpointAccount)
                .ToArrayAsync(cancellationToken);
            foreach (var reservation in pending)
            {
                var existing = await database.Grants.SingleOrDefaultAsync(x =>
                    x.IdentityId == identity.Id && x.TargetId == reservation.TargetId, cancellationToken);
                if (existing is null)
                {
                    database.Grants.Add(new AccessGrant
                    {
                        Identity = identity,
                        IdentityId = identity.Id,
                        EndpointAccount = reservation.EndpointAccount,
                        EndpointAccountId = reservation.EndpointAccountId,
                        TargetId = reservation.TargetId,
                        Account = reservation.EndpointAccount.Account,
                        CertificatePrincipal = reservation.EndpointAccount.CertificatePrincipal,
                        SshLoginKeyId = reservation.EndpointAccount.SshLoginKeyId,
                        Enabled = true
                    });
                }
                database.PreRegisteredGrants.Remove(reservation);
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
