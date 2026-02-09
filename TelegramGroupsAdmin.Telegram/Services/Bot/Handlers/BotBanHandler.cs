using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using static TelegramGroupsAdmin.Core.BackgroundJobs.DeduplicationKeys;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for ban operations (ban, temp-ban, unban, kick).
/// Handles Telegram API calls across all managed chats and database updates.
/// Does NOT know about trust, warnings, or notifications (orchestrator composes those).
/// This is the ONLY layer that should touch ITelegramBotClientFactory for ban operations.
/// </summary>
public class BotBanHandler : IBotBanHandler
{
    private readonly IBotChatService _chatService;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly IJobScheduler _jobScheduler;
    private readonly ITelegramUserRepository _userRepository;
    private readonly ILogger<BotBanHandler> _logger;

    public BotBanHandler(
        IBotChatService chatService,
        ITelegramBotClientFactory botClientFactory,
        IJobScheduler jobScheduler,
        ITelegramUserRepository userRepository,
        ILogger<BotBanHandler> logger)
    {
        _chatService = chatService;
        _botClientFactory = botClientFactory;
        _jobScheduler = jobScheduler;
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BanResult> BanAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Executing ban for user {User} by {Executor}",
            user.ToLogDebug(), executor.GetDisplayText());

        try
        {
            var apiClient = await _botClientFactory.GetApiClientAsync();
            var healthyChatIds = _chatService.GetHealthyChatIds();

            var successCount = 0;
            var failCount = 0;

            await Parallel.ForEachAsync(healthyChatIds, cancellationToken, async (chatId, ct) =>
            {
                try
                {
                    await apiClient.BanChatMemberAsync(chatId, user.Id, ct: ct);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ban user {UserId} in chat {ChatId}", user.Id, chatId);
                    Interlocked.Increment(ref failCount);
                }
            });

            // Update source of truth: is_banned column on telegram_users
            await _userRepository.SetBanStatusAsync(user.Id, isBanned: true, expiresAt: null, cancellationToken);

            _logger.LogInformation(
                "Ban completed for {User}: {Success} succeeded, {Failed} failed",
                user.ToLogInfo(), successCount, failCount);

            return BanResult.Succeeded(successCount, failCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute ban for user {User}", user.ToLogDebug());
            return BanResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<BanResult> BanInChatAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Executing single-chat ban for {User} in {Chat} by {Executor}",
            user.ToLogDebug(), chat.ToLogDebug(), executor.GetDisplayText());

        try
        {
            var apiClient = await _botClientFactory.GetApiClientAsync();
            await apiClient.BanChatMemberAsync(chat.Id, user.Id, ct: cancellationToken);

            // Ensure global ban status is set (idempotent if already banned)
            await _userRepository.SetBanStatusAsync(user.Id, isBanned: true, expiresAt: null, cancellationToken);

            _logger.LogInformation(
                "Single-chat ban completed for {User} in {Chat} (lazy sync)",
                user.ToLogInfo(), chat.ToLogInfo());

            return BanResult.Succeeded(chatsAffected: 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute single-chat ban for {User} in {Chat}",
                user.ToLogDebug(), chat.ToLogDebug());
            return BanResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<TempBanResult> TempBanAsync(
        UserIdentity user,
        Actor executor,
        TimeSpan duration,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Executing temp ban for user {User} for {Duration} by {Executor}",
            user.ToLogDebug(), duration, executor.GetDisplayText());

        try
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);

            // Ban globally (permanent in Telegram, lifted by background job)
            var apiClient = await _botClientFactory.GetApiClientAsync();
            var healthyChatIds = _chatService.GetHealthyChatIds();

            var successCount = 0;
            var failCount = 0;

            await Parallel.ForEachAsync(healthyChatIds, cancellationToken, async (chatId, ct) =>
            {
                try
                {
                    await apiClient.BanChatMemberAsync(chatId, user.Id, ct: ct);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to temp ban user {UserId} in chat {ChatId}", user.Id, chatId);
                    Interlocked.Increment(ref failCount);
                }
            });

            // Update source of truth: is_banned column with expiry
            await _userRepository.SetBanStatusAsync(user.Id, isBanned: true, expiresAt: expiresAt, cancellationToken);

            // Schedule automatic unban via Quartz.NET
            var payload = new TempbanExpiryJobPayload(
                User: user,
                Reason: reason ?? "Temporary ban",
                ExpiresAt: expiresAt);

            var delaySeconds = (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            var jobId = await _jobScheduler.ScheduleJobAsync(
                "TempbanExpiry",
                payload,
                delaySeconds: delaySeconds,
                deduplicationKey: None,
                cancellationToken);

            _logger.LogInformation(
                "Temp ban completed for {User}: {Success} succeeded, {Failed} failed. " +
                "Expires at {ExpiresAt} (JobId: {JobId})",
                user.ToLogInfo(), successCount, failCount, expiresAt, jobId);

            return TempBanResult.Succeeded(successCount, expiresAt, failCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute temp ban for user {User}", user.ToLogDebug());
            return TempBanResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<UnbanResult> UnbanAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Executing unban for user {User} by {Executor}",
            user.ToLogDebug(), executor.GetDisplayText());

        try
        {
            // Unban from all Telegram chats
            var apiClient = await _botClientFactory.GetApiClientAsync();
            var healthyChatIds = _chatService.GetHealthyChatIds();

            var successCount = 0;
            var failCount = 0;

            await Parallel.ForEachAsync(healthyChatIds, cancellationToken, async (chatId, ct) =>
            {
                try
                {
                    await apiClient.UnbanChatMemberAsync(chatId, user.Id, onlyIfBanned: true, ct: ct);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unban user {UserId} in chat {ChatId}", user.Id, chatId);
                    Interlocked.Increment(ref failCount);
                }
            });

            // Update source of truth: clear is_banned flag
            await _userRepository.SetBanStatusAsync(user.Id, isBanned: false, expiresAt: null, cancellationToken);

            _logger.LogInformation(
                "Unban completed for {User}: {Success} succeeded, {Failed} failed",
                user.ToLogInfo(), successCount, failCount);

            return UnbanResult.Succeeded(successCount, failCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute unban for user {User}", user.ToLogDebug());
            return UnbanResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<BanResult> KickFromChatAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Executing kick for user {User} from chat {Chat} by {Executor}",
            user.ToLogDebug(), chat.ToLogDebug(), executor.GetDisplayText());

        try
        {
            var apiClient = await _botClientFactory.GetApiClientAsync();

            // Ban then immediately unban (removes user from chat without permanent ban)
            await apiClient.BanChatMemberAsync(chat.Id, user.Id, ct: cancellationToken);
            await apiClient.UnbanChatMemberAsync(chat.Id, user.Id, onlyIfBanned: true, ct: cancellationToken);

            _logger.LogInformation(
                "Kicked {User} from chat {Chat}",
                user.ToLogInfo(), chat.ToLogInfo());

            // NOTE: No database state change - kick is a one-time action, not a persistent ban
            return BanResult.Succeeded(chatsAffected: 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kick user {User} from chat {Chat}",
                user.ToLogDebug(), chat.ToLogDebug());
            return BanResult.Failed(ex.Message);
        }
    }
}
