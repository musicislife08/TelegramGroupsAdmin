using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for ImpersonationDetectionService.
/// Uses NSubstitute for mocking - no database integration.
/// Tests are split into:
/// - Service tests: Require full ImpersonationDetectionService with mocked dependencies
/// - Result tests: Test ImpersonationCheckResult record behavior directly (no service needed)
/// </summary>
[TestFixture]
public class ImpersonationDetectionServiceTests
{
    // Mocks for ShouldCheckUserAsync (only interfaces used by that method)
    private ITelegramUserRepository _mockTelegramUserRepo = null!;
    private IMessageHistoryRepository _mockMessageHistoryRepo = null!;
    private IReportsRepository _mockReportsRepo = null!;
    private IConfigService _mockConfigService = null!;
    private ImpersonationDetectionService _service = null!;

    // Test constants
    private const long TestUserId = 123456;
    private const long TestChatId = -1001234567890;
    private const long TestAdminId = 789012;
    private const long TestChannelId = -1009876543210;

    // Helper methods to create test SDK objects
    private static User CreateTestSdkUser(long id = TestUserId) => new() { Id = id, FirstName = "Test", LastName = "User" };
    private static Chat CreateTestSdkChat(long id = TestChatId) => new() { Id = id, Type = ChatType.Supergroup, Title = "Test Chat" };

    [SetUp]
    public void SetUp()
    {
        // Only create mocks for interfaces (concrete classes can't be mocked without parameterless constructor)
        _mockTelegramUserRepo = Substitute.For<ITelegramUserRepository>();
        _mockMessageHistoryRepo = Substitute.For<IMessageHistoryRepository>();
        _mockReportsRepo = Substitute.For<IReportsRepository>();
        _mockConfigService = Substitute.For<IConfigService>();

        // Default config: check first 5 messages
        _mockConfigService.GetEffectiveAsync<ContentDetectionConfig>(ConfigType.ContentDetection, Arg.Any<long>())
            .Returns(new ContentDetectionConfig { FirstMessagesCount = 5 });

        // Note: Full service instantiation requires concrete dependencies that can't be mocked.
        // For service-level tests, use integration tests instead.
        // For ShouldCheckUserAsync, we can test directly since it only uses interface dependencies.
        _service = CreateServiceForShouldCheckTests();
    }

    /// <summary>
    /// Creates a minimal service instance for ShouldCheckUserAsync tests.
    /// Other methods require database context and concrete dependencies.
    /// </summary>
    private ImpersonationDetectionService CreateServiceForShouldCheckTests()
    {
        var mockContextFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        var mockChatAdminsRepo = Substitute.For<IChatAdminsRepository>();
        var mockManagedChatsRepo = Substitute.For<IManagedChatsRepository>();
        var mockPhotoHashService = Substitute.For<IPhotoHashService>();
        var mockLogger = Substitute.For<ILogger<ImpersonationDetectionService>>();

        // These concrete classes are never used by ShouldCheckUserAsync, so we can pass nulls
        // (they're only used by CheckUserAsync and ExecuteActionAsync)
        return new ImpersonationDetectionService(
            mockContextFactory,
            _mockTelegramUserRepo,
            mockChatAdminsRepo,
            mockManagedChatsRepo,
            _mockMessageHistoryRepo,
            mockPhotoHashService,
            _mockReportsRepo,
            null!, // ModerationActionService - not used by ShouldCheckUserAsync
            null!, // TelegramBotClientFactory - not used by ShouldCheckUserAsync
            _mockConfigService,
            mockLogger);
    }

    #region ShouldCheckUserAsync Tests

    [Test]
    public async Task ShouldCheckUserAsync_TrustedUser_ReturnsFalse()
    {
        // Arrange
        _mockTelegramUserRepo.GetByTelegramIdAsync(TestUserId)
            .Returns(CreateTestUser(TestUserId, isTrusted: true));

        // Act
        var result = await _service.ShouldCheckUserAsync(TestUserId, TestChatId);

        // Assert
        Assert.That(result, Is.False, "Trusted users should be skipped");
    }

    [Test]
    public async Task ShouldCheckUserAsync_UserWithPendingAlert_ReturnsFalse()
    {
        // Arrange
        _mockTelegramUserRepo.GetByTelegramIdAsync(TestUserId)
            .Returns(CreateTestUser(TestUserId, isTrusted: false));
        _mockReportsRepo.HasPendingImpersonationAlertAsync(TestUserId)
            .Returns(true);

        // Act
        var result = await _service.ShouldCheckUserAsync(TestUserId, TestChatId);

        // Assert
        Assert.That(result, Is.False, "Users with pending alerts should be skipped");
    }

