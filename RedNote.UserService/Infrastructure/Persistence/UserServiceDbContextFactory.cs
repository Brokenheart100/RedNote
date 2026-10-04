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
        "Username=postgres;" +
        "Password=cS85CY0A5+}0umePd8PgpY";

    public UserServiceDbContext CreateDbContext(
        string[] args)
    {
        var optionsBuilder =
            new DbContextOptionsBuilder<UserServiceDbContext>();

        optionsBuilder.UseNpgsql(
            DesignTimeConnectionString);

        return new UserServiceDbContext(
            optionsBuilder.Options);
    }
}