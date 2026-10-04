using Microsoft.EntityFrameworkCore;
using RedNote.UserService.Domain.Users;

namespace RedNote.UserService.Infrastructure.Persistence;

public sealed class UserServiceDbContext(
    DbContextOptions<UserServiceDbContext> options)
    : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles =>
        Set<UserProfile>();

    public DbSet<UserFollow> UserFollows =>
        Set<UserFollow>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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