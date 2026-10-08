using Microsoft.EntityFrameworkCore;
using RedNote.UserService.Domain.Users;

namespace RedNote.UserService.Infrastructure.Persistence;

public sealed class UserServiceDbContext(
    DbContextOptions<UserServiceDbContext> options)
    : DbContext(options)
{
    public DbSet<Contracts.Admin.AdminAuditEntry> AdminAudit => Set<RedNote.Contracts.Admin.AdminAuditEntry>();
    public DbSet<UserRestriction> UserRestrictions => Set<RedNote.UserService.Domain.Users.UserRestriction>();
    public DbSet<UserProfile> UserProfiles =>
        Set<UserProfile>();

    public DbSet<UserFollow> UserFollows =>
        Set<UserFollow>();

    // Lock the existing profile to coordinate even the first restriction write.
    public async Task<UserProfile?> LockProfileForWriteAsync(Guid userId, CancellationToken ct)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("A user profile write lock requires an active transaction.");

        var profiles = await UserProfiles
            .FromSqlInterpolated($"SELECT * FROM \"UserProfiles\" WHERE \"UserId\" = {userId} FOR UPDATE")
            .ToListAsync(ct);
        return profiles.SingleOrDefault();
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<RedNote.Contracts.Admin.AdminAuditEntry>(entity =>
        {
            entity.HasIndex(entry => entry.CreatedAtUtc);
            entity.HasIndex(entry => new { entry.ActorUserId, entry.CreatedAtUtc });
            entity.HasIndex(entry => new { entry.TargetId, entry.CreatedAtUtc });
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("UserProfiles");

            entity.HasKey(profile => profile.UserId);

            entity.Property(profile => profile.Nickname)
                .HasMaxLength(64);

            entity.Property(profile => profile.AvatarUrl)
                .HasMaxLength(2048);

            entity.Property(profile => profile.Bio)
                .HasMaxLength(500);

            entity.Property(profile => profile.CreatedAtUtc)
                .IsRequired();

            entity.Property(profile => profile.UpdatedAtUtc)
                .IsRequired();
        });

        modelBuilder.Entity<UserFollow>(entity =>
        {
            entity.ToTable("UserFollows");

            entity.HasKey(follow => new
            {
                follow.FollowerUserId,
                follow.FollowingUserId
            });


            entity.Property(follow => follow.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(
                follow => follow.FollowingUserId);
        });
    }
}
