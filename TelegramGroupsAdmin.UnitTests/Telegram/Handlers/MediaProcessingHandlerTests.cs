using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramBotDocument = Telegram.Bot.Types.Document;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Handlers;

/// <summary>
/// Test suite for MediaProcessingHandler static methods.
/// Tests media detection logic for all supported media types.
///
/// Test Coverage:
/// - 14 static media detection tests (migrated from MessageProcessingServiceTests)
/// - Validates: Animation, Video, Audio, Voice, Sticker, VideoNote, Document detection
/// - Tests priority order (Animation before Document for GIFs)
/// - Tests null handling and edge cases
///
/// Note: Instance method tests (ProcessMediaAsync) removed - better covered by integration tests.
///
/// Created: 2025-10-31 (REFACTOR-1 Phase 2)
/// </summary>
[TestFixture]
public class MediaProcessingHandlerTests
{
    #region Static Media Detection Tests (Migrated from MessageProcessingServiceTests)

    /// <summary>
    /// Validates Animation (GIF) metadata extraction including FileId, FileSize, FileName, MimeType, Duration.
    /// Ensures Animation type takes priority in detection order.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_Animation_ReturnsCorrectMetadata()
    {
        // Migrated from MessageProcessingServiceTests
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

        // Act: Detect media using static method
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Returns Animation metadata
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo(MediaType.Animation));
        Assert.That(result.FileId, Is.EqualTo("test_anim_id"));
        Assert.That(result.FileSize, Is.EqualTo(1024000L));
        Assert.That(result.FileName, Is.EqualTo("test.gif"));
        Assert.That(result.MimeType, Is.EqualTo("video/mp4"));
        Assert.That(result.Duration, Is.EqualTo(5));
    }

    /// <summary>
    /// Validates Video metadata extraction for standard video files.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_Video_ReturnsCorrectMetadata()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo(MediaType.Video));
        Assert.That(result.FileId, Is.EqualTo("test_video_id"));
        Assert.That(result.FileSize, Is.EqualTo(5242880L));
        Assert.That(result.FileName, Is.EqualTo("vacation.mp4"));
        Assert.That(result.MimeType, Is.EqualTo("video/mp4"));
        Assert.That(result.Duration, Is.EqualTo(120));
    }

    /// <summary>
    /// Validates Audio (music file) metadata extraction including Title field.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_Audio_ReturnsCorrectMetadata()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo(MediaType.Audio));
        Assert.That(result.FileId, Is.EqualTo("test_audio_id"));
        Assert.That(result.FileSize, Is.EqualTo(3145728L));
        Assert.That(result.FileName, Is.EqualTo("song.mp3"));
        Assert.That(result.MimeType, Is.EqualTo("audio/mpeg"));
        Assert.That(result.Duration, Is.EqualTo(180));
    }

    /// <summary>
    /// Validates fallback filename generation when Audio has Title but no FileName.
    /// Tests FileName ?? Title fallback logic.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_AudioWithoutFilename_UsesTitle()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Should use Title when FileName is null
        Assert.That(result, Is.Not.Null);
        Assert.That(result.FileName, Is.EqualTo("Song Title"));
    }

    /// <summary>
    /// Validates filename generation using FileUniqueId when both FileName and Title are missing.
    /// Tests complete fallback chain: FileName ?? Title ?? generated name.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_AudioWithoutFilenameOrTitle_UsesGeneratedName()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Should generate filename using FileUniqueId
        Assert.That(result, Is.Not.Null);
        Assert.That(result.FileName, Is.EqualTo("audio_unique456.mp3"));
    }

    /// <summary>
    /// Validates Voice message (OGG voice note) detection with auto-generated filename.
    /// Voice messages never have user-provided filenames, always generated.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_Voice_ReturnsGeneratedFilename()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Voice always generates filename
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo(MediaType.Voice));
        Assert.That(result.FileId, Is.EqualTo("test_voice_id"));
        Assert.That(result.FileSize, Is.EqualTo(51200L));
        Assert.That(result.FileName, Is.EqualTo("voice_voice789.ogg"));
        Assert.That(result.MimeType, Is.EqualTo("audio/ogg"));
        Assert.That(result.Duration, Is.EqualTo(10));
    }

    /// <summary>
    /// Validates Sticker (WebP format) detection with auto-generated filename.
    /// Stickers have no duration and always use WebP MIME type.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_Sticker_ReturnsCorrectMetadata()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Stickers have no duration, always WebP
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo(MediaType.Sticker));
        Assert.That(result.FileId, Is.EqualTo("test_sticker_id"));
        Assert.That(result.FileSize, Is.EqualTo(20480L));
        Assert.That(result.FileName, Is.EqualTo("sticker_sticker999.webp"));
        Assert.That(result.MimeType, Is.EqualTo("image/webp"));
        Assert.That(result.Duration, Is.Null);
    }

    /// <summary>
    /// Validates VideoNote (circular video message) detection with auto-generated filename.
    /// VideoNotes are always MP4 format with duration.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_VideoNote_ReturnsCorrectMetadata()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo(MediaType.VideoNote));
        Assert.That(result.FileId, Is.EqualTo("test_videonote_id"));
        Assert.That(result.FileSize, Is.EqualTo(1048576L));
        Assert.That(result.FileName, Is.EqualTo("videonote_videonote888.mp4"));
        Assert.That(result.MimeType, Is.EqualTo("video/mp4"));
        Assert.That(result.Duration, Is.EqualTo(30));
    }

    /// <summary>
    /// Validates Document detection returns metadata only (no duration).
    /// Documents are NOT downloaded for display, only file scanner downloads temporarily.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_Document_ReturnsMetadataOnly()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Document has no duration
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo(MediaType.Document));
        Assert.That(result.FileId, Is.EqualTo("test_doc_id"));
        Assert.That(result.FileSize, Is.EqualTo(2097152L));
        Assert.That(result.FileName, Is.EqualTo("report.pdf"));
        Assert.That(result.MimeType, Is.EqualTo("application/pdf"));
        Assert.That(result.Duration, Is.Null);
    }

    /// <summary>
    /// Validates fallback filename for Documents without FileName property.
    /// Uses "document" literal as fallback.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_DocumentWithoutFilename_UsesFallback()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Uses "document" fallback
        Assert.That(result, Is.Not.Null);
        Assert.That(result.FileName, Is.EqualTo("document"));
    }

    /// <summary>
    /// Validates null return when message has no media attachments (text-only message).
    /// </summary>
    [Test]
    public void DetectMediaAttachment_NoMedia_ReturnsNull()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Message with no media (text only)
        var message = new Message
        {
            Text = "Hello world"
        };

        // Act
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: No media found
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// CRITICAL TEST: Validates priority order when Telegram sends GIFs with BOTH Animation and Document properties.
    /// Animation must take precedence to avoid incorrect classification as scannable document.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_PriorityOrder_AnimationBeforeDocument()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Should return Animation (priority order)
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo(MediaType.Animation));
        Assert.That(result.FileId, Is.EqualTo("anim_id"));
    }

    /// <summary>
    /// Validates null FileSize handling (Telegram API returns nullable long).
    /// Ensures we default to 0 instead of crashing.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_NullFileSize_DefaultsToZero()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Animation with null FileSize
        var message = new Message
        {
            Animation = new Animation
            {
                FileId = "test_id",
                FileSize = null, // Telegram API can return null
                FileName = "test.gif",
                MimeType = "video/mp4",
                Duration = 3
            }
        };

        // Act
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Defaults to 0
        Assert.That(result, Is.Not.Null);
        Assert.That(result.FileSize, Is.EqualTo(0L));
    }

    /// <summary>
    /// Validates null MimeType handling with appropriate fallback.
    /// Ensures we provide sensible defaults instead of null.
    /// </summary>
    [Test]
    public void DetectMediaAttachment_NullMimeType_UsesDefault()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Animation with null MimeType
        var message = new Message
        {
            Animation = new Animation
            {
                FileId = "test_id",
                FileSize = 1000000,
                FileName = "test.gif",
                MimeType = null, // Telegram API can return null
                Duration = 3
            }
        };

        // Act
        var result = MediaProcessingHandler.DetectMediaAttachment(message);

        // Assert: Uses default "video/mp4"
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MimeType, Is.EqualTo("video/mp4"));
    }

    #endregion
}
