namespace Charac.Server.Data.Entities;

internal sealed class SshTarget
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Address { get; set; }
    public int Port { get; set; } = 22;
    public bool Enabled { get; set; }
    public Guid? UserCertificateAuthorityId { get; set; }
    public UserCertificateAuthority? UserCertificateAuthority { get; set; }
    public List<SshHostKey> HostKeys { get; set; } = [];
}
