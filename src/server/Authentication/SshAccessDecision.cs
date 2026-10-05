namespace Charac.Server.Authentication;

/// <summary>A point-in-time authorization result, not a client token or a reusable authorization cache.</summary>
internal sealed record SshAccessDecision(
    Guid GrantId,
    Guid TargetId,
    string Address,
    int Port,
    string Account,
    string? CertificatePrincipal,
    Guid? UserCertificateAuthorityId,
    string? UserCertificateAuthorityPublicKey,
    string? SigningKeyReference,
    IReadOnlyList<string> HostPublicKeys,
    DateTimeOffset? GrantExpiresAt,
    Guid? SshLoginKeyId,
    string? SshLoginKeyFileName);
