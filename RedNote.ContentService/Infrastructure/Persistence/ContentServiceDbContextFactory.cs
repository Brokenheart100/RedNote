using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RedNote.ContentService.Infrastructure.Persistence;

internal sealed class ContentServiceDbContextFactory
    : IDesignTimeDbContextFactory<ContentServiceDbContext>
{
    public ContentServiceDbContext CreateDbContext(
        string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__contentdb");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Host=localhost;" +
                "Port=6543;" +
                "Database=contentdb;" +
                "Username=postgres;" +
                "Password=cS85CY0A5+}0umePd8PgpY";
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<
                ContentServiceDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString);

        return new ContentServiceDbContext(
            optionsBuilder.Options);
    }
}