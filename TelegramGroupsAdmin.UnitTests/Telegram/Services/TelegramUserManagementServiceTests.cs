using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for TelegramUserManagementService.
/// Tests business logic for ToggleTrustAsync and UnbanAsync,
/// and verifies delegation to repositories for query methods.
/// Uses ITelegramUserRepository and IUserActionsRepository interfaces
/// (enabled by Issue #127 interface extraction).
/// </summary>
[TestFixture]
public class TelegramUserManagementServiceTests
{
    private ITelegramUserRepository _mockUserRepo = null!;
    private IUserActionsRepository _mockActionsRepo = null!;
    private ILogger<TelegramUserManagementService> _mockLogger = null!;
    private TelegramUserManagementService _service = null!;

    private const long TestUserId = 123456789;

    [SetUp]
    public void Setup()
    {
        _mockUserRepo = Substitute.For<ITelegramUserRepository>();
        _mockActionsRepo = Substitute.For<IUserActionsRepository>();

        // Logger is required by constructor but intentionally not verified.
        // Logging is an implementation detail - tests focus on business logic outcomes.
        _mockLogger = Substitute.For<ILogger<TelegramUserManagementService>>();

        _service = new TelegramUserManagementService(
            _mockUserRepo,
            _mockActionsRepo,
            _mockLogger);
    }

    #region ToggleTrustAsync Tests

