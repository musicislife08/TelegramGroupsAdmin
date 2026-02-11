using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating test chats with various configurations.
/// Each test should build exactly the chats it needs with specific states.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// var chat = await new TestChatBuilder(Factory.Services)
///     .WithId(-1001234567890)
///     .WithTitle("Test Chat")
///     .AsGroup()
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestChatBuilder
{
    private readonly IServiceProvider _services;
    private long? _chatId;
    private string _chatName = "Test Chat";
    private ManagedChatType _chatType = ManagedChatType.Group;
    private BotChatStatus _botStatus = BotChatStatus.Administrator;
    private bool _isAdmin = true;
    private bool _isActive = true;
    private bool _isDeleted;
    private DateTimeOffset _addedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _lastSeenAt;
    private string? _settingsJson;
    private string? _chatIconPath;

    public TestChatBuilder(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Sets the chat ID. If not called, a random negative ID will be generated.
    /// Telegram group chat IDs are negative.
    /// </summary>
    public TestChatBuilder WithId(long chatId)
    {
        _chatId = chatId;
        return this;
    }

    /// <summary>
    /// Sets the chat title/name.
    /// </summary>
    public TestChatBuilder WithTitle(string title)
    {
        _chatName = title;
        return this;
    }

    /// <summary>
    /// Sets the chat type (Group, Supergroup, Channel).
    /// </summary>
    public TestChatBuilder WithChatType(ManagedChatType chatType)
    {
        _chatType = chatType;
        return this;
    }

    /// <summary>
    /// Configures the chat as a regular Group.
    /// </summary>
    public TestChatBuilder AsGroup() => WithChatType(ManagedChatType.Group);

    /// <summary>
    /// Configures the chat as a Supergroup.
    /// </summary>
    public TestChatBuilder AsSupergroup() => WithChatType(ManagedChatType.Supergroup);

    /// <summary>
    /// Configures the chat as a Channel.
    /// </summary>
    public TestChatBuilder AsChannel() => WithChatType(ManagedChatType.Channel);

    /// <summary>
    /// Sets the bot's status in the chat.
    /// </summary>
    public TestChatBuilder WithBotStatus(BotChatStatus status)
    {
        _botStatus = status;
        return this;
    }

    /// <summary>
    /// Sets whether the bot is an admin in the chat.
    /// Default is true.
    /// </summary>
    public TestChatBuilder WithAdminStatus(bool isAdmin)
    {
        _isAdmin = isAdmin;
        return this;
    }

    /// <summary>
    /// Sets whether the chat is active (bot has admin permissions).
    /// Default is true.
    /// </summary>
    public TestChatBuilder WithActiveStatus(bool isActive)
    {
        _isActive = isActive;
        return this;
    }

    /// <summary>
    /// Sets whether the chat is deleted (soft-deleted).
    /// Default is false.
    /// </summary>
    public TestChatBuilder WithDeletedStatus(bool isDeleted)
    {
        _isDeleted = isDeleted;
        return this;
    }

    /// <summary>
    /// Sets the timestamp when the bot was added to the chat.
    /// </summary>
    public TestChatBuilder AddedAt(DateTimeOffset timestamp)
    {
        _addedAt = timestamp;
        return this;
    }

    /// <summary>
    /// Sets the last seen timestamp for the chat.
    /// </summary>
    public TestChatBuilder LastSeenAt(DateTimeOffset timestamp)
    {
        _lastSeenAt = timestamp;
        return this;
    }

    /// <summary>
    /// Sets custom settings JSON for the chat.
    /// </summary>
    public TestChatBuilder WithSettings(string settingsJson)
    {
        _settingsJson = settingsJson;
        return this;
    }

    /// <summary>
    /// Sets the chat icon path.
    /// </summary>
    public TestChatBuilder WithIcon(string iconPath)
    {
        _chatIconPath = iconPath;
        return this;
    }

    /// <summary>
    /// Builds and persists the chat to the database.
    /// Returns a TestChat containing the chat record for testing.
    /// </summary>
    public async Task<TestChat> BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var chatRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();

        // Generate a random negative chat ID if not specified (Telegram group IDs are negative)
        var chatId = _chatId ?? -100_0000_0000 - Random.Shared.Next(1, 999_999_999);

        var chatRecord = new ManagedChatRecord(
            Identity: new ChatIdentity(chatId, _chatName),
            ChatType: _chatType,
            BotStatus: _botStatus,
            IsAdmin: _isAdmin,
            AddedAt: _addedAt,
            IsActive: _isActive,
            IsDeleted: _isDeleted,
            LastSeenAt: _lastSeenAt,
            SettingsJson: _settingsJson,
            ChatIconPath: _chatIconPath
        );

        await chatRepository.UpsertAsync(chatRecord, cancellationToken);

        return new TestChat(chatRecord);
    }
}

/// <summary>
/// Represents a test chat for E2E testing.
/// </summary>
public record TestChat(ManagedChatRecord Record)
{
    public long ChatId => Record.Identity.Id;
    public string? ChatName => Record.Identity.ChatName;
    public ManagedChatType ChatType => Record.ChatType;
    public bool IsActive => Record.IsActive;
    public bool IsAdmin => Record.IsAdmin;
}
