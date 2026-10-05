namespace Charac.Server.Data.Entities;

/// <summary>An explicitly enrolled host public key, not a user login key or certificate authority.</summary>
internal sealed class SshHostKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TargetId { get; set; }
    public SshTarget Target { get; set; } = null!;
    public required string PublicKey { get; set; }
    public bool Enabled { get; set; }
}
