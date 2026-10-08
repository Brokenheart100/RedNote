using Microsoft.EntityFrameworkCore;
using RedNote.SearchService.Domain.History;

namespace RedNote.SearchService.Infrastructure.Persistence;

public sealed class SearchServiceDbContext(DbContextOptions<SearchServiceDbContext> options) : DbContext(options)
{
    public DbSet<SearchHistoryEntry> SearchHistory => Set<SearchHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SearchHistoryEntry>(entity =>
        {
            entity.ToTable("SearchHistory", "search");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Keyword).HasMaxLength(100).IsRequired();
            entity.Property(entry => entry.NormalizedKeyword).HasMaxLength(200).IsRequired();
            entity.HasIndex(entry => new { entry.UserId, entry.NormalizedKeyword }).IsUnique();
            entity.HasIndex(entry => new { entry.UserId, entry.LastSearchedAtUtc });
        });
    }
}
