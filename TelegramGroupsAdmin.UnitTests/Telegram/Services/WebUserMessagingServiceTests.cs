using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TL;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for WebUserMessagingService.
///
/// Testing strategy:
/// - ITelegramSessionManager is mocked to control client availability without real connections.
/// - IWTelegramApiClient is mocked to control peer resolution and message send/edit results.
/// - TelegramFloodWaitException and TL.RpcException are thrown via NSubstitute to validate
///   each catch branch in isolation.
/// - The generic Exception catch branches verify that raw exception messages are NOT leaked
///   to callers (security fix — surface only a safe fallback string).
/// - No real HTTP, Telegram, or WTelegram connections are made.
/// </summary>
[TestFixture]
public class WebUserMessagingServiceTests
{
    private const string TestWebUserId = "test-web-user-id";
    private const long TestChatId = -100123456789L;
    private const int TestMessageId = 42;

#pragma warning disable NUnit1032 // Mocks don't need disposal
    private ITelegramSessionManager _mockSessionManager = null!;
    private IWTelegramApiClient _mockClient = null!;
#pragma warning restore NUnit1032
    private ILogger<WebUserMessagingService> _mockLogger = null!;
    private WebUserMessagingService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockSessionManager = Substitute.For<ITelegramSessionManager>();
        _mockLogger = Substitute.For<ILogger<WebUserMessagingService>>();
        _mockClient = Substitute.For<IWTelegramApiClient>();

        _sut = new WebUserMessagingService(_mockSessionManager, _mockLogger);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static WebUserIdentity MakeWebUser(
        string id = TestWebUserId,
        string? email = "admin@example.com",
        PermissionLevel level = PermissionLevel.Admin)
        => new(id, email, level);

    private static InputPeer MakePeer()
        => new InputPeerChannel(123456789L, 9876543210L);

    private static TelegramFloodWaitException MakeFloodWaitException(int waitSeconds = 30)
        => new(waitSeconds, DateTimeOffset.UtcNow.AddSeconds(waitSeconds));

    // ─────────────────────────────────────────────────────────────────────────

    #region CheckFeatureAvailabilityAsync

    [Test]
    public async Task CheckFeatureAvailabilityAsync_ClientIsNull_ReturnsUnavailableWithConnectMessage()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns((IWTelegramApiClient?)null);

