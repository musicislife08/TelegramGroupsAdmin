using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.RateLimiting;
using TgSpam_PreFilterApi;
using TgSpam_PreFilterApi.Configuration;
using TgSpam_PreFilterApi.Data;
using TgSpam_PreFilterApi.Services.BackgroundServices;
using TgSpam_PreFilterApi.Services.Telegram;
using TgSpam_PreFilterApi.Services.Vision;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHybridCache(options =>
{
    options.MaximumPayloadBytes = 10 * 1024 * 1024; // 10 MB
});

builder.Services.AddHttpClient<SeoPreviewScraper>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; SeoPreviewScraper/1.0)");
});

var limiter = PartitionedRateLimiter.Create<HttpRequestMessage, string>(_ =>
    RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: "virustotal",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 4,                    // 4 requests
            Window = TimeSpan.FromMinutes(1),   // per minute
            SegmentsPerWindow = 1,              // 1 segment is fine here
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        }));

var limiterOptions = new RateLimiterStrategyOptions
{
    RateLimiter = async args =>
    {
        var request = args.Context.GetRequestMessage();
        if (request is null)
        {
            return RejectedRateLimitLease.Instance;
        }

        var lease = await limiter.AcquireAsync(
            request,
            permitCount: 1,
            cancellationToken: args.Context.CancellationToken);

        return lease.IsAcquired ? lease : RejectedRateLimitLease.Instance;
    },

    OnRejected = static _ =>
    {
        Console.WriteLine("Rate limit hit for VirusTotal.");
        return ValueTask.CompletedTask;
    }
};


builder.Services.AddHttpClient<IThreatIntelService, VirusTotalService>(options =>
    {
        options.BaseAddress = new Uri("https://www.virustotal.com/api/v3/");
        options.DefaultRequestHeaders.Add("x-apikey", Environment.GetEnvironmentVariable("VIRUSTOTAL_API_KEY")
                                                      ?? throw new InvalidOperationException("VIRUSTOTAL_API_KEY not set"));
    })
    .AddResilienceHandler("virustotal", resiliencePipelineBuilder =>
    {
        resiliencePipelineBuilder.AddRateLimiter(limiterOptions);
    });

// HttpClient factory
builder.Services.AddHttpClient();

// Configuration from environment variables
builder.Services.Configure<OpenAIOptions>(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
    options.Model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
    options.MaxTokens = int.TryParse(Environment.GetEnvironmentVariable("OPENAI_MAX_TOKENS"), out var maxTokens) ? maxTokens : 500;
});

builder.Services.Configure<TelegramOptions>(options =>
{
    options.HistoryBotToken = Environment.GetEnvironmentVariable("TELEGRAM_HISTORY_BOT_TOKEN") ?? "";
    options.ChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? "";
});

builder.Services.Configure<MessageHistoryOptions>(options =>
{
    options.DatabasePath = Environment.GetEnvironmentVariable("MESSAGE_HISTORY_DATABASE_PATH") ?? "/data/message_history.db";
    options.RetentionHours = int.TryParse(Environment.GetEnvironmentVariable("MESSAGE_HISTORY_RETENTION_HOURS"), out var hours) ? hours : 24;
    options.CleanupIntervalMinutes = int.TryParse(Environment.GetEnvironmentVariable("MESSAGE_HISTORY_CLEANUP_INTERVAL_MINUTES"), out var minutes) ? minutes : 5;
});

builder.Services.Configure<SpamDetectionOptions>(options =>
{
    options.TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("SPAM_DETECTION_TIMEOUT_SECONDS"), out var timeout) ? timeout : 30;
    options.ImageLookupRetryDelayMs = int.TryParse(Environment.GetEnvironmentVariable("SPAM_DETECTION_RETRY_DELAY_MS"), out var delay) ? delay : 100;
    options.MinConfidenceThreshold = int.TryParse(Environment.GetEnvironmentVariable("SPAM_DETECTION_MIN_CONFIDENCE"), out var confidence) ? confidence : 85;
});

// Database
builder.Services.AddSingleton<MessageHistoryRepository>();

