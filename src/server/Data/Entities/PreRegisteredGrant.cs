namespace Charac.Server.Data.Entities;

/// <summary>Grant reserved for an OIDC username before its subject is known.</summary>
internal sealed class PreRegisteredGrant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Issuer { get; set; }
    public required string UserName { get; set; }
    public Guid TargetId { get; set; }
    public SshTarget Target { get; set; } = null!;
    public required string Account { get; set; }
    public Guid EndpointAccountId { get; set; }
    public SshEndpointAccount EndpointAccount { get; set; } = null!;
    public Guid? SshLoginKeyId { get; set; }
    public SshLoginKey? SshLoginKey { get; set; }
    public bool Enabled { get; set; }
}
