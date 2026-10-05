namespace Charac.Server.Data.Entities;

/// <summary>Durable history; a row is never proof that an SSH channel is still alive.</summary>
internal sealed class WorkspaceRecord
{
    public Guid Id { get; set; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public Guid TargetId { get; set; }
    public required string Account { get; set; }
    public required string Authentication { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? EndReason { get; set; }
}
