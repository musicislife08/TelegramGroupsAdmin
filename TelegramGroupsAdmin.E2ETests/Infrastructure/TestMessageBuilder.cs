using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating test messages with various configurations.
/// Each test should build exactly the messages it needs with specific states.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// var message = await new TestMessageBuilder(Factory.Services)
///     .InChat(chat.ChatId)
///     .FromUser(123456789, "testuser")
///     .WithText("Hello, world!")
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestMessageBuilder
{
    private readonly IServiceProvider _services;
    private long? _messageId;
    private long _userId = 123456789;
    private string? _userName;
    private string? _firstName = "Test";
    private string? _lastName = "User";
    private long _chatId = -1001234567890;
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;
    private string? _messageText = "Test message";
    private string? _photoFileId;
    private int? _photoFileSize;
    private string? _urls;
    private DateTimeOffset? _editDate;
    private string? _contentHash;
    private string? _chatName = "Test Chat";
    private string? _photoLocalPath;
    private string? _photoThumbnailPath;
    private string? _chatIconPath;
    private string? _userPhotoPath;
    private DateTimeOffset? _deletedAt;
    private string? _deletionSource;
    private long? _replyToMessageId;
    private string? _replyToUser;
    private string? _replyToText;
    private MediaType? _mediaType;
    private string? _mediaFileId;
    private long? _mediaFileSize;
    private string? _mediaFileName;
    private string? _mediaMimeType;
    private string? _mediaLocalPath;
    private int? _mediaDuration;
    private MessageTranslation? _translation;
    private ContentCheckSkipReason _contentCheckSkipReason = ContentCheckSkipReason.NotSkipped;

    public TestMessageBuilder(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Sets the message ID. If not called, a random ID will be generated.
    /// </summary>
    public TestMessageBuilder WithId(long messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>
    /// Sets the chat ID for this message.
    /// </summary>
    public TestMessageBuilder InChat(long chatId)
    {
        _chatId = chatId;
        return this;
    }

    /// <summary>
    /// Sets the chat using a TestChat.
    /// </summary>
    public TestMessageBuilder InChat(TestChat chat)
    {
        _chatId = chat.ChatId;
        _chatName = chat.ChatName;
        return this;
    }

    /// <summary>
    /// Sets the sender of the message.
    /// </summary>
    public TestMessageBuilder FromUser(long userId, string? userName = null, string? firstName = null, string? lastName = null)
    {
        _userId = userId;
        _userName = userName;
        _firstName = firstName ?? "Test";
        _lastName = lastName ?? "User";
        return this;
    }

    /// <summary>
    /// Sets the message text content.
    /// </summary>
    public TestMessageBuilder WithText(string text)
    {
        _messageText = text;
        return this;
    }

    /// <summary>
    /// Sets the message timestamp.
    /// </summary>
    public TestMessageBuilder At(DateTimeOffset timestamp)
    {
        _timestamp = timestamp;
        return this;
    }

    /// <summary>
    /// Sets URLs contained in the message.
    /// </summary>
    public TestMessageBuilder WithUrls(string urls)
    {
        _urls = urls;
        return this;
    }

    /// <summary>
    /// Adds a photo attachment to the message.
    /// </summary>
    public TestMessageBuilder WithPhoto(string fileId, int? fileSize = null, string? localPath = null, string? thumbnailPath = null)
    {
        _photoFileId = fileId;
        _photoFileSize = fileSize;
        _photoLocalPath = localPath;
        _photoThumbnailPath = thumbnailPath;
        return this;
    }

    /// <summary>
    /// Marks the message as deleted.
    /// </summary>
    public TestMessageBuilder AsDeleted(string source = "test")
    {
        _deletedAt = DateTimeOffset.UtcNow;
        _deletionSource = source;
        return this;
    }

    /// <summary>
    /// Sets the message as a reply to another message.
    /// </summary>
    public TestMessageBuilder AsReplyTo(long messageId, string? user = null, string? text = null)
    {
        _replyToMessageId = messageId;
        _replyToUser = user;
        _replyToText = text;
        return this;
    }

    /// <summary>
    /// Adds a media attachment (video, audio, document, etc.).
    /// </summary>
    public TestMessageBuilder WithMedia(MediaType mediaType, string fileId, long? fileSize = null, string? fileName = null, string? mimeType = null, int? duration = null, string? localPath = null)
    {
        _mediaType = mediaType;
        _mediaFileId = fileId;
        _mediaFileSize = fileSize;
        _mediaFileName = fileName;
        _mediaMimeType = mimeType;
        _mediaDuration = duration;
        _mediaLocalPath = localPath;
        return this;
    }

    /// <summary>
    /// Marks the message as edited.
    /// </summary>
    public TestMessageBuilder AsEdited(DateTimeOffset? editDate = null)
    {
        _editDate = editDate ?? DateTimeOffset.UtcNow;
        return this;
    }

    /// <summary>
    /// Sets the content hash for the message.
    /// </summary>
    public TestMessageBuilder WithContentHash(string contentHash)
    {
        _contentHash = contentHash;
        return this;
    }

    /// <summary>
    /// Sets the chat icon path.
    /// </summary>
    public TestMessageBuilder WithChatIcon(string iconPath)
    {
        _chatIconPath = iconPath;
        return this;
    }

    /// <summary>
    /// Sets the user photo path.
    /// </summary>
    public TestMessageBuilder WithUserPhoto(string photoPath)
    {
        _userPhotoPath = photoPath;
        return this;
    }

    /// <summary>
    /// Adds a translation to the message.
    /// </summary>
    public TestMessageBuilder WithTranslation(string translatedText, string detectedLanguage, decimal? confidence = null)
    {
        _translation = new MessageTranslation(
            Id: 0, // Will be assigned by database
            MessageId: _messageId,
            EditId: null,
            TranslatedText: translatedText,
            DetectedLanguage: detectedLanguage,
            Confidence: confidence,
            TranslatedAt: DateTimeOffset.UtcNow
        );
        return this;
    }

    /// <summary>
    /// Sets the content check skip reason.
    /// </summary>
    public TestMessageBuilder WithContentCheckSkipReason(ContentCheckSkipReason reason)
    {
        _contentCheckSkipReason = reason;
        return this;
    }

    /// <summary>
    /// Sets the chat name for display.
    /// </summary>
    public TestMessageBuilder WithChatName(string chatName)
    {
        _chatName = chatName;
        return this;
    }

    /// <summary>
    /// Builds and persists the message to the database.
    /// Returns a TestMessage containing the message record for testing.
    /// </summary>
    public async Task<TestMessage> BuildAsync(CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();

        // Generate a random message ID if not specified
        var messageId = _messageId ?? Random.Shared.NextInt64(1, 999_999_999);

        var messageRecord = new MessageRecord(
            MessageId: messageId,
            UserId: _userId,
            UserName: _userName,
            FirstName: _firstName,
            LastName: _lastName,
            ChatId: _chatId,
            Timestamp: _timestamp,
            MessageText: _messageText,
            PhotoFileId: _photoFileId,
            PhotoFileSize: _photoFileSize,
            Urls: _urls,
            EditDate: _editDate,
            ContentHash: _contentHash,
            ChatName: _chatName,
            PhotoLocalPath: _photoLocalPath,
            PhotoThumbnailPath: _photoThumbnailPath,
            ChatIconPath: _chatIconPath,
            UserPhotoPath: _userPhotoPath,
            DeletedAt: _deletedAt,
            DeletionSource: _deletionSource,
            ReplyToMessageId: _replyToMessageId,
            ReplyToUser: _replyToUser,
            ReplyToText: _replyToText,
            MediaType: _mediaType,
            MediaFileId: _mediaFileId,
            MediaFileSize: _mediaFileSize,
            MediaFileName: _mediaFileName,
            MediaMimeType: _mediaMimeType,
            MediaLocalPath: _mediaLocalPath,
            MediaDuration: _mediaDuration,
            Translation: _translation,
            ContentCheckSkipReason: _contentCheckSkipReason
        );

        await messageRepository.InsertMessageAsync(messageRecord, ct);

        return new TestMessage(messageRecord);
    }
}

/// <summary>
/// Represents a test message for E2E testing.
/// </summary>
public record TestMessage(MessageRecord Record)
{
    public long MessageId => Record.MessageId;
    public long ChatId => Record.ChatId;
    public long UserId => Record.UserId;
    public string? Text => Record.MessageText;
    public DateTimeOffset Timestamp => Record.Timestamp;
}
