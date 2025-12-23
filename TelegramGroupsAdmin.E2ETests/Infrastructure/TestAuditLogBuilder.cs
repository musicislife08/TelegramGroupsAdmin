using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating audit log entries for E2E testing.
/// Creates audit records via the IAuditService.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// await new TestAuditLogBuilder(Factory.Services)
///     .WithEventType(AuditEventType.UserLogin)
///     .WithWebUserActor(testUser.Id, testUser.Email)
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestAuditLogBuilder
{
    private readonly IServiceProvider _services;
    private AuditEventType _eventType = AuditEventType.UserLogin;
    private Actor _actor = Actor.AutoDetection;
    private Actor? _target;
    private string? _value;

    public TestAuditLogBuilder(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Sets the audit event type.
    /// </summary>
    public TestAuditLogBuilder WithEventType(AuditEventType eventType)
    {
        _eventType = eventType;
        return this;
    }

    /// <summary>
    /// Sets the actor as a web user.
    /// </summary>
    public TestAuditLogBuilder WithWebUserActor(string userId, string? email = null)
    {
        _actor = Actor.FromWebUser(userId, email);
        return this;
    }

    /// <summary>
    /// Sets the actor as a Telegram user.
    /// </summary>
    public TestAuditLogBuilder WithTelegramUserActor(long telegramUserId, string? username = null, string? firstName = null, string? lastName = null)
    {
        _actor = Actor.FromTelegramUser(telegramUserId, username, firstName, lastName);
        return this;
    }

    /// <summary>
    /// Sets the actor as a system identifier.
    /// </summary>
    public TestAuditLogBuilder WithSystemActor(string systemIdentifier)
    {
        _actor = Actor.FromSystem(systemIdentifier);
        return this;
    }

    /// <summary>
    /// Sets the actor using a pre-built Actor object.
    /// </summary>
    public TestAuditLogBuilder WithActor(Actor actor)
    {
        _actor = actor;
        return this;
    }

    /// <summary>
    /// Sets the target as a web user.
    /// </summary>
    public TestAuditLogBuilder WithWebUserTarget(string userId, string? email = null)
    {
        _target = Actor.FromWebUser(userId, email);
        return this;
    }

    /// <summary>
    /// Sets the target as a Telegram user.
    /// </summary>
    public TestAuditLogBuilder WithTelegramUserTarget(long telegramUserId, string? username = null, string? firstName = null, string? lastName = null)
    {
        _target = Actor.FromTelegramUser(telegramUserId, username, firstName, lastName);
        return this;
    }

    /// <summary>
    /// Sets the target as a system identifier.
    /// </summary>
    public TestAuditLogBuilder WithSystemTarget(string systemIdentifier)
    {
        _target = Actor.FromSystem(systemIdentifier);
        return this;
    }

    /// <summary>
    /// Sets the target using a pre-built Actor object.
    /// </summary>
    public TestAuditLogBuilder WithTarget(Actor target)
    {
        _target = target;
        return this;
    }

    /// <summary>
    /// Sets the optional value/details for the audit entry.
    /// </summary>
    public TestAuditLogBuilder WithValue(string value)
    {
        _value = value;
        return this;
    }

    /// <summary>
    /// Builds and persists the audit log entry.
    /// </summary>
    public async Task BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        await auditService.LogEventAsync(_eventType, _actor, _target, _value, cancellationToken);
    }

    #region Common Event Shortcuts

    /// <summary>
    /// Creates a login event for a web user.
    /// </summary>
    public TestAuditLogBuilder AsLoginEvent(string userId, string? email = null)
    {
        return WithEventType(AuditEventType.UserLogin)
            .WithWebUserActor(userId, email);
    }

    /// <summary>
    /// Creates a login failed event for a web user.
    /// </summary>
    public TestAuditLogBuilder AsLoginFailedEvent(string userId, string? email = null)
    {
        return WithEventType(AuditEventType.UserLoginFailed)
            .WithWebUserActor(userId, email);
    }

    /// <summary>
    /// Creates a permission changed event.
    /// </summary>
    public TestAuditLogBuilder AsPermissionChangedEvent(string actorUserId, string targetUserId, string? actorEmail = null, string? targetEmail = null, string? details = null)
    {
        var builder = WithEventType(AuditEventType.UserPermissionChanged)
            .WithWebUserActor(actorUserId, actorEmail)
            .WithWebUserTarget(targetUserId, targetEmail);

        if (!string.IsNullOrEmpty(details))
        {
            builder.WithValue(details);
        }

        return builder;
    }

    /// <summary>
    /// Creates a user registered event.
    /// </summary>
    public TestAuditLogBuilder AsUserRegisteredEvent(string userId, string? email = null)
    {
        return WithEventType(AuditEventType.UserRegistered)
            .WithWebUserActor(userId, email);
    }

    /// <summary>
    /// Creates a configuration changed event by the system.
    /// </summary>
    public TestAuditLogBuilder AsConfigChangedEvent(string details)
    {
        return WithEventType(AuditEventType.ConfigurationChanged)
            .WithSystemActor("system")
            .WithValue(details);
    }

    #endregion
}
