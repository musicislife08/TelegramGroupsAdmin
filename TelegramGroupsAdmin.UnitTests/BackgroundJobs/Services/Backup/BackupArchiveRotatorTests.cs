using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using TelegramGroupsAdmin.BackgroundJobs.Constants;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>
/// Tests for <see cref="BackupArchiveRotator"/> against real .tar.gz files built in a temp directory
/// with the real <see cref="BackupEncryptionService"/>. A "wrapped" file is built exactly the way the
/// pre-fix rotation job damaged backups: the whole archive encrypted with the legacy single-shot format.
/// </summary>
[TestFixture]
public class BackupArchiveRotatorTests
{
    private const string OldPassphrase = "old-passphrase-12345";
    private const string NewPassphrase = "new-passphrase-67890";
    private const string DatabaseJson = """{"t":[1]}""";
    private static readonly byte[] MetadataJson = """{"version":"3.1","table_count":1}"""u8.ToArray();
    private static readonly byte[] GifBytes = "GIF89a"u8.ToArray();
    private const string GifEntryName = "media/ban-gifs/a.gif";

    private BackupEncryptionService _encryption = null!;
    private BackupArchiveRotator _rotator = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _encryption = new BackupEncryptionService(NullLogger<BackupEncryptionService>.Instance);
        _rotator = new BackupArchiveRotator(_encryption, new RecyclableMemoryStreamManager(), NullLogger<BackupArchiveRotator>.Instance);
        _directory = Directory.CreateTempSubdirectory("tga_rotator_").FullName;
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    #region Inspect

    [Test]
    public async Task InspectAsync_EncryptedArchive_ReturnsEncrypted() =>
        Assert.That(await _rotator.InspectAsync(EncryptedArchive(OldPassphrase)), Is.EqualTo(BackupFileState.Encrypted));

    [Test]
    public async Task InspectAsync_PlainArchive_ReturnsPlain() =>
        Assert.That(await _rotator.InspectAsync(PlainArchive()), Is.EqualTo(BackupFileState.Plain));

    [Test]
    public async Task InspectAsync_WrappedArchive_ReturnsWrapped() =>
        Assert.That(await _rotator.InspectAsync(Wrap(EncryptedArchive(OldPassphrase), NewPassphrase)), Is.EqualTo(BackupFileState.Wrapped));

    [Test]
    public async Task InspectAsync_RandomBytes_ReturnsUnreadable()
    {
        var path = Path.Combine(_directory, "random.tar.gz");
        await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(256));