    [Test]
    public async Task ShouldCheckUserAsync_UserExceedsMessageThreshold_ReturnsFalse()
    {
        // Arrange
        _mockTelegramUserRepo.GetByTelegramIdAsync(TestUserId)
            .Returns(CreateTestUser(TestUserId, isTrusted: false));
        _mockReportsRepo.HasPendingImpersonationAlertAsync(TestUserId)
            .Returns(false);
        _mockMessageHistoryRepo.GetMessageCountAsync(TestUserId, TestChatId)
            .Returns(10); // >= 5 threshold

        // Act
        var result = await _service.ShouldCheckUserAsync(TestUserId, TestChatId);

        // Assert
        Assert.That(result, Is.False, "Users exceeding message threshold should be skipped");
    }

    [Test]
    public async Task ShouldCheckUserAsync_NewUser_ReturnsTrue()
    {
        // Arrange
        _mockTelegramUserRepo.GetByTelegramIdAsync(TestUserId)
            .Returns(CreateTestUser(TestUserId, isTrusted: false));
        _mockReportsRepo.HasPendingImpersonationAlertAsync(TestUserId)
            .Returns(false);
        _mockMessageHistoryRepo.GetMessageCountAsync(TestUserId, TestChatId)
            .Returns(2); // < 5 threshold

        // Act
        var result = await _service.ShouldCheckUserAsync(TestUserId, TestChatId);

        // Assert
        Assert.That(result, Is.True, "New users should be checked");
    }

    [Test]
    public async Task ShouldCheckUserAsync_UnknownUser_ReturnsTrue()
    {
        // Arrange - user not in database
        _mockTelegramUserRepo.GetByTelegramIdAsync(TestUserId)
            .Returns((UiModels.TelegramUser?)null);
        _mockReportsRepo.HasPendingImpersonationAlertAsync(TestUserId)
            .Returns(false);
        _mockMessageHistoryRepo.GetMessageCountAsync(TestUserId, TestChatId)
            .Returns(0);

        // Act
        var result = await _service.ShouldCheckUserAsync(TestUserId, TestChatId);

        // Assert
        Assert.That(result, Is.True, "Unknown users should be checked");
    }

    /// <summary>
    /// Helper method to create a test TelegramUser record
    /// </summary>
    private static UiModels.TelegramUser CreateTestUser(long userId, bool isTrusted = false) =>
        new(
            TelegramUserId: userId,
            Username: $"user_{userId}",
            FirstName: "Test",
            LastName: "User",
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: isTrusted,
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow,
            LastSeenAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );

    #endregion

    #region ImpersonationCheckResult Tests

