namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Result of one repair attempt on the damaged backups in a directory.
/// </summary>
/// <param name="RepairedCount">Backups the supplied passphrase opened and that are now normal backups.</param>
/// <param name="StillWrappedFiles">File names still damaged after this attempt, sorted.</param>
public record WrappedRepairResult(int RepairedCount, IReadOnlyList<string> StillWrappedFiles);
