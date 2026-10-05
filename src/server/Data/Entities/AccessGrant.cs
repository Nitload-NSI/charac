namespace Charac.Server.Data.Entities;

/// <summary>One external identity maps to one OS account per target in the first schema.</summary>
internal sealed class AccessGrant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdentityId { get; set; }
    public AccessIdentity Identity { get; set; } = null!;
    public Guid TargetId { get; set; }
    public SshTarget Target { get; set; } = null!;
    public required string Account { get; set; }
    public Guid? SshLoginKeyId { get; set; }
    public SshLoginKey? SshLoginKey { get; set; }
    public string? CertificatePrincipal { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
