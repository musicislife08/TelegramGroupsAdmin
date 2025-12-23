using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating Telegram users in the database for E2E testing.
/// Uses ITelegramUserRepository to create proper telegram_users entries.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var user = await new TestTelegramUserBuilder(Factory.Services)
///     .WithUserId(123456789)
///     .WithUsername("testuser")
///     .WithName("Test", "User")
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestTelegramUserBuilder
{
    private readonly IServiceProvider _services;

    private long _telegramUserId;
    private string? _username;
    private string? _firstName = "Test";
    private string? _lastName = "User";
    private string? _userPhotoPath;
    private string? _photoHash;
    private string? _photoFileUniqueId;
    private bool _isBot;
    private bool _isTrusted;
    private bool _botDmEnabled;
    private DateTimeOffset _firstSeenAt = DateTimeOffset.UtcNow.AddDays(-7);
    private DateTimeOffset _lastSeenAt = DateTimeOffset.UtcNow;

    public TestTelegramUserBuilder(IServiceProvider services)
    {
        _services = services;
        // Generate a random user ID by default
        _telegramUserId = Random.Shared.NextInt64(100_000_000, 999_999_999);
    }

    /// <summary>
    /// Sets the Telegram user ID.
    /// </summary>
    public TestTelegramUserBuilder WithUserId(long userId)
    {
        _telegramUserId = userId;
        return this;
    }

    /// <summary>
    /// Sets the username (without @).
    /// </summary>
    public TestTelegramUserBuilder WithUsername(string username)
    {
        _username = username;
        return this;
    }

    /// <summary>
    /// Sets the first and last name.
    /// </summary>
    public TestTelegramUserBuilder WithName(string firstName, string? lastName = null)
    {
        _firstName = firstName;
        _lastName = lastName;
        return this;
    }

    /// <summary>
    /// Sets the user photo path.
    /// </summary>
    public TestTelegramUserBuilder WithPhoto(string photoPath, string? hash = null, string? fileUniqueId = null)
    {
        _userPhotoPath = photoPath;
        _photoHash = hash;
        _photoFileUniqueId = fileUniqueId;
        return this;
    }

    /// <summary>
    /// Marks the user as a bot.
    /// </summary>
    public TestTelegramUserBuilder AsBot()
    {
        _isBot = true;
        return this;
    }

    /// <summary>
    /// Marks the user as trusted.
    /// </summary>
    public TestTelegramUserBuilder AsTrusted()
    {
        _isTrusted = true;
        return this;
    }

    /// <summary>
    /// Marks the user as having bot DM enabled.
    /// </summary>
    public TestTelegramUserBuilder WithBotDmEnabled()
    {
        _botDmEnabled = true;
        return this;
    }

    /// <summary>
    /// Sets the first seen timestamp.
    /// </summary>
    public TestTelegramUserBuilder FirstSeenAt(DateTimeOffset timestamp)
    {
        _firstSeenAt = timestamp;
        return this;
    }

    /// <summary>
    /// Sets the last seen timestamp.
    /// </summary>
    public TestTelegramUserBuilder LastSeenAt(DateTimeOffset timestamp)
    {
        _lastSeenAt = timestamp;
        return this;
    }

    /// <summary>
    /// Builds and persists the Telegram user to the database.
    /// </summary>
    public async Task<TelegramUser> BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        var now = DateTimeOffset.UtcNow;
        var user = new TelegramUser(
            TelegramUserId: _telegramUserId,
            Username: _username,
            FirstName: _firstName,
            LastName: _lastName,
            UserPhotoPath: _userPhotoPath,
            PhotoHash: _photoHash,
            PhotoFileUniqueId: _photoFileUniqueId,
            IsBot: _isBot,
            IsTrusted: _isTrusted,
            IsBanned: false,
            BotDmEnabled: _botDmEnabled,
            FirstSeenAt: _firstSeenAt,
            LastSeenAt: _lastSeenAt,
            CreatedAt: now,
            UpdatedAt: now
        );

        await userRepository.UpsertAsync(user, cancellationToken);

        return user;
    }
}