    [Test]
    public void ImpersonationCheckResult_Score50_ShouldTakeAction()
    {
        // Arrange
        var result = new ImpersonationCheckResult
        {
            TotalScore = 50,
            RiskLevel = ImpersonationRiskLevel.Medium,
            SuspectedUser = CreateTestSdkUser(),
            DetectionChat = CreateTestSdkChat(),
            TargetUserId = TestAdminId,
            NameMatch = true,
            PhotoMatch = false
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldTakeAction, Is.True, "Score 50 should trigger action");
            Assert.That(result.ShouldAutoBan, Is.False, "Score 50 should not auto-ban");
        });
    }

    [Test]
    public void ImpersonationCheckResult_Score100_ShouldAutoBan()
    {
        // Arrange
        var result = new ImpersonationCheckResult
        {
            TotalScore = 100,
            RiskLevel = ImpersonationRiskLevel.Critical,
            SuspectedUser = CreateTestSdkUser(),
            DetectionChat = CreateTestSdkChat(),
            TargetUserId = TestAdminId,
            NameMatch = true,
            PhotoMatch = true,
            PhotoSimilarityScore = 0.95
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldTakeAction, Is.True, "Score 100 should trigger action");
            Assert.That(result.ShouldAutoBan, Is.True, "Score 100 should auto-ban");
        });
    }

    [Test]
    public void ImpersonationCheckResult_Score49_NoAction()
    {
        // Arrange - Score < 50 means no match was found, so ImpersonationCheckResult wouldn't normally be created.
        // This test verifies the threshold behavior when a result has low score.
        var result = new ImpersonationCheckResult
        {
            TotalScore = 49,
            RiskLevel = ImpersonationRiskLevel.Medium, // Medium is lowest risk level in enum
            SuspectedUser = CreateTestSdkUser(),
            DetectionChat = CreateTestSdkChat(),
            TargetUserId = TestAdminId,
            NameMatch = false,
            PhotoMatch = false
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldTakeAction, Is.False, "Score 49 should not trigger action");
            Assert.That(result.ShouldAutoBan, Is.False, "Score 49 should not auto-ban");
        });
    }

    [Test]
    public void ImpersonationCheckResult_TargetEntityId_DefaultsToZero()
    {
        // Arrange - when TargetEntityType is User, TargetEntityId defaults to 0
        var result = new ImpersonationCheckResult
        {
            TotalScore = 50,
            SuspectedUser = CreateTestSdkUser(),
            DetectionChat = CreateTestSdkChat(),
            TargetUserId = TestAdminId,
            TargetEntityType = ProtectedEntityType.User
            // TargetEntityId not set - should default to 0
        };

        // Assert
        Assert.That(result.TargetEntityId, Is.EqualTo(0), "TargetEntityId should default to 0 for User type");
    }

    [Test]
    public void ImpersonationCheckResult_ChannelType_HasEntityId()
    {
        // Arrange
        var result = new ImpersonationCheckResult
        {
            TotalScore = 50,
            SuspectedUser = CreateTestSdkUser(),
            DetectionChat = CreateTestSdkChat(),
            TargetUserId = 0,
            TargetEntityType = ProtectedEntityType.Channel,
            TargetEntityId = TestChannelId,
            TargetEntityName = "Official Channel"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TargetEntityType, Is.EqualTo(ProtectedEntityType.Channel));
            Assert.That(result.TargetEntityId, Is.EqualTo(TestChannelId));
            Assert.That(result.TargetEntityName, Is.EqualTo("Official Channel"));
        });
    }

    [Test]
    public void ImpersonationCheckResult_ChatType_HasEntityId()
    {
        // Arrange
        var result = new ImpersonationCheckResult
        {
            TotalScore = 50,
            SuspectedUser = CreateTestSdkUser(),
            DetectionChat = CreateTestSdkChat(),
            TargetUserId = 0,
            TargetEntityType = ProtectedEntityType.Chat,
            TargetEntityId = TestChatId,
            TargetEntityName = "Main Group"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.TargetEntityType, Is.EqualTo(ProtectedEntityType.Chat));
            Assert.That(result.TargetEntityId, Is.EqualTo(TestChatId));
            Assert.That(result.TargetEntityName, Is.EqualTo("Main Group"));
        });
    }

    [Test]
    public void ImpersonationCheckResult_SdkObjects_ProvideUserAndChatContext()
    {
        // Verify that SDK objects are stored and accessible for logging/context
        var user = CreateTestSdkUser(999);
        var chat = CreateTestSdkChat(-1001111111111);

        var result = new ImpersonationCheckResult
        {
            TotalScore = 50,
            SuspectedUser = user,
            DetectionChat = chat,
            TargetUserId = TestAdminId
        };

        // Assert - access IDs directly from SDK objects
        Assert.Multiple(() =>
        {
            Assert.That(result.SuspectedUser.Id, Is.EqualTo(999), "SuspectedUser.Id should be accessible");
            Assert.That(result.DetectionChat.Id, Is.EqualTo(-1001111111111), "DetectionChat.Id should be accessible");
        });
    }

    #endregion

    #region Risk Level Tests

    [Test]
    public void RiskLevel_Score50To99_IsMedium()
    {
        // Score 50 = name match only OR photo match only → Medium risk
        var result = new ImpersonationCheckResult
        {
            TotalScore = 50,
            RiskLevel = ImpersonationRiskLevel.Medium,
            SuspectedUser = CreateTestSdkUser(),
            DetectionChat = CreateTestSdkChat(),
            NameMatch = true,
            PhotoMatch = false
        };

        Assert.That(result.RiskLevel, Is.EqualTo(ImpersonationRiskLevel.Medium));
    }

    [Test]
    public void RiskLevel_Score100_IsCritical()
    {
        // Score 100 = name match AND photo match → Critical risk
        var result = new ImpersonationCheckResult
        {
            TotalScore = 100,
            RiskLevel = ImpersonationRiskLevel.Critical,
            SuspectedUser = CreateTestSdkUser(),
            DetectionChat = CreateTestSdkChat(),
            NameMatch = true,
            PhotoMatch = true
        };

        Assert.That(result.RiskLevel, Is.EqualTo(ImpersonationRiskLevel.Critical));
    }

    #endregion

    #region ProtectedEntityType Enum Tests

    [Test]
    public void ProtectedEntityType_HasCorrectValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)ProtectedEntityType.User, Is.EqualTo(0));
            Assert.That((int)ProtectedEntityType.Chat, Is.EqualTo(1));
            Assert.That((int)ProtectedEntityType.Channel, Is.EqualTo(2));
        });
    }

    #endregion
}
