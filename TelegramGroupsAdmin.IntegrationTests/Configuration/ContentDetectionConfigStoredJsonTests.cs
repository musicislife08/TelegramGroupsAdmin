using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Configuration;

/// <summary>
/// Configs stored before a setting existed must read as that setting's default. Anchor: the
/// canonical global config row (<see cref="GoldenDatasetConstants.ContentDetectionConfigs.GlobalRowId"/>),
/// whose JSON has no <c>HamSkipThreshold</c> key.
/// </summary>
[TestFixture]
public class ContentDetectionConfigStoredJsonTests
{
    private MigrationTestHelper _helper = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_helper.ConnectionString));
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _helper.Dispose();
    }

    [Test]
    public async Task GetGlobalConfigAsync_StoredJsonWithoutHamSkipThreshold_ReadsDefault()
    {
        // Guard the precondition: the stored row predates the setting.
        var keyCount = await _helper.ExecuteScalarAsync<long>(
            $"""
            SELECT count(*) FROM content_detection_configs
            WHERE id = {GoldenDatasetConstants.ContentDetectionConfigs.GlobalRowId}
              AND (config_json -> 'ImageSpam' ? 'HamSkipThreshold' OR config_json -> 'VideoSpam' ? 'HamSkipThreshold')
            """);
        Assert.That(keyCount, Is.Zero, "canonical global config must not carry HamSkipThreshold");

        await using var scope = _provider.CreateAsyncScope();
        var config = await scope.ServiceProvider.GetRequiredService<IContentDetectionConfigRepository>().GetGlobalConfigAsync();

        using (Assert.EnterMultipleScope())
        {
            // Proves the stored row was read, not the repository's fallback defaults.
            Assert.That(config.ImageSpam.OcrConfidenceThreshold,
                Is.EqualTo(GoldenDatasetConstants.ContentDetectionConfigs.GlobalImageOcrConfidenceThreshold));
            Assert.That(config.ImageSpam.HamSkipThreshold, Is.EqualTo(ImageContentConfig.DefaultHamSkipThreshold));
            Assert.That(config.VideoSpam.HamSkipThreshold, Is.EqualTo(VideoContentConfig.DefaultHamSkipThreshold));
        }
    }

    [Test]
    public async Task UpdateGlobalConfigAsync_HamSkipThreshold_RoundTrips()
    {
        await using var scope = _provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IContentDetectionConfigRepository>();
        var config = await repository.GetGlobalConfigAsync();
        config.ImageSpam.HamSkipThreshold = 0.97;
        config.VideoSpam.HamSkipThreshold = 0.91;

        await repository.UpdateGlobalConfigAsync(config);
        var reloaded = await repository.GetGlobalConfigAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.ImageSpam.HamSkipThreshold, Is.EqualTo(0.97));
            Assert.That(reloaded.VideoSpam.HamSkipThreshold, Is.EqualTo(0.91));
        }
    }
}
