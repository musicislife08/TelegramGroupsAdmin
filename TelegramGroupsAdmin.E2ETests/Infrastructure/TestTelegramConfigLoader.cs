using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Test-specific ITelegramConfigLoader that returns a dummy token.
/// This allows E2E tests to run without configuring a real bot token.
/// </summary>
public class TestTelegramConfigLoader : ITelegramConfigLoader
{
    private const string TestBotToken = "test-bot-token-for-e2e-tests";

    /// <summary>
    /// Always returns a dummy token for E2E tests.
    /// </summary>
    public Task<string> LoadConfigAsync()
    {
        return Task.FromResult(TestBotToken);
    }
}
