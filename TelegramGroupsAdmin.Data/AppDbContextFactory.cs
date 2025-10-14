using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TelegramGroupsAdmin.Data;

/// <summary>
/// Design-time factory for EF Core migrations
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Use a dummy connection string for design-time operations
        // Actual connection string comes from configuration at runtime
        optionsBuilder.UseNpgsql("Host=localhost;Database=telegram_groups_admin;Username=tgadmin;Password=changeme");

        return new AppDbContext(optionsBuilder.Options);
    }
}
