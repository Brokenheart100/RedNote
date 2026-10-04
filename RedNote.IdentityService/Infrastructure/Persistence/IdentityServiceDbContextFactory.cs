using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RedNote.IdentityService.Infrastructure.Persistence;

/// <summary>
/// 为 EF Core CLI 提供设计时 DbContext。
/// </summary>
public sealed class IdentityServiceDbContextFactory
    : IDesignTimeDbContextFactory<IdentityServiceDbContext>
{
    private const string ConnectionStringEnvironmentVariable =
        "ConnectionStrings__identitydb";

    private const string DesignTimeConnectionString =
        "Host=localhost;Port=6543;Database=identitydb;Username=postgres;Password=cS85CY0A5+}0umePd8PgpY";

    public IdentityServiceDbContext CreateDbContext(
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
            new DbContextOptionsBuilder<
                IdentityServiceDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString);

        return new IdentityServiceDbContext(
            optionsBuilder.Options);
    }
}