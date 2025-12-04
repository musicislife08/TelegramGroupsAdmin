using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Test-specific TelegramConfigLoader that returns a dummy token.
/// This allows E2E tests to run without configuring a real bot token.
/// </summary>
public class TestTelegramConfigLoader : TelegramConfigLoader
{
    private const string TestBotToken = "test-bot-token-for-e2e-tests";

    public TestTelegramConfigLoader(IServiceScopeFactory scopeFactory)
        : base(scopeFactory, NullLogger<TelegramConfigLoader>.Instance)
    {
    }

    /// <summary>
    /// Always returns a dummy token for E2E tests.
    /// </summary>
    public override Task<string> LoadConfigAsync()
    {
        return Task.FromResult(TestBotToken);
    }
}
