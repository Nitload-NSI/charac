namespace Charac.Server.Data.Entities;

internal sealed class AccessIdentity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public bool Enabled { get; set; }
}
