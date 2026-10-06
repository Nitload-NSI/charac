namespace Charac.Server.Data.Entities;

/// <summary>Shared backend SSH credentials for one target system account.</summary>
internal sealed class SshEndpointAccount
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TargetId { get; set; }
    public SshTarget Target { get; set; } = null!;
    public required string Account { get; set; }
    public Guid? SshLoginKeyId { get; set; }
    public SshLoginKey? SshLoginKey { get; set; }
    public string? CertificatePrincipal { get; set; }
    public bool Enabled { get; set; }
}
