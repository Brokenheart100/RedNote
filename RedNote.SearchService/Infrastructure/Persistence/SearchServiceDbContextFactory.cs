using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RedNote.SearchService.Infrastructure.Persistence;

public sealed class SearchServiceDbContextFactory : IDesignTimeDbContextFactory<SearchServiceDbContext>
{
    public SearchServiceDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SearchServiceDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__searchdb")
                ?? "Host=localhost;Port=6543;Database=searchdb;Username=postgres")
            .Options);
}
