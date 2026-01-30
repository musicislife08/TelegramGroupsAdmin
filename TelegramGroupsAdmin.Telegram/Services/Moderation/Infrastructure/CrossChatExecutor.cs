using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

/// <summary>
/// Executes actions across all healthy managed chats in parallel.
/// Pure infrastructure - callers provide the action to perform per chat.
/// Rate limiting is handled by the Telegram.Bot library internally.
/// </summary>
public class CrossChatExecutor : ICrossChatExecutor
{
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly IBotChatHealthService _chatHealthService;
    private readonly ILogger<CrossChatExecutor> _logger;

    public CrossChatExecutor(
        IManagedChatsRepository managedChatsRepository,
        IBotChatHealthService chatHealthService,
        ILogger<CrossChatExecutor> logger)
    {
        _managedChatsRepository = managedChatsRepository;
        _chatHealthService = chatHealthService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CrossChatResult> ExecuteAcrossChatsAsync(
        Func<long, CancellationToken, Task> action,
        string actionName,
        CancellationToken cancellationToken = default)
    {
        var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken: cancellationToken);
        var activeChatIds = allChats
            .Where(c => c.IsActive && !c.IsDeleted)
            .Select(c => c.ChatId)
            .ToList();

        // Health gate: Filter for chats where bot has confirmed permissions
        var actionableChatIds = _chatHealthService.FilterHealthyChats(activeChatIds);

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

        // Parallel execution - rate limiting handled by Telegram.Bot library
        var tasks = actionableChatIds.Select(async chatId =>
        {
            try
            {
                await action(chatId, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to execute {ActionName} in chat {ChatId}", actionName, chatId);
                return false;
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