        Assert.That(await _rotator.InspectAsync(path), Is.EqualTo(BackupFileState.Unreadable));
    }

    [Test]
    public async Task InspectAsync_ArchiveWithoutDatabaseEntry_ReturnsUnreadable()
    {
        var path = BuildArchive("nodb.tar.gz", (BackupConstants.MetadataEntryName, MetadataJson));

        Assert.That(await _rotator.InspectAsync(path), Is.EqualTo(BackupFileState.Unreadable));
    }

    #endregion

    #region Reencrypt

    [Test]
    public async Task ReencryptAsync_EncryptedEntry_OpensOnlyWithNewPassphrase()
    {
        var path = EncryptedArchive(OldPassphrase);

        var outcome = await _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase);

        var entries = ReadEntries(path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(ReencryptOutcome.Reencrypted));
            Assert.That(ReadFirstBytes(path, 2), Is.EqualTo(new byte[] { 0x1f, 0x8b }), "the file must stay a gzip archive");
            Assert.That(Decrypt(entries[BackupConstants.EncryptedDatabaseEntryName], NewPassphrase), Is.EqualTo(DatabaseJson));
            Assert.That(() => Decrypt(entries[BackupConstants.EncryptedDatabaseEntryName], OldPassphrase), Throws.TypeOf<CryptographicException>());
            Assert.That(entries[BackupConstants.MetadataEntryName], Is.EqualTo(MetadataJson), "metadata is copied unchanged");
            Assert.That(entries[GifEntryName], Is.EqualTo(GifBytes), "media is copied unchanged");
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty, "no temp file is left behind");
        }
    }

    [Test]
    public async Task ReencryptAsync_AlreadyOnNewPassphrase_ReturnsAlreadyCurrent_AndLeavesFileUntouched()
    {
        var path = EncryptedArchive(NewPassphrase);
        var before = await File.ReadAllBytesAsync(path);

        var outcome = await _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(ReencryptOutcome.AlreadyCurrent));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(before));
        }
    }

    [Test]
    public async Task ReencryptAsync_NeitherPassphraseOpens_Throws_AndLeavesFileUntouched()
    {
        var path = EncryptedArchive("some-other-passphrase-000");
        var before = await File.ReadAllBytesAsync(path);

        Assert.That(async () => await _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase),
            Throws.InstanceOf<CryptographicException>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
    }

    [Test]
    public async Task ReencryptAsync_PlainEntry_IsEncryptedAndRenamed()
    {
        var path = PlainArchive();

        var outcome = await _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase);

        var entries = ReadEntries(path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(ReencryptOutcome.Reencrypted));
            Assert.That(entries.ContainsKey(BackupConstants.PlainDatabaseEntryName), Is.False);
            Assert.That(Decrypt(entries[BackupConstants.EncryptedDatabaseEntryName], NewPassphrase), Is.EqualTo(DatabaseJson));
        }
    }

    [Test]
    public void ReencryptAsync_WrappedFile_ThrowsInvalidOperation()
    {
        var path = Wrap(EncryptedArchive(OldPassphrase), OldPassphrase);

        Assert.That(async () => await _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase),
            Throws.InvalidOperationException);
    }

    #endregion

    #region Repair wrapped

    [Test]
    public async Task TryRepairWrappedAsync_RightOriginal_RestoresANormalArchiveOnTheStoredPassphrase()
    {
        // The setup passphrase locks the inside; a past rotation wrapped the whole file with the now-stored one.
        var path = Wrap(EncryptedArchive(OldPassphrase), NewPassphrase);

        var repaired = await _rotator.TryRepairWrappedAsync(path, storedPassphrase: NewPassphrase, originalPassphrase: OldPassphrase);

        var entries = ReadEntries(path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(repaired, Is.True);
            Assert.That(ReadFirstBytes(path, 2), Is.EqualTo(new byte[] { 0x1f, 0x8b }));
            Assert.That(Decrypt(entries[BackupConstants.EncryptedDatabaseEntryName], NewPassphrase), Is.EqualTo(DatabaseJson));
            Assert.That(entries[GifEntryName], Is.EqualTo(GifBytes));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
    }

    [Test]
    public async Task TryRepairWrappedAsync_WrongOriginal_ReturnsFalse_AndLeavesFileUntouched()
    {
        var path = Wrap(EncryptedArchive(OldPassphrase), NewPassphrase);
        var before = await File.ReadAllBytesAsync(path);

        var repaired = await _rotator.TryRepairWrappedAsync(path, NewPassphrase, "not-the-original-passphrase");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repaired, Is.False);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
    }

    [Test]
    public void TryRepairWrappedAsync_WrongStoredPassphrase_Throws()
    {
        var path = Wrap(EncryptedArchive(OldPassphrase), NewPassphrase);

        Assert.That(async () => await _rotator.TryRepairWrappedAsync(path, "not-the-stored-passphrase", OldPassphrase),
            Throws.InstanceOf<CryptographicException>());
    }

    [Test]
    public async Task TryRepairWrappedAsync_PlainInner_IsRepairedWithoutTheOriginalPassphrase()
    {
        // The old job also wrapped unencrypted backups made before encryption was turned on.
        var path = Wrap(PlainArchive(), NewPassphrase);

        var repaired = await _rotator.TryRepairWrappedAsync(path, NewPassphrase, "any-passphrase-at-all");

        var entries = ReadEntries(path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(repaired, Is.True);
            Assert.That(entries.ContainsKey(BackupConstants.PlainDatabaseEntryName), Is.False);
            Assert.That(Decrypt(entries[BackupConstants.EncryptedDatabaseEntryName], NewPassphrase), Is.EqualTo(DatabaseJson));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
    }

    [Test]
    public async Task TryRepairWrappedAsync_WrongStoredPassphrase_LeavesFileAndNoTemp()
    {
        var path = Wrap(EncryptedArchive(OldPassphrase), NewPassphrase);
        var before = await File.ReadAllBytesAsync(path);

        Assert.That(async () => await _rotator.TryRepairWrappedAsync(path, "not-the-stored-passphrase", OldPassphrase),
            Throws.InstanceOf<CryptographicException>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
    }

    #endregion

    #region Rewrap

    [Test]
    public async Task RewrapAsync_MovesTheOuterLayerToTheNewPassphrase_AndKeepsItRepairable()
    {
        // Setup passphrase inside, outer layer on the passphrase stored before this rotation.
        const string setupPassphrase = "setup-passphrase-00000";
        var path = Wrap(EncryptedArchive(setupPassphrase), OldPassphrase);

        await _rotator.RewrapAsync(path, OldPassphrase, NewPassphrase);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _rotator.InspectAsync(path), Is.EqualTo(BackupFileState.Wrapped), "the file stays damaged until repaired");
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
        Assert.That(await _rotator.TryRepairWrappedAsync(path, storedPassphrase: NewPassphrase, originalPassphrase: setupPassphrase), Is.True,
            "after the rotation stores the new passphrase, the file can still be repaired");
    }

    [Test]
    public async Task RewrapAsync_OuterNotOnOldPassphrase_Throws_AndLeavesFileUntouched()
    {
        var path = Wrap(EncryptedArchive(OldPassphrase), "some-other-passphrase-000");
        var before = await File.ReadAllBytesAsync(path);

        Assert.That(async () => await _rotator.RewrapAsync(path, OldPassphrase, NewPassphrase),
            Throws.InstanceOf<CryptographicException>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
    }

    #endregion

    #region Verification

    [Test]
    public async Task ReencryptAsync_RewriteFailsVerification_LeavesOriginalAndNoTemp()
    {
        // No metadata entry: the rewritten copy fails verification and must never replace the original.
        using var plain = new MemoryStream(Encoding.UTF8.GetBytes(DatabaseJson));
        using var cipher = new MemoryStream();
        _encryption.EncryptBackup(plain, cipher, OldPassphrase);
        var path = BuildArchive("nometa.tar.gz", (BackupConstants.EncryptedDatabaseEntryName, cipher.ToArray()));
        var before = await File.ReadAllBytesAsync(path);

        Assert.That(async () => await _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase),
            Throws.InvalidOperationException.With.Message.Contains("Verification"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
    }

    #endregion

    #region Helpers

    private string EncryptedArchive(string passphrase)
    {
        using var plain = new MemoryStream(Encoding.UTF8.GetBytes(DatabaseJson));
        using var cipher = new MemoryStream();
        _encryption.EncryptBackup(plain, cipher, passphrase);
        return BuildArchive($"backup_{Guid.NewGuid():N}.tar.gz",
            (BackupConstants.MetadataEntryName, MetadataJson),
            (BackupConstants.EncryptedDatabaseEntryName, cipher.ToArray()),
            (GifEntryName, GifBytes));
    }

    private string PlainArchive() =>
        BuildArchive($"backup_{Guid.NewGuid():N}.tar.gz",
            (BackupConstants.MetadataEntryName, MetadataJson),
            (BackupConstants.PlainDatabaseEntryName, Encoding.UTF8.GetBytes(DatabaseJson)),
            (GifEntryName, GifBytes));

    /// <summary>Reproduces the pre-fix rotation job's output: the whole archive, legacy-encrypted.</summary>
    private string Wrap(string path, string passphrase)
    {
        File.WriteAllBytes(path, _encryption.EncryptBackup(File.ReadAllBytes(path), passphrase));
        return path;
    }

    private string BuildArchive(string fileName, params (string Name, byte[] Data)[] entries)
    {
        var path = Path.Combine(_directory, fileName);
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) });
        }
        return path;
    }

    private static Dictionary<string, byte[]> ReadEntries(string path)
    {
        var result = new Dictionary<string, byte[]>();
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            using var ms = new MemoryStream();
            entry.DataStream?.CopyTo(ms);
            result[entry.Name] = ms.ToArray();
        }
        return result;
    }

    private string Decrypt(byte[] cipher, string passphrase)
    {
        using var input = new MemoryStream(cipher);
        using var output = new MemoryStream();
        _encryption.DecryptBackup(input, output, passphrase);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static byte[] ReadFirstBytes(string path, int count)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    #endregion
}
