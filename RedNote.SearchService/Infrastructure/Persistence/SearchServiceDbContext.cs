using Microsoft.EntityFrameworkCore;

namespace RedNote.SearchService.Infrastructure.Persistence;

public sealed class SearchServiceDbContext(
    DbContextOptions<SearchServiceDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }
}