using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating Telegram-to-web user mappings in the database for E2E testing.
/// Uses ITelegramUserMappingRepository to create proper telegram_user_mappings entries.
/// This allows testing the Profile page's linked accounts display.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var mapping = await new TestTelegramUserMappingBuilder(Factory.Services)
///     .WithTelegramId(123456789)
///     .WithTelegramUsername("testuser")
///     .LinkedToWebUser(testUser.Id)
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestTelegramUserMappingBuilder
{
    private readonly IServiceProvider _services;

    private long _telegramId;
    private string? _telegramUsername;
    private string? _userId;
    private DateTimeOffset _linkedAt = DateTimeOffset.UtcNow;
    private bool _isActive = true;

    public TestTelegramUserMappingBuilder(IServiceProvider services)
    {
        _services = services;
        // Generate a random Telegram ID by default
        _telegramId = Random.Shared.NextInt64(100_000_000, 999_999_999);
    }

    /// <summary>
    /// Sets the Telegram user ID.
    /// </summary>
    public TestTelegramUserMappingBuilder WithTelegramId(long telegramId)
    {
        _telegramId = telegramId;
        return this;
    }

    /// <summary>
    /// Sets the Telegram username (without @).
    /// </summary>
    public TestTelegramUserMappingBuilder WithTelegramUsername(string username)
    {
        _telegramUsername = username;
        return this;
    }

    /// <summary>
    /// Links to a specific web user by their ID.
    /// </summary>
    public TestTelegramUserMappingBuilder LinkedToWebUser(string userId)
    {
        _userId = userId;
        return this;
    }

    /// <summary>
    /// Sets when the account was linked.
    /// </summary>
    public TestTelegramUserMappingBuilder LinkedAt(DateTimeOffset timestamp)
    {
        _linkedAt = timestamp;
        return this;
    }

    /// <summary>
    /// Sets whether the mapping is active (default true).
    /// </summary>
    public TestTelegramUserMappingBuilder IsActive(bool active)
    {
        _isActive = active;
        return this;
    }

    /// <summary>
    /// Marks the mapping as inactive (unlinked).
    /// </summary>
    public TestTelegramUserMappingBuilder AsUnlinked()
    {
        _isActive = false;
        return this;
    }

    /// <summary>
    /// Builds and persists the Telegram user mapping to the database.
    /// First creates the telegram_user record (FK requirement), then creates the mapping.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if LinkedToWebUser() was not called.</exception>
    public async Task<TelegramUserMappingRecord> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_userId))
        {
            throw new InvalidOperationException("LinkedToWebUser() must be called before BuildAsync()");
        }

        using var scope = _services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var mappingRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();

        // Create the telegram_user first (FK constraint requires this)
        var now = DateTimeOffset.UtcNow;
        var telegramUser = new TelegramUser(
            TelegramUserId: _telegramId,
            Username: _telegramUsername,
            FirstName: _telegramUsername ?? "Test",
            LastName: "User",
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: false,
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: now,
            LastSeenAt: now,
            CreatedAt: now,
            UpdatedAt: now,
            IsActive: true
        );
        await userRepository.UpsertAsync(telegramUser, cancellationToken);

        // Now create the mapping
        var mapping = new TelegramUserMappingRecord(
            Id: 0, // Will be set by database
            TelegramId: _telegramId,
            TelegramUsername: _telegramUsername,
            UserId: _userId,
            LinkedAt: _linkedAt,
            IsActive: _isActive
        );

        var id = await mappingRepository.InsertAsync(mapping, cancellationToken);

        // Return with the generated ID
        return mapping with { Id = id };
    }
}
