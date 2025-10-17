using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of impersonation detection check
/// </summary>
public record ImpersonationCheckResult
{
    public bool ShouldTakeAction => TotalScore >= 50;
    public bool ShouldAutoBan => TotalScore >= 100;

    public int TotalScore { get; init; }
    public ImpersonationRiskLevel RiskLevel { get; init; }

    public long SuspectedUserId { get; init; }
    public long TargetUserId { get; init; }
    public long ChatId { get; init; }

    public bool NameMatch { get; init; }
    public bool PhotoMatch { get; init; }
    public double? PhotoSimilarityScore { get; init; }
}

/// <summary>
/// Service for detecting impersonation attempts (name + photo matching)
/// </summary>
public interface IImpersonationDetectionService
{
    /// <summary>
    /// Checks if a user should be checked for impersonation
    /// based on message count and trusted status
    /// </summary>
    Task<bool> ShouldCheckUserAsync(long userId, long chatId);

    /// <summary>
    /// Checks a user for impersonation against all chat admins
    /// Returns null if no matches found (score = 0)
    /// </summary>
    Task<ImpersonationCheckResult?> CheckUserAsync(
        long userId,
        long chatId,
        string? firstName,
        string? lastName,
        string? photoPath);

    /// <summary>
    /// Executes action based on check result (auto-ban, log alert)
    /// </summary>
    Task ExecuteActionAsync(ImpersonationCheckResult result);
}

