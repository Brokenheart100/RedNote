using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RedNote.AdminService.Infrastructure.Persistence;

public sealed class AdminServiceDbContextFactory : IDesignTimeDbContextFactory<AdminServiceDbContext>
{
    public AdminServiceDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AdminServiceDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__admindb")
            ?? "Host=localhost;Port=6543;Database=admindb;Username=postgres").Options);
}
