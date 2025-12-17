using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Configuration;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Tests for UserAutoTrustService - specifically the trust gaming protection
/// </summary>
[TestFixture]
public class UserAutoTrustServiceTests
{
    private IDetectionResultsRepository _detectionResultsRepo = null!;
    private IUserActionsRepository _userActionsRepo = null!;
    private IContentDetectionConfigRepository _configRepo = null!;
    private ITelegramUserRepository _userRepo = null!;
    private ILogger<UserAutoTrustService> _logger = null!;
    private UserAutoTrustService _service = null!;

    private const long TestUserId = 123456789;
    private const long TestChatId = -100123456789;

    [SetUp]
    public void Setup()
    {
        _detectionResultsRepo = Substitute.For<IDetectionResultsRepository>();
        _userActionsRepo = Substitute.For<IUserActionsRepository>();
        _configRepo = Substitute.For<IContentDetectionConfigRepository>();
        _userRepo = Substitute.For<ITelegramUserRepository>();
        _logger = Substitute.For<ILogger<UserAutoTrustService>>();

        _service = new UserAutoTrustService(
            _detectionResultsRepo,
            _userActionsRepo,
            _configRepo,
            _userRepo,
            _logger);
    }

    [Test]
    public async Task CheckAndApplyAutoTrust_FeatureDisabled_DoesNotTrust()
    {
        // Arrange
        var config = new ContentDetectionConfig { FirstMessageOnly = false };
        _configRepo.GetEffectiveConfigAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        await _service.CheckAndApplyAutoTrustAsync(TestUserId, TestChatId);

        // Assert - should not even check user or messages
        await _userRepo.DidNotReceive().GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _userActionsRepo.DidNotReceive().InsertAsync(Arg.Any<UserActionRecord>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAndApplyAutoTrust_UserNotFound_DoesNotTrust()
    {
        // Arrange
        var config = new ContentDetectionConfig { FirstMessageOnly = true };
        _configRepo.GetEffectiveConfigAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(config);
        _userRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((UiModels.TelegramUser?)null);

        // Act
        await _service.CheckAndApplyAutoTrustAsync(TestUserId, TestChatId);

        // Assert
        await _userActionsRepo.DidNotReceive().InsertAsync(Arg.Any<UserActionRecord>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAndApplyAutoTrust_AccountTooYoung_DoesNotTrust()
    {
        // Arrange - account is 1 hour old, requirement is 24 hours
        var config = new ContentDetectionConfig
        {
            FirstMessageOnly = true,
            AutoTrustMinAccountAgeHours = 24,
            FirstMessagesCount = 3,
            AutoTrustMinMessageLength = 20
        };
        _configRepo.GetEffectiveConfigAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(config);

        var user = CreateTestUser(firstSeenHoursAgo: 1); // Only 1 hour old
        _userRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Act
        await _service.CheckAndApplyAutoTrustAsync(TestUserId, TestChatId);

        // Assert - should not even check messages since account is too young
        await _detectionResultsRepo.DidNotReceive()
            .GetRecentNonSpamResultsForUserAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _userActionsRepo.DidNotReceive().InsertAsync(Arg.Any<UserActionRecord>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAndApplyAutoTrust_AccountOldEnough_NotEnoughMessages_DoesNotTrust()
    {
        // Arrange - account is 48 hours old (passes), but only 2 qualifying messages (need 3)
        var config = new ContentDetectionConfig
        {
            FirstMessageOnly = true,
            AutoTrustMinAccountAgeHours = 24,
            FirstMessagesCount = 3,
            AutoTrustMinMessageLength = 20
        };
        _configRepo.GetEffectiveConfigAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(config);

        var user = CreateTestUser(firstSeenHoursAgo: 48);
        _userRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Only 2 messages returned (need 3)
        _detectionResultsRepo.GetRecentNonSpamResultsForUserAsync(TestUserId, 3, 20, Arg.Any<CancellationToken>())
            .Returns(CreateDetectionResults(2));

        // Act
        await _service.CheckAndApplyAutoTrustAsync(TestUserId, TestChatId);

        // Assert
        await _userActionsRepo.DidNotReceive().InsertAsync(Arg.Any<UserActionRecord>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAndApplyAutoTrust_BothConditionsMet_TrustsUser()
    {
        // Arrange - account is 48 hours old AND has 3 qualifying messages
        var config = new ContentDetectionConfig
        {
            FirstMessageOnly = true,
            AutoTrustMinAccountAgeHours = 24,
            FirstMessagesCount = 3,
            AutoTrustMinMessageLength = 20
        };
        _configRepo.GetEffectiveConfigAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(config);

        var user = CreateTestUser(firstSeenHoursAgo: 48);
        _userRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);

        // 3 messages returned (meets threshold)
        _detectionResultsRepo.GetRecentNonSpamResultsForUserAsync(TestUserId, 3, 20, Arg.Any<CancellationToken>())
            .Returns(CreateDetectionResults(3));

        _userActionsRepo.InsertAsync(Arg.Any<UserActionRecord>(), Arg.Any<CancellationToken>())
            .Returns(1L);

        // Act
        await _service.CheckAndApplyAutoTrustAsync(TestUserId, TestChatId);

        // Assert - trust action should be created
        await _userActionsRepo.Received(1).InsertAsync(
            Arg.Is<UserActionRecord>(r => r.ActionType == UserActionType.Trust && r.UserId == TestUserId),
            Arg.Any<CancellationToken>());
        await _userRepo.Received(1).UpdateTrustStatusAsync(TestUserId, true, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAndApplyAutoTrust_ZeroAccountAgeRequired_SkipsAgeCheck()
    {
        // Arrange - AutoTrustMinAccountAgeHours = 0 means skip age check
        var config = new ContentDetectionConfig
        {
            FirstMessageOnly = true,
            AutoTrustMinAccountAgeHours = 0, // Disabled
            FirstMessagesCount = 3,
            AutoTrustMinMessageLength = 20
        };
        _configRepo.GetEffectiveConfigAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(config);

        var user = CreateTestUser(firstSeenHoursAgo: 0); // Brand new account
        _userRepo.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);

        _detectionResultsRepo.GetRecentNonSpamResultsForUserAsync(TestUserId, 3, 20, Arg.Any<CancellationToken>())
            .Returns(CreateDetectionResults(3));

        _userActionsRepo.InsertAsync(Arg.Any<UserActionRecord>(), Arg.Any<CancellationToken>())
            .Returns(1L);

        // Act
        await _service.CheckAndApplyAutoTrustAsync(TestUserId, TestChatId);

        // Assert - should trust despite 0 age because age check is disabled
        await _userActionsRepo.Received(1).InsertAsync(Arg.Any<UserActionRecord>(), Arg.Any<CancellationToken>());
    }

    #region Helpers

    private static UiModels.TelegramUser CreateTestUser(int firstSeenHoursAgo)
    {
        var now = DateTimeOffset.UtcNow;
        return new UiModels.TelegramUser(
            TelegramUserId: TestUserId,
            Username: "testuser",
            FirstName: "Test",
            LastName: "User",
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: false,
            BotDmEnabled: false,
            FirstSeenAt: now.AddHours(-firstSeenHoursAgo),
            LastSeenAt: now,
            CreatedAt: now.AddHours(-firstSeenHoursAgo),
            UpdatedAt: now
        );
    }

    private static List<DetectionResultRecord> CreateDetectionResults(int count)
    {
        var results = new List<DetectionResultRecord>();
        for (var i = 0; i < count; i++)
        {
            results.Add(new DetectionResultRecord
            {
                Id = i + 1,
                MessageId = 1000 + i,
                UserId = TestUserId,
                IsSpam = false,
                Confidence = 0,
                NetConfidence = 0,
                DetectedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                DetectionSource = "test",
                DetectionMethod = "test",
                MessageText = "This is a test message that is long enough",
                AddedBy = Core.Models.Actor.AutoDetection
            });
        }
        return results;
    }

    #endregion
}
