using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

/// <summary>
/// Mapping extensions between Data layer DTOs and Business layer models
/// for ContentDetection configuration.
/// </summary>
public static class ContentDetectionConfigMappings
{
    // ============================================================================
    // Root ContentDetectionConfig mappings
    // ============================================================================

    extension(ContentDetectionConfigData data)
    {
        public ContentDetectionConfig ToModel() => new()
        {
            FirstMessageOnly = data.FirstMessageOnly,
            FirstMessagesCount = data.FirstMessagesCount,
            AutoTrustMinMessageLength = data.AutoTrustMinMessageLength,
            AutoTrustMinAccountAgeHours = data.AutoTrustMinAccountAgeHours,
            MinMessageLength = data.MinMessageLength,
            AutoBanThreshold = data.AutoBanThreshold,
            ReviewQueueThreshold = data.ReviewQueueThreshold,
            MaxConfidenceVetoThreshold = data.MaxConfidenceVetoThreshold,
            TrainingMode = data.TrainingMode,

            // Use null-coalescing for sub-configs to handle partial JSON from legacy data
            StopWords = (data.StopWords ?? new StopWordsConfigData()).ToModel(),
            Similarity = (data.Similarity ?? new SimilarityConfigData()).ToModel(),
            Bayes = (data.Bayes ?? new BayesConfigData()).ToModel(),
            InvisibleChars = (data.InvisibleChars ?? new InvisibleCharsConfigData()).ToModel(),
            Translation = (data.Translation ?? new TranslationConfigData()).ToModel(),
            Spacing = (data.Spacing ?? new SpacingConfigData()).ToModel(),
            AIVeto = (data.AIVeto ?? new AIVetoConfigData()).ToModel(),
            UrlBlocklist = (data.UrlBlocklist ?? new UrlBlocklistConfigData()).ToModel(),
            ThreatIntel = (data.ThreatIntel ?? new ThreatIntelConfigData()).ToModel(),
            SeoScraping = (data.SeoScraping ?? new SeoScrapingConfigData()).ToModel(),
            ImageSpam = (data.ImageSpam ?? new ImageContentConfigData()).ToModel(),
            VideoSpam = (data.VideoSpam ?? new VideoContentConfigData()).ToModel(),
            FileScanning = (data.FileScanning ?? new FileScanningDetectionConfigData()).ToModel()
        };
    }

    extension(ContentDetectionConfig model)
    {
        public ContentDetectionConfigData ToData() => new()
        {
            FirstMessageOnly = model.FirstMessageOnly,
            FirstMessagesCount = model.FirstMessagesCount,
            AutoTrustMinMessageLength = model.AutoTrustMinMessageLength,
            AutoTrustMinAccountAgeHours = model.AutoTrustMinAccountAgeHours,
            MinMessageLength = model.MinMessageLength,
            AutoBanThreshold = model.AutoBanThreshold,
            ReviewQueueThreshold = model.ReviewQueueThreshold,
            MaxConfidenceVetoThreshold = model.MaxConfidenceVetoThreshold,
            TrainingMode = model.TrainingMode,

            StopWords = model.StopWords.ToData(),
            Similarity = model.Similarity.ToData(),
            Bayes = model.Bayes.ToData(),
            InvisibleChars = model.InvisibleChars.ToData(),
            Translation = model.Translation.ToData(),
            Spacing = model.Spacing.ToData(),
            AIVeto = model.AIVeto.ToData(),
            UrlBlocklist = model.UrlBlocklist.ToData(),
            ThreatIntel = model.ThreatIntel.ToData(),
            SeoScraping = model.SeoScraping.ToData(),
            ImageSpam = model.ImageSpam.ToData(),
            VideoSpam = model.VideoSpam.ToData(),
            FileScanning = model.FileScanning.ToData()
        };
    }

    // ============================================================================
    // StopWordsConfig mappings
    // ============================================================================

