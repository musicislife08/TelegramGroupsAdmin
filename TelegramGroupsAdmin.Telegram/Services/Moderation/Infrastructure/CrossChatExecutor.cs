using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

/// <summary>
/// Executes actions across all healthy managed chats with rate limiting.
/// Uses SemaphoreSlim(3) to respect Telegram rate limits.
/// </summary>
public class CrossChatExecutor : ICrossChatExecutor
{
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly IChatManagementService _chatManagementService;
    private readonly ILogger<CrossChatExecutor> _logger;

    private const int MaxConcurrentApiCalls = 3;

    public CrossChatExecutor(
        ITelegramBotClientFactory botClientFactory,
        IManagedChatsRepository managedChatsRepository,
        IChatManagementService chatManagementService,
        ILogger<CrossChatExecutor> logger)
    {
        _botClientFactory = botClientFactory;
        _managedChatsRepository = managedChatsRepository;
        _chatManagementService = chatManagementService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CrossChatResult> ExecuteAcrossChatsAsync(
        Func<ITelegramOperations, long, CancellationToken, Task> action,
        string actionName,
        CancellationToken cancellationToken = default)
    {
        var operations = await _botClientFactory.GetOperationsAsync();
        var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken: cancellationToken);
        var activeChatIds = allChats
            .Where(c => c.IsActive && !c.IsDeleted)
            .Select(c => c.ChatId)
            .ToList();

        // Health gate: Filter for chats where bot has confirmed permissions
        var actionableChatIds = _chatManagementService.FilterHealthyChats(activeChatIds);

        // Log chats skipped due to health issues
        var skippedChats = activeChatIds.Except(actionableChatIds).ToList();
        if (skippedChats.Count > 0)
        {
            _logger.LogWarning(
                "Skipping {Count} unhealthy chats for {ActionName} action: {ChatIds}. " +
                "Bot lacks required permissions (admin + ban members) in these chats.",
                skippedChats.Count,
                actionName,
                string.Join(", ", skippedChats));
        }

        // Parallel execution with concurrency limit (respects Telegram rate limits)
        using var semaphore = new SemaphoreSlim(MaxConcurrentApiCalls);
        var tasks = actionableChatIds.Select(async chatId =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await action(operations, chatId, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to execute {ActionName} in chat {ChatId}", actionName, chatId);
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(success => success);
        var failCount = results.Count(success => !success);

        _logger.LogInformation(
            "Cross-chat {ActionName} completed: {Success} succeeded, {Failed} failed, {Skipped} skipped",
            actionName, successCount, failCount, skippedChats.Count);

        return new CrossChatResult(successCount, failCount, skippedChats.Count);
    }
}
