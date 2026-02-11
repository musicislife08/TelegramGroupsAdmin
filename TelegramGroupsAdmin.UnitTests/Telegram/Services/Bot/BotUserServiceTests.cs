using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Bot;

/// <summary>
/// Unit tests for BotUserService.
/// Tests caching behavior and admin status mapping logic.
///
/// Architecture:
/// - BotUserService wraps IBotUserHandler with GetMe caching via IBotIdentityCache
/// - IsAdminAsync maps ChatMemberStatus to boolean (Administrator/Creator → true)
/// - Minimal dependencies (no repositories) - suitable for unit tests
/// </summary>
[TestFixture]
public class BotUserServiceTests
{
    private IBotUserHandler _mockUserHandler = null!;
    private IBotIdentityCache _mockIdentityCache = null!;
    private ILogger<BotUserService> _mockLogger = null!;
    private BotUserService _service = null!;

    private static readonly User TestBotUser = new()
    {
        Id = 123456789L,
        IsBot = true,
        FirstName = "TestBot",
        Username = "test_bot"
    };

    [SetUp]
    public void SetUp()
    {
        _mockUserHandler = Substitute.For<IBotUserHandler>();
        _mockIdentityCache = Substitute.For<IBotIdentityCache>();
        _mockLogger = Substitute.For<ILogger<BotUserService>>();

        _service = new BotUserService(
            _mockUserHandler,
            _mockIdentityCache,
            _mockLogger);
    }

    #region GetMeAsync Tests

    [Test]
    public async Task GetMeAsync_CacheHit_ReturnsCachedUser()
    {
        // Arrange - Cache returns bot user
        _mockIdentityCache.GetCachedBotUser().Returns(TestBotUser);

        // Act
        var result = await _service.GetMeAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(TestBotUser.Id));
            Assert.That(result.Username, Is.EqualTo(TestBotUser.Username));
        }

        // Verify handler was NOT called (cache hit)
        await _mockUserHandler.DidNotReceive().GetMeAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetMeAsync_CacheMiss_FetchesFromApiAndCaches()
    {
        // Arrange - Cache returns null, handler returns bot user
        _mockIdentityCache.GetCachedBotUser().Returns((User?)null);
        _mockUserHandler.GetMeAsync(Arg.Any<CancellationToken>()).Returns(TestBotUser);

        // Act
        var result = await _service.GetMeAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(TestBotUser.Id));
            Assert.That(result.Username, Is.EqualTo(TestBotUser.Username));
        }

        // Verify handler was called
        await _mockUserHandler.Received(1).GetMeAsync(Arg.Any<CancellationToken>());

        // Verify result was cached
        _mockIdentityCache.Received(1).SetBotUser(TestBotUser);
    }

    [Test]
    public async Task GetMeAsync_MultipleCalls_OnlyFetchesOnce()
    {
        // Arrange - First call: cache miss, subsequent: cache hit
        var callCount = 0;
        _mockIdentityCache.GetCachedBotUser().Returns(_ =>
        {
            // First call returns null, subsequent calls return cached user
            return callCount++ == 0 ? null : TestBotUser;
        });
        _mockUserHandler.GetMeAsync(Arg.Any<CancellationToken>()).Returns(TestBotUser);

        // Act
        await _service.GetMeAsync();
        await _service.GetMeAsync();
        await _service.GetMeAsync();

        // Assert - Handler should only be called once
        await _mockUserHandler.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetBotIdAsync Tests

    [Test]
    public async Task GetBotIdAsync_ReturnsBotId()
    {
        // Arrange
        _mockIdentityCache.GetCachedBotUser().Returns(TestBotUser);

        // Act
        var result = await _service.GetBotIdAsync();

        // Assert
        Assert.That(result, Is.EqualTo(TestBotUser.Id));
    }

    #endregion

    #region IsAdminAsync Tests

    [Test]
    [TestCase(ChatMemberStatus.Administrator, true)]
    [TestCase(ChatMemberStatus.Creator, true)]
    [TestCase(ChatMemberStatus.Member, false)]
    [TestCase(ChatMemberStatus.Restricted, false)]
    [TestCase(ChatMemberStatus.Left, false)]
    [TestCase(ChatMemberStatus.Kicked, false)]
    public async Task IsAdminAsync_ReturnsCorrectStatus(ChatMemberStatus status, bool expectedResult)
    {
        // Arrange
        const long chatId = -100123456789L;
        const long userId = 12345L;

        var chatMember = CreateChatMember(status, userId);
        _mockUserHandler.GetChatMemberAsync(chatId, userId, Arg.Any<CancellationToken>())
            .Returns(chatMember);

        // Act
        var result = await _service.IsAdminAsync(chatId, userId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult));
    }

    [Test]
    public async Task IsAdminAsync_ExceptionThrown_ReturnsFalse()
    {
        // Arrange
        const long chatId = -100123456789L;
        const long userId = 12345L;

        _mockUserHandler.GetChatMemberAsync(chatId, userId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("User not found"));

        // Act
        var result = await _service.IsAdminAsync(chatId, userId);

        // Assert - Graceful fallback to false
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetChatMemberAsync Tests

    [Test]
    public async Task GetChatMemberAsync_PassesThroughToHandler()
    {
        // Arrange
        const long chatId = -100123456789L;
        const long userId = 12345L;

        var expectedMember = CreateChatMember(ChatMemberStatus.Member, userId);
        _mockUserHandler.GetChatMemberAsync(chatId, userId, Arg.Any<CancellationToken>())
            .Returns(expectedMember);

        // Act
        var result = await _service.GetChatMemberAsync(chatId, userId);

        // Assert
        Assert.That(result.Status, Is.EqualTo(ChatMemberStatus.Member));
        await _mockUserHandler.Received(1).GetChatMemberAsync(chatId, userId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Create a ChatMember instance with the specified status.
    /// Telegram.Bot.Types.ChatMember is abstract, so we use concrete implementations.
    /// </summary>
    private static ChatMember CreateChatMember(ChatMemberStatus status, long userId)
    {
        var user = new User { Id = userId, FirstName = "TestUser", IsBot = false };

        return status switch
        {
            ChatMemberStatus.Creator => new ChatMemberOwner { User = user },
            ChatMemberStatus.Administrator => new ChatMemberAdministrator { User = user },
            ChatMemberStatus.Member => new ChatMemberMember { User = user },
            ChatMemberStatus.Restricted => new ChatMemberRestricted { User = user },
            ChatMemberStatus.Left => new ChatMemberLeft { User = user },
            ChatMemberStatus.Kicked => new ChatMemberBanned { User = user },
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    #endregion
}
