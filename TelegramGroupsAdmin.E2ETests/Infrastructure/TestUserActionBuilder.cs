using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating user moderation action records for E2E testing.
/// Creates action records via the IUserActionsRepository.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// await new TestUserActionBuilder(Factory.Services)
///     .WithUserId(123456)
///     .WithActionType(UserActionType.Ban)
///     .WithSystemIssuer("bot_protection")
///     .WithReason("Spam detected")
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestUserActionBuilder
{
    private readonly IServiceProvider _services;
    private long _userId;
    private UserActionType _actionType = UserActionType.Ban;
    private long? _messageId;
    private Actor _issuedBy = Actor.BotProtection;
    private DateTimeOffset _issuedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _expiresAt;
    private string? _reason;

    public TestUserActionBuilder(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Sets the Telegram user ID who this action targets.
    /// </summary>
    public TestUserActionBuilder WithUserId(long userId)
    {
        _userId = userId;
        return this;
    }

    /// <summary>
    /// Sets the action type (Ban, Warn, Mute, Trust, Unban).
    /// </summary>
    public TestUserActionBuilder WithActionType(UserActionType actionType)
    {
        _actionType = actionType;
        return this;
    }

    /// <summary>
    /// Sets the message ID that triggered this action (optional).
    /// </summary>
    public TestUserActionBuilder WithMessageId(long messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>
    /// Sets who issued this action using an Actor.
    /// </summary>
    public TestUserActionBuilder WithIssuer(Actor issuedBy)
    {
        _issuedBy = issuedBy;
        return this;
    }

    /// <summary>
    /// Sets the issuer as a system identifier.
    /// </summary>
    public TestUserActionBuilder WithSystemIssuer(string systemIdentifier)
    {
        _issuedBy = Actor.FromSystem(systemIdentifier);
        return this;
    }

    /// <summary>
    /// Sets the issuer as a web user.
    /// </summary>
    public TestUserActionBuilder WithWebUserIssuer(string userId, string? email = null)
    {
        _issuedBy = Actor.FromWebUser(userId, email);
        return this;
    }

    /// <summary>
    /// Sets the issuer as a Telegram user.
    /// </summary>
    public TestUserActionBuilder WithTelegramUserIssuer(long telegramUserId, string? username = null, string? firstName = null, string? lastName = null)
    {
        _issuedBy = Actor.FromTelegramUser(telegramUserId, username, firstName, lastName);
        return this;
    }

    /// <summary>
    /// Sets when this action was issued.
    /// </summary>
    public TestUserActionBuilder IssuedAt(DateTimeOffset timestamp)
    {
        _issuedAt = timestamp;
        return this;
    }

    /// <summary>
    /// Sets when this action expires (for temporary bans/mutes).
    /// </summary>
    public TestUserActionBuilder ExpiresAt(DateTimeOffset timestamp)
    {
        _expiresAt = timestamp;
        return this;
    }

    /// <summary>
    /// Sets the action to be permanent (no expiration).
    /// </summary>
    public TestUserActionBuilder AsPermanent()
    {
        _expiresAt = null;
        return this;
    }

    /// <summary>
    /// Sets the action to expire after a specified duration from now.
    /// </summary>
    public TestUserActionBuilder ExpiresIn(TimeSpan duration)
    {
        _expiresAt = DateTimeOffset.UtcNow.Add(duration);
        return this;
    }

    /// <summary>
    /// Sets the reason for this action.
    /// </summary>
    public TestUserActionBuilder WithReason(string reason)
    {
        _reason = reason;
        return this;
    }

    /// <summary>
    /// Builds and persists the user action record.
    /// Returns the generated action ID.
    /// </summary>
    public async Task<long> BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();

        var record = new UserActionRecord(
            Id: 0, // Will be assigned by database
            UserId: _userId,
            ActionType: _actionType,
            MessageId: _messageId,
            IssuedBy: _issuedBy,
            IssuedAt: _issuedAt,
            ExpiresAt: _expiresAt,
            Reason: _reason
        );

        return await repository.InsertAsync(record, cancellationToken);
    }

    #region Common Action Shortcuts

    /// <summary>
    /// Creates a ban action from bot protection.
    /// </summary>
    public TestUserActionBuilder AsBan(long userId, string? reason = null)
    {
        var builder = WithUserId(userId)
            .WithActionType(UserActionType.Ban)
            .WithSystemIssuer("bot_protection");

        if (!string.IsNullOrEmpty(reason))
        {
            builder.WithReason(reason);
        }

        return builder;
    }

    /// <summary>
    /// Creates a warn action from auto-detection.
    /// </summary>
    public TestUserActionBuilder AsWarn(long userId, string? reason = null)
    {
        var builder = WithUserId(userId)
            .WithActionType(UserActionType.Warn)
            .WithSystemIssuer("auto_detection");

        if (!string.IsNullOrEmpty(reason))
        {
            builder.WithReason(reason);
        }

        return builder;
    }

    /// <summary>
    /// Creates a mute action.
    /// </summary>
    public TestUserActionBuilder AsMute(long userId, TimeSpan duration, string? reason = null)
    {
        var builder = WithUserId(userId)
            .WithActionType(UserActionType.Mute)
            .WithSystemIssuer("auto_detection")
            .ExpiresIn(duration);

        if (!string.IsNullOrEmpty(reason))
        {
            builder.WithReason(reason);
        }

        return builder;
    }

    /// <summary>
    /// Creates a trust action.
    /// </summary>
    public TestUserActionBuilder AsTrust(long userId, string? reason = null)
    {
        var builder = WithUserId(userId)
            .WithActionType(UserActionType.Trust)
            .WithSystemIssuer("auto_trust");

        if (!string.IsNullOrEmpty(reason))
        {
            builder.WithReason(reason);
        }

        return builder;
    }

    /// <summary>
    /// Creates an unban action.
    /// </summary>
    public TestUserActionBuilder AsUnban(long userId, string? reason = null)
    {
        var builder = WithUserId(userId)
            .WithActionType(UserActionType.Unban);

        if (!string.IsNullOrEmpty(reason))
        {
            builder.WithReason(reason);
        }

        return builder;
    }

    #endregion
}
