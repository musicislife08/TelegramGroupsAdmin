using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramGroupsAdmin.IntegrationTests.TestHelpers;

/// <summary>
/// Factory for creating Telegram.Bot types in tests.
/// Telegram.Bot v22+ uses auto-generated classes with public setters,
/// so we can use standard object initializers.
/// </summary>
public static class TelegramTestFactory
{
    /// <summary>
    /// Creates a Message with the specified properties.
    /// </summary>
    public static Message CreateMessage(
        int messageId = 1,
        long chatId = 123456789L,
        ChatType chatType = ChatType.Private,
        long? fromUserId = null,
        string? text = null)
    {
        var message = new Message
        {
            Id = messageId,  // Note: property is "Id", maps to "message_id" in JSON
            Date = DateTime.UtcNow,
            Chat = CreateChat(chatId, chatType)
        };

        if (fromUserId.HasValue)
        {
            message.From = CreateUser(fromUserId.Value);
        }

        if (text != null)
        {
            message.Text = text;
        }

        return message;
    }

    /// <summary>
    /// Creates a Chat with the specified properties.
    /// </summary>
    public static Chat CreateChat(
        long id = 123456789L,
        ChatType type = ChatType.Private,
        string? title = null,
        string? username = null)
    {
        return new Chat
        {
            Id = id,
            Type = type,
            Title = title,
            Username = username
        };
    }

    /// <summary>
    /// Creates a User with the specified properties.
    /// </summary>
    public static User CreateUser(
        long id = 123456789L,
        string firstName = "Test",
        string? lastName = null,
        string? username = null,
        bool isBot = false)
    {
        return new User
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Username = username,
            IsBot = isBot
        };
    }

    /// <summary>
    /// Creates a ChatFullInfo with the specified properties.
    /// Used for GetChatAsync mock responses which return full chat details.
    /// </summary>
    public static ChatFullInfo CreateChatFullInfo(
        long id = 123456789L,
        ChatType type = ChatType.Private,
        string? title = null,
        string? username = null,
        ChatPermissions? permissions = null)
    {
        return new ChatFullInfo
        {
            Id = id,
            Type = type,
            Title = title,
            Username = username,
            Permissions = permissions,
            AccentColorId = 0 // Required property in ChatFullInfo
        };
    }
}
