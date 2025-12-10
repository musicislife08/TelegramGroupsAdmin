using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Test-specific TelegramBotClientFactory that returns a mock ITelegramBotClient.
/// This allows E2E tests to run without a real Telegram bot token.
/// </summary>
/// <remarks>
/// This class inherits from TelegramBotClientFactory to override virtual methods.
/// Base constructor parameters are null since we override all methods that use them.
/// </remarks>
public class TestTelegramBotClientFactory : TelegramBotClientFactory
{
    private readonly ITelegramBotClient _mockClient;
    private ITelegramOperations? _mockOperations;

    public TestTelegramBotClientFactory()
        : base(null!, null!) // Base constructor params unused since we override all methods
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
    /// Returns ITelegramOperations wrapper around the mock client.
    /// </summary>
    public override Task<ITelegramOperations> GetOperationsAsync()
    {
        _mockOperations ??= new TelegramOperations(
            _mockClient,
            Substitute.For<ILogger<TelegramOperations>>());
        return Task.FromResult(_mockOperations);
    }

    /// <summary>
    /// Gets the mock client for test verification if needed.
    /// </summary>
    public ITelegramBotClient MockClient => _mockClient;

    /// <summary>
    /// Gets the mock operations wrapper for test verification.
    /// </summary>
    public ITelegramOperations MockOperations => _mockOperations
        ?? throw new InvalidOperationException("Call GetOperationsAsync() first");
}
