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
/// Integration tests for ProfileScanResultsRepository covering the
/// ai_explicit_display_text column.
///
/// Setup-data source (canonical only):
/// - Write path tests: SUT InsertAsync IS the assertion subject, for a canonical user
///   with no scan rows (@Juvenileii); the test reads the user's (empty) history first.
/// - Read path tests: user 9220500615182 (@bagging_armado), scans 530 and 534
///   from 23_profile_scan_results.sql, read-only; @unreadbackspin's NameOnly row 528
///   (GoldenDatasetConstants.ProfileRescan), read-only.
/// </summary>
[TestFixture]
public class ProfileScanResultsRepositoryTests
{
    // Canonical anchors (from 23_profile_scan_results.sql)
    private const long CanonicalFlaggedUserId = GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId;
    private const long CanonicalFlaggedScanId = 534L;

    // Canonical user with no profile_scan_results rows: the write-path FK parent.
    private const long UnscannedUserId = GoldenDatasetConstants.IdentityService.UnscannedUserId;

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
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();

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

    private async Task AssertUnscannedAsync()
    {
        var history = await _repository!.GetByUserIdAsync(UnscannedUserId, CancellationToken.None);
        Assert.That(history, Is.Empty, "anchor must have no scan rows in canonical");
    }

    [TestCase(true, false, ProfileScanSource.FullScan)]
    [TestCase(false, true, ProfileScanSource.NameOnly)]
    [TestCase(false, false, ProfileScanSource.FullScan)]
    public async Task InsertAsync_PersistsNameFlagsAndSource(bool explicitDisplayText, bool promotionalDisplayText, ProfileScanSource source)
    {
        await AssertUnscannedAsync();
        var record = new ProfileScanResultRecord(
            Id: 0,
            UserId: UnscannedUserId,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 4.7m,
            Outcome: ProfileScanOutcome.Banned,
            RuleScore: 0.0m,
            AiScore: 4.7m,
            AiReason: "test reason",
            AiSignals: "test_signal",
            ExplicitDisplayText: explicitDisplayText,
            PromotionalDisplayText: promotionalDisplayText,
            Source: source);

        var insertedId = await _repository!.InsertAsync(record, CancellationToken.None);
        var history = await _repository.GetByUserIdAsync(UnscannedUserId, CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(history[0].Id, Is.EqualTo(insertedId));
            Assert.That(history[0].ExplicitDisplayText, Is.EqualTo(explicitDisplayText));
            Assert.That(history[0].PromotionalDisplayText, Is.EqualTo(promotionalDisplayText));
            Assert.That(history[0].Source, Is.EqualTo(source));
        }
    }

    [Test]
    public async Task GetByUserIdAsync_CanonicalRows_DefaultToFullScanAndNotPromotional()
    {
        // Rows written before the columns existed read back with the column defaults.
        var history = await _repository!.GetByUserIdAsync(CanonicalFlaggedUserId, CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history.Select(h => (h.Source, h.PromotionalDisplayText)),
            Is.All.EqualTo((ProfileScanSource.FullScan, false)));
    }

    [Test]
    public async Task GetLatestSourceAsync_CanonicalFullScanUser_ReturnsFullScan()
    {
        var source = await _repository!.GetLatestSourceAsync(CanonicalFlaggedUserId, CancellationToken.None);

        Assert.That(source, Is.EqualTo(ProfileScanSource.FullScan));
    }

    [Test]
    public async Task GetLatestSourceAsync_CanonicalNameOnlyUser_ReturnsNameOnly()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var rows = await ctx.ProfileScanResults.AsNoTracking()
            .Where(r => r.UserId == GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId)
            .ToListAsync();
        Assert.That(rows.Select(r => (r.Id, r.Source)),
            Is.EqualTo(new[] { (GoldenDatasetConstants.ProfileRescan.NameOnlyLatestScanId, (short)ProfileScanSource.NameOnly) }),
            "anchor's only scan row must be the edited NameOnly row 528");

        var source = await _repository!.GetLatestSourceAsync(
            GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId, CancellationToken.None);

        Assert.That(source, Is.EqualTo(ProfileScanSource.NameOnly));
    }

    [Test]
    public async Task GetLatestSourceAsync_UnscannedUser_ReturnsNull()
    {
        await AssertUnscannedAsync();

        var source = await _repository!.GetLatestSourceAsync(UnscannedUserId, CancellationToken.None);

        Assert.That(source, Is.Null);
    }

    [Test]
    public async Task UpdateProfileScanScoreAsync_SetsScoreAndAdvancesScannedAt_LeavesProfileFields()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UnscannedUserId);
        Assert.That(before.ProfileScannedAt, Is.Null, "anchor has never been scanned");
        var users = _scope!.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var start = PostgresTimestamps.FloorToMicrosecond(DateTimeOffset.UtcNow);

        await users.UpdateProfileScanScoreAsync(UnscannedUserId, 3.2m);
        var end = PostgresTimestamps.CeilingToMicrosecond(DateTimeOffset.UtcNow);

        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UnscannedUserId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.ProfileScanScore, Is.EqualTo(3.2m));
            Assert.That(after.ProfileScannedAt, Is.InRange(start, end));
            Assert.That(after.Bio, Is.EqualTo(before.Bio));
            Assert.That(after.ProfilePhotoId, Is.EqualTo(before.ProfilePhotoId));
            Assert.That(after.PersonalChannelId, Is.EqualTo(before.PersonalChannelId));
        }
    }

    [Test]
    public async Task GetByUserIdAsync_CanonicalFlaggedUser_NewestFirstWithFlag()
    {
        var history = await _repository!.GetByUserIdAsync(CanonicalFlaggedUserId, CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(2),
            $"Canonical rows for user {CanonicalFlaggedUserId} not found - check 23_profile_scan_results.sql");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(history[0].Id, Is.EqualTo(CanonicalFlaggedScanId));
            Assert.That(history[0].ExplicitDisplayText, Is.True);
            Assert.That(history[1].ExplicitDisplayText, Is.False);
        }
    }

    [Test]
    public async Task GetByUserIdAsync_NoScanForUser_ReturnsEmpty() => await AssertUnscannedAsync();
}
