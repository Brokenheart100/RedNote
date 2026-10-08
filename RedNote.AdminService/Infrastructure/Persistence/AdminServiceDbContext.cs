using Microsoft.EntityFrameworkCore;
using RedNote.AdminService.Domain.Audit;

namespace RedNote.AdminService.Infrastructure.Persistence;

public sealed class AdminServiceDbContext(DbContextOptions<AdminServiceDbContext> options) : DbContext(options)
{
    public DbSet<AdminAuditProjection> Audit => Set<AdminAuditProjection>();

    // Conflict handling is atomic and preserves the original audit on duplicate delivery/import.
    public Task<int> InsertAuditIfAbsentAsync(AdminAuditProjection entry, CancellationToken ct) =>
        Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AdminAuditProjections"
                ("Source", "Id", "ActorUserId", "Action", "TargetType", "TargetId", "Reason", "Change", "TraceId", "CreatedAtUtc")
            VALUES ({entry.Source}, {entry.Id}, {entry.ActorUserId}, {entry.Action}, {entry.TargetType},
                {entry.TargetId}, {entry.Reason}, {entry.Change}, {entry.TraceId}, {entry.CreatedAtUtc.ToUniversalTime()})
            ON CONFLICT ("Source", "Id") DO NOTHING
            """, ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<AdminAuditProjection>();
        entry.ToTable("AdminAuditProjections");
        entry.HasKey(x => new { x.Source, x.Id });
        entry.Property(x => x.Source).HasMaxLength(20);
        entry.Property(x => x.Action).HasMaxLength(80);
        entry.Property(x => x.TargetType).HasMaxLength(30);
        entry.Property(x => x.Reason).HasMaxLength(500);
        entry.Property(x => x.Change).HasMaxLength(1000);
        entry.Property(x => x.TraceId).HasMaxLength(64);
        entry.HasIndex(x => new { x.CreatedAtUtc, x.Id });
        entry.HasIndex(x => new { x.Source, x.CreatedAtUtc });
        entry.HasIndex(x => new { x.ActorUserId, x.CreatedAtUtc });
        entry.HasIndex(x => new { x.TargetId, x.CreatedAtUtc });
    }
}
