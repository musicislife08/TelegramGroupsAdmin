using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Configuration;

/// <summary>
/// Integration tests for ConfigService that exercise the real DI graph end-to-end:
/// real ConfigRepository + real AuditService + real HybridCache + real PostgreSQL.
/// Verifies that mutations land in the configs table AND emit a row into audit_logs.
/// </summary>
[TestFixture]
public class ConfigServiceIntegrationTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IConfigService? _sut;
    private IDbContextFactory<AppDbContext>? _contextFactory;

    private static readonly Actor TestActor =
        Actor.FromWebUser(GoldenDataset.Users.User1_Id, GoldenDataset.Users.User1_Email);

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        var keyDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}"));
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(keyDirectory);

        var dataSource = new NpgsqlDataSourceBuilder(_testHelper.ConnectionString).Build();
        services.AddSingleton(dataSource);
        services.AddDbContextFactory<AppDbContext>((_, options) => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });
        services.AddHybridCache();

        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IConfigService, ConfigService>();

        _serviceProvider = services.BuildServiceProvider();

        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            await GoldenDataset.SeedWebUsersOnlyAsync(context);
        }

        _sut = _serviceProvider.GetRequiredService<IConfigService>();
        _contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    private async Task<int> CountAuditLogsAsync()
    {
        await using var context = await _contextFactory!.CreateDbContextAsync();
        return await context.AuditLogs.CountAsync();
    }

    private async Task<Data.Models.AuditLogRecordDto?> GetLatestAuditLogAsync()
    {
        await using var context = await _contextFactory!.CreateDbContextAsync();
        return await context.AuditLogs.OrderByDescending(a => a.Id).FirstOrDefaultAsync();
    }

    [Test]
    public async Task SaveWelcomeAsync_AppendsAuditLogRow()
    {
        var chat = new ChatIdentity(7777, "Test Chat");
        var config = new WelcomeConfig { Enabled = true, MainWelcomeMessage = "hi" };
        var actor = TestActor;

        var before = await CountAuditLogsAsync();
        await _sut!.SaveWelcomeAsync(chat, config, actor);
        var after = await CountAuditLogsAsync();

        Assert.That(after, Is.EqualTo(before + 1));
        var lastEntry = await GetLatestAuditLogAsync();
        Assert.That(lastEntry, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(lastEntry!.EventType, Is.EqualTo((Data.Models.AuditEventType)AuditEventType.ConfigurationChanged));
            Assert.That(lastEntry.Value, Does.Contain("Welcome"));
            Assert.That(lastEntry.Value, Does.Contain("Test Chat"));
        });
    }

    [Test]
    public async Task SaveBanCelebrationAsync_AppendsAuditLogRow()
    {
        var chat = new ChatIdentity(8888, "Celebration Chat");
        var config = new BanCelebrationConfig { Enabled = true };
        var actor = TestActor;

        var before = await CountAuditLogsAsync();
        await _sut!.SaveBanCelebrationAsync(chat, config, actor);
        var after = await CountAuditLogsAsync();

        Assert.That(after, Is.EqualTo(before + 1));
        var lastEntry = await GetLatestAuditLogAsync();
        Assert.That(lastEntry, Is.Not.Null);
        Assert.That(lastEntry!.Value, Does.Contain("BanCelebration"));
    }

    [Test]
    public async Task DeleteWelcomeAsync_AppendsAuditLogRow()
    {
        var chat = new ChatIdentity(9999, "Delete Test");
        var actor = TestActor;

        // First save so there's something to delete (and to skip past the save's audit row).
        await _sut!.SaveWelcomeAsync(chat, new WelcomeConfig(), actor);

        var before = await CountAuditLogsAsync();
        await _sut.DeleteWelcomeAsync(chat, actor);
        var after = await CountAuditLogsAsync();

        Assert.That(after, Is.EqualTo(before + 1));
        var lastEntry = await GetLatestAuditLogAsync();
        Assert.That(lastEntry, Is.Not.Null);
        Assert.That(lastEntry!.Value, Does.Contain("deleted"));
    }

    [Test]
    public async Task SaveBotTokenAsync_AuditValueIsTokenName_NotPlaintext()
    {
        const string secret = "1234567890:VERY-SECRET-TOKEN";
        var actor = TestActor;

        var before = await CountAuditLogsAsync();
        await _sut!.SaveBotTokenAsync(secret, actor);
        var after = await CountAuditLogsAsync();

        Assert.That(after, Is.EqualTo(before + 1));
        var lastEntry = await GetLatestAuditLogAsync();
        Assert.That(lastEntry, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(lastEntry!.Value, Is.EqualTo("TelegramBotToken"));
            Assert.That(lastEntry.Value, Does.Not.Contain(secret));
        });
    }
}
