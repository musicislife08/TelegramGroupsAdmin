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
///   from 23_profile_scan_results.sql, read-only.
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

    [TestCase(true)]
    [TestCase(false)]
    public async Task InsertAsync_PersistsExplicitDisplayText(bool explicitDisplayText)
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
            ExplicitDisplayText: explicitDisplayText);

        var insertedId = await _repository!.InsertAsync(record, CancellationToken.None);
        var history = await _repository.GetByUserIdAsync(UnscannedUserId, CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(history[0].Id, Is.EqualTo(insertedId));
            Assert.That(history[0].ExplicitDisplayText, Is.EqualTo(explicitDisplayText));
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
