using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
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

        using var entry = await ReadDatabaseEntryAsync(path, cancellationToken);
        using var plaintext = streamManager.GetStream("BackupArchiveRotator.Plaintext");

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
            catch (CryptographicException oldFailure)
            {
                if (OpensWith(entry, newPassphrase))
                {
                    logger.LogInformation("Backup {FileName} is already on the new passphrase", Path.GetFileName(path));
                    return ReencryptOutcome.AlreadyCurrent;
                }

                ExceptionDispatchInfo.Throw(oldFailure);
            }
        }

        await RewriteWithNewDatabaseEntryAsync(path, path, plaintext, newPassphrase, cancellationToken);
        logger.LogInformation("Re-encrypted the database entry of backup {FileName}", Path.GetFileName(path));
        return ReencryptOutcome.Reencrypted;
    }

    public async Task<bool> TryRepairWrappedAsync(string path, string storedPassphrase, string originalPassphrase, CancellationToken cancellationToken = default)
    {
        var innerPath = $"{path}.{Guid.NewGuid().ToString("N")[..8]}.inner.tmp";
        try
        {
            await using (var wrapped = File.OpenRead(path))
            await using (var inner = File.Create(innerPath))
            {
                encryptionService.DecryptBackup(wrapped, inner, storedPassphrase);
            }

            if (await InspectAsync(innerPath, cancellationToken) != BackupFileState.Encrypted)
                throw new InvalidOperationException($"Backup {Path.GetFileName(path)} does not contain an encrypted backup archive");

            using var entry = await ReadDatabaseEntryAsync(innerPath, cancellationToken);
            using var plaintext = streamManager.GetStream("BackupArchiveRotator.RepairPlaintext");
            try
            {
                encryptionService.DecryptBackup(entry, plaintext, originalPassphrase);
            }
            catch (CryptographicException)
            {
                logger.LogInformation("Damaged backup {FileName} does not open with the supplied passphrase", Path.GetFileName(path));
                return false;
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

    /// <summary>
    /// Writes a copy of <paramref name="sourcePath"/> with its database entry replaced by
    /// <paramref name="plaintext"/> encrypted under <paramref name="passphrase"/>, verifies the copy, and
    /// moves it over <paramref name="targetPath"/>.
    /// </summary>
    private async Task RewriteWithNewDatabaseEntryAsync(string sourcePath, string targetPath, Stream plaintext, string passphrase, CancellationToken cancellationToken)
    {
        plaintext.Position = 0;
        using var cipher = streamManager.GetStream("BackupArchiveRotator.Cipher");
        encryptionService.EncryptBackup(plaintext, cipher, passphrase);

        var tempPath = $"{targetPath}.{Guid.NewGuid().ToString("N")[..8]}.tmp";
        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var sourceGzip = new GZipStream(source, CompressionMode.Decompress))
            using (var reader = new TarReader(sourceGzip))
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var targetGzip = new GZipStream(target, CompressionLevel.Optimal))
            await using (var writer = new TarWriter(targetGzip, leaveOpen: true))
            {
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
                        new PaxTarEntry(entry.EntryType, entry.Name) { DataStream = data },
                        cancellationToken);
                }
            }

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
                using var plain = streamManager.GetStream("BackupArchiveRotator.VerifyPlain");
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
}
