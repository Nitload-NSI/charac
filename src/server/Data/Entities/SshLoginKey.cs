namespace Charac.Server.Data.Entities;

/// <summary>Metadata for a server-owned SSH login key; private material stays in a protected file.</summary>
internal sealed class SshLoginKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string FileName { get; set; }
    public bool Enabled { get; set; }
}