    extension(StopWordsConfigData data)
    {
        public StopWordsConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            ConfidenceThreshold = data.ConfidenceThreshold,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(StopWordsConfig model)
    {
        public StopWordsConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            ConfidenceThreshold = model.ConfidenceThreshold,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // SimilarityConfig mappings
    // ============================================================================

    extension(SimilarityConfigData data)
    {
        public SimilarityConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            Threshold = data.Threshold,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(SimilarityConfig model)
    {
        public SimilarityConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            Threshold = model.Threshold,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // BayesConfig mappings
    // ============================================================================

    extension(BayesConfigData data)
    {
        public BayesConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            MinSpamProbability = data.MinSpamProbability,
            ConfidenceThreshold = data.ConfidenceThreshold,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(BayesConfig model)
    {
        public BayesConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            MinSpamProbability = model.MinSpamProbability,
            ConfidenceThreshold = model.ConfidenceThreshold,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // InvisibleCharsConfig mappings
    // ============================================================================

    extension(InvisibleCharsConfigData data)
    {
        public InvisibleCharsConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(InvisibleCharsConfig model)
    {
        public InvisibleCharsConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // TranslationConfig mappings
    // ============================================================================

    extension(TranslationConfigData data)
    {
        public TranslationConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            CheckTranslatedContent = data.CheckTranslatedContent,
            MinMessageLength = data.MinMessageLength,
            LatinScriptThreshold = data.LatinScriptThreshold,
            LanguageDetectionConfidenceThreshold = data.LanguageDetectionConfidenceThreshold,
            ConfidenceThreshold = data.ConfidenceThreshold,
            WarnNonEnglish = data.WarnNonEnglish,
            WarningMessage = data.WarningMessage,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(TranslationConfig model)
    {
        public TranslationConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            CheckTranslatedContent = model.CheckTranslatedContent,
            MinMessageLength = model.MinMessageLength,
            LatinScriptThreshold = model.LatinScriptThreshold,
            LanguageDetectionConfidenceThreshold = model.LanguageDetectionConfidenceThreshold,
            ConfidenceThreshold = model.ConfidenceThreshold,
            WarnNonEnglish = model.WarnNonEnglish,
            WarningMessage = model.WarningMessage,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // SpacingConfig mappings
    // ============================================================================

    extension(SpacingConfigData data)
    {
        public SpacingConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            MinWordsCount = data.MinWordsCount,
            ShortWordLength = data.ShortWordLength,
            ShortWordRatioThreshold = data.ShortWordRatioThreshold,
            SpaceRatioThreshold = data.SpaceRatioThreshold,
            ConfidenceThreshold = data.ConfidenceThreshold,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(SpacingConfig model)
    {
        public SpacingConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            MinWordsCount = model.MinWordsCount,
            ShortWordLength = model.ShortWordLength,
            ShortWordRatioThreshold = model.ShortWordRatioThreshold,
            SpaceRatioThreshold = model.SpaceRatioThreshold,
            ConfidenceThreshold = model.ConfidenceThreshold,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // AIVetoConfig mappings
    // SystemPrompt removed - prompts come from prompt_versions table
    // ============================================================================

    extension(AIVetoConfigData data)
    {
        public AIVetoConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            CheckShortMessages = data.CheckShortMessages,
            MessageHistoryCount = data.MessageHistoryCount,
            ConfidenceThreshold = data.ConfidenceThreshold,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(AIVetoConfig model)
    {
        public AIVetoConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            CheckShortMessages = model.CheckShortMessages,
            MessageHistoryCount = model.MessageHistoryCount,
            ConfidenceThreshold = model.ConfidenceThreshold,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // UrlBlocklistConfig mappings
    // ============================================================================

    extension(UrlBlocklistConfigData data)
    {
        public UrlBlocklistConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            CacheDuration = TimeSpan.FromSeconds(data.CacheDurationSeconds),
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(UrlBlocklistConfig model)
    {
        public UrlBlocklistConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            CacheDurationSeconds = model.CacheDuration.TotalSeconds,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // ThreatIntelConfig mappings
    // ============================================================================

    extension(ThreatIntelConfigData data)
    {
        public ThreatIntelConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            UseVirusTotal = data.UseVirusTotal,
            Timeout = TimeSpan.FromSeconds(data.TimeoutSeconds),
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(ThreatIntelConfig model)
    {
        public ThreatIntelConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            UseVirusTotal = model.UseVirusTotal,
            TimeoutSeconds = model.Timeout.TotalSeconds,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // SeoScrapingConfig mappings
    // ============================================================================

    extension(SeoScrapingConfigData data)
    {
        public SeoScrapingConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            Timeout = TimeSpan.FromSeconds(data.TimeoutSeconds),
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(SeoScrapingConfig model)
    {
        public SeoScrapingConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            TimeoutSeconds = model.Timeout.TotalSeconds,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // ImageContentConfig mappings
    // ============================================================================

    extension(ImageContentConfigData data)
    {
        public ImageContentConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            UseOpenAIVision = data.UseOpenAIVision,
            UseOCR = data.UseOCR,
            OcrConfidenceThreshold = data.OcrConfidenceThreshold,
            MinOcrTextLength = data.MinOcrTextLength,
            UseHashSimilarity = data.UseHashSimilarity,
            HashSimilarityThreshold = data.HashSimilarityThreshold,
            HashMatchConfidence = data.HashMatchConfidence,
            MaxTrainingSamplesToCompare = data.MaxTrainingSamplesToCompare,
            Timeout = TimeSpan.FromSeconds(data.TimeoutSeconds),
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(ImageContentConfig model)
    {
        public ImageContentConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            UseOpenAIVision = model.UseOpenAIVision,
            UseOCR = model.UseOCR,
            OcrConfidenceThreshold = model.OcrConfidenceThreshold,
            MinOcrTextLength = model.MinOcrTextLength,
            UseHashSimilarity = model.UseHashSimilarity,
            HashSimilarityThreshold = model.HashSimilarityThreshold,
            HashMatchConfidence = model.HashMatchConfidence,
            MaxTrainingSamplesToCompare = model.MaxTrainingSamplesToCompare,
            TimeoutSeconds = model.Timeout.TotalSeconds,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // VideoContentConfig mappings
    // ============================================================================

    extension(VideoContentConfigData data)
    {
        public VideoContentConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            UseOpenAIVision = data.UseOpenAIVision,
            UseOCR = data.UseOCR,
            OcrConfidenceThreshold = data.OcrConfidenceThreshold,
            MinOcrTextLength = data.MinOcrTextLength,
            UseHashSimilarity = data.UseHashSimilarity,
            HashSimilarityThreshold = data.HashSimilarityThreshold,
            HashMatchConfidence = data.HashMatchConfidence,
            MaxTrainingSamplesToCompare = data.MaxTrainingSamplesToCompare,
            Timeout = TimeSpan.FromSeconds(data.TimeoutSeconds),
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(VideoContentConfig model)
    {
        public VideoContentConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            UseOpenAIVision = model.UseOpenAIVision,
            UseOCR = model.UseOCR,
            OcrConfidenceThreshold = model.OcrConfidenceThreshold,
            MinOcrTextLength = model.MinOcrTextLength,
            UseHashSimilarity = model.UseHashSimilarity,
            HashSimilarityThreshold = model.HashSimilarityThreshold,
            HashMatchConfidence = model.HashMatchConfidence,
            MaxTrainingSamplesToCompare = model.MaxTrainingSamplesToCompare,
            TimeoutSeconds = model.Timeout.TotalSeconds,
            AlwaysRun = model.AlwaysRun
        };
    }

    // ============================================================================
    // FileScanningDetectionConfig mappings
    // ============================================================================

    extension(FileScanningDetectionConfigData data)
    {
        public FileScanningDetectionConfig ToModel() => new()
        {
            UseGlobal = data.UseGlobal,
            Enabled = data.Enabled,
            AlwaysRun = data.AlwaysRun
        };
    }

    extension(FileScanningDetectionConfig model)
    {
        public FileScanningDetectionConfigData ToData() => new()
        {
            UseGlobal = model.UseGlobal,
            Enabled = model.Enabled,
            AlwaysRun = model.AlwaysRun
        };
    }
}
