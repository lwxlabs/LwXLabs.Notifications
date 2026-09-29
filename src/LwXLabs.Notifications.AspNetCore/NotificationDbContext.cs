using Flow.EntityFrameworkCore;
using LwXLab.Data.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LwXLabs.Notifications.AspNetCore;

public sealed class NotificationDbContext : LwXDbContext<NotificationDbContext>
{
    public NotificationDbContext(
        DbContextOptions<NotificationDbContext> options,
        LwXDbContextDescriptor<NotificationDbContext> descriptor)
        : base(options, descriptor) { }

    public DbSet<NotificationApplicationEntity> Applications => Set<NotificationApplicationEntity>();
    public DbSet<NotificationInstallationEntity> Installations => Set<NotificationInstallationEntity>();
    public DbSet<NotificationItemEntity> Notifications => Set<NotificationItemEntity>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.AddFlowMessaging();

        modelBuilder.Entity<NotificationApplicationEntity>(entity =>
        {
            entity.ToTable("notification_apps");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ApiKeyHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<NotificationInstallationEntity>(entity =>
        {
            entity.ToTable("notification_installations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.InstallationId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.UserId).HasMaxLength(200);
            entity.Property(x => x.Platform).HasMaxLength(64);
            entity.Property(x => x.PushEndpoint).HasMaxLength(2048);
            entity.Property(x => x.PushEndpointKind).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.ConnectionTokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.InstallationId }).IsUnique();
            entity.HasIndex(x => x.ConnectionTokenHash).IsUnique();
            entity.HasIndex(x => new { x.ApplicationId, x.UserId });
        });

        modelBuilder.Entity<NotificationItemEntity>(entity =>
        {
            entity.ToTable("notification_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TargetKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.TargetValue).HasMaxLength(256).IsRequired();
            entity.Property(x => x.DeliveryMode).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.DedupeKey).HasMaxLength(256);
            entity.Property(x => x.DedupeIdentity).HasMaxLength(300).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.DedupeIdentity }).IsUnique();
            entity.HasIndex(x => new { x.ApplicationId, x.DedupeKey });
            entity.HasIndex(x => new { x.Status, x.ExpiresAt });
        });
    }
}