    [Test]
    public async Task ToggleTrustAsync_ReturnsFalse_WhenUserNotFound()
    {
        // Arrange
        _mockUserRepo.GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var executor = Actor.FromSystem("Test");

        // Act
        var result = await _service.ToggleTrustAsync(TestUserId, executor);

        // Assert
        Assert.That(result, Is.False);
        await _mockUserRepo.DidNotReceive().UpdateTrustStatusAsync(
            Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ToggleTrustAsync_TrustsUser_WhenNotCurrentlyTrusted()
    {
        // Arrange
        var user = CreateTelegramUser(isTrusted: false);
        _mockUserRepo.GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);

        var executor = Actor.FromSystem("Test");

        // Act
        var result = await _service.ToggleTrustAsync(TestUserId, executor);

        // Assert
        Assert.That(result, Is.True);
        await _mockUserRepo.Received(1).UpdateTrustStatusAsync(
            TestUserId, true, Arg.Any<CancellationToken>());
        await _mockActionsRepo.Received(1).InsertAsync(
            Arg.Is<UserActionRecord>(a => a.ActionType == UserActionType.Trust && a.UserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ToggleTrustAsync_UntrustsUser_WhenCurrentlyTrusted()
    {
        // Arrange
        var user = CreateTelegramUser(isTrusted: true);
        _mockUserRepo.GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);

        var executor = Actor.FromSystem("Test");

        // Act
        var result = await _service.ToggleTrustAsync(TestUserId, executor);

        // Assert
        Assert.That(result, Is.True);
        await _mockUserRepo.Received(1).UpdateTrustStatusAsync(
            TestUserId, false, Arg.Any<CancellationToken>());
        await _mockActionsRepo.Received(1).ExpireTrustsForUserAsync(TestUserId, Arg.Any<long?>(), Arg.Any<CancellationToken>());
        await _mockActionsRepo.Received(1).InsertAsync(
            Arg.Is<UserActionRecord>(a => a.ActionType == UserActionType.Untrust && a.UserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ToggleTrustAsync_BlocksUntrustingSystemAccount()
    {
        // Arrange - Use Telegram system account ID (777000 = Telegram notifications)
        const long systemUserId = 777000;
        var systemUser = CreateTelegramUser(telegramUserId: systemUserId, isTrusted: true);
        _mockUserRepo.GetByTelegramIdAsync(systemUserId, Arg.Any<CancellationToken>())
            .Returns(systemUser);

        var executor = Actor.FromSystem("Test");

        // Act
        var result = await _service.ToggleTrustAsync(systemUserId, executor);

        // Assert - Should fail to untrust system account
        Assert.That(result, Is.False);
        await _mockUserRepo.DidNotReceive().UpdateTrustStatusAsync(
            Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ToggleTrustAsync_AllowsTrustingSystemAccount()
    {
        // Arrange - System account not yet trusted (edge case)
        const long systemUserId = 777000;
        var systemUser = CreateTelegramUser(telegramUserId: systemUserId, isTrusted: false);
        _mockUserRepo.GetByTelegramIdAsync(systemUserId, Arg.Any<CancellationToken>())
            .Returns(systemUser);

        var executor = Actor.FromSystem("Test");

        // Act
        var result = await _service.ToggleTrustAsync(systemUserId, executor);

        // Assert - Should succeed (trusting system accounts is allowed)
        Assert.That(result, Is.True);
        await _mockUserRepo.Received(1).UpdateTrustStatusAsync(
            systemUserId, true, Arg.Any<CancellationToken>());
    }

    #endregion

    #region UnbanAsync Tests

    [Test]
    public async Task UnbanAsync_ReturnsFalse_WhenUserNotFound()
    {
        // Arrange
        _mockUserRepo.GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var executor = Actor.FromSystem("Test");

        // Act
        var result = await _service.UnbanAsync(TestUserId, executor);

        // Assert
        Assert.That(result, Is.False);
        await _mockActionsRepo.DidNotReceive().ExpireBansForUserAsync(
            Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnbanAsync_ReturnsFalse_WhenUserNotBanned()
    {
        // Arrange
        var user = CreateTelegramUser();
        _mockUserRepo.GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockUserRepo.IsBannedAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromSystem("Test");

        // Act
        var result = await _service.UnbanAsync(TestUserId, executor);

        // Assert
        Assert.That(result, Is.False);
        await _mockActionsRepo.DidNotReceive().ExpireBansForUserAsync(
            Arg.Any<long>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnbanAsync_Succeeds_WhenUserIsBanned()
    {
        // Arrange
        var user = CreateTelegramUser();
        _mockUserRepo.GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockUserRepo.IsBannedAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(true);

        var executor = Actor.FromSystem("Test");

        // Act
        var result = await _service.UnbanAsync(TestUserId, executor);

        // Assert
        Assert.That(result, Is.True);
        await _mockActionsRepo.Received(1).ExpireBansForUserAsync(
            TestUserId, null, Arg.Any<CancellationToken>());
        await _mockActionsRepo.Received(1).InsertAsync(
            Arg.Is<UserActionRecord>(a => a.ActionType == UserActionType.Unban && a.UserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnbanAsync_UsesProvidedReason()
    {
        // Arrange
        var user = CreateTelegramUser();
        _mockUserRepo.GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        _mockUserRepo.IsBannedAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(true);

        var executor = Actor.FromSystem("Test");
        const string customReason = "Ban reversed after review";

        // Act
        var result = await _service.UnbanAsync(TestUserId, executor, customReason);

        // Assert
        Assert.That(result, Is.True);
        await _mockActionsRepo.Received(1).InsertAsync(
            Arg.Is<UserActionRecord>(a =>
                a.ActionType == UserActionType.Unban &&
                a.Reason == customReason),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Query Delegation Tests

    [Test]
    public async Task GetUserDetailAsync_DelegatesToRepository()
    {
        // Arrange
        var expectedDetail = new TelegramUserDetail { User = UserIdentity.FromId(TestUserId) };
        _mockUserRepo.GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(expectedDetail);

        // Act
        var result = await _service.GetUserDetailAsync(TestUserId);

        // Assert
        Assert.That(result, Is.SameAs(expectedDetail));
        await _mockUserRepo.Received(1).GetUserDetailAsync(TestUserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task IsBannedAsync_DelegatesToRepository()
    {
        // Arrange
        _mockUserRepo.IsBannedAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.IsBannedAsync(TestUserId);

        // Assert
        Assert.That(result, Is.True);
        await _mockUserRepo.Received(1).IsBannedAsync(TestUserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetActiveWarningCountAsync_DelegatesToRepository()
    {
        // Arrange
        _mockUserRepo.GetActiveWarningCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(3);

        // Act
        var result = await _service.GetActiveWarningCountAsync(TestUserId);

        // Assert
        Assert.That(result, Is.EqualTo(3));
        await _mockUserRepo.Received(1).GetActiveWarningCountAsync(TestUserId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private static TelegramUser CreateTelegramUser(
        long telegramUserId = TestUserId,
        bool isTrusted = false)
    {
        return new TelegramUser(
            TelegramUserId: telegramUserId,
            Username: "testuser",
            FirstName: "Test",
            LastName: "User",
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: isTrusted,
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow.AddDays(-30),
            LastSeenAt: DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt: DateTimeOffset.UtcNow.AddHours(-1),
            IsActive: true
        );
    }

    #endregion
}
