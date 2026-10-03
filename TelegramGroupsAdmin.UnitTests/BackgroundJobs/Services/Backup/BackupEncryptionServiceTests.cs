using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.BackgroundJobs.Constants;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>
/// Unit tests for BackupEncryptionService.
/// Covers round trips in both file formats and how short or truncated files are rejected:
/// always as a named format error (InvalidOperationException), never as a raw end-of-stream error.
/// </summary>
[TestFixture]
public class BackupEncryptionServiceTests
{
    private const string Passphrase = "unit-test-passphrase";
    private const string Plaintext = "hello";

    // Chunked layout for the 5-byte plaintext above:
    // header (52) + chunk length (4) + ciphertext (5) + tag (16) + end-of-data marker (4)
    private const int ChunkLengthPrefix = sizeof(int);
    private const int EndMarker = sizeof(int);
    private const int SingleChunkFileSize =
        EncryptionConstants.ChunkedHeaderSize + ChunkLengthPrefix + 5 + EncryptionConstants.TagSizeBytes + EndMarker;

    private BackupEncryptionService _service = null!;

    [SetUp]
    public void SetUp() => _service = new BackupEncryptionService(NullLogger<BackupEncryptionService>.Instance);

    #region Round Trips

    [Test]
    public void ChunkedFormat_RoundTrips()
    {
        var encrypted = EncryptChunked(Plaintext);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encrypted.AsSpan(0, EncryptionConstants.ChunkedMagicHeader.Length).ToArray(),
                Is.EqualTo(EncryptionConstants.ChunkedMagicHeader));
            Assert.That(encrypted, Has.Length.EqualTo(SingleChunkFileSize));
            Assert.That(DecryptStream(encrypted), Is.EqualTo(Plaintext));
        }
    }

    [Test]
    public void ChunkedFormat_EmptyPlaintext_IsExactlyHeaderPlusEndMarker_AndRoundTrips()
    {
        // The smallest file the encryptor can produce. It pins ChunkedHeaderSize to what is actually
        // written, and proves the minimum-size check does not reject a valid file.
        var encrypted = EncryptChunked(string.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encrypted, Has.Length.EqualTo(EncryptionConstants.ChunkedHeaderSize + EndMarker));
            Assert.That(DecryptStream(encrypted), Is.Empty);
            Assert.That(_service.DecryptBackup(encrypted, Passphrase), Is.Empty);
        }
    }

    [Test]
    public void ChunkedFormat_DecryptsThroughTheByteArrayOverload()
    {
        var encrypted = EncryptChunked(Plaintext);

        var decrypted = _service.DecryptBackup(encrypted, Passphrase);

        Assert.That(Encoding.UTF8.GetString(decrypted), Is.EqualTo(Plaintext));
    }

    [Test]
    public void LegacyFormat_RoundTrips_ThroughBothOverloads()
    {
        var encrypted = _service.EncryptBackup(Encoding.UTF8.GetBytes(Plaintext), Passphrase);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encrypted.AsSpan(0, EncryptionConstants.LegacyMagicHeader.Length).ToArray(),
                Is.EqualTo(EncryptionConstants.LegacyMagicHeader));
            Assert.That(Encoding.UTF8.GetString(_service.DecryptBackup(encrypted, Passphrase)), Is.EqualTo(Plaintext));
            Assert.That(DecryptStream(encrypted), Is.EqualTo(Plaintext));
        }
    }

    [Test]
    public void ChunkedFormat_WrongPassphrase_ThrowsCryptographicException()
    {
        var encrypted = EncryptChunked(Plaintext);

        Assert.That(() => DecryptStream(encrypted, "a-different-passphrase"), Throws.TypeOf<CryptographicException>());
    }

    #endregion

    #region Unrecognized Input

    [TestCase(0)]
    [TestCase(3)]
    [TestCase(6)]
    public void DecryptStream_ShorterThanAMagicHeader_ThrowsUnrecognizedHeader(int length)
    {
        var input = EncryptionConstants.ChunkedMagicHeader.AsSpan(0, length).ToArray();

        Assert.That(() => DecryptStream(input),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("recognized encrypted backup header"));
    }

    [Test]
    public void DecryptStream_UnknownMagicHeader_ThrowsUnrecognizedHeader()
    {
        var input = Encoding.ASCII.GetBytes("NOTABACKUP-AT-ALL");

        Assert.That(() => DecryptStream(input),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("recognized encrypted backup header"));
    }

    #endregion

    #region Chunked Format - Truncated Header

    // 7 = magic only, 8 = magic + version, 20 = part of the salt, 51 = one byte short of the header
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(20)]
    [TestCase(51)]
    public void DecryptStream_ChunkedFileCutInsideTheHeader_ThrowsTooSmall(int bytesKept)
    {
        var truncated = EncryptChunked(Plaintext).AsSpan(0, bytesKept).ToArray();

        Assert.That(() => DecryptStream(truncated),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("too small"));
    }

    [TestCase(7)]
    [TestCase(30)]
    [TestCase(51)]
    public void DecryptBytes_ChunkedFileCutInsideTheHeader_ThrowsTooSmall(int bytesKept)
    {
        var truncated = EncryptChunked(string.Empty).AsSpan(0, bytesKept).ToArray();

        Assert.That(() => _service.DecryptBackup(truncated, Passphrase),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains($"too small (minimum {EncryptionConstants.ChunkedHeaderSize + EndMarker} bytes)"));
    }

    [Test]
    public void DecryptBytes_CompleteHeaderButPartialEndMarker_ThrowsTruncated()
    {
        // 55 bytes: the whole header plus three of the four end-marker bytes
        var truncated = EncryptChunked(string.Empty).AsSpan(0, EncryptionConstants.ChunkedHeaderSize + EndMarker - 1).ToArray();

        Assert.That(() => _service.DecryptBackup(truncated, Passphrase),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("truncated"));
    }

    [Test]
    public void DecryptBytes_UnsupportedVersion_IsReportedEvenWhenTheHeaderIsAlsoCutShort()
    {
        // Both overloads must agree: the byte[] overload hands off to the stream overload
        byte[] input = [.. EncryptionConstants.ChunkedMagicHeader, 0x02];

        Assert.That(() => _service.DecryptBackup(input, Passphrase),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Unsupported chunked format version: 2"));
    }

    [Test]
    public void DecryptStream_UnsupportedVersion_IsReportedEvenWhenTheHeaderIsAlsoCutShort()
    {
        // A future layout this reader does not know must read as "unsupported", not "too small"
        byte[] input = [.. EncryptionConstants.ChunkedMagicHeader, 0x02];

        Assert.That(() => DecryptStream(input),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Unsupported chunked format version: 2"));
    }

    [Test]
    public void DecryptStream_UnsupportedVersion_WithACompleteFile_IsReported()
    {
        var encrypted = EncryptChunked(Plaintext);
        encrypted[EncryptionConstants.ChunkedMagicHeader.Length] = 0x7F;

        Assert.That(() => DecryptStream(encrypted),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Unsupported chunked format version: 127"));
    }

    #endregion

    #region Chunked Format - Truncated Body

    // Offsets into the 81-byte single-chunk file. The chunk index in the error is the chunk
    // being read when the data ran out.
    [TestCase(52, 0, Description = "header only, no chunk length")]
    [TestCase(54, 0, Description = "partial chunk length")]
    [TestCase(58, 0, Description = "partial ciphertext")]
    [TestCase(70, 0, Description = "partial authentication tag")]
    [TestCase(77, 1, Description = "chunk complete, end-of-data marker missing")]
    [TestCase(80, 1, Description = "partial end-of-data marker")]
    public void DecryptStream_ChunkedFileCutInsideTheBody_ThrowsTruncated(int bytesKept, int expectedChunk)
    {
        var truncated = EncryptChunked(Plaintext).AsSpan(0, bytesKept).ToArray();

        Assert.That(() => DecryptStream(truncated),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("truncated")
                .And.Message.Contains($"while reading chunk {expectedChunk}"));
    }

    [Test]
    public void DecryptBytes_ChunkedFileCutInsideTheBody_ThrowsTruncated()
    {
        var truncated = EncryptChunked(Plaintext).AsSpan(0, SingleChunkFileSize - EndMarker).ToArray();

        Assert.That(() => _service.DecryptBackup(truncated, Passphrase),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("truncated"));
    }

    #endregion

    #region Legacy Format - Too Small

    [Test]
    public void LegacyFileShorterThanItsFixedParts_ThrowsTooSmall_ThroughBothOverloads()
    {
        // Magic header plus ten bytes: far short of header + salt + nonce + tag
        byte[] input = [.. EncryptionConstants.LegacyMagicHeader, .. new byte[10]];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => _service.DecryptBackup(input, Passphrase),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("too small"));
            Assert.That(() => DecryptStream(input),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("too small"));
        }
    }

    #endregion

    #region Helpers

    private byte[] EncryptChunked(string plaintext)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(plaintext));
        using var output = new MemoryStream();
        _service.EncryptBackup(input, output, Passphrase);
        return output.ToArray();
    }

    private string DecryptStream(byte[] encrypted, string passphrase = Passphrase)
    {
        using var input = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        _service.DecryptBackup(input, output, passphrase);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    #endregion
}
