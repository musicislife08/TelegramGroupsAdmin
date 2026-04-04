using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for UserRepository (web user management).
/// Validates AnyUsersExistAsync against a real PostgreSQL database.
/// </summary>
[TestFixture]
[Category("Integration")]
public class UserRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    [Test]
    public async Task AnyUsersExistAsync_EmptyDatabase_ReturnsFalse()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddScoped<IUserRepository, UserRepository>();

        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var result = await repo.AnyUsersExistAsync(cancellationToken: CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task AnyUsersExistAsync_WithExistingUser_ReturnsTrue()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(
                Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}")));

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        services.AddScoped<IUserRepository, UserRepository>();

        _serviceProvider = services.BuildServiceProvider();

        // Seed golden dataset (creates web users in the users table)
        var dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken: CancellationToken.None);
        await GoldenDataset.SeedAsync(context, dataProtectionProvider);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var result = await repo.AnyUsersExistAsync(cancellationToken: CancellationToken.None);

        Assert.That(result, Is.True);
    }
}
