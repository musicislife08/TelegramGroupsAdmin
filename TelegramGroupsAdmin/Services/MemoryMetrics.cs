using System.Diagnostics.Metrics;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Services.Auth;
using TelegramGroupsAdmin.Services.Docs;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Media;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Registers ObservableGauge instruments on every stateful singleton to enable
/// per-component memory attribution in Prometheus/Grafana.
/// Gauges are pull-based — callbacks only fire on /metrics scrape, zero overhead between scrapes.
/// </summary>
public sealed class MemoryMetrics
{
    public MemoryMetrics(
        IChatCache chatCache,
        IChatHealthCache chatHealthCache,
        ITelegramSessionManager sessionManager,
        IMediaRefetchQueueService mediaRefetchQueue,
        IDocumentationService documentationService,
        IRateLimitService rateLimitService,
        IIntermediateAuthService intermediateAuthService,
        IPendingRecoveryCodesService pendingRecoveryCodesService,
        IMLTextClassifierService mlClassifier,
        IBayesClassifierService bayesClassifier)
    {
        var meter = new Meter("TelegramGroupsAdmin.Memory");

        // --- Telegram caches ---
        meter.CreateObservableGauge(
            "tga.cache.chat.count",
            () => chatCache.Count,
            description: "Number of Telegram Chat objects in ChatCache");

        meter.CreateObservableGauge(
            "tga.cache.chat_health.count",
            () => chatHealthCache.Count,
            description: "Number of entries in ChatHealthCache");

        // --- WTelegram session manager ---
        meter.CreateObservableGauge(
            "tga.sessions.client.count",
            () => sessionManager.ActiveClientCount,
            description: "Number of active WTelegram client connections");

        // --- Media refetch queue ---
        meter.CreateObservableGauge(
            "tga.queue.media_refetch.depth",
            () => mediaRefetchQueue.GetQueueDepth(),
            description: "Number of in-flight media refetch requests");

        // --- Documentation cache ---
        meter.CreateObservableGauge(
            "tga.cache.docs.initialized",
            () => documentationService.IsInitialized ? 1 : 0,
            description: "Whether the documentation cache has been loaded (1=yes, 0=no)");

        // --- Auth state ---
        meter.CreateObservableGauge(
            "tga.cache.rate_limit.count",
            () => rateLimitService.EntryCount,
            description: "Number of active rate limit tracking entries");

        meter.CreateObservableGauge(
            "tga.cache.auth_tokens.count",
            () => intermediateAuthService.EntryCount,
            description: "Number of pending intermediate auth tokens");

        meter.CreateObservableGauge(
            "tga.cache.recovery_codes.count",
            () => pendingRecoveryCodesService.EntryCount,
            description: "Number of pending recovery code entries");

        // --- ML models ---
        meter.CreateObservableGauge(
            "tga.ml.sdca.loaded",
            () => mlClassifier.GetMetadata() is not null ? 1 : 0,
            description: "Whether the ML.NET SDCA spam model is loaded (1=yes, 0=no)");

        meter.CreateObservableGauge(
            "tga.ml.bayes.loaded",
            () => bayesClassifier.GetMetadata() is not null ? 1 : 0,
            description: "Whether the Bayes spam model is loaded (1=yes, 0=no)");

        meter.CreateObservableGauge(
            "tga.ml.bayes.vocab.spam",
            () => bayesClassifier.GetMetadata()?.SpamVocabularySize ?? 0,
            description: "Number of unique words in the Bayes spam vocabulary");

        meter.CreateObservableGauge(
            "tga.ml.bayes.vocab.ham",
            () => bayesClassifier.GetMetadata()?.HamVocabularySize ?? 0,
            description: "Number of unique words in the Bayes ham vocabulary");

        // --- Semantic Kernel cache (static) ---
        meter.CreateObservableGauge(
            "tga.cache.kernel.count",
            () => SemanticKernelChatService.CachedKernelCount,
            description: "Number of cached Semantic Kernel instances");
    }
}
