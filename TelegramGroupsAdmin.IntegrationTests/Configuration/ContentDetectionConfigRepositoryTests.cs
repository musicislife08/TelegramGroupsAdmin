using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Configuration;

/// <summary>
/// Integration tests for ContentDetectionConfigRepository.
/// Uses real PostgreSQL via Testcontainers to validate:
///
/// 1. TimeSpan → double (seconds) conversion when saving to database
/// 2. double (seconds) → TimeSpan conversion when loading from database
/// 3. Round-trip integrity for all timeout-related properties
/// 4. AlwaysRun flag persistence (the bug that prompted Issue #252)
///
/// These tests validate the fix for the Npgsql interval parsing issue
/// where EF Core's ToJson() couldn't deserialize TimeSpan from JSON.
/// </summary>
[TestFixture]
public class ContentDetectionConfigRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IContentDetectionConfigRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

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
        });

        // Register repository
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();

        _serviceProvider = services.BuildServiceProvider();

        // Create repository instance
        var scope = _serviceProvider.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<IContentDetectionConfigRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region Round-Trip Tests

    [Test]
    public async Task SaveAndLoad_CasTimeout_PreservesTimeSpan()
    {
        // Arrange
        var config = new ContentDetectionConfig
        {
            Cas = new CasConfig
            {
                Enabled = true,
                ApiUrl = "https://api.cas.chat",
                Timeout = TimeSpan.FromSeconds(10)
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.Cas.Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public async Task SaveAndLoad_ThreatIntelTimeout_PreservesTimeSpan()
    {
        // Arrange
        var config = new ContentDetectionConfig
        {
            ThreatIntel = new ThreatIntelConfig
            {
                Enabled = true,
                UseVirusTotal = true,
                Timeout = TimeSpan.FromSeconds(45)
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.ThreatIntel.Timeout, Is.EqualTo(TimeSpan.FromSeconds(45)));
    }

    [Test]
    public async Task SaveAndLoad_UrlBlocklistCacheDuration_PreservesTimeSpan()
    {
        // Arrange
        var config = new ContentDetectionConfig
        {
            UrlBlocklist = new UrlBlocklistConfig
            {
                Enabled = true,
                CacheDuration = TimeSpan.FromHours(12)
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.UrlBlocklist.CacheDuration, Is.EqualTo(TimeSpan.FromHours(12)));
    }

    [Test]
    public async Task SaveAndLoad_ImageSpamTimeout_PreservesTimeSpan()
    {
        // Arrange
        var config = new ContentDetectionConfig
        {
            ImageSpam = new ImageContentConfig
            {
                Enabled = true,
                Timeout = TimeSpan.FromSeconds(30)
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.ImageSpam.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public async Task SaveAndLoad_VideoSpamTimeout_PreservesTimeSpan()
    {
        // Arrange
        var config = new ContentDetectionConfig
        {
            VideoSpam = new VideoContentConfig
            {
                Enabled = true,
                Timeout = TimeSpan.FromMinutes(1)
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.VideoSpam.Timeout, Is.EqualTo(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public async Task SaveAndLoad_SeoScrapingTimeout_PreservesTimeSpan()
    {
        // Arrange
        var config = new ContentDetectionConfig
        {
            SeoScraping = new SeoScrapingConfig
            {
                Enabled = true,
                Timeout = TimeSpan.FromSeconds(15)
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.SeoScraping.Timeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
    }

    #endregion

    #region AlwaysRun Flag Tests (Issue #252 Root Cause)

    [Test]
    public async Task SaveAndLoad_AlwaysRunTrue_PreservesFlag()
    {
        // Arrange - This was the exact bug in Issue #252
        var config = new ContentDetectionConfig
        {
            UrlBlocklist = new UrlBlocklistConfig
            {
                Enabled = true,
                AlwaysRun = true
            },
            FileScanning = new FileScanningDetectionConfig
            {
                Enabled = true,
                AlwaysRun = true
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert - These were returning false before the fix
        Assert.Multiple(() =>
        {
            Assert.That(loaded.UrlBlocklist.AlwaysRun, Is.True, "UrlBlocklist.AlwaysRun should be persisted");
            Assert.That(loaded.FileScanning.AlwaysRun, Is.True, "FileScanning.AlwaysRun should be persisted");
        });
    }

    [Test]
    public async Task SaveAndLoad_AllAlwaysRunFlags_PreservesAll()
    {
        // Arrange
        var config = new ContentDetectionConfig
        {
            StopWords = new StopWordsConfig { AlwaysRun = true },
            Similarity = new SimilarityConfig { AlwaysRun = true },
            Cas = new CasConfig { AlwaysRun = true },
            Bayes = new BayesConfig { AlwaysRun = true },
            InvisibleChars = new InvisibleCharsConfig { AlwaysRun = true },
            Translation = new TranslationConfig { AlwaysRun = true },
            Spacing = new SpacingConfig { AlwaysRun = true },
            AIVeto = new AIVetoConfig { AlwaysRun = true },
            UrlBlocklist = new UrlBlocklistConfig { AlwaysRun = true },
            ThreatIntel = new ThreatIntelConfig { AlwaysRun = true },
            SeoScraping = new SeoScrapingConfig { AlwaysRun = true },
            ImageSpam = new ImageContentConfig { AlwaysRun = true },
            VideoSpam = new VideoContentConfig { AlwaysRun = true },
            FileScanning = new FileScanningDetectionConfig { AlwaysRun = true }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(loaded.StopWords.AlwaysRun, Is.True, "StopWords.AlwaysRun");
            Assert.That(loaded.Similarity.AlwaysRun, Is.True, "Similarity.AlwaysRun");
            Assert.That(loaded.Cas.AlwaysRun, Is.True, "Cas.AlwaysRun");
            Assert.That(loaded.Bayes.AlwaysRun, Is.True, "Bayes.AlwaysRun");
            Assert.That(loaded.InvisibleChars.AlwaysRun, Is.True, "InvisibleChars.AlwaysRun");
            Assert.That(loaded.Translation.AlwaysRun, Is.True, "Translation.AlwaysRun");
            Assert.That(loaded.Spacing.AlwaysRun, Is.True, "Spacing.AlwaysRun");
            Assert.That(loaded.AIVeto.AlwaysRun, Is.True, "AIVeto.AlwaysRun");
            Assert.That(loaded.UrlBlocklist.AlwaysRun, Is.True, "UrlBlocklist.AlwaysRun");
            Assert.That(loaded.ThreatIntel.AlwaysRun, Is.True, "ThreatIntel.AlwaysRun");
            Assert.That(loaded.SeoScraping.AlwaysRun, Is.True, "SeoScraping.AlwaysRun");
            Assert.That(loaded.ImageSpam.AlwaysRun, Is.True, "ImageSpam.AlwaysRun");
            Assert.That(loaded.VideoSpam.AlwaysRun, Is.True, "VideoSpam.AlwaysRun");
            Assert.That(loaded.FileScanning.AlwaysRun, Is.True, "FileScanning.AlwaysRun");
        });
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task SaveAndLoad_FractionalSeconds_PreservesPrecision()
    {
        // Arrange - Test fractional seconds
        var config = new ContentDetectionConfig
        {
            Cas = new CasConfig
            {
                Timeout = TimeSpan.FromMilliseconds(1500) // 1.5 seconds
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.Cas.Timeout.TotalSeconds, Is.EqualTo(1.5).Within(0.001));
    }

    [Test]
    public async Task SaveAndLoad_ZeroTimeout_PreservesZero()
    {
        // Arrange
        var config = new ContentDetectionConfig
        {
            Cas = new CasConfig
            {
                Timeout = TimeSpan.Zero
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.Cas.Timeout, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task SaveAndLoad_LargeCacheDuration_PreservesValue()
    {
        // Arrange - 7 days cache
        var config = new ContentDetectionConfig
        {
            UrlBlocklist = new UrlBlocklistConfig
            {
                CacheDuration = TimeSpan.FromDays(7)
            }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.That(loaded.UrlBlocklist.CacheDuration, Is.EqualTo(TimeSpan.FromDays(7)));
    }

    [Test]
    public async Task SaveAndLoad_AllTimeoutValues_PreservesAll()
    {
        // Arrange - Comprehensive test of all timeout properties
        var config = new ContentDetectionConfig
        {
            Cas = new CasConfig { Timeout = TimeSpan.FromSeconds(5) },
            ThreatIntel = new ThreatIntelConfig { Timeout = TimeSpan.FromSeconds(30) },
            UrlBlocklist = new UrlBlocklistConfig { CacheDuration = TimeSpan.FromHours(24) },
            SeoScraping = new SeoScrapingConfig { Timeout = TimeSpan.FromSeconds(10) },
            ImageSpam = new ImageContentConfig { Timeout = TimeSpan.FromSeconds(30) },
            VideoSpam = new VideoContentConfig { Timeout = TimeSpan.FromSeconds(60) }
        };

        // Act
        await _repository!.UpdateGlobalConfigAsync(config, "test");
        var loaded = await _repository.GetGlobalConfigAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Cas.Timeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(loaded.ThreatIntel.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(loaded.UrlBlocklist.CacheDuration, Is.EqualTo(TimeSpan.FromHours(24)));
            Assert.That(loaded.SeoScraping.Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(loaded.ImageSpam.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(loaded.VideoSpam.Timeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
        });
    }

    #endregion
}
