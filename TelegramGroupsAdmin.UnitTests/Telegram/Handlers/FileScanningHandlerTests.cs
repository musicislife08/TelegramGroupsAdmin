using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramBotDocument = Telegram.Bot.Types.Document;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Handlers;

/// <summary>
/// Test suite for FileScanningHandler static methods.
/// Tests scannable file detection logic for document attachments.
///
/// Test Coverage:
/// - 9 static file detection tests (migrated from MessageProcessingServiceTests)
/// - Validates pure Document detection (PDF, DOCX, EXE, etc.)
/// - Tests critical media exclusion (GIFs, videos with Document property)
/// - Tests null handling and default values
///
/// Note: Instance method tests (ProcessFileScanningAsync) removed - better covered by integration tests.
///
/// Created: 2025-10-31 (REFACTOR-1 Phase 2)
/// </summary>
[TestFixture]
public class FileScanningHandlerTests
{
    #region Static File Detection Tests (Migrated from MessageProcessingServiceTests)

    /// <summary>
    /// Validates pure Document detection (PDF) without media properties.
    /// This is the main use case for file scanning: attachments like PDF, DOCX, EXE, ZIP, APK.
    /// </summary>
    [Test]
    public void DetectScannableFile_PureDocument_ReturnsFileMetadata()
    {
        // Migrated from MessageProcessingServiceTests
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
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: Returns document metadata for scanning
        Assert.That(result, Is.Not.Null);
        Assert.That(result.FileId, Is.EqualTo("doc_id"));
        Assert.That(result.FileSize, Is.EqualTo(2048000L));
        Assert.That(result.FileName, Is.EqualTo("report.pdf"));
        Assert.That(result.ContentType, Is.EqualTo("application/pdf"));
    }

    /// <summary>
    /// CRITICAL TEST: Validates exclusion of GIFs from file scanning.
    /// Telegram sends GIFs with BOTH Animation and Document properties.
    /// Must check Animation first to avoid false positive scans.
    /// </summary>
    [Test]
    public void DetectScannableFile_AnimationWithDocument_ReturnsNull()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Telegram GIF with BOTH Animation and Document properties
        var message = new Message
        {
            Animation = new Animation { FileId = "gif_id", FileSize = 500000 },
            Document = new TelegramBotDocument { FileId = "doc_id", FileName = "test.gif" }
        };

        // Act
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: Returns null (GIF is media, not scannable document)
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Validates exclusion of Videos from file scanning.
    /// Videos can have Document property but should not be scanned for malware.
    /// </summary>
    [Test]
    public void DetectScannableFile_VideoWithDocument_ReturnsNull()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Video with Document property
        var message = new Message
        {
            Video = new Video { FileId = "video_id", FileSize = 1000000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: Exclude video from document scanning
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Validates exclusion of Audio files from file scanning.
    /// Audio files cannot contain executable malware, skip scanning.
    /// </summary>
    [Test]
    public void DetectScannableFile_AudioWithDocument_ReturnsNull()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Audio with Document property
        var message = new Message
        {
            Audio = new Audio { FileId = "audio_id", FileSize = 2000000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: Exclude audio from document scanning
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Validates exclusion of Voice messages from file scanning.
    /// Voice notes (OGG format) cannot contain executable malware.
    /// </summary>
    [Test]
    public void DetectScannableFile_VoiceWithDocument_ReturnsNull()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Voice with Document property
        var message = new Message
        {
            Voice = new Voice { FileId = "voice_id", FileSize = 50000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: Exclude voice from document scanning
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Validates exclusion of Stickers from file scanning.
    /// Stickers (WebP format) cannot contain executable malware.
    /// </summary>
    [Test]
    public void DetectScannableFile_StickerWithDocument_ReturnsNull()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Sticker with Document property
        var message = new Message
        {
            Sticker = new Sticker { FileId = "sticker_id", FileSize = 20000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: Exclude sticker from document scanning
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Validates exclusion of VideoNotes from file scanning.
    /// Circular video messages cannot contain executable malware.
    /// </summary>
    [Test]
    public void DetectScannableFile_VideoNoteWithDocument_ReturnsNull()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: VideoNote with Document property
        var message = new Message
        {
            VideoNote = new VideoNote { FileId = "videonote_id", FileSize = 1000000 },
            Document = new TelegramBotDocument { FileId = "doc_id" }
        };

        // Act
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: Exclude video note from document scanning
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Validates null return when message has no Document property.
    /// Text-only messages or photo messages don't trigger file scanning.
    /// </summary>
    [Test]
    public void DetectScannableFile_NoDocument_ReturnsNull()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Message with no attachments
        var message = new Message
        {
            Text = "Hello world"
        };

        // Act
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: No document found
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Validates handling of null FileSize (Telegram API returns nullable long).
    /// Ensures we default to 0 instead of crashing.
    /// </summary>
    [Test]
    public void DetectScannableFile_DocumentWithNullFileSize_DefaultsToZero()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Document with null FileSize
        var message = new Message
        {
            Document = new TelegramBotDocument
            {
                FileId = "doc_id",
                FileSize = null, // Telegram API can return null
                FileName = "unknown_size.pdf",
                MimeType = "application/pdf"
            }
        };

        // Act
        var result = FileScanningHandler.DetectScannableFile(message);

        // Assert: Defaults FileSize to 0
        Assert.That(result, Is.Not.Null);
        Assert.That(result.FileSize, Is.EqualTo(0L));
    }

    #endregion

}
