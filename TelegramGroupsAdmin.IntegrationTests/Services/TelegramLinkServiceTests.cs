using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.IntegrationTests.Services;

/// <summary>
/// Integration tests for TelegramLinkService.
/// Tests token generation and account unlinking with a real PostgreSQL database.
/// </summary>
[TestFixture]
public class TelegramLinkServiceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ITelegramLinkService? _linkService;
    private ITelegramLinkTokenRepository? _tokenRepo;
    private ITelegramUserMappingRepository? _mappingRepo;
    private IDataProtectionProvider? _dataProtectionProvider;

    // Test user IDs from golden dataset
    private const string TestUserId1 = GoldenDataset.Users.User1_Id;
    private const string TestUserId2 = GoldenDataset.Users.User2_Id;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Configure Data Protection with ephemeral keys
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}")));

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory
        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        // Register repositories
        services.AddScoped<ITelegramLinkTokenRepository, TelegramLinkTokenRepository>();
        services.AddScoped<ITelegramUserMappingRepository, TelegramUserMappingRepository>();

        // Register the service under test
        services.AddScoped<ITelegramLinkService, TelegramLinkService>();

        _serviceProvider = services.BuildServiceProvider();
        _dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();

        // Seed golden dataset (creates test users we'll reference)
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            await GoldenDataset.SeedAsync(context, _dataProtectionProvider);
        }

        // Create service and repository instances
        var scope = _serviceProvider.CreateScope();
        _linkService = scope.ServiceProvider.GetRequiredService<ITelegramLinkService>();
        _tokenRepo = scope.ServiceProvider.GetRequiredService<ITelegramLinkTokenRepository>();
        _mappingRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region GenerateLinkTokenAsync Tests

    [Test]
    public async Task GenerateLinkTokenAsync_ShouldCreateToken_WithCorrectProperties()
    {
        // Act
        var token = await _linkService!.GenerateLinkTokenAsync(TestUserId1);

        // Assert
        Assert.That(token, Is.Not.Null);
        Assert.That(token.Token, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Token.Length, Is.EqualTo(12), "Token should be 12 characters");
        Assert.That(token.UserId, Is.EqualTo(TestUserId1));
        Assert.That(token.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow));
        Assert.That(token.ExpiresAt, Is.LessThan(DateTimeOffset.UtcNow.AddMinutes(16)),
            "Token should expire within 15-16 minutes");
        Assert.That(token.UsedAt, Is.Null);
        Assert.That(token.UsedByTelegramId, Is.Null);
    }

    [Test]
    public async Task GenerateLinkTokenAsync_ShouldRevokeExistingTokens()
    {
        // Arrange - create first token
        var firstToken = await _linkService!.GenerateLinkTokenAsync(TestUserId1);

        // Act - create second token (should revoke first)
        var secondToken = await _linkService.GenerateLinkTokenAsync(TestUserId1);

        // Assert - tokens are different
        Assert.That(secondToken.Token, Is.Not.EqualTo(firstToken.Token));

        // Verify first token is revoked (not in active tokens)
        var activeTokens = (await _tokenRepo!.GetActiveTokensForUserAsync(TestUserId1)).ToList();
        Assert.That(activeTokens.Count, Is.EqualTo(1), "Should have exactly one active token");
        Assert.That(activeTokens.First().Token, Is.EqualTo(secondToken.Token),
            "Only the second token should be active");
    }

    [Test]
    public async Task GenerateLinkTokenAsync_TokenShouldBeUrlSafe()
    {
        // Act
        var token = await _linkService!.GenerateLinkTokenAsync(TestUserId1);

        // Assert - token should not contain URL-unsafe characters
        Assert.That(token.Token, Does.Not.Contain("+"));
        Assert.That(token.Token, Does.Not.Contain("/"));
        Assert.That(token.Token, Does.Not.Contain("="));
    }

    #endregion

    #region UnlinkAccountAsync Tests

    [Test]
    public async Task UnlinkAccountAsync_ShouldReturnFalse_WhenMappingNotFound()
    {
        // Act - try to unlink non-existent mapping
        var result = await _linkService!.UnlinkAccountAsync(99999, TestUserId1);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UnlinkAccountAsync_ShouldReturnFalse_WhenMappingBelongsToAnotherUser()
    {
        // Arrange - create mapping for User1
        var mapping = new TelegramUserMappingRecord(
            Id: 0,
            TelegramId: 123456789,
            TelegramUsername: "testuser",
            UserId: TestUserId1,
            LinkedAt: DateTimeOffset.UtcNow,
            IsActive: true
        );
        var mappingId = await _mappingRepo!.InsertAsync(mapping);

        // Act - try to unlink as User2 (not the owner)
        var result = await _linkService!.UnlinkAccountAsync(mappingId, TestUserId2);

        // Assert - should fail because mapping belongs to User1
        Assert.That(result, Is.False);

        // Verify mapping is still active
        var userMappings = await _mappingRepo.GetByUserIdAsync(TestUserId1);
        Assert.That(userMappings.Any(m => m.Id == mappingId && m.IsActive), Is.True,
            "Mapping should still be active");
    }

    [Test]
    public async Task UnlinkAccountAsync_ShouldSucceed_WhenMappingBelongsToUser()
    {
        // Arrange - create mapping for User1
        var mapping = new TelegramUserMappingRecord(
            Id: 0,
            TelegramId: 987654321,
            TelegramUsername: "unlinktest",
            UserId: TestUserId1,
            LinkedAt: DateTimeOffset.UtcNow,
            IsActive: true
        );
        var mappingId = await _mappingRepo!.InsertAsync(mapping);

        // Act - unlink as User1 (the owner)
        var result = await _linkService!.UnlinkAccountAsync(mappingId, TestUserId1);

        // Assert - should succeed
        Assert.That(result, Is.True);

        // Verify mapping is deactivated
        var userMappings = await _mappingRepo.GetByUserIdAsync(TestUserId1);
        Assert.That(userMappings.Any(m => m.Id == mappingId && m.IsActive), Is.False,
            "Mapping should no longer be active");
    }

    #endregion
}
