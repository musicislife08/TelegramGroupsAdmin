using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using TelegramGroupsAdmin.BackgroundJobs.Constants;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <inheritdoc />
public class BackupArchiveRotator(
    IBackupEncryptionService encryptionService,
    RecyclableMemoryStreamManager streamManager,
    ILogger<BackupArchiveRotator> logger) : IBackupArchiveRotator
{
    private static readonly byte[] GzipMagic = [0x1f, 0x8b];

    public async Task<BackupFileState> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var header = new byte[EncryptionConstants.LegacyMagicHeader.Length];
            int read;
            await using (var probe = File.OpenRead(path))
            {
                read = await probe.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
            }

            if (read == header.Length
                && (header.AsSpan().SequenceEqual(EncryptionConstants.LegacyMagicHeader)
                    || header.AsSpan().SequenceEqual(EncryptionConstants.ChunkedMagicHeader)))
            {
                return BackupFileState.Wrapped;
            }

            if (read < GzipMagic.Length || !header.AsSpan(0, GzipMagic.Length).SequenceEqual(GzipMagic))
                return BackupFileState.Unreadable;

            await using var file = File.OpenRead(path);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (await tar.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
            {
                if (entry.Name == BackupConstants.EncryptedDatabaseEntryName)
                    return BackupFileState.Encrypted;
                if (entry.Name == BackupConstants.PlainDatabaseEntryName)
                    return BackupFileState.Plain;
            }

            return BackupFileState.Unreadable;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Backup {Path} could not be read as an archive", path);
            return BackupFileState.Unreadable;
        }
    }

    public async Task<ReencryptOutcome> ReencryptAsync(string path, string oldPassphrase, string newPassphrase, CancellationToken cancellationToken = default)
    {
        var state = await InspectAsync(path, cancellationToken);
        if (state is not (BackupFileState.Encrypted or BackupFileState.Plain))
            throw new InvalidOperationException($"Backup {Path.GetFileName(path)} is {state} and cannot be re-encrypted");

        using var plaintext = streamManager.GetStream("BackupArchiveRotator.Plaintext");
        using (var entry = await ReadDatabaseEntryAsync(path, cancellationToken))
        {
            if (state == BackupFileState.Plain)
            {
                await entry.CopyToAsync(plaintext, cancellationToken);
            }
            else
            {
                try
                {
                    encryptionService.DecryptBackup(entry, plaintext, oldPassphrase);
                }
                catch (CryptographicException) when (OpensWith(entry, newPassphrase))
                {
                    logger.LogInformation("Backup {FileName} is already on the new passphrase", Path.GetFileName(path));
                    return ReencryptOutcome.AlreadyCurrent;
                }
            }
        } // the ciphertext copy is released before the rewrite allocates its own

        await RewriteWithNewDatabaseEntryAsync(path, path, plaintext, newPassphrase, cancellationToken);
        logger.LogInformation("Re-encrypted the database entry of backup {FileName}", Path.GetFileName(path));
        return ReencryptOutcome.Reencrypted;
    }

    public async Task<bool> TryRepairWrappedAsync(string path, string storedPassphrase, string originalPassphrase, CancellationToken cancellationToken = default)
    {
        var innerPath = TempPathFor(path, "inner.tmp");
        try
        {
            await DecryptFileAsync(path, innerPath, storedPassphrase);

            var innerState = await InspectAsync(innerPath, cancellationToken);
            if (innerState is not (BackupFileState.Encrypted or BackupFileState.Plain))
                throw new InvalidOperationException($"Backup {Path.GetFileName(path)} does not contain a readable backup archive");

            using var plaintext = streamManager.GetStream("BackupArchiveRotator.RepairPlaintext");
            using (var entry = await ReadDatabaseEntryAsync(innerPath, cancellationToken))
            {
                if (innerState == BackupFileState.Plain)
                {
                    // An unencrypted backup the old job wrapped: no inner passphrase is needed
                    await entry.CopyToAsync(plaintext, cancellationToken);
                }
                else
                {
                    try
                    {
                        encryptionService.DecryptBackup(entry, plaintext, originalPassphrase);
                    }
                    catch (CryptographicException)
                    {
                        logger.LogInformation("Damaged backup {FileName} does not open with the supplied passphrase", Path.GetFileName(path));
                        return false;
                    }
                }
            }

            await RewriteWithNewDatabaseEntryAsync(innerPath, path, plaintext, storedPassphrase, cancellationToken);
            logger.LogInformation("Repaired damaged backup {FileName}", Path.GetFileName(path));
            return true;
        }
        finally
        {
            DeleteIfExists(innerPath);
        }
    }

    public async Task RewrapAsync(string path, string oldPassphrase, string newPassphrase, CancellationToken cancellationToken = default)
    {
        var innerPath = TempPathFor(path, "inner.tmp");
        var tempPath = TempPathFor(path, "tmp");
        try
        {
            await DecryptFileAsync(path, innerPath, oldPassphrase);
            var innerLength = new FileInfo(innerPath).Length;

            await using (var inner = File.OpenRead(innerPath))
            await using (var rewrapped = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encryptionService.EncryptBackup(inner, rewrapped, newPassphrase);
            }

            // Verify: the new outer layer opens with the new passphrase and gives back the same archive
            var check = new CountingStream();
            await using (var rewrapped = File.OpenRead(tempPath))
            {
                encryptionService.DecryptBackup(rewrapped, check, newPassphrase);
            }

            if (check.Length != innerLength)
                throw new InvalidOperationException($"Verification of re-wrapped backup failed (decrypted {check.Length} of {innerLength} bytes)");

            File.Move(tempPath, path, overwrite: true);
            logger.LogInformation("Moved the outer layer of damaged backup {FileName} to the new passphrase", Path.GetFileName(path));
        }
        finally
        {
            DeleteIfExists(innerPath);
            DeleteIfExists(tempPath);
        }
    }

    /// <summary>
    /// Writes a copy of <paramref name="sourcePath"/> with its database entry replaced by
    /// <paramref name="plaintext"/> encrypted under <paramref name="passphrase"/>, verifies the copy, and
    /// moves it over <paramref name="targetPath"/>.
    /// </summary>
    private async Task RewriteWithNewDatabaseEntryAsync(string sourcePath, string targetPath, Stream plaintext, string passphrase, CancellationToken cancellationToken)
    {
        var tempPath = TempPathFor(targetPath, "tmp");
        try
        {
            using (var cipher = streamManager.GetStream("BackupArchiveRotator.Cipher"))
            {
                plaintext.Position = 0;
                encryptionService.EncryptBackup(plaintext, cipher, passphrase);

                await using var source = File.OpenRead(sourcePath);
                await using var sourceGzip = new GZipStream(source, CompressionMode.Decompress);
                using var reader = new TarReader(sourceGzip);
                await using var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var targetGzip = new GZipStream(target, CompressionLevel.Optimal);
                await using var writer = new TarWriter(targetGzip, leaveOpen: true);

                while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
                {
                    if (entry.Name is BackupConstants.EncryptedDatabaseEntryName or BackupConstants.PlainDatabaseEntryName)
                    {
                        cipher.Position = 0;
                        await writer.WriteEntryAsync(
                            new PaxTarEntry(TarEntryType.RegularFile, BackupConstants.EncryptedDatabaseEntryName) { DataStream = cipher },
                            cancellationToken);
                        continue;
                    }

                    using var data = streamManager.GetStream("BackupArchiveRotator.EntryCopy");
                    if (entry.DataStream != null)
                        await entry.DataStream.CopyToAsync(data, cancellationToken);
                    data.Position = 0;
                    await writer.WriteEntryAsync(
                        new PaxTarEntry(entry.EntryType, entry.Name)
                        {
                            DataStream = data,
                            ModificationTime = entry.ModificationTime,
                            Mode = entry.Mode
                        },
                        cancellationToken);
                }
            } // the new ciphertext is released before verification reads the file back

            await VerifyAsync(tempPath, passphrase, plaintext.Length, cancellationToken);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    /// <summary>
    /// Proves a rewritten archive restores: metadata is present and the database entry fully decrypts
    /// to the expected length with the passphrase it was written under.
    /// </summary>
    private async Task VerifyAsync(string path, string passphrase, long expectedLength, CancellationToken cancellationToken)
    {
        var hasMetadata = false;
        long decryptedLength = -1;

        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (await tar.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            if (entry.Name == BackupConstants.MetadataEntryName)
            {
                hasMetadata = true;
            }
            else if (entry.Name == BackupConstants.EncryptedDatabaseEntryName && entry.DataStream != null)
            {
                using var cipher = streamManager.GetStream("BackupArchiveRotator.VerifyCipher");
                await entry.DataStream.CopyToAsync(cipher, cancellationToken);
                cipher.Position = 0;
                var plain = new CountingStream();
                encryptionService.DecryptBackup(cipher, plain, passphrase);
                decryptedLength = plain.Length;
            }
        }

        if (!hasMetadata || decryptedLength != expectedLength)
        {
            throw new InvalidOperationException(
                $"Verification of rewritten backup failed (metadata present: {hasMetadata}, decrypted {decryptedLength} of {expectedLength} bytes)");
        }
    }

    private async Task DecryptFileAsync(string sourcePath, string targetPath, string passphrase)
    {
        await using var source = File.OpenRead(sourcePath);
        await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encryptionService.DecryptBackup(source, target, passphrase);
    }

    private static string TempPathFor(string path, string suffix) => $"{path}.{Guid.NewGuid().ToString("N")[..8]}.{suffix}";

    private async Task<RecyclableMemoryStream> ReadDatabaseEntryAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (await tar.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            if (entry.Name is not (BackupConstants.EncryptedDatabaseEntryName or BackupConstants.PlainDatabaseEntryName))
                continue;

            var data = streamManager.GetStream("BackupArchiveRotator.DatabaseEntry");
            if (entry.DataStream != null)
                await entry.DataStream.CopyToAsync(data, cancellationToken);
            data.Position = 0;
            return data;
        }

        throw new InvalidOperationException($"Backup {Path.GetFileName(path)} has no database entry");
    }

    private bool OpensWith(Stream cipher, string passphrase)
    {
        try
        {
            cipher.Position = 0;
            encryptionService.DecryptBackup(cipher, Stream.Null, passphrase);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to delete temp file {Path}", path);
        }
    }

    /// <summary>A write-only stream that keeps only the number of bytes written, for verification.</summary>
    private sealed class CountingStream : Stream
    {
        private long _length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => _length += count;
        public override void Write(ReadOnlySpan<byte> buffer) => _length += buffer.Length;
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
