using NSubstitute;
using Telegram.Bot;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Test-specific TelegramBotClientFactory that returns a mock ITelegramBotClient.
/// This allows E2E tests to run without a real Telegram bot token.
/// </summary>
public class TestTelegramBotClientFactory : TelegramBotClientFactory
{
    private readonly ITelegramBotClient _mockClient;

    public TestTelegramBotClientFactory()
    {
        _mockClient = Substitute.For<ITelegramBotClient>();
    }

    /// <summary>
    /// Always returns the mock client, ignoring the token.
    /// </summary>
    public override ITelegramBotClient GetOrCreate(string botToken)
    {
        return _mockClient;
    }

    /// <summary>
    /// Gets the mock client for test verification if needed.
    /// </summary>
    public ITelegramBotClient MockClient => _mockClient;
}
