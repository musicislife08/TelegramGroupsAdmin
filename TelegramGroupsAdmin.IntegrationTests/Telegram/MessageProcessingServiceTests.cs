using Telegram.Bot.Types;
using TelegramBotDocument = Telegram.Bot.Types.Document;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Handlers;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram;

/// <summary>
/// REFACTOR-1: Tests updated to call extracted handler methods directly (no more reflection)
/// Tests validate behavior is preserved after extraction from MessageProcessingService.
/// These tests focus on pure static functions with zero
/// dependencies that can be tested in isolation.
///
/// Tests created: 2025-10-28 before REFACTOR-1
/// Purpose: Catch regressions during extraction of MediaDownloadHandler, FileScanningHandler, TranslationHandler
/// </summary>
[TestFixture]
public class MessageProcessingServiceTests
{
    #region DetectMediaAttachment Tests

    [Test]
    public void DetectMediaAttachment_Animation_ReturnsCorrectMetadata()
    {
        // Arrange: Message with Animation (GIF)
        var message = new Message
        {
            Animation = new Animation
            {
                FileId = "test_anim_id",
                FileSize = 1024000,
                FileName = "test.gif",
                MimeType = "video/mp4",
                Duration = 5
            }
        };

        // Act: Detect media using reflection to call private static method
        var result = CallDetectMediaAttachment(message);

        // Assert: Returns Animation metadata
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.MediaType, Is.EqualTo(MediaType.Animation));
        Assert.That(result.Value.FileId, Is.EqualTo("test_anim_id"));
        Assert.That(result.Value.FileSize, Is.EqualTo(1024000L));
        Assert.That(result.Value.FileName, Is.EqualTo("test.gif"));
        Assert.That(result.Value.MimeType, Is.EqualTo("video/mp4"));
        Assert.That(result.Value.Duration, Is.EqualTo(5));
    }

    [Test]
    public void DetectMediaAttachment_Video_ReturnsCorrectMetadata()
    {
        // Arrange: Message with Video
        var message = new Message
        {
            Video = new Video
            {
                FileId = "test_video_id",
                FileSize = 5242880,
                FileName = "vacation.mp4",
                MimeType = "video/mp4",
                Duration = 120
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.MediaType, Is.EqualTo(MediaType.Video));
        Assert.That(result.Value.FileId, Is.EqualTo("test_video_id"));
        Assert.That(result.Value.FileSize, Is.EqualTo(5242880L));
        Assert.That(result.Value.FileName, Is.EqualTo("vacation.mp4"));
        Assert.That(result.Value.MimeType, Is.EqualTo("video/mp4"));
        Assert.That(result.Value.Duration, Is.EqualTo(120));
    }

    [Test]
    public void DetectMediaAttachment_Audio_ReturnsCorrectMetadata()
    {
        // Arrange: Message with Audio (music file with metadata)
        var message = new Message
        {
            Audio = new Audio
            {
                FileId = "test_audio_id",
                FileSize = 3145728,
                FileName = "song.mp3",
                Title = "Amazing Song",
                MimeType = "audio/mpeg",
                Duration = 180
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.MediaType, Is.EqualTo(MediaType.Audio));
        Assert.That(result.Value.FileId, Is.EqualTo("test_audio_id"));
        Assert.That(result.Value.FileSize, Is.EqualTo(3145728L));
        Assert.That(result.Value.FileName, Is.EqualTo("song.mp3"));
        Assert.That(result.Value.MimeType, Is.EqualTo("audio/mpeg"));
        Assert.That(result.Value.Duration, Is.EqualTo(180));
    }

    [Test]
    public void DetectMediaAttachment_AudioWithoutFilename_UsesTitle()
    {
        // Arrange: Audio without FileName, has Title
        var message = new Message
        {
            Audio = new Audio
            {
                FileId = "test_audio_id",
                FileSize = 2000000,
                Title = "Song Title",
                FileUniqueId = "unique123",
                MimeType = "audio/mpeg",
                Duration = 150
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert: Should use Title when FileName is null
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.FileName, Is.EqualTo("Song Title"));
    }

    [Test]
    public void DetectMediaAttachment_AudioWithoutFilenameOrTitle_UsesGeneratedName()
    {
        // Arrange: Audio without FileName or Title
        var message = new Message
        {
            Audio = new Audio
            {
                FileId = "test_audio_id",
                FileSize = 2000000,
                FileUniqueId = "unique456",
                MimeType = "audio/mpeg",
                Duration = 150
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert: Should generate filename using FileUniqueId
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.FileName, Is.EqualTo("audio_unique456.mp3"));
    }

    [Test]
    public void DetectMediaAttachment_Voice_ReturnsGeneratedFilename()
    {
        // Arrange: Message with Voice (OGG voice note)
        var message = new Message
        {
            Voice = new Voice
            {
                FileId = "test_voice_id",
                FileSize = 51200,
                FileUniqueId = "voice789",
                MimeType = "audio/ogg",
                Duration = 10
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert: Voice always generates filename
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.MediaType, Is.EqualTo(MediaType.Voice));
        Assert.That(result.Value.FileId, Is.EqualTo("test_voice_id"));
        Assert.That(result.Value.FileSize, Is.EqualTo(51200L));
        Assert.That(result.Value.FileName, Is.EqualTo("voice_voice789.ogg"));
        Assert.That(result.Value.MimeType, Is.EqualTo("audio/ogg"));
        Assert.That(result.Value.Duration, Is.EqualTo(10));
    }

    [Test]
    public void DetectMediaAttachment_Sticker_ReturnsCorrectMetadata()
    {
        // Arrange: Message with Sticker (WebP format)
        var message = new Message
        {
            Sticker = new Sticker
            {
                FileId = "test_sticker_id",
                FileSize = 20480,
                FileUniqueId = "sticker999",
                IsAnimated = false,
                IsVideo = false
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert: Stickers have no duration, always WebP
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.MediaType, Is.EqualTo(MediaType.Sticker));
        Assert.That(result.Value.FileId, Is.EqualTo("test_sticker_id"));
        Assert.That(result.Value.FileSize, Is.EqualTo(20480L));
        Assert.That(result.Value.FileName, Is.EqualTo("sticker_sticker999.webp"));
        Assert.That(result.Value.MimeType, Is.EqualTo("image/webp"));
        Assert.That(result.Value.Duration, Is.Null);
    }

    [Test]
    public void DetectMediaAttachment_VideoNote_ReturnsCorrectMetadata()
    {
        // Arrange: Message with VideoNote (circular video)
        var message = new Message
        {
            VideoNote = new VideoNote
            {
                FileId = "test_videonote_id",
                FileSize = 1048576,
                FileUniqueId = "videonote888",
                Duration = 30
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.MediaType, Is.EqualTo(MediaType.VideoNote));
        Assert.That(result.Value.FileId, Is.EqualTo("test_videonote_id"));
        Assert.That(result.Value.FileSize, Is.EqualTo(1048576L));
        Assert.That(result.Value.FileName, Is.EqualTo("videonote_videonote888.mp4"));
        Assert.That(result.Value.MimeType, Is.EqualTo("video/mp4"));
        Assert.That(result.Value.Duration, Is.EqualTo(30));
    }

    [Test]
    public void DetectMediaAttachment_Document_ReturnsMetadataOnly()
    {
        // Arrange: Message with Document
        var message = new Message
        {
            Document = new TelegramBotDocument
            {
                FileId = "test_doc_id",
                FileSize = 2097152,
                FileName = "report.pdf",
                MimeType = "application/pdf"
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert: Document has no duration
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.MediaType, Is.EqualTo(MediaType.Document));
        Assert.That(result.Value.FileId, Is.EqualTo("test_doc_id"));
        Assert.That(result.Value.FileSize, Is.EqualTo(2097152L));
        Assert.That(result.Value.FileName, Is.EqualTo("report.pdf"));
        Assert.That(result.Value.MimeType, Is.EqualTo("application/pdf"));
        Assert.That(result.Value.Duration, Is.Null);
    }

    [Test]
    public void DetectMediaAttachment_DocumentWithoutFilename_UsesFallback()
    {
        // Arrange: Document without filename
        var message = new Message
        {
            Document = new TelegramBotDocument
            {
                FileId = "test_doc_id",
                FileSize = 1000000,
                MimeType = "application/octet-stream"
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert: Uses "document" fallback
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.FileName, Is.EqualTo("document"));
    }

    [Test]
    public void DetectMediaAttachment_NoMedia_ReturnsNull()
    {
        // Arrange: Message with no media (text only)
        var message = new Message
        {
            Text = "Hello world"
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert: No media found
        Assert.That(result, Is.Null);
    }

    [Test]
    public void DetectMediaAttachment_PriorityOrder_AnimationBeforeDocument()
    {
        // Arrange: Telegram GIF with BOTH Animation and Document properties
        // This is how Telegram API works - GIFs have both properties
        var message = new Message
        {
            Animation = new Animation
            {
                FileId = "anim_id",
                FileSize = 500000,
                FileName = "test.gif",
                MimeType = "video/mp4",
                Duration = 3
            },
            Document = new TelegramBotDocument
            {
                FileId = "doc_id",
                FileSize = 500000,
                FileName = "test.gif",
                MimeType = "video/mp4"
            }
        };

        // Act
        var result = CallDetectMediaAttachment(message);

        // Assert: Should return Animation (priority order)
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.MediaType, Is.EqualTo(MediaType.Animation));
        Assert.That(result.Value.FileId, Is.EqualTo("anim_id"));
    }

    #endregion

    #region HasFileAttachment Tests

    [Test]
    public void HasFileAttachment_PureDocument_ReturnsTrue()
    {
        // Arrange: Pure document attachment (PDF)
        var message = new Message
        {
            Document = new TelegramBotDocument
            {
                FileId = "doc_id",
                FileSize = 2048000,
                FileName = "report.pdf",
                MimeType = "application/pdf"
            }
        };

        // Act
        var result = CallHasFileAttachment(message, out var fileId, out var size, out var name, out var type);

        // Assert: Returns true with metadata
        Assert.That(result, Is.True);
        Assert.That(fileId, Is.EqualTo("doc_id"));
        Assert.That(size, Is.EqualTo(2048000L));
        Assert.That(name, Is.EqualTo("report.pdf"));
        Assert.That(type, Is.EqualTo("application/pdf"));
    }

    [Test]
    public void HasFileAttachment_AnimationWithDocument_ReturnsFalse()
    {
        // Arrange: Telegram GIF with BOTH Animation and Document properties
        // CRITICAL TEST: This is the bug fix for false positives
        var message = new Message
        {
            Animation = new Animation { FileId = "gif_id", FileSize = 500000 },
            Document = new TelegramBotDocument { FileId = "doc_id", FileName = "test.gif" }
        };

        // Act
        var result = CallHasFileAttachment(message, out var fileId, out var size, out var name, out var type);

        // Assert: Returns false (GIF is media, not scannable document)
        Assert.That(result, Is.False);
        Assert.That(fileId, Is.Null);
        Assert.That(size, Is.EqualTo(0));
        Assert.That(name, Is.Null);
        Assert.That(type, Is.Null);
    }

    [Test]
    public void HasFileAttachment_VideoWithDocument_ReturnsFalse()
    {
        // Arrange: Video with Document property
        var message = new Message
        {
            Video = new Video { FileId = "video_id", FileSize = 1000000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = CallHasFileAttachment(message, out _, out _, out _, out _);

        // Assert: Exclude video from document scanning
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasFileAttachment_AudioWithDocument_ReturnsFalse()
    {
        // Arrange: Audio with Document property
        var message = new Message
        {
            Audio = new Audio { FileId = "audio_id", FileSize = 2000000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = CallHasFileAttachment(message, out _, out _, out _, out _);

        // Assert: Exclude audio from document scanning
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasFileAttachment_VoiceWithDocument_ReturnsFalse()
    {
        // Arrange: Voice with Document property
        var message = new Message
        {
            Voice = new Voice { FileId = "voice_id", FileSize = 50000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = CallHasFileAttachment(message, out _, out _, out _, out _);

        // Assert: Exclude voice from document scanning
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasFileAttachment_StickerWithDocument_ReturnsFalse()
    {
        // Arrange: Sticker with Document property
        var message = new Message
        {
            Sticker = new Sticker { FileId = "sticker_id", FileSize = 20000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = CallHasFileAttachment(message, out _, out _, out _, out _);

        // Assert: Exclude sticker from document scanning
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasFileAttachment_VideoNoteWithDocument_ReturnsFalse()
    {
        // Arrange: VideoNote with Document property
        var message = new Message
        {
            VideoNote = new VideoNote { FileId = "videonote_id", FileSize = 1000000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = CallHasFileAttachment(message, out _, out _, out _, out _);

        // Assert: Exclude video note from document scanning
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasFileAttachment_NoDocument_ReturnsFalse()
    {
        // Arrange: Message with no attachments
        var message = new Message
        {
            Text = "Hello world"
        };

        // Act
        var result = CallHasFileAttachment(message, out var fileId, out var size, out var name, out var type);

        // Assert: No document found
        Assert.That(result, Is.False);
        Assert.That(fileId, Is.Null);
        Assert.That(size, Is.EqualTo(0));
        Assert.That(name, Is.Null);
        Assert.That(type, Is.Null);
    }

    #endregion

    #region CalculateLatinScriptRatio Tests

    [Test]
    public void CalculateLatinScriptRatio_PureEnglish_ReturnsOne()
    {
        // Arrange: Pure English text
        var text = "Hello world! This is a test message.";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: 100% Latin script
        Assert.That(ratio, Is.EqualTo(1.0).Within(0.01));
    }

    [Test]
    public void CalculateLatinScriptRatio_PureCyrillic_ReturnsZero()
    {
        // Arrange: Pure Cyrillic text (Russian)
        var text = "Привет мир! Это тестовое сообщение.";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: 0% Latin script
        Assert.That(ratio, Is.EqualTo(0.0).Within(0.01));
    }

    [Test]
    public void CalculateLatinScriptRatio_MixedText_ReturnsPartialRatio()
    {
        // Arrange: Mixed Latin + Cyrillic
        // "Hello" = 5 Latin chars, "мир" = 3 Cyrillic chars
        var text = "Hello мир";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: 5 Latin / 8 total = 0.625
        Assert.That(ratio, Is.EqualTo(0.625).Within(0.01));
    }

    [Test]
    public void CalculateLatinScriptRatio_PunctuationIgnored_ReturnsCorrectRatio()
    {
        // Arrange: Text with lots of punctuation and emoji
        // Only letters/digits count: "Hello" = 5 Latin, "мир" = 3 Cyrillic
        var text = "Hello!!! 😊 мир???";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: 5 Latin / 8 total = 0.625
        Assert.That(ratio, Is.EqualTo(0.625).Within(0.01));
    }

    [Test]
    public void CalculateLatinScriptRatio_EmptyString_ReturnsZero()
    {
        // Arrange: Empty text
        var text = "";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: 0.0 (default for empty)
        Assert.That(ratio, Is.EqualTo(0.0));
    }

    [Test]
    public void CalculateLatinScriptRatio_WhitespaceOnly_ReturnsZero()
    {
        // Arrange: Whitespace only
        var text = "   \t\n  ";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: 0.0 (no letters/digits)
        Assert.That(ratio, Is.EqualTo(0.0));
    }

    [Test]
    public void CalculateLatinScriptRatio_NumbersCountAsLatin_ReturnsOne()
    {
        // Arrange: Numbers are in Latin range (0x0000-0x024F)
        var text = "123 456 789";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: Numbers count as Latin
        Assert.That(ratio, Is.EqualTo(1.0).Within(0.01));
    }

    [Test]
    public void CalculateLatinScriptRatio_ArabicScript_ReturnsZero()
    {
        // Arrange: Arabic text
        var text = "مرحبا بالعالم";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: 0% Latin (Arabic is outside Latin range)
        Assert.That(ratio, Is.EqualTo(0.0).Within(0.01));
    }

    [Test]
    public void CalculateLatinScriptRatio_ChineseCharacters_ReturnsZero()
    {
        // Arrange: Chinese text
        var text = "你好世界";

        // Act
        var ratio = CallCalculateLatinScriptRatio(text);

        // Assert: 0% Latin
        Assert.That(ratio, Is.EqualTo(0.0).Within(0.01));
    }

    #endregion

    #region Translation Eligibility Tests

    [Test]
    public void ShouldTranslate_DisabledConfig_ReturnsFalse()
    {
        // Arrange: Translation disabled in config
        var enabled = false;
        var minLength = 10;
        var threshold = 0.8;
        var text = "Привет мир, это тестовое сообщение";

        // Act: Check eligibility (mimics lines 371-376)
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              CallCalculateLatinScriptRatio(text) < threshold;

        // Assert: Don't translate (disabled)
        Assert.That(shouldTranslate, Is.False);
    }

    [Test]
    public void ShouldTranslate_TooShort_ReturnsFalse()
    {
        // Arrange: Enabled but message too short
        var enabled = true;
        var minLength = 20;
        var threshold = 0.8;
        var text = "Привет"; // 6 chars

        // Act
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              CallCalculateLatinScriptRatio(text) < threshold;

        // Assert: Don't translate (too short)
        Assert.That(shouldTranslate, Is.False);
    }

    [Test]
    public void ShouldTranslate_AlreadyEnglish_ReturnsFalse()
    {
        // Arrange: Enabled but message already in English
        var enabled = true;
        var minLength = 10;
        var threshold = 0.8;
        var text = "Hello world, this is a test message";

        // Act
        var latinRatio = CallCalculateLatinScriptRatio(text);
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              latinRatio < threshold;

        // Assert: Don't translate (already English)
        Assert.That(shouldTranslate, Is.False);
        Assert.That(latinRatio, Is.GreaterThan(0.9)); // High Latin ratio
    }

    [Test]
    public void ShouldTranslate_NonEnglishLongMessage_ReturnsTrue()
    {
        // Arrange: Enabled, long, non-English message
        var enabled = true;
        var minLength = 10;
        var threshold = 0.8;
        var text = "Привет мир, это тестовое сообщение для проверки перевода";

        // Act
        var latinRatio = CallCalculateLatinScriptRatio(text);
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              latinRatio < threshold;

        // Assert: Should translate
        Assert.That(shouldTranslate, Is.True);
        Assert.That(latinRatio, Is.LessThan(0.1)); // Low Latin ratio
    }

    [Test]
    public void ShouldTranslate_BoundaryCase_ExactMinLength_ReturnsTrue()
    {
        // Arrange: Message exactly at minimum length threshold
        var enabled = true;
        var minLength = 20;
        var threshold = 0.8;
        var text = "Привет мир тест тест"; // Exactly 20 chars

        // Act
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              CallCalculateLatinScriptRatio(text) < threshold;

        // Assert: Should translate (>= check)
        Assert.That(shouldTranslate, Is.True);
        Assert.That(text.Length, Is.EqualTo(20));
    }

    [Test]
    public void ShouldTranslate_BoundaryCase_ExactThreshold_ReturnsFalse()
    {
        // Arrange: Latin ratio exactly at threshold (0.8)
        var enabled = true;
        var minLength = 10;
        var threshold = 0.8;
        // Craft text with exactly 80% Latin: "HelloTest" = 9 Latin, "мир" = 3 Cyrillic (but only 9 Latin total for 75%)
        // Need 8 Latin + 2 Cyrillic = 0.8
        var text = "HelloHel мм"; // 8 Latin, 2 Cyrillic = 0.8

        // Act
        var latinRatio = CallCalculateLatinScriptRatio(text);
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              latinRatio < threshold;

        // Assert: Don't translate (< check, not <=)
        Assert.That(shouldTranslate, Is.False);
        Assert.That(latinRatio, Is.EqualTo(0.8).Within(0.01));
    }

    #endregion

    #region Service Message Detection Tests

    [Test]
    public void IsServiceMessage_NewChatMembers_ReturnsTrue()
    {
        // Arrange: Message with new members (join event)
        var message = new Message
        {
            NewChatMembers = [new User { Id = 123, FirstName = "John" }]
        };

        // Act: Check service message flags (mimics lines 109-117)
        var isService = message.NewChatMembers != null ||
                        message.LeftChatMember != null ||
                        message.NewChatPhoto != null ||
                        message.DeleteChatPhoto == true ||
                        message.NewChatTitle != null ||
                        message.PinnedMessage != null ||
                        message.GroupChatCreated == true ||
                        message.SupergroupChatCreated == true ||
                        message.ChannelChatCreated == true;

        // Assert: Detected as service message
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_LeftChatMember_ReturnsTrue()
    {
        // Arrange: Message with left member (user left)
        var message = new Message
        {
            LeftChatMember = new User { Id = 456, FirstName = "Jane" }
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_NewChatPhoto_ReturnsTrue()
    {
        // Arrange: Chat icon changed
        var message = new Message
        {
            NewChatPhoto = [new PhotoSize { FileId = "photo_id" }]
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_DeleteChatPhoto_ReturnsTrue()
    {
        // Arrange: Chat icon removed
        var message = new Message
        {
            DeleteChatPhoto = true
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_NewChatTitle_ReturnsTrue()
    {
        // Arrange: Chat renamed
        var message = new Message
        {
            NewChatTitle = "New Group Name"
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_PinnedMessage_ReturnsTrue()
    {
        // Arrange: Pin notification
        var message = new Message
        {
            PinnedMessage = new Message { Text = "Important message" }
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_GroupChatCreated_ReturnsTrue()
    {
        // Arrange: New group created
        var message = new Message
        {
            GroupChatCreated = true
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_SupergroupChatCreated_ReturnsTrue()
    {
        // Arrange: Group upgraded to supergroup
        var message = new Message
        {
            SupergroupChatCreated = true
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_ChannelChatCreated_ReturnsTrue()
    {
        // Arrange: New channel created
        var message = new Message
        {
            ChannelChatCreated = true
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert
        Assert.That(isService, Is.True);
    }

    [Test]
    public void IsServiceMessage_RegularTextMessage_ReturnsFalse()
    {
        // Arrange: Regular user message
        var message = new Message
        {
            Text = "Hello world",
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = 456 },
            From = new User { Id = 789, FirstName = "Test" }
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert: Not a service message
        Assert.That(isService, Is.False);
    }

    [Test]
    public void IsServiceMessage_MultipleProperties_ReturnsTrue()
    {
        // Arrange: Edge case - multiple service properties (shouldn't happen but test OR logic)
        var message = new Message
        {
            NewChatTitle = "New Name",
            GroupChatCreated = true
        };

        // Act
        var isService = IsServiceMessageCheck(message);

        // Assert: OR logic - any property triggers true
        Assert.That(isService, Is.True);
    }

    /// <summary>
    /// Helper to check service message (mimics lines 109-117)
    /// </summary>
    private static bool IsServiceMessageCheck(Message message)
    {
        return message.NewChatMembers != null ||
               message.LeftChatMember != null ||
               message.NewChatPhoto != null ||
               message.DeleteChatPhoto == true ||
               message.NewChatTitle != null ||
               message.PinnedMessage != null ||
               message.GroupChatCreated == true ||
               message.SupergroupChatCreated == true ||
               message.ChannelChatCreated == true;
    }

    #endregion

    #region REFACTOR-1: Direct Handler Method Calls (No Reflection)

    /// <summary>
    /// Call MediaProcessingHandler.DetectMediaAttachment (public static method)
    /// </summary>
    private static (MediaType MediaType, string FileId, long FileSize, string? FileName, string? MimeType, int? Duration)?
        CallDetectMediaAttachment(Message message)
    {
        var result = MediaProcessingHandler.DetectMediaAttachment(message);
        if (result == null)
            return null;

        return (result.MediaType, result.FileId, result.FileSize, result.FileName, result.MimeType, result.Duration);
    }

    /// <summary>
    /// Call FileScanningHandler.DetectScannableFile (public static method)
    /// </summary>
    private static bool CallHasFileAttachment(
        Message message,
        out string? fileId,
        out long fileSize,
        out string? fileName,
        out string? contentType)
    {
        var result = FileScanningHandler.DetectScannableFile(message);
        if (result == null)
        {
            fileId = null;
            fileSize = 0;
            fileName = null;
            contentType = null;
            return false;
        }

        fileId = result.FileId;
        fileSize = result.FileSize;
        fileName = result.FileName;
        contentType = result.ContentType;
        return true;
    }

    /// <summary>
    /// Call TranslationHandler.CalculateLatinScriptRatio (public static method)
    /// </summary>
    private static double CallCalculateLatinScriptRatio(string text)
    {
        return TranslationHandler.CalculateLatinScriptRatio(text);
    }

    #endregion
}
