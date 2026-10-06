using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// A scan that finds no User API session scores the name alone. Real scan service,
/// scoring engine (prompt building and parsing), repositories and identity service on canonical
/// data; only the Telegram session manager and the AI provider are faked.
///
/// Canonical anchor (read-only before the scan): <see cref="GoldenDatasetConstants.FlaggedNames.NameOnlyScanUserId"/>
/// (@loucurtsinger), never scanned. The row the scan writes and the user's scan fields are the
/// assertion subject.
/// </summary>
[TestFixture]
public class NameOnlyScanTests
{
    private const long UserId = GoldenDatasetConstants.FlaggedNames.NameOnlyScanUserId;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _provider;
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private string? _userPrompt;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _sessions = Substitute.For<ITelegramSessionManager>();
        var chat = Substitute.For<IChatService>();
        chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(true);
        chat.GetCompletionAsync(
                AIFeatureType.ProfileScan, Arg.Any<string>(), Arg.Do<string>(u => _userPrompt = u),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult
            {
                Content = """{"score": 1.0, "reason": "Ordinary personal name.", "signals_detected": [], "contains_nudity": false, "explicit_display_text": false, "promotional_display_text": false}"""
            });

        _provider = new ServiceCollection()
            .AddDataServices(_testHelper.ConnectionString)
            .AddLogging()
            .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
            .AddScoped<IProfileScanResultsRepository, ProfileScanResultsRepository>()
            .AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>()
            .AddScoped<IUserIdentityService, UserIdentityService>()
            .AddSingleton(Substitute.For<IProfileScanGate>())
            .AddSingleton(chat)
            .AddSingleton(Substitute.For<IUrlPreFilterService>())
            .AddSingleton(Substitute.For<IUrlContentScrapingService>())
            .AddSingleton(Substitute.For<IStopWordsRepository>())
            .AddScoped<IProfileScoringEngine, ProfileScoringEngine>()
            .AddSingleton(Substitute.For<IConfigService>()) // no stored override → default thresholds
            .AddSingleton(Substitute.For<IBotModerationService>())
            .AddSingleton(Substitute.For<IReportsRepository>())
            .AddSingleton(Substitute.For<IAdminNotificationService>())
            .BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _testHelper?.Dispose();
    }

    private ProfileScanService NewScanService() => new(
        _sessions,
        _provider!.GetRequiredService<IServiceScopeFactory>(),
        new PipelineMetrics(),
        new RecyclableMemoryStreamManager(),
        Substitute.For<IImageProcessor>(),
        NullLogger<ProfileScanService>.Instance);

    [Test]
    public async Task NoSession_Scan_WritesNameOnlyRowAndUpdatesUser()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UserId);
        Assert.Multiple(() =>
        {
            Assert.That(before.IsTrusted, Is.False);
            Assert.That(before.IsBot, Is.False);
            Assert.That(before.ProfileScannedAt, Is.Null);
            Assert.That(before.ProfileScanScore, Is.Null);
        });
        Assert.That(await ctx.ProfileScanResults.CountAsync(r => r.UserId == UserId), Is.Zero);
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        // Load-bearing: unstubbed, NSubstitute returns a substitute client (recursive mock) and the
        // scan would fall back through "user not resolvable" instead of "no session".
        _sessions.GetClientForChatAsync(chatId, Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);
        UserIdentity identity;
        await using (var scope = _provider!.CreateAsyncScope())
            identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ResolveAsync(UserId, CancellationToken.None);
        var start = PostgresTimestamps.FloorToMicrosecond(DateTimeOffset.UtcNow);

        var result = await NewScanService().ScanUserProfileAsync(
            identity, ChatIdentity.FromId(chatId), CancellationToken.None);
        var end = PostgresTimestamps.CeilingToMicrosecond(DateTimeOffset.UtcNow);

        var row = await ctx.ProfileScanResults.AsNoTracking().SingleAsync(r => r.UserId == UserId);
        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UserId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(ProfileScanSource.NameOnly));
            Assert.That(result.SkipReason, Is.Null);
            Assert.That(row.Source, Is.EqualTo((short)ProfileScanSource.NameOnly));
            Assert.That(row.Score, Is.EqualTo(1.0m));
            Assert.That(row.Outcome, Is.EqualTo((int)ProfileScanOutcome.Clean));
            Assert.That(row.RuleScore, Is.EqualTo(0.0m));
            Assert.That(row.AiScore, Is.EqualTo(1.0m));
            Assert.That(row.AiReason, Is.EqualTo("Ordinary personal name."));
            Assert.That(row.AiExplicitDisplayText, Is.False);
            Assert.That(row.AiPromotionalDisplayText, Is.False);
            Assert.That(after.ProfileScanScore, Is.EqualTo(1.0m));
            Assert.That(after.ProfileScannedAt, Is.InRange(start, end));
            Assert.That(after.Bio, Is.EqualTo(before.Bio));
            Assert.That(_userPrompt, Does.Contain($"<display_name>{before.FirstName} {before.LastName}</display_name>"));
            Assert.That(_userPrompt, Does.Contain($"<username>{before.Username}</username>"));
            Assert.That(_userPrompt, Does.Contain("<bio>Unknown (could not be retrieved)</bio>"));
        });
    }
}
