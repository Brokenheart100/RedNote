using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RedNote.IdentityService.Domain.Users;

namespace RedNote.IdentityService.Infrastructure.Persistence;

public sealed class IdentityServiceDbContext(
    DbContextOptions<IdentityServiceDbContext> options)
    : IdentityDbContext<
        ApplicationUser,
        IdentityRole<Guid>,
        Guid>(options)
{
    protected override void OnModelCreating(
        ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.UseOpenIddict();

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.CreatedAtUtc)
                .IsRequired();

            entity.Property(user => user.DisplayName)
                .HasMaxLength(64);

            entity.Property(user => user.FamilyName)
                .HasMaxLength(64);
        });
    }
}