using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RedNote.UserService.Infrastructure.Persistence;

public sealed class UserServiceDbContextFactory
    : IDesignTimeDbContextFactory<UserServiceDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;" +
        "Port=6543;" +
        "Database=userdb;" +
        "Username=postgres";

    public UserServiceDbContext CreateDbContext(
        string[] args)
    {
        var optionsBuilder =
            new DbContextOptionsBuilder<UserServiceDbContext>();

        // Aspire injects the actual database credentials, including during EF migration commands.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__userdb");
        optionsBuilder.UseNpgsql(
            string.IsNullOrWhiteSpace(connectionString) ? DesignTimeConnectionString : connectionString);

        return new UserServiceDbContext(
            optionsBuilder.Options);
    }
}
