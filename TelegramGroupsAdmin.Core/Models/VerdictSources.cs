namespace TelegramGroupsAdmin.Core.Models;

public static class VerdictSources
{
    extension(VerdictSource source)
    {
        /// <summary>User-facing name of what caused a verdict event.</summary>
        public string ToDisplayText() => source switch
        {
            VerdictSource.ContentScan => "Automatic scan",
            VerdictSource.FileScan => "File scan",
            VerdictSource.AutoBan => "Auto-ban",
            VerdictSource.WebMarkSpam => "Marked spam (web)",
            VerdictSource.WebMarkHam => "Marked clean (web)",
            VerdictSource.SpamCommand => "/spam command",
            VerdictSource.ReviewSpam => "Review queue: spam",
            VerdictSource.ReviewClean => "Review queue: clean",
            VerdictSource.TrainingDataPage => "Training data page",
            VerdictSource.TrainingExclude => "Removed from training",
            VerdictSource.Import => "Imported sample",
            VerdictSource.LegacyManual => "Manual (legacy)",
            _ => source.ToString()
        };
    }
}
