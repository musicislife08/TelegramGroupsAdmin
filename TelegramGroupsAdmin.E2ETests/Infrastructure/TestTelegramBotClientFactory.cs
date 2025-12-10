using NSubstitute;
using Telegram.Bot;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Test-specific TelegramBotClientFactory that returns a mock ITelegramBotClient.
/// This allows E2E tests to run without a real Telegram bot token.
/// </summary>
/// <remarks>
/// This class doesn't inherit from TelegramBotClientFactory because:
/// 1. The base class now requires TelegramConfigLoader in its constructor
/// 2. Test infrastructure creates this before DI is configured
/// 3. We override all methods anyway, so inheritance provides no value
/// </remarks>
public class TestTelegramBotClientFactory : TelegramBotClientFactory
{
    private readonly ITelegramBotClient _mockClient;

    public TestTelegramBotClientFactory()
        : base(null!) // Base constructor param unused since we override all methods
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
    /// Always returns the mock client asynchronously.
    /// </summary>
    public override Task<ITelegramBotClient> GetBotClientAsync()
    {
        return Task.FromResult(_mockClient);
    }

    /// <summary>
    /// Gets the mock client for test verification if needed.
    /// </summary>
    public ITelegramBotClient MockClient => _mockClient;
}
