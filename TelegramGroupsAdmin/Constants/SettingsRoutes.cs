namespace TelegramGroupsAdmin.Constants;

/// <summary>
/// Constants for Settings page routes and titles.
/// Single source of truth - used in nav links, switch cases, and title mappings.
/// </summary>
public static class SettingsRoutes
{
    public const string BasePath = "/settings";

    public static class System
    {
        public const string Section = "system";

        public const string General = "general";
        public const string GeneralTitle = "General Settings";

        public const string Security = "security";
        public const string SecurityTitle = "Security Settings";

        public const string Accounts = "accounts";
        public const string AccountsTitle = "Admin Accounts";

        public const string AiProviders = "ai-providers";
        public const string AiProvidersTitle = "AI Providers";

        public const string Email = "email";
        public const string EmailTitle = "Email";

        public const string ClamAv = "clamav";
        public const string ClamAvTitle = "ClamAV";

        public const string VirusTotal = "virustotal";
        public const string VirusTotalTitle = "VirusTotal";

        public const string Logging = "logging";
        public const string LoggingTitle = "Logging Settings";

        public const string BackgroundJobs = "background-jobs";
        public const string BackgroundJobsTitle = "Background Jobs";

        public const string BackupConfig = "backup-config";
        public const string BackupConfigTitle = "Backup Configuration";
    }

    public static class Telegram
    {
        public const string Section = "telegram";

        public const string BotConfig = "bot-config";
        public const string BotConfigTitle = "Bot Configuration";

        public const string ServiceMessages = "service-messages";
        public const string ServiceMessagesTitle = "Service Messages";
    }

    public static class Notifications
    {
        public const string Section = "notifications";

        public const string WebPush = "web-push";
        public const string WebPushTitle = "Web Push Notifications";
    }

    public static class ContentDetection
    {
        public const string Section = "content-detection";

        public const string Algorithms = "algorithms";
        public const string AlgorithmsTitle = "Detection Algorithms";

        public const string Tuning = "tuning";
        public const string TuningTitle = "Algorithm Tuning";

        public const string AiIntegration = "ai-integration";
        public const string AiIntegrationTitle = "AI Integration";

        public const string UrlFilters = "url-filters";
        public const string UrlFiltersTitle = "URL Filtering";

        public const string FileScanning = "file-scanning";
        public const string FileScanningTitle = "File Scanning";
    }

    public static class TrainingData
    {
        public const string Section = "training-data";

        public const string Stopwords = "stopwords";
        public const string StopwordsTitle = "Stop Words Library";

        public const string Samples = "samples";
        public const string SamplesTitle = "Training Samples";
    }

    /// <summary>
    /// Helper to build a settings route path.
    /// </summary>
    public static string BuildPath(string section, string subSection)
        => $"{BasePath}/{section}/{subSection}";
}
