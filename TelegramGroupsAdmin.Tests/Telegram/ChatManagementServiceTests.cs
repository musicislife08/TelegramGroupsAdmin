using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.Tests.Telegram;

/// <summary>
/// Tests for ChatManagementService health gate functionality.
/// Created: 2025-11-12 as part of enum refactoring (PR review suggestion)
/// Purpose: Validate GetHealthyChatIds() fail-closed behavior and enum usage
/// </summary>
[TestFixture]
public class ChatManagementServiceTests
{
    private ChatManagementService _service = null!;
    private ILogger<ChatManagementService> _logger = null!;
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        _logger = Substitute.For<ILogger<ChatManagementService>>();
        _serviceProvider = Substitute.For<IServiceProvider>();
        _service = new ChatManagementService(_serviceProvider, _logger);
    }

    [Test]
    public void GetHealthyChatIds_EmptyCache_ReturnsEmptySet()
    {
        // Arrange: Empty health cache (cold start scenario)
        _service.SetHealthCacheForTesting(new Dictionary<long, ChatHealthStatus>());

        // Act
        var result = _service.GetHealthyChatIds();

        // Assert: Fail-closed behavior - returns empty set when health unknown
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetHealthyChatIds_MixedStatuses_ReturnsOnlyHealthy()
    {
        // Arrange: Cache with mixed health statuses
        var healthCache = new Dictionary<long, ChatHealthStatus>
        {
            { 100, new ChatHealthStatus { ChatId = 100, Status = ChatHealthStatusType.Healthy } },
            { 200, new ChatHealthStatus { ChatId = 200, Status = ChatHealthStatusType.Warning } },
            { 300, new ChatHealthStatus { ChatId = 300, Status = ChatHealthStatusType.Error } },
            { 400, new ChatHealthStatus { ChatId = 400, Status = ChatHealthStatusType.Healthy } },
            { 500, new ChatHealthStatus { ChatId = 500, Status = ChatHealthStatusType.Unknown } },
            { 600, new ChatHealthStatus { ChatId = 600, Status = ChatHealthStatusType.NotApplicable } }
        };
        _service.SetHealthCacheForTesting(healthCache);

        // Act
        var result = _service.GetHealthyChatIds();

        // Assert: Only Healthy status chats returned (100, 400)
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain(100));
        Assert.That(result, Does.Contain(400));
        Assert.That(result, Does.Not.Contain(200)); // Warning excluded
        Assert.That(result, Does.Not.Contain(300)); // Error excluded
        Assert.That(result, Does.Not.Contain(500)); // Unknown excluded
        Assert.That(result, Does.Not.Contain(600)); // NotApplicable excluded
    }

    [Test]
    public void GetHealthyChatIds_AllHealthy_ReturnsAllChatIds()
    {
        // Arrange: All chats have Healthy status
        var healthCache = new Dictionary<long, ChatHealthStatus>
        {
            { 100, new ChatHealthStatus { ChatId = 100, Status = ChatHealthStatusType.Healthy } },
            { 200, new ChatHealthStatus { ChatId = 200, Status = ChatHealthStatusType.Healthy } },
            { 300, new ChatHealthStatus { ChatId = 300, Status = ChatHealthStatusType.Healthy } }
        };
        _service.SetHealthCacheForTesting(healthCache);

        // Act
        var result = _service.GetHealthyChatIds();

        // Assert: All chat IDs returned
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result, Does.Contain(100));
        Assert.That(result, Does.Contain(200));
        Assert.That(result, Does.Contain(300));
    }

    [Test]
    public void GetHealthyChatIds_NoHealthyChats_ReturnsEmptySet()
    {
        // Arrange: Cache populated but no Healthy chats
        var healthCache = new Dictionary<long, ChatHealthStatus>
        {
            { 100, new ChatHealthStatus { ChatId = 100, Status = ChatHealthStatusType.Warning } },
            { 200, new ChatHealthStatus { ChatId = 200, Status = ChatHealthStatusType.Error } },
            { 300, new ChatHealthStatus { ChatId = 300, Status = ChatHealthStatusType.Unknown } }
        };
        _service.SetHealthCacheForTesting(healthCache);

        // Act
        var result = _service.GetHealthyChatIds();

        // Assert: Empty set because no chats are Healthy
        Assert.That(result, Is.Empty);
    }
}
