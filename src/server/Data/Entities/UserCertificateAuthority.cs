namespace Charac.Server.Data.Entities;

/// <summary>User certificate issuer. Private key material is stored outside this database.</summary>
internal sealed class UserCertificateAuthority
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string PublicKey { get; set; }
    public required string SigningKeyReference { get; set; }
    public bool Enabled { get; set; }
}