/// <summary>
/// Service for detecting potential admin impersonators using composite scoring.
/// Checks: Name similarity (50 pts) + Photo similarity (50 pts) = 0-100 pts
/// - 100 pts = Auto-ban (high confidence, both name AND photo match)
/// - 50-99 pts = Review queue (single match, needs human review)
/// - &lt;50 pts = Allowed (no significant similarity)
/// </summary>
public class ImpersonationDetectionService : IImpersonationDetectionService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TelegramUserRepository _telegramUserRepository;
    private readonly IChatAdminsRepository _chatAdminsRepository;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly MessageHistoryRepository _messageHistoryRepository;
    private readonly IPhotoHashService _photoHashService;
    private readonly IImpersonationAlertsRepository _impersonationAlertsRepository;
    private readonly ModerationActionService _moderationActionService;
    private readonly TelegramBotClientFactory _botClientFactory;
    private readonly TelegramOptions _telegramOptions;
    private readonly IConfigService _configService;
    private readonly ILogger<ImpersonationDetectionService> _logger;

    // Name matching threshold (80% similar = possible impersonation)
    private const double NameSimilarityThreshold = 0.8;

    // Photo matching threshold (90% similar = possible impersonation)
    private const double PhotoSimilarityThreshold = 0.9;

    public ImpersonationDetectionService(
        IDbContextFactory<AppDbContext> contextFactory,
        TelegramUserRepository telegramUserRepository,
        IChatAdminsRepository chatAdminsRepository,
        IManagedChatsRepository managedChatsRepository,
        MessageHistoryRepository messageHistoryRepository,
        IPhotoHashService photoHashService,
        IImpersonationAlertsRepository impersonationAlertsRepository,
        ModerationActionService moderationActionService,
        TelegramBotClientFactory botClientFactory,
        IOptions<TelegramOptions> telegramOptions,
        IConfigService configService,
        ILogger<ImpersonationDetectionService> logger)
    {
        _contextFactory = contextFactory;
        _telegramUserRepository = telegramUserRepository;
        _chatAdminsRepository = chatAdminsRepository;
        _managedChatsRepository = managedChatsRepository;
        _messageHistoryRepository = messageHistoryRepository;
        _photoHashService = photoHashService;
        _impersonationAlertsRepository = impersonationAlertsRepository;
        _moderationActionService = moderationActionService;
        _botClientFactory = botClientFactory;
        _telegramOptions = telegramOptions.Value;
        _configService = configService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> ShouldCheckUserAsync(long userId, long chatId)
    {
        // 1. Check if user is globally trusted (bypass all checks)
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId);
        if (user?.IsTrusted == true)
        {
            _logger.LogDebug("User {UserId} is trusted, skipping impersonation check", userId);
            return false;
        }

        // 2. Check if user has pending alert (avoid duplicate checks)
        var hasPendingAlert = await _impersonationAlertsRepository.HasPendingAlertAsync(userId);
        if (hasPendingAlert)
        {
            _logger.LogDebug("User {UserId} already has pending impersonation alert", userId);
            return false;
        }

        // 3. Check message count (only check first N messages per chat)
        var messageCount = await _messageHistoryRepository.GetMessageCountAsync(userId, chatId);
        var config = await _configService.GetEffectiveAsync<SpamDetectionConfig>(ConfigType.SpamDetection, chatId)
                     ?? new SpamDetectionConfig();

        var threshold = config.FirstMessagesCount;
        if (messageCount >= threshold)
        {
            _logger.LogDebug(
                "User {UserId} has {MessageCount} messages in chat {ChatId}, threshold {Threshold}, skipping check",
                userId, messageCount, chatId, threshold);
            return false;
        }

        _logger.LogDebug(
            "User {UserId} should be checked for impersonation (messages: {MessageCount}/{Threshold})",
            userId, messageCount, threshold);
        return true;
    }

    /// <inheritdoc/>
    public async Task<ImpersonationCheckResult?> CheckUserAsync(
        long userId,
        long chatId,
        string? firstName,
        string? lastName,
        string? photoPath)
    {
        _logger.LogDebug(
            "Checking user {UserId} for impersonation in chat {ChatId}",
            userId, chatId);

        // Get all admins from all managed chats
        var allAdmins = await GetAllAdminsWithDataAsync();

        if (allAdmins.Count == 0)
        {
            _logger.LogWarning("No admins found in any managed chat, skipping impersonation check");
            return null;
        }

        // Check against each admin
        ImpersonationCheckResult? bestMatch = null;
        int highestScore = 0;

        foreach (var admin in allAdmins)
        {
            // Skip self-match (user can't impersonate themselves)
            if (admin.TelegramUserId == userId)
                continue;

            int score = 0;
            bool nameMatch = false;
            bool photoMatch = false;
            double? photoSimilarity = null;

            // Check name similarity (50 points)
            if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(admin.FirstName))
            {
                var similarity = CalculateNameSimilarity(
                    firstName, lastName,
                    admin.FirstName, admin.LastName);

                if (similarity >= NameSimilarityThreshold)
                {
                    nameMatch = true;
                    score += 50;
                    _logger.LogDebug(
                        "Name match detected: User {UserId} '{UserName}' vs Admin {AdminId} '{AdminName}' (similarity: {Similarity:F2})",
                        userId, $"{firstName} {lastName}".Trim(),
                        admin.TelegramUserId, $"{admin.FirstName} {admin.LastName}".Trim(),
                        similarity);
                }
            }

            // Check photo similarity (50 points)
            if (!string.IsNullOrWhiteSpace(photoPath) && !string.IsNullOrWhiteSpace(admin.UserPhotoPath))
            {
                try
                {
                    // Compute hashes for both photos
                    var userHash = await _photoHashService.ComputePhotoHashAsync(photoPath);
                    var adminHash = await _photoHashService.ComputePhotoHashAsync(admin.UserPhotoPath);

                    if (userHash != null && adminHash != null)
                    {
                        var similarity = _photoHashService.CompareHashes(userHash, adminHash);
                        photoSimilarity = similarity;

                        if (similarity >= PhotoSimilarityThreshold)
                        {
                            photoMatch = true;
                            score += 50;
                            _logger.LogDebug(
                                "Photo match detected: User {UserId} vs Admin {AdminId} (similarity: {Similarity:F2})",
                                userId, admin.TelegramUserId, similarity);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to compare photos for User {UserId} vs Admin {AdminId}",
                        userId, admin.TelegramUserId);
                }
            }

            // Track highest scoring match
            if (score > highestScore)
            {
                highestScore = score;
                bestMatch = new ImpersonationCheckResult
                {
                    TotalScore = score,
                    RiskLevel = score >= 100 ? ImpersonationRiskLevel.Critical : ImpersonationRiskLevel.Medium,
                    SuspectedUserId = userId,
                    TargetUserId = admin.TelegramUserId,
                    ChatId = chatId,
                    NameMatch = nameMatch,
                    PhotoMatch = photoMatch,
                    PhotoSimilarityScore = photoSimilarity
                };
            }
        }

        // Only return if score >= 50 (at least one attribute matched)
        if (bestMatch != null && bestMatch.TotalScore >= 50)
        {
            _logger.LogWarning(
                "Impersonation detected: User {UserId} vs Admin {TargetId} in chat {ChatId} (score: {Score}, risk: {Risk})",
                userId, bestMatch.TargetUserId, chatId, bestMatch.TotalScore, bestMatch.RiskLevel);
            return bestMatch;
        }

        _logger.LogDebug("No impersonation detected for user {UserId} in chat {ChatId}", userId, chatId);
        return null;
    }

    /// <inheritdoc/>
    public async Task ExecuteActionAsync(ImpersonationCheckResult result)
    {
        try
        {
            // 1. Create alert record
            var alert = new ImpersonationAlertRecord
            {
                SuspectedUserId = result.SuspectedUserId,
                TargetUserId = result.TargetUserId,
                ChatId = result.ChatId,
                TotalScore = result.TotalScore,
                RiskLevel = result.RiskLevel,
                NameMatch = result.NameMatch,
                PhotoMatch = result.PhotoMatch,
                PhotoSimilarityScore = result.PhotoSimilarityScore,
                DetectedAt = DateTimeOffset.UtcNow,
                AutoBanned = result.ShouldAutoBan
            };

            var alertId = await _impersonationAlertsRepository.CreateAlertAsync(alert);

            _logger.LogInformation(
                "Created impersonation alert #{AlertId}: User {SuspectedUserId} → Admin {TargetUserId} (score: {Score})",
                alertId, result.SuspectedUserId, result.TargetUserId, result.TotalScore);

            // 2. Auto-ban if score >= 100 (high confidence)
            if (result.ShouldAutoBan)
            {
                var reason = $"Auto-banned: Impersonation detected (name match: {result.NameMatch}, photo match: {result.PhotoMatch}, score: {result.TotalScore})";

                var botClient = _botClientFactory.GetOrCreate(_telegramOptions.BotToken);
                var banResult = await _moderationActionService.BanUserAsync(
                    botClient: botClient,
                    userId: result.SuspectedUserId,
                    messageId: null,
                    executorId: "system:impersonation",
                    reason: reason);

                if (banResult.Success)
                {
                    _logger.LogWarning(
                        "Auto-banned user {UserId} for impersonation (score: {Score}, chats affected: {ChatsAffected})",
                        result.SuspectedUserId, result.TotalScore, banResult.ChatsAffected);
                }
                else
                {
                    _logger.LogError(
                        "Failed to auto-ban user {UserId} for impersonation: {Error}",
                        result.SuspectedUserId, banResult.ErrorMessage);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Impersonation alert created for manual review (score: {Score} < 100, no auto-ban)",
                    result.TotalScore);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to execute impersonation action for user {UserId}",
                result.SuspectedUserId);
            throw;
        }
    }

    /// <summary>
    /// Get all admins from all managed chats with their user data
    /// </summary>
    private async Task<List<AdminUserData>> GetAllAdminsWithDataAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Query all admins across all managed chats with user data
        var admins = await (
            from ca in context.ChatAdmins
            join mc in context.ManagedChats on ca.ChatId equals mc.ChatId
            join tu in context.TelegramUsers on ca.TelegramId equals tu.TelegramUserId into tuGroup
            from tu in tuGroup.DefaultIfEmpty()
            where ca.IsActive && mc.IsActive
            select new AdminUserData
            {
                TelegramUserId = ca.TelegramId,
                ChatId = ca.ChatId,
                FirstName = tu != null ? tu.FirstName : null,
                LastName = tu != null ? tu.LastName : null,
                UserPhotoPath = tu != null ? tu.UserPhotoPath : null
            }
        )
        .AsNoTracking()
        .Distinct()
        .ToListAsync();

        _logger.LogDebug("Found {Count} active admins across all managed chats", admins.Count);
        return admins;
    }

    /// <summary>
    /// Calculate name similarity using normalized Levenshtein distance
    /// Returns 0.0 (completely different) to 1.0 (identical)
    /// </summary>
    private double CalculateNameSimilarity(
        string? firstName1, string? lastName1,
        string? firstName2, string? lastName2)
    {
        // Normalize names (combine first+last, lowercase, trim whitespace)
        var name1 = $"{firstName1} {lastName1}".ToLowerInvariant().Trim();
        var name2 = $"{firstName2} {lastName2}".ToLowerInvariant().Trim();

        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
            return 0.0;

        var distance = LevenshteinDistance(name1, name2);
        var maxLength = Math.Max(name1.Length, name2.Length);

        // Convert distance to similarity (0 distance = 100% similar)
        return 1.0 - ((double)distance / maxLength);
    }

    /// <summary>
    /// Compute Levenshtein distance (edit distance) between two strings
    /// </summary>
    private int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;

        if (string.IsNullOrEmpty(target))
            return source.Length;

        var matrix = new int[source.Length + 1, target.Length + 1];

        // Initialize first row and column
        for (int i = 0; i <= source.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= target.Length; j++)
            matrix[0, j] = j;

        // Fill matrix
        for (int i = 1; i <= source.Length; i++)
        {
            for (int j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(
                        matrix[i - 1, j] + 1,      // Deletion
                        matrix[i, j - 1] + 1),     // Insertion
                    matrix[i - 1, j - 1] + cost);  // Substitution
            }
        }

        return matrix[source.Length, target.Length];
    }

    /// <summary>
    /// Internal data structure for admin user data
    /// </summary>
    private class AdminUserData
    {
        public long TelegramUserId { get; set; }
        public long ChatId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? UserPhotoPath { get; set; }
    }
}