// Telegram services
builder.Services.AddSingleton<TelegramBotClientFactory>();
builder.Services.AddScoped<ITelegramImageService, TelegramImageService>();

// Background services
builder.Services.AddHostedService<HistoryBotService>();
builder.Services.AddHostedService<CleanupBackgroundService>();

// OpenAI Vision
builder.Services.AddHttpClient<IVisionSpamDetectionService, OpenAIVisionSpamDetectionService>();

// Register blocklist service
builder.Services.AddScoped<SpamCheckService>();

var app = builder.Build();

// Initialize database
var repository = app.Services.GetRequiredService<MessageHistoryRepository>();
await repository.InitializeDatabaseAsync();

app.MapPost("/check", async (
    SpamCheckRequest request,
    SpamCheckService textSpamService,
    MessageHistoryRepository historyRepo,
    ITelegramImageService imageService,
    IVisionSpamDetectionService visionService,
    IOptions<SpamDetectionOptions> spamOptions,
    ILogger<Program> logger) =>
{
    // Check for image spam
    if (request.ImageCount > 0)
    {
        logger.LogInformation("Processing image spam check for user {UserId} in chat {ChatId}", request.UserId, request.ChatId);

        // Try to find user's recent photo
        var photoMessage = await historyRepo.GetUserRecentPhotoAsync(long.Parse(request.UserId), long.Parse(request.ChatId));

        // Retry once if not found (race condition handling)
        if (photoMessage == null)
        {
            logger.LogDebug("Photo not found on first attempt, retrying after {Delay}ms", spamOptions.Value.ImageLookupRetryDelayMs);
            await Task.Delay(spamOptions.Value.ImageLookupRetryDelayMs);
            photoMessage = await historyRepo.GetUserRecentPhotoAsync(long.Parse(request.UserId), long.Parse(request.ChatId));
        }

        if (photoMessage == null)
        {
            logger.LogWarning("No recent photo found for user {UserId} in chat {ChatId}", request.UserId, request.ChatId);
            return Results.Ok(new CheckResult(false, "No recent image found", 0));
        }

        // Download image
        var imageStream = await imageService.DownloadPhotoAsync(photoMessage.FileId);
        if (imageStream == null)
        {
            logger.LogError("Failed to download photo {FileId} for user {UserId}", photoMessage.FileId, request.UserId);
            return Results.Ok(new CheckResult(false, "Failed to download image", 0));
        }

        // Analyze with OpenAI Vision
        var result = await visionService.AnalyzeImageAsync(
            imageStream,
            request.Message ?? photoMessage.MessageText);

        logger.LogInformation(
            "Image spam check complete for user {UserId}: Spam={Spam}, Confidence={Confidence}",
            request.UserId,
            result.Spam,
            result.Confidence);

        return Results.Ok(result);
    }

    // Check for text spam (existing logic)
    if (!string.IsNullOrWhiteSpace(request.Message))
    {
        logger.LogInformation("Processing text spam check");
        var result = await textSpamService.CheckMessageAsync(request.Message);
        return Results.Ok(result);
    }

    // Empty message
    return Results.Ok(new CheckResult(false, "Empty message", 0));
});

// Health check endpoint
app.MapGet("/health", async (MessageHistoryRepository historyRepo) =>
{
    var stats = await historyRepo.GetStatsAsync();
    return Results.Ok(new
    {
        status = "healthy",
        historyBot = new
        {
            totalMessages = stats.TotalMessages,
            totalUsers = stats.UniqueUsers,
            messagesWithPhotos = stats.PhotoCount,
            oldestMessage = stats.OldestTimestamp.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(stats.OldestTimestamp.Value).ToString("g")
                : null,
            newestMessage = stats.NewestTimestamp.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(stats.NewestTimestamp.Value).ToString("g")
                : null
        }
    });
});

app.Run();


file sealed class RejectedRateLimitLease : RateLimitLease
{
    public static readonly RejectedRateLimitLease Instance = new();

    public override bool IsAcquired => false;
    public override IEnumerable<string> MetadataNames => [];
    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        metadata = null;
        return false;
    }
}