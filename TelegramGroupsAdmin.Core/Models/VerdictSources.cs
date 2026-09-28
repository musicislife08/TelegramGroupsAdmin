namespace TelegramGroupsAdmin.Core.Models;

public static class VerdictSources
{
    extension(VerdictSource source)
    {
        public bool IsScan() => source is VerdictSource.ContentScan or VerdictSource.FileScan;

        public bool IsDecision() => !source.IsScan();
    }
}
