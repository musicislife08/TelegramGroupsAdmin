using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services;

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
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly IChatAdminsRepository _chatAdminsRepository;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly IPhotoHashService _photoHashService;
    private readonly IReportsRepository _reportsRepository;
    private readonly IBotModerationService _moderationActionService;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly IConfigService _configService;
    private readonly ILogger<ImpersonationDetectionService> _logger;

    // Name matching threshold (80% similar = possible impersonation)
    private const double NameSimilarityThreshold = 0.8;

    // Photo matching threshold (90% similar = possible impersonation)
    private const double PhotoSimilarityThreshold = 0.9;

    public ImpersonationDetectionService(
        IDbContextFactory<AppDbContext> contextFactory,
        ITelegramUserRepository telegramUserRepository,
        IChatAdminsRepository chatAdminsRepository,
        IManagedChatsRepository managedChatsRepository,
        IMessageHistoryRepository messageHistoryRepository,
        IPhotoHashService photoHashService,
        IReportsRepository reportsRepository,
        IBotModerationService moderationActionService,
        ITelegramBotClientFactory botClientFactory,
        IConfigService configService,
        ILogger<ImpersonationDetectionService> logger)
    {
        _contextFactory = contextFactory;
        _telegramUserRepository = telegramUserRepository;
        _chatAdminsRepository = chatAdminsRepository;
        _managedChatsRepository = managedChatsRepository;
        _messageHistoryRepository = messageHistoryRepository;
        _photoHashService = photoHashService;
        _reportsRepository = reportsRepository;
        _moderationActionService = moderationActionService;
        _botClientFactory = botClientFactory;
        _configService = configService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> ShouldCheckUserAsync(long userId, long chatId)
    {
        // 1. Check if user is globally trusted (bypass all checks)
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId);
        var chat = await _managedChatsRepository.GetByChatIdAsync(chatId);

        if (user?.IsTrusted == true)
        {
            _logger.LogDebug("{User} is trusted, skipping impersonation check", user.ToLogDebug());
            return false;
        }

        // 2. Check if user has pending alert (avoid duplicate checks)
        var hasPendingAlert = await _reportsRepository.HasPendingImpersonationAlertAsync(userId);
        if (hasPendingAlert)
        {
            _logger.LogDebug("{User} already has pending impersonation alert", user.ToLogDebug());
            return false;
        }

        // 3. Check message count (only check first N messages per chat)
        var messageCount = await _messageHistoryRepository.GetMessageCountAsync(userId, chatId);
        var config = await _configService.GetEffectiveAsync<ContentDetectionConfig>(ConfigType.ContentDetection, chatId)
                     ?? new ContentDetectionConfig();

        var threshold = config.FirstMessagesCount;
        if (messageCount >= threshold)
        {
            _logger.LogDebug(
                "{User} has {MessageCount} messages in {Chat}, threshold {Threshold}, skipping check",
                user.ToLogDebug(), messageCount, chat.ToLogDebug(), threshold);
            return false;
        }

        _logger.LogDebug(
            "{User} should be checked for impersonation (messages: {MessageCount}/{Threshold})",
            user.ToLogDebug(), messageCount, threshold);
        return true;
    }

    /// <inheritdoc/>
    public async Task<ImpersonationCheckResult?> CheckUserAsync(
        User user,
        Chat chat,
        string? photoPath)
    {
        _logger.LogDebug(
            "Checking {User} for impersonation in {Chat}",
            user.ToLogDebug(), chat.ToLogDebug());

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
            if (admin.TelegramUserId == user.Id)
                continue;

            int score = 0;
            bool nameMatch = false;
            bool photoMatch = false;
            double? photoSimilarity = null;

            // Check name similarity (50 points)
            if (!string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(admin.FirstName))
            {
                var nameSimilarity = StringUtilities.CalculateNameSimilarity(
                    user.FirstName, user.LastName,
                    admin.FirstName, admin.LastName);

                if (nameSimilarity >= NameSimilarityThreshold)
                {
                    nameMatch = true;
                    score += 50;
                    _logger.LogDebug(
                        "Name match detected: {User} vs Admin {Admin} (similarity: {Similarity:F2})",
                        user.ToLogDebug(), admin.ToLogDebug(), nameSimilarity);
                }
            }

            // Check photo similarity (50 points)
            var (isPhotoMatch, photoSim) = await ComparePhotosAsync(photoPath, admin.UserPhotoPath);
            photoSimilarity = photoSim;

            if (isPhotoMatch)
            {
                photoMatch = true;
                score += 50;
                _logger.LogDebug(
                    "Photo match detected: {User} vs Admin {Admin} (similarity: {Similarity:F2})",
                    user.ToLogDebug(), admin.ToLogDebug(), photoSim);
            }

            // Track highest scoring match
            if (score > highestScore)
            {
                highestScore = score;
                bestMatch = new ImpersonationCheckResult
                {
                    TotalScore = score,
                    RiskLevel = score >= 100 ? ImpersonationRiskLevel.Critical : ImpersonationRiskLevel.Medium,
                    SuspectedUser = user,
                    DetectionChat = chat,
                    TargetUserId = admin.TelegramUserId,
                    TargetEntityType = ProtectedEntityType.User,
                    TargetEntityId = admin.TelegramUserId,
                    TargetEntityName = $"{admin.FirstName} {admin.LastName}".Trim(),
                    NameMatch = nameMatch,
                    PhotoMatch = photoMatch,
                    PhotoSimilarityScore = photoSimilarity
                };
            }
        }

        // Also check against protected entities (chat names and linked channels)
        bestMatch = await CheckUserAgainstProtectedEntitiesAsync(
            user, chat, photoPath, highestScore, bestMatch);

        // Only return if score >= 50 (at least one attribute matched)
        if (bestMatch != null && bestMatch.TotalScore >= 50)
        {
            var targetDescription = bestMatch.TargetEntityType == ProtectedEntityType.User
                ? $"Admin {bestMatch.TargetUserId}"
                : $"{bestMatch.TargetEntityType} '{bestMatch.TargetEntityName}' ({bestMatch.TargetEntityId})";

            _logger.LogWarning(
                "Impersonation detected: {User} vs {TargetDescription} in {Chat} (score: {Score}, risk: {Risk})",
                user.ToLogDebug(), targetDescription, chat.ToLogDebug(), bestMatch.TotalScore, bestMatch.RiskLevel);
            return bestMatch;
        }

        _logger.LogDebug("No impersonation detected for {User} in {Chat}", user.ToLogDebug(), chat.ToLogDebug());
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
                SuspectedUserId = result.SuspectedUser.Id,
                TargetUserId = result.TargetUserId,
                ChatId = result.DetectionChat.Id,
                TotalScore = result.TotalScore,
                RiskLevel = result.RiskLevel,
                NameMatch = result.NameMatch,
                PhotoMatch = result.PhotoMatch,
                PhotoSimilarityScore = result.PhotoSimilarityScore,
                DetectedAt = DateTimeOffset.UtcNow,
                AutoBanned = result.ShouldAutoBan
            };

            var alertId = await _reportsRepository.InsertImpersonationAlertAsync(alert);

            _logger.LogInformation(
                "Created impersonation alert #{AlertId}: {SuspectedUser} → Admin ({TargetUserId}) (score: {Score})",
                alertId, result.SuspectedUser.ToLogInfo(), result.TargetUserId, result.TotalScore);

            // 2. Auto-ban if score >= 100 (high confidence)
            if (result.ShouldAutoBan)
            {
                var reason = $"Auto-banned: Impersonation detected (name match: {result.NameMatch}, photo match: {result.PhotoMatch}, score: {result.TotalScore})";

                var executor = Core.Models.Actor.Impersonation;
                var banResult = await _moderationActionService.BanUserAsync(
                    new BanIntent
                    {
                        User = UserIdentity.From(result.SuspectedUser),
                        Executor = executor,
                        Reason = reason,
                        Chat = ChatIdentity.From(result.DetectionChat) // Enables ban celebration
                    });

                if (banResult.Success)
                {
                    _logger.LogWarning(
                        "Auto-banned {User} for impersonation (score: {Score}, chats affected: {ChatsAffected})",
                        result.SuspectedUser.ToLogDebug(), result.TotalScore, banResult.ChatsAffected);
                }
                else
                {
                    _logger.LogError(
                        "Failed to auto-ban {User} for impersonation: {Error}",
                        result.SuspectedUser.ToLogDebug(), banResult.ErrorMessage);
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
                "Failed to execute impersonation action for {User}",
                result.SuspectedUser.ToLogDebug());
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
    /// Internal data structure for admin user data
    /// </summary>
    private class AdminUserData
    {
        public long TelegramUserId { get; set; }
        public long ChatId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? UserPhotoPath { get; set; }

        // Computed log formatting (matches extension method pattern)
        public string ToLogInfo()
            => LogDisplayName.UserInfo(FirstName, LastName, null, TelegramUserId);

        public string ToLogDebug()
            => LogDisplayName.UserDebug(FirstName, LastName, null, TelegramUserId);
    }

    /// <summary>
    /// Get protected entities (chat names and linked channels) for impersonation detection.
    /// </summary>
    private async Task<List<ProtectedEntity>> GetProtectedEntitiesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = new List<ProtectedEntity>();

        // Get chat names from managed chats
        var chatEntities = await context.ManagedChats
            .Where(mc => mc.IsActive && !string.IsNullOrEmpty(mc.ChatName))
            .Select(mc => new ProtectedEntity
            {
                Type = ProtectedEntityType.Chat,
                Id = mc.ChatId,
                Name = mc.ChatName!,
                PhotoHash = null // Chat photos not hashed yet
            })
            .AsNoTracking()
            .ToListAsync();

        entities.AddRange(chatEntities);

        // Get linked channels
        var channelEntities = await context.LinkedChannels
            .Where(lc => !string.IsNullOrEmpty(lc.ChannelName))
            .Select(lc => new ProtectedEntity
            {
                Type = ProtectedEntityType.Channel,
                Id = lc.ChannelId,
                Name = lc.ChannelName!,
                PhotoHash = lc.PhotoHash
            })
            .AsNoTracking()
            .ToListAsync();

        entities.AddRange(channelEntities);

        _logger.LogDebug(
            "Found {ChatCount} chat names and {ChannelCount} linked channels for impersonation protection",
            chatEntities.Count,
            channelEntities.Count);

        return entities;
    }

    /// <summary>
    /// Check user against protected entities (chat names and linked channels)
    /// </summary>
    private async Task<ImpersonationCheckResult?> CheckUserAgainstProtectedEntitiesAsync(
        User user,
        Chat chat,
        string? photoPath,
        int currentHighestScore,
        ImpersonationCheckResult? currentBestMatch)
    {
        var protectedEntities = await GetProtectedEntitiesAsync();

        if (protectedEntities.Count == 0)
        {
            return currentBestMatch;
        }

        var bestMatch = currentBestMatch;
        var highestScore = currentHighestScore;

        // Pre-compute user's photo hash ONCE before the loop to avoid N+1 (computed per entity with photo)
        byte[]? userPhotoHash = null;
        if (!string.IsNullOrWhiteSpace(photoPath) && protectedEntities.Any(e => e.PhotoHash != null))
        {
            try
            {
                userPhotoHash = await _photoHashService.ComputePhotoHashAsync(photoPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute photo hash for {User}", user.ToLogDebug());
            }
        }

        foreach (var entity in protectedEntities)
        {
            int score = 0;
            bool nameMatch = false;
            bool photoMatch = false;
            double? photoSimilarity = null;

            // Check name similarity against entity name (50 points)
            // Compare user's first name + last name against entity name
            if (!string.IsNullOrWhiteSpace(user.FirstName))
            {
                var userName = $"{user.FirstName} {user.LastName}".Trim();
                var similarity = StringUtilities.CalculateStringSimilarity(userName, entity.Name);

                if (similarity >= NameSimilarityThreshold)
                {
                    nameMatch = true;
                    score += 50;
                    _logger.LogDebug(
                        "Name match detected: {User} vs {EntityType} '{EntityName}' (similarity: {Similarity:F2})",
                        user.ToLogDebug(), entity.Type, entity.Name, similarity);
                }
            }

            // Check photo similarity against entity photo (50 points) - channels only
            // Uses pre-computed userPhotoHash to avoid N+1 hash computations
            var (isPhotoMatch, photoSim) = ComparePhotoHashes(userPhotoHash, entity.PhotoHash);
            photoSimilarity = photoSim;

            if (isPhotoMatch)
            {
                photoMatch = true;
                score += 50;
                _logger.LogDebug(
                    "Photo match detected: {User} vs {EntityType} ({EntityId}) (similarity: {Similarity:F2})",
                    user.ToLogDebug(), entity.Type, entity.Id, photoSim);
            }

            // Track highest scoring match
            if (score > highestScore)
            {
                highestScore = score;
                bestMatch = new ImpersonationCheckResult
                {
                    TotalScore = score,
                    RiskLevel = score >= 100 ? ImpersonationRiskLevel.Critical : ImpersonationRiskLevel.Medium,
                    SuspectedUser = user,
                    DetectionChat = chat,
                    TargetUserId = 0, // Not a user
                    TargetEntityType = entity.Type,
                    TargetEntityId = entity.Id,
                    TargetEntityName = entity.Name,
                    NameMatch = nameMatch,
                    PhotoMatch = photoMatch,
                    PhotoSimilarityScore = photoSimilarity
                };
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Internal data structure for protected entity (chat or channel)
    /// </summary>
    private class ProtectedEntity
    {
        public ProtectedEntityType Type { get; init; }
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public byte[]? PhotoHash { get; init; }
    }

    /// <summary>
    /// Compare two photos by computing and comparing their perceptual hashes.
    /// </summary>
    /// <param name="userPhotoPath">Path to the user's photo</param>
    /// <param name="targetPhotoPath">Path to the target's photo</param>
    /// <param name="precomputedUserHash">Optional pre-computed user hash to avoid N+1</param>
    /// <returns>Tuple of (isMatch, similarity score)</returns>
    private async Task<(bool isMatch, double? similarity)> ComparePhotosAsync(
        string? userPhotoPath,
        string? targetPhotoPath,
        byte[]? precomputedUserHash = null)
    {
        if (string.IsNullOrWhiteSpace(userPhotoPath) || string.IsNullOrWhiteSpace(targetPhotoPath))
            return (false, null);

        try
        {
            var userHash = precomputedUserHash ?? await _photoHashService.ComputePhotoHashAsync(userPhotoPath);
            var targetHash = await _photoHashService.ComputePhotoHashAsync(targetPhotoPath);

            if (userHash == null || targetHash == null)
                return (false, null);

            var similarity = _photoHashService.CompareHashes(userHash, targetHash);
            return (similarity >= PhotoSimilarityThreshold, similarity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compare photos: {UserPhoto} vs {TargetPhoto}", userPhotoPath, targetPhotoPath);
            return (false, null);
        }
    }

    /// <summary>
    /// Compare a pre-computed user hash against an entity's stored hash.
    /// </summary>
    private (bool isMatch, double? similarity) ComparePhotoHashes(byte[]? userHash, byte[]? entityHash)
    {
        if (userHash == null || entityHash == null)
            return (false, null);

        var similarity = _photoHashService.CompareHashes(userHash, entityHash);
        return (similarity >= PhotoSimilarityThreshold, similarity);
    }
}
