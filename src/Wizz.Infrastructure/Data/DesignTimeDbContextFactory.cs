namespace Wizz.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

/// <summary>
/// مصنع DbContext وقت التصميم للـ Migrations.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WizzDbContext>
{
    public WizzDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=wizz.db";

        var optionsBuilder = new DbContextOptionsBuilder<WizzDbContext>();
        optionsBuilder.UseSqlite(connectionString);

        return new WizzDbContext(optionsBuilder.Options);
    }
}