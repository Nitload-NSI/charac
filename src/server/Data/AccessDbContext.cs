using Microsoft.EntityFrameworkCore;
using Charac.Server.Data.Entities;

namespace Charac.Server.Data;

internal sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public DbSet<AccessIdentity> Identities => Set<AccessIdentity>();
    public DbSet<UserCertificateAuthority> UserCertificateAuthorities => Set<UserCertificateAuthority>();
    public DbSet<SshTarget> Targets => Set<SshTarget>();
    public DbSet<SshHostKey> HostKeys => Set<SshHostKey>();
    public DbSet<SshLoginKey> LoginKeys => Set<SshLoginKey>();
    public DbSet<WorkspaceRecord> WorkspaceRecords => Set<WorkspaceRecord>();
    public DbSet<AccessGrant> Grants => Set<AccessGrant>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("access");

        model.Entity<AccessIdentity>(entity =>
        {
            entity.ToTable("identities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Issuer).HasMaxLength(512);
            entity.Property(x => x.Subject).HasMaxLength(256);
            entity.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        });

        model.Entity<UserCertificateAuthority>(entity =>
        {
            entity.ToTable("user_certificate_authorities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.PublicKey).HasMaxLength(2048);
            entity.Property(x => x.SigningKeyReference).HasMaxLength(1024);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        model.Entity<SshTarget>(entity =>
        {
            entity.ToTable("targets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Address).HasMaxLength(253);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasOne(x => x.UserCertificateAuthority).WithMany()
                .HasForeignKey(x => x.UserCertificateAuthorityId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<SshHostKey>(entity =>
        {
            entity.ToTable("host_keys");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PublicKey).HasMaxLength(2048);
            entity.HasIndex(x => new { x.TargetId, x.PublicKey }).IsUnique();
            entity.HasOne(x => x.Target).WithMany(x => x.HostKeys)
                .HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<SshLoginKey>(entity =>
        {
            entity.ToTable("ssh_login_keys");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.FileName).HasMaxLength(255);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => x.FileName).IsUnique();
        });

        model.Entity<WorkspaceRecord>(entity =>
        {
            entity.ToTable("workspace_records");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Issuer).HasMaxLength(512);
            entity.Property(x => x.Subject).HasMaxLength(256);
            entity.Property(x => x.Account).HasMaxLength(256);
            entity.Property(x => x.Authentication).HasMaxLength(16);
            entity.Property(x => x.EndReason).HasMaxLength(32);
            entity.HasIndex(x => new { x.Issuer, x.Subject, x.OpenedAt });
        });

        model.Entity<AccessGrant>(entity =>
        {
            entity.ToTable("grants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Account).HasMaxLength(256);
            entity.Property(x => x.CertificatePrincipal).HasMaxLength(256);
            entity.HasIndex(x => new { x.IdentityId, x.TargetId }).IsUnique();
            entity.HasOne(x => x.Identity).WithMany()
                .HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Target).WithMany()
                .HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SshLoginKey).WithMany()
                .HasForeignKey(x => x.SshLoginKeyId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateChanges()
    {
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case AccessIdentity identity:
                    RequireId(identity.Id);
                    RequireText(identity.Issuer, 512);
                    RequireText(identity.Subject, 256);
                    break;
                case UserCertificateAuthority authority:
                    RequireId(authority.Id);
                    RequireText(authority.Name, 128);
                    RequireText(authority.PublicKey, 2048);
                    RequireText(authority.SigningKeyReference, 1024);
                    break;
                case SshTarget target:
                    RequireId(target.Id);
                    if (target.UserCertificateAuthorityId is { } authorityId)
                        RequireId(authorityId);
                    RequireText(target.Name, 128);
                    RequireText(target.Address, 253);
                    if (target.Port is < 1 or > 65535)
                        throw new InvalidOperationException("SSH port must be between 1 and 65535.");
                    break;
                case SshHostKey key:
                    RequireId(key.Id);
                    RequireId(key.TargetId);
                    RequireText(key.PublicKey, 2048);
                    break;
                case SshLoginKey loginKey:
                    RequireId(loginKey.Id);
                    RequireText(loginKey.Name, 128);
                    RequireText(loginKey.FileName, 255);
                    if (!Charac.Server.Ssh.SshKeyStore.IsValidFileName(loginKey.FileName))
                        throw new InvalidOperationException("SSH login key file name must be a plain file name.");
                    break;
                case WorkspaceRecord workspace:
                    RequireId(workspace.Id);
                    RequireId(workspace.TargetId);
                    RequireText(workspace.Issuer, 512);
                    RequireText(workspace.Subject, 256);
                    RequireText(workspace.Account, 256);
                    RequireText(workspace.Authentication, 16);
                    if (workspace.EndReason is not null)
                        RequireText(workspace.EndReason, 32);
                    if (workspace.OpenedAt.Offset != TimeSpan.Zero ||
                        workspace.ClosedAt is { Offset: var closedOffset } && closedOffset != TimeSpan.Zero)
                        throw new InvalidOperationException("Workspace timestamps must use UTC.");
                    break;
                case AccessGrant grant:
                    RequireId(grant.Id);
                    RequireId(grant.IdentityId);
                    RequireId(grant.TargetId);
                    if (grant.SshLoginKeyId is { } loginKeyId)
                        RequireId(loginKeyId);
                    RequireText(grant.Account, 256);
                    if (grant.CertificatePrincipal is not null)
                        RequireText(grant.CertificatePrincipal, 256);
                    if (grant.ExpiresAt is { Offset: var offset } && offset != TimeSpan.Zero)
                        throw new InvalidOperationException("Grant expiry must use UTC.");
                    break;
            }
        }
    }

    private static void RequireId(Guid value)
    {
        if (value == Guid.Empty)
            throw new InvalidOperationException("Management record IDs must not be empty.");
    }

    private static void RequireText(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new InvalidOperationException("A management field is empty or exceeds its length limit.");
    }
}
