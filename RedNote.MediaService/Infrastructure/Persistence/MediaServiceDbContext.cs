using Microsoft.EntityFrameworkCore;
using RedNote.MediaService.Domain.Media;

namespace RedNote.MediaService.Infrastructure.Persistence;

public sealed class MediaServiceDbContext(
    DbContextOptions<MediaServiceDbContext> options)
    : DbContext(options)
{
    public DbSet<MediaAsset> MediaAssets =>
        Set<MediaAsset>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.ToTable("MediaAssets");

            entity.HasKey(media => media.Id);

            entity.Property(media => media.FileName)
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(media => media.ContentType)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(media => media.ObjectKey)
                .HasMaxLength(1024)
                .IsRequired();

            entity.Property(media => media.Size)
                .IsRequired();

            entity.Property(media => media.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(
                media => new
                {
                    media.OwnerUserId,
                    media.CreatedAtUtc
                });
        });
    }
}