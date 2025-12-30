using NSubstitute;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Pure unit tests for TelegramLinkService.
/// Uses NSubstitute to mock dependencies - no database required.
/// Tests token generation and account unlinking logic.
/// </summary>
[TestFixture]
public class TelegramLinkServiceTests
{
    private ITelegramLinkTokenRepository _mockTokenRepo = null!;
    private ITelegramUserMappingRepository _mockMappingRepo = null!;
    private TelegramLinkService _linkService = null!;

    private const string TestUserId = "test-user-123";

    [SetUp]
    public void SetUp()
    {
        _mockTokenRepo = Substitute.For<ITelegramLinkTokenRepository>();
        _mockMappingRepo = Substitute.For<ITelegramUserMappingRepository>();
        _linkService = new TelegramLinkService(_mockTokenRepo, _mockMappingRepo);
    }

    #region GenerateLinkTokenAsync Tests

    [Test]
    public async Task GenerateLinkTokenAsync_RevokesExistingTokens()
    {
        // Arrange
        _mockTokenRepo.RevokeUnusedTokensForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _mockTokenRepo.InsertAsync(Arg.Any<TelegramLinkTokenRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        await _linkService.GenerateLinkTokenAsync(TestUserId);

        // Assert - verify RevokeUnusedTokensForUserAsync was called
        await _mockTokenRepo.Received(1).RevokeUnusedTokensForUserAsync(TestUserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateLinkTokenAsync_InsertsNewToken()
    {
        // Arrange
        TelegramLinkTokenRecord? capturedToken = null;
        _mockTokenRepo.InsertAsync(Arg.Do<TelegramLinkTokenRecord>(t => capturedToken = t), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _linkService.GenerateLinkTokenAsync(TestUserId);

        // Assert - verify InsertAsync was called with correct data
        await _mockTokenRepo.Received(1).InsertAsync(Arg.Any<TelegramLinkTokenRecord>(), Arg.Any<CancellationToken>());
        Assert.That(capturedToken, Is.Not.Null);
        Assert.That(capturedToken!.UserId, Is.EqualTo(TestUserId));
        Assert.That(capturedToken.Token.Length, Is.EqualTo(12));
    }

    [Test]
    public async Task GenerateLinkTokenAsync_ReturnsTokenWith15MinuteExpiry()
    {
        // Arrange
        _mockTokenRepo.InsertAsync(Arg.Any<TelegramLinkTokenRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var before = DateTimeOffset.UtcNow;
        var result = await _linkService.GenerateLinkTokenAsync(TestUserId);
        var after = DateTimeOffset.UtcNow;

        // Assert - expiry should be ~15 minutes from now
        Assert.That(result.ExpiresAt, Is.GreaterThan(before.AddMinutes(14)));
        Assert.That(result.ExpiresAt, Is.LessThan(after.AddMinutes(16)));
    }

    [Test]
    public async Task GenerateLinkTokenAsync_GeneratesUrlSafeToken()
    {
        // Arrange
        _mockTokenRepo.InsertAsync(Arg.Any<TelegramLinkTokenRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _linkService.GenerateLinkTokenAsync(TestUserId);

        // Assert - token should not contain URL-unsafe characters
        Assert.That(result.Token, Does.Not.Contain("+"));
        Assert.That(result.Token, Does.Not.Contain("/"));
        Assert.That(result.Token, Does.Not.Contain("="));
    }

    [Test]
    public async Task GenerateLinkTokenAsync_ReturnsUnusedToken()
    {
        // Arrange
        _mockTokenRepo.InsertAsync(Arg.Any<TelegramLinkTokenRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _linkService.GenerateLinkTokenAsync(TestUserId);

        // Assert - token should be unused
        Assert.That(result.UsedAt, Is.Null);
        Assert.That(result.UsedByTelegramId, Is.Null);
    }

    #endregion

    #region UnlinkAccountAsync Tests

    [Test]
    public async Task UnlinkAccountAsync_MappingNotFound_ReturnsFalse()
    {
        // Arrange - empty mapping list (no mappings for user)
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<TelegramUserMappingRecord>());

        // Act
        var result = await _linkService.UnlinkAccountAsync(999, TestUserId);

        // Assert
        Assert.That(result, Is.False);
        await _mockMappingRepo.DidNotReceive().DeactivateAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnlinkAccountAsync_MappingBelongsToAnotherUser_ReturnsFalse()
    {
        // Arrange - user has a mapping, but different ID than requested
        var userMappings = new List<TelegramUserMappingRecord>
        {
            new(Id: 100, TelegramId: 123456, TelegramUsername: "user", UserId: TestUserId, LinkedAt: DateTimeOffset.UtcNow, IsActive: true)
        };
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(userMappings.AsEnumerable());

        // Act - try to unlink mapping ID 999 (not in user's list)
        var result = await _linkService.UnlinkAccountAsync(999, TestUserId);

        // Assert
        Assert.That(result, Is.False);
        await _mockMappingRepo.DidNotReceive().DeactivateAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnlinkAccountAsync_MappingBelongsToUser_CallsDeactivate()
    {
        // Arrange
        const long mappingId = 100;
        var userMappings = new List<TelegramUserMappingRecord>
        {
            new(Id: mappingId, TelegramId: 123456, TelegramUsername: "user", UserId: TestUserId, LinkedAt: DateTimeOffset.UtcNow, IsActive: true)
        };
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(userMappings.AsEnumerable());
        _mockMappingRepo.DeactivateAsync(mappingId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _linkService.UnlinkAccountAsync(mappingId, TestUserId);

        // Assert
        Assert.That(result, Is.True);
        await _mockMappingRepo.Received(1).DeactivateAsync(mappingId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnlinkAccountAsync_DeactivateFails_ReturnsFalse()
    {
        // Arrange
        const long mappingId = 100;
        var userMappings = new List<TelegramUserMappingRecord>
        {
            new(Id: mappingId, TelegramId: 123456, TelegramUsername: "user", UserId: TestUserId, LinkedAt: DateTimeOffset.UtcNow, IsActive: true)
        };
        _mockMappingRepo.GetByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(userMappings.AsEnumerable());
        _mockMappingRepo.DeactivateAsync(mappingId, Arg.Any<CancellationToken>())
            .Returns(false); // Deactivation fails

        // Act
        var result = await _linkService.UnlinkAccountAsync(mappingId, TestUserId);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion
}
