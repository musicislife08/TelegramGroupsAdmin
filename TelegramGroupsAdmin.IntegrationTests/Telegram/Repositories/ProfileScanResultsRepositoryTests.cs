using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for ProfileScanResultsRepository covering the new
/// ai_explicit_display_text column.
///
/// Setup-data source:
/// - Write path tests: SUT InsertAsync IS the assertion subject (allowed
///   exception to the no-inline-test-data-injection rule). The FK parent
///   row in telegram_users is seeded via raw INSERT (the FK parent is
///   infrastructure, not the SUT).
/// - Read path tests: canonical extension only (user 9220500615182,
///   scan ID 534 from 23_profile_scan_results.sql).
/// </summary>
[TestFixture]
public class ProfileScanResultsRepositoryTests
{
    // Canonical anchors (from 23_profile_scan_results.sql)
    private const long CanonicalFlaggedUserId = 9220500615182L;
    private const long CanonicalFlaggedScanId = 534L;

    // Synthetic write-path FK parents. Chosen outside the canonical
    // telegram_user_id range [9_000_000_000_000, 10_000_000_000_000) so
    // they cannot collide with golden-template rows.
    private const long WritePathUserIdTrue = 99999999999L;
    private const long WritePathUserIdFalse = 99999999988L;

    // A user that has no profile_scan_results row in canonical (also
    // outside the canonical user-id range).
    private const long UnseenUserId = 12121212121L;

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IProfileScanResultsRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddScoped<IProfileScanResultsRepository, ProfileScanResultsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IProfileScanResultsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    /// <summary>
    /// Seed a minimal telegram_users row so that profile_scan_results
    /// inserts satisfy the FK_profile_scan_results_telegram_users_user_id
    /// constraint. The FK parent is infrastructure for the write-path
    /// tests, not the SUT.
    /// </summary>
    private async Task SeedTelegramUserAsync(long telegramUserId)
    {
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO telegram_users (telegram_user_id, is_bot, is_trusted, is_active, is_banned, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at, has_pinned_stories, is_fake, is_scam, is_verified, profile_scan_excluded, kick_count) VALUES ({telegramUserId}, false, false, true, false, false, NOW(), NOW(), NOW(), NOW(), false, false, false, false, false, 0)");
    }

    [Test]
    public async Task InsertAsync_WithExplicitDisplayTextTrue_PersistsTrue()
    {
        await SeedTelegramUserAsync(WritePathUserIdTrue);

        var record = new ProfileScanResultRecord(
            Id: 0,
            UserId: WritePathUserIdTrue,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 4.7m,
            Outcome: ProfileScanOutcome.Banned,
            RuleScore: 0.0m,
            AiScore: 4.7m,
            AiReason: "test reason",
            AiSignals: "test_signal",
            ExplicitDisplayText: true);

        var insertedId = await _repository!.InsertAsync(record, CancellationToken.None);
        var roundTripped = await _repository.GetLatestByUserIdAsync(WritePathUserIdTrue, CancellationToken.None);

        Assert.That(roundTripped, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(roundTripped!.Id, Is.EqualTo(insertedId));
            Assert.That(roundTripped.ExplicitDisplayText, Is.True);
        }
    }

    [Test]
    public async Task InsertAsync_WithExplicitDisplayTextFalse_PersistsFalse()
    {
        await SeedTelegramUserAsync(WritePathUserIdFalse);

        var record = new ProfileScanResultRecord(
            Id: 0,
            UserId: WritePathUserIdFalse,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 1.0m,
            Outcome: ProfileScanOutcome.Clean,
            RuleScore: 0.0m,
            AiScore: 1.0m,
            AiReason: null,
            AiSignals: null,
            ExplicitDisplayText: false);

        await _repository!.InsertAsync(record, CancellationToken.None);
        var roundTripped = await _repository.GetLatestByUserIdAsync(WritePathUserIdFalse, CancellationToken.None);

        Assert.That(roundTripped, Is.Not.Null);
        Assert.That(roundTripped!.ExplicitDisplayText, Is.False);
    }

    [Test]
    public async Task GetLatestByUserIdAsync_CanonicalFlaggedUser_ReturnsFlaggedRow()
    {
        var latest = await _repository!.GetLatestByUserIdAsync(
            CanonicalFlaggedUserId,
            CancellationToken.None);

        Assert.That(latest, Is.Not.Null,
            $"Canonical row for user {CanonicalFlaggedUserId} not found - check 23_profile_scan_results.sql");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(latest!.Id, Is.EqualTo(CanonicalFlaggedScanId));
            Assert.That(latest.ExplicitDisplayText, Is.True);
        }
    }

    [Test]
    public async Task GetLatestByUserIdAsync_NoScanForUser_ReturnsNull()
    {
        var latest = await _repository!.GetLatestByUserIdAsync(
            userId: UnseenUserId,
            CancellationToken.None);

        Assert.That(latest, Is.Null);
    }
}
