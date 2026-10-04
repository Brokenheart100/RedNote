using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RedNote.MediaService.Infrastructure.Persistence;

public sealed class MediaServiceDbContextFactory
    : IDesignTimeDbContextFactory<MediaServiceDbContext>
{
    private const string ConnectionStringEnvironmentVariable =
        "ConnectionStrings__mediadb";
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=6543;Database=mediadb;Username=postgres;Password=cS85CY0A5+}0umePd8PgpY";

    public MediaServiceDbContext CreateDbContext(
        string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DesignTimeConnectionString;
        }

        var optionsBuilder = new DbContextOptionsBuilder<MediaServiceDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString);

        return new MediaServiceDbContext(
            optionsBuilder.Options);
    }
}