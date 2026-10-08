using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RedNote.RecommendationService.Infrastructure.Persistence;

public sealed class RecommendationItem
{
    public Guid PostId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string[] Tags { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsPublished { get; set; }
    public bool IsHidden { get; set; }
    public long SourceRevision { get; set; }
    public long LastAcknowledgedRevision { get; set; }
}
public sealed class RecommendationFeedbackState
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public bool IsActive { get; set; }
    public long SourceRevision { get; set; }
    public long Version { get; set; }
    public long LastAcknowledgedRevision { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
public sealed class FeedbackReceipt
{
    public Guid RequestId { get; set; }
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public string Type { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}
public sealed class ProjectionCheckpoint
{
    public string Name { get; set; } = "";
    public Guid RunId { get; set; }
    public Guid? AfterId { get; set; }
    public string Phase { get; set; } = "catalog";
    public string Cursor { get; set; } = "";
}
public sealed class RecommendationDbContext(DbContextOptions<RecommendationDbContext> options) : DbContext(options)
{
    public DbSet<RecommendationItem> Items => Set<RecommendationItem>();
    public DbSet<RecommendationFeedbackState> Feedback => Set<RecommendationFeedbackState>();
    public DbSet<FeedbackReceipt> Receipts => Set<FeedbackReceipt>();
    public DbSet<ProjectionCheckpoint> Checkpoints => Set<ProjectionCheckpoint>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecommendationItem>(e =>
        {
            e.HasKey(x => x.PostId);
            e.Property(x => x.SourceRevision).IsConcurrencyToken();
            e.HasIndex(x => new { x.IsPublished, x.IsHidden, x.CreatedAtUtc });
        });
        modelBuilder.Entity<RecommendationFeedbackState>(e =>
        {
            e.HasKey(x => new { x.PostId, x.UserId, x.Type });
            e.Property(x => x.Type).HasMaxLength(16);
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<FeedbackReceipt>(e =>
        {
            e.HasKey(x => new { x.RequestId, x.UserId, x.PostId, x.Type });
            e.Property(x => x.Type).HasMaxLength(16);
            e.HasIndex(x => x.RecordedAtUtc);
        });
        modelBuilder.Entity<ProjectionCheckpoint>(e => { e.HasKey(x => x.Name); e.Property(x => x.Name).HasMaxLength(64); });
    }
}
internal sealed class RecommendationDbContextFactory : IDesignTimeDbContextFactory<RecommendationDbContext>
{
    public RecommendationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<RecommendationDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__recommendationdb")
            ?? "Host=localhost;Port=6543;Database=recommendationdb;Username=postgres").Options);
}