        // Act
        var result = await _sut.CheckFeatureAvailabilityAsync(MakeWebUser());

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.IsAvailable, Is.False);
        Assert.That(result.UnavailableReason, Does.Contain("No connected Telegram account"));
    }

    [Test]
    public async Task CheckFeatureAvailabilityAsync_ClientExists_ReturnsAvailable()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);

        // Act
        var result = await _sut.CheckFeatureAvailabilityAsync(MakeWebUser());

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.UnavailableReason, Is.Null);
    }

    [Test]
    public async Task CheckFeatureAvailabilityAsync_ThrowsFloodWaitException_ReturnsRateLimitedMessage()
    {
        // Arrange
        var flood = MakeFloodWaitException(waitSeconds: 45);
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(flood);

        // Act
        var result = await _sut.CheckFeatureAvailabilityAsync(MakeWebUser());

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.IsAvailable, Is.False);
        Assert.That(result.UnavailableReason, Does.Contain("Rate limited"));
        Assert.That(result.UnavailableReason, Does.Contain("45"));
    }

    [Test]
    public async Task CheckFeatureAvailabilityAsync_ThrowsGenericException_ReturnsSafeGenericMessage()
    {
        // Arrange — a raw exception with a sensitive message that must NOT reach the caller
        const string sensitiveMessage = "Internal server token: abc-secret-xyz";
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(sensitiveMessage));

        // Act
        var result = await _sut.CheckFeatureAvailabilityAsync(MakeWebUser());

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.IsAvailable, Is.False);
        Assert.That(result.UnavailableReason, Does.Contain("unexpected error"));
        Assert.That(result.UnavailableReason, Does.Not.Contain(sensitiveMessage));
    }

    #endregion

    #region CanSendToChatAsync

    [Test]
    public async Task CanSendToChatAsync_ClientIsNull_ReturnsCannotSend()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns((IWTelegramApiClient?)null);

        // Act
        var result = await _sut.CanSendToChatAsync(MakeWebUser(), TestChatId);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.CanSend, Is.False);
        Assert.That(result.UnavailableReason, Does.Contain("No connected Telegram account"));
    }

    [Test]
    public async Task CanSendToChatAsync_PeerFoundOnFirstLookup_ReturnsCanSend()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(MakePeer());

        // Act
        var result = await _sut.CanSendToChatAsync(MakeWebUser(), TestChatId);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.CanSend, Is.True);
        Assert.That(result.UnavailableReason, Is.Null);

        // Verify warm cache was NOT needed
        await _mockClient.DidNotReceive().WarmPeerCacheAsync();
    }

    [Test]
    public async Task CanSendToChatAsync_PeerNotFoundThenFoundAfterWarm_ReturnsCanSend()
    {
        // Arrange — first call returns null, warm cache is called, second call returns a peer
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);

        var callCount = 0;
        _mockClient.GetInputPeerForChat(TestChatId).Returns(_ =>
        {
            callCount++;
            return callCount == 1 ? null : MakePeer();
        });

        // Act
        var result = await _sut.CanSendToChatAsync(MakeWebUser(), TestChatId);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.CanSend, Is.True);
        Assert.That(result.UnavailableReason, Is.Null);
        await _mockClient.Received(1).WarmPeerCacheAsync();
    }

    [Test]
    public async Task CanSendToChatAsync_PeerNotFoundEvenAfterWarm_ReturnsNotMember()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns((InputPeer?)null);

        // Act
        var result = await _sut.CanSendToChatAsync(MakeWebUser(), TestChatId);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.CanSend, Is.False);
        Assert.That(result.UnavailableReason, Does.Contain("not a member"));
        await _mockClient.Received(1).WarmPeerCacheAsync();
    }

    [Test]
    public async Task CanSendToChatAsync_ThrowsFloodWaitException_ReturnsRateLimitedMessage()
    {
        // Arrange
        var flood = MakeFloodWaitException(waitSeconds: 60);
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(flood);

        // Act
        var result = await _sut.CanSendToChatAsync(MakeWebUser(), TestChatId);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.CanSend, Is.False);
        Assert.That(result.UnavailableReason, Does.Contain("Rate limited"));
        Assert.That(result.UnavailableReason, Does.Contain("60"));
    }

    [Test]
    public async Task CanSendToChatAsync_ThrowsGenericException_ReturnsSafeGenericMessage()
    {
        // Arrange
        const string sensitiveMessage = "DB credential: password=hunter2";
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(sensitiveMessage));

        // Act
        var result = await _sut.CanSendToChatAsync(MakeWebUser(), TestChatId);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.CanSend, Is.False);
        Assert.That(result.UnavailableReason, Does.Contain("unexpected error"));
        Assert.That(result.UnavailableReason, Does.Not.Contain(sensitiveMessage));
    }

    #endregion

    #region SendMessageAsync — input validation

    [Test]
    public async Task SendMessageAsync_EmptyText_ReturnsValidationError()
    {
        // Arrange + Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "   ");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("cannot be empty"));

        // No client interaction for a validation-only failure
        await _mockSessionManager.DidNotReceive().GetClientAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessageAsync_TextExceedsMaxLength_ReturnsValidationError()
    {
        // Arrange — 4097 characters, one over the 4096 limit
        var oversizedText = new string('x', 4097);

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, oversizedText);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("too long"));
        Assert.That(result.ErrorMessage, Does.Contain("4097"));

        await _mockSessionManager.DidNotReceive().GetClientAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessageAsync_TextAtExactMaxLength_DoesNotReturnValidationError()
    {
        // Arrange — 4096 characters, exactly at the limit — should not be a validation error
        var exactText = new string('x', 4096);
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(MakePeer());
        _mockClient
            .SendMessageAsync(Arg.Any<InputPeer>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(new TL.Message { id = 999 });

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, exactText);

        // Assert — length validation passed, message was sent
        Assert.That(result.Success, Is.True);
    }

    #endregion

    #region SendMessageAsync — runtime paths

    [Test]
    public async Task SendMessageAsync_ClientIsNull_ReturnsNoAccountError()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns((IWTelegramApiClient?)null);

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "Hello world");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("No connected Telegram account"));
    }

    [Test]
    public async Task SendMessageAsync_PeerFoundDirectly_SendsMessageAndReturnsSuccess()
    {
        // Arrange
        var peer = MakePeer();
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(peer);
        _mockClient
            .SendMessageAsync(peer, "Hello world", 0)
            .Returns(new TL.Message { id = 101 });

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "Hello world");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _mockClient.DidNotReceive().WarmPeerCacheAsync();
    }

    [Test]
    public async Task SendMessageAsync_PeerNotFoundThenFoundAfterWarm_SendsMessageAndReturnsSuccess()
    {
        // Arrange
        var peer = MakePeer();
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);

        var callCount = 0;
        _mockClient.GetInputPeerForChat(TestChatId).Returns(_ =>
        {
            callCount++;
            return callCount == 1 ? null : peer;
        });

        _mockClient
            .SendMessageAsync(Arg.Any<InputPeer>(), "Hello world", 0)
            .Returns(new TL.Message { id = 202 });

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "Hello world");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _mockClient.Received(1).WarmPeerCacheAsync();
    }

    [Test]
    public async Task SendMessageAsync_PeerNotFoundEvenAfterWarm_ReturnsNotMemberError()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns((InputPeer?)null);

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "Hello world");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("not a member"));
    }

    [Test]
    public async Task SendMessageAsync_WithReplyToMessageId_PassesReplyToIdToClient()
    {
        // Arrange
        const int replyToId = 555;
        var peer = MakePeer();
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(peer);
        _mockClient
            .SendMessageAsync(peer, "Reply text", replyToId)
            .Returns(new TL.Message { id = 303 });

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "Reply text", replyToMessageId: replyToId);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.True);
        await _mockClient.Received(1).SendMessageAsync(peer, "Reply text", replyToId);
    }

    [Test]
    public async Task SendMessageAsync_ThrowsFloodWaitException_ReturnsRateLimitedMessage()
    {
        // Arrange
        var flood = MakeFloodWaitException(waitSeconds: 120);
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(MakePeer());
        _mockClient
            .SendMessageAsync(Arg.Any<InputPeer>(), Arg.Any<string>(), Arg.Any<int>())
            .ThrowsAsync(flood);

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "Hello world");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Rate limited"));
        Assert.That(result.ErrorMessage, Does.Contain("120"));
    }

    [Test]
    public async Task SendMessageAsync_ThrowsRpcException_ReturnsTelegramErrorMessage()
    {
        // Arrange
        var rpcEx = new TL.RpcException(400, "MESSAGE_NOT_MODIFIED");
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(MakePeer());
        _mockClient
            .SendMessageAsync(Arg.Any<InputPeer>(), Arg.Any<string>(), Arg.Any<int>())
            .ThrowsAsync(rpcEx);

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "Hello world");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.StartWith("Telegram error:"));
        Assert.That(result.ErrorMessage, Does.Contain("MESSAGE_NOT_MODIFIED"));
    }

    [Test]
    public async Task SendMessageAsync_ThrowsGenericException_ReturnsSafeGenericMessageNotRawException()
    {
        // Arrange — sensitive exception message must not reach the caller (security fix)
        const string sensitiveMessage = "API_KEY=sk-abc123-do-not-expose";
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(MakePeer());
        _mockClient
            .SendMessageAsync(Arg.Any<InputPeer>(), Arg.Any<string>(), Arg.Any<int>())
            .ThrowsAsync(new Exception(sensitiveMessage));

        // Act
        var result = await _sut.SendMessageAsync(MakeWebUser(), TestChatId, "Hello world");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("unexpected error"));
        Assert.That(result.ErrorMessage, Does.Not.Contain(sensitiveMessage));
    }

    #endregion

    #region EditMessageAsync — input validation

    [Test]
    public async Task EditMessageAsync_EmptyText_ReturnsValidationError()
    {
        // Arrange + Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("cannot be empty"));

        await _mockSessionManager.DidNotReceive().GetClientAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EditMessageAsync_WhitespaceOnlyText_ReturnsValidationError()
    {
        // Arrange + Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "   \t\n");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("cannot be empty"));
    }

    [Test]
    public async Task EditMessageAsync_TextExceedsMaxLength_ReturnsValidationError()
    {
        // Arrange — 4097 characters
        var oversizedText = new string('y', 4097);

        // Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, oversizedText);

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("too long"));
        Assert.That(result.ErrorMessage, Does.Contain("4097"));
    }

    #endregion

    #region EditMessageAsync — runtime paths

    [Test]
    public async Task EditMessageAsync_ClientIsNull_ReturnsNoAccountError()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns((IWTelegramApiClient?)null);

        // Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "Updated text");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("No connected Telegram account"));
    }

    [Test]
    public async Task EditMessageAsync_PeerFoundDirectly_EditsMessageAndReturnsSuccess()
    {
        // Arrange
        var peer = MakePeer();
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(peer);
        _mockClient
            .Messages_EditMessage(peer, TestMessageId, "Updated text")
            .Returns(Substitute.For<UpdatesBase>());

        // Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "Updated text");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _mockClient.DidNotReceive().WarmPeerCacheAsync();
    }

    [Test]
    public async Task EditMessageAsync_PeerNotFoundThenFoundAfterWarm_EditsMessageAndReturnsSuccess()
    {
        // Arrange
        var peer = MakePeer();
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);

        var callCount = 0;
        _mockClient.GetInputPeerForChat(TestChatId).Returns(_ =>
        {
            callCount++;
            return callCount == 1 ? null : peer;
        });

        _mockClient
            .Messages_EditMessage(Arg.Any<InputPeer>(), TestMessageId, "Updated text")
            .Returns(Substitute.For<UpdatesBase>());

        // Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "Updated text");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        await _mockClient.Received(1).WarmPeerCacheAsync();
    }

    [Test]
    public async Task EditMessageAsync_PeerNotFoundEvenAfterWarm_ReturnsNotMemberError()
    {
        // Arrange
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns((InputPeer?)null);

        // Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "Updated text");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("not a member"));
        await _mockClient.Received(1).WarmPeerCacheAsync();
    }

    [Test]
    public async Task EditMessageAsync_ThrowsFloodWaitException_ReturnsRateLimitedMessage()
    {
        // Arrange
        var flood = MakeFloodWaitException(waitSeconds: 90);
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(MakePeer());
        _mockClient
            .Messages_EditMessage(Arg.Any<InputPeer>(), Arg.Any<int>(), Arg.Any<string>())
            .ThrowsAsync(flood);

        // Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "Updated text");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Rate limited"));
        Assert.That(result.ErrorMessage, Does.Contain("90"));
    }

    [Test]
    public async Task EditMessageAsync_ThrowsRpcException_ReturnsTelegramErrorMessage()
    {
        // Arrange
        var rpcEx = new TL.RpcException(400, "MESSAGE_NOT_MODIFIED");
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(MakePeer());
        _mockClient
            .Messages_EditMessage(Arg.Any<InputPeer>(), Arg.Any<int>(), Arg.Any<string>())
            .ThrowsAsync(rpcEx);

        // Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "Updated text");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.StartWith("Telegram error:"));
        Assert.That(result.ErrorMessage, Does.Contain("MESSAGE_NOT_MODIFIED"));
    }

    [Test]
    public async Task EditMessageAsync_ThrowsGenericException_ReturnsSafeGenericMessageNotRawException()
    {
        // Arrange — sensitive exception message must not reach the caller (security fix)
        const string sensitiveMessage = "DB_HOST=internal.corp:5432";
        _mockSessionManager
            .GetClientAsync(TestWebUserId, Arg.Any<CancellationToken>())
            .Returns(_mockClient);
        _mockClient.GetInputPeerForChat(TestChatId).Returns(MakePeer());
        _mockClient
            .Messages_EditMessage(Arg.Any<InputPeer>(), Arg.Any<int>(), Arg.Any<string>())
            .ThrowsAsync(new Exception(sensitiveMessage));

        // Act
        var result = await _sut.EditMessageAsync(MakeWebUser(), TestChatId, TestMessageId, "Updated text");

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("unexpected error"));
        Assert.That(result.ErrorMessage, Does.Not.Contain(sensitiveMessage));
    }

    #endregion
}
