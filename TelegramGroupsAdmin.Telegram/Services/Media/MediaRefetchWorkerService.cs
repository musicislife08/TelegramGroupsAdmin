using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.Telegram.Services.Media;

/// <summary>
/// Background service with 4 fixed workers processing media refetch queue
/// Workers download media/photos from Telegram and notify UI components via SignalR events
/// </summary>
public class MediaRefetchWorkerService : BackgroundService
{
    private readonly MediaRefetchQueueService _queueService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessageProcessingService _messageProcessingService;
    private readonly TelegramBotClientFactory _botClientFactory;
    private readonly TelegramConfigLoader _configLoader;
    private readonly ILogger<MediaRefetchWorkerService> _logger;

    public MediaRefetchWorkerService(
        IMediaRefetchQueueService queueService,
        IServiceScopeFactory scopeFactory,
        MessageProcessingService messageProcessingService,
        TelegramBotClientFactory botClientFactory,
        TelegramConfigLoader configLoader,
        ILogger<MediaRefetchWorkerService> logger)
    {
        _queueService = (MediaRefetchQueueService)queueService;
        _scopeFactory = scopeFactory;
        _messageProcessingService = messageProcessingService;
        _botClientFactory = botClientFactory;
        _configLoader = configLoader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MediaRefetchWorkerService started with 4 workers");

        // Start 4 fixed workers
        var workers = Enumerable.Range(0, 4)
            .Select(workerId => ProcessQueueAsync(workerId, stoppingToken))
            .ToArray();

        await Task.WhenAll(workers);

        _logger.LogInformation("MediaRefetchWorkerService stopped");
    }

    private async Task ProcessQueueAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker {WorkerId} started", workerId);

        try
        {
            await foreach (var request in _queueService.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (request.Type == RefetchType.Media)
                    {
                        await ProcessMediaRequestAsync(request, workerId);
                    }
                    else
                    {
                        await ProcessUserPhotoRequestAsync(request, workerId);
                    }

                    // Mark completed and cleanup deduplication tracking
                    _queueService.MarkCompleted(request);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker {WorkerId} failed to process request: {Request}", workerId, request);
                    // Still mark as completed to prevent infinite retry
                    _queueService.MarkCompleted(request);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker {WorkerId} cancelled", workerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker {WorkerId} encountered fatal error", workerId);
            throw;
        }
    }

    private async Task ProcessMediaRequestAsync(RefetchRequest request, int workerId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var messageRepo = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
        var mediaService = scope.ServiceProvider.GetRequiredService<TelegramMediaService>();

        _logger.LogInformation("Worker {WorkerId} refetching media: message {MessageId} type {MediaType}",
            workerId, request.MessageId, request.MediaType);

        // Get message data from database
        var message = await messageRepo.GetMessageAsync(request.MessageId);
        if (message == null)
        {
            _logger.LogWarning("Message {MessageId} not found in database", request.MessageId);
            return;
        }

        if (string.IsNullOrEmpty(message.MediaFileId))
        {
            _logger.LogWarning("Message {MessageId} has no media_file_id", request.MessageId);
            return;
        }

        // Download media from Telegram
        var localPath = await mediaService.DownloadAndSaveMediaAsync(
            message.MediaFileId,
            request.MediaType!.Value,
            message.MediaFileName,
            message.ChatId,
            message.MessageId);

        if (localPath != null)
        {
            // Update message with local path
            await messageRepo.UpdateMediaLocalPathAsync(request.MessageId, localPath);

            _logger.LogInformation("Worker {WorkerId} completed media refetch: {LocalPath}", workerId, localPath);

            // Notify UI components via SignalR event (triggers Blazor re-render)
            _messageProcessingService.RaiseMediaUpdated(request.MessageId, request.MediaType!.Value);
        }
    }

    private async Task ProcessUserPhotoRequestAsync(RefetchRequest request, int workerId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var photoService = scope.ServiceProvider.GetRequiredService<TelegramPhotoService>();

        _logger.LogInformation("Worker {WorkerId} refetching user photo: user {UserId}", workerId, request.UserId);

        // Load bot config from database
        var botToken = await _configLoader.LoadConfigAsync();

        // Get singleton bot client from factory (same instance used by TelegramAdminBotService)
        var botClient = _botClientFactory.GetOrCreate(botToken);

        // Get user's current file_unique_id from database
        var user = await userRepo.GetByIdAsync(request.UserId!.Value);
        var knownPhotoId = user?.PhotoFileUniqueId;

        // Download photo (will check if changed)
        var result = await photoService.GetUserPhotoWithMetadataAsync(
            botClient,
            request.UserId!.Value,
            knownPhotoId);

        if (result != null)
        {
            // Update telegram_users with new file_unique_id
            await userRepo.UpdatePhotoFileUniqueIdAsync(
                request.UserId!.Value,
                result.FileUniqueId,
                result.RelativePath);

            _logger.LogInformation("Worker {WorkerId} completed user photo refetch: {UserId}", workerId, request.UserId);

            // User photo updates don't have a UI event yet (would need OnUserPhotoUpdated event)
            // For now, users refresh page to see updated photos
        }
    }
}
