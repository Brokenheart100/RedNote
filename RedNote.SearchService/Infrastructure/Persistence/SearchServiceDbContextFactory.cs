using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RedNote.SearchService.Infrastructure.Persistence;

public sealed class SearchServiceDbContextFactory
    : IDesignTimeDbContextFactory<SearchServiceDbContext>
{
    private const string ConnectionStringEnvironmentVariable =
        "ConnectionStrings__searchdb";

    private const string DesignTimeConnectionString =
        "Host=localhost;Port=6543;Database=searchdb;Username=postgres;Password=cS85CY0A5+}0umePd8PgpY";

    public SearchServiceDbContext CreateDbContext(
        string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                DesignTimeConnectionString;
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<SearchServiceDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString);

        return new SearchServiceDbContext(
            optionsBuilder.Options);
    }
}