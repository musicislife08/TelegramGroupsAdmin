using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using Anthropic;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Metrics;

namespace TelegramGroupsAdmin.AI.Services;

/// <summary>
/// Implementation of IChatService using Microsoft.Extensions.AI (IChatClient).
/// Supports OpenAI, Azure OpenAI, and OpenAI-compatible local endpoints.
/// Clients use the OpenAI SDK's default shared transport
/// (<see cref="System.ClientModel.Primitives.HttpClientPipelineTransport.Shared"/>),
/// so the underlying HTTP handler is pooled across all clients automatically.
/// The static cache, keyed by connection + model + key, persists across scoped
/// instances for reuse. Evicted clients are disposed defensively
/// (IChatClient : IDisposable) — Dispose is a no-op for the OpenAI/Azure MEAI
/// clients today, but providers whose clients hold resources (e.g. the Anthropic
/// client added later) rely on it.
/// </summary>
public class ChatService : IChatService
{
    // Static cache - persists across scoped instances for client reuse.
    // Thread safety: ConcurrentDictionary + GetOrAdd provides atomic access.
    // Cache bounds: expected <10 entries (connections × models). Entries are
    // disposed on eviction via InvalidateCache().
    private static readonly ConcurrentDictionary<string, CachedClient> ClientCache = new();
    private readonly ISystemConfigRepository _configRepository;
    private readonly ILogger<ChatService> _logger;
    private readonly ApiMetrics _apiMetrics;
    private readonly CacheMetrics _cacheMetrics;

    public static int CachedClientCount => ClientCache.Count;

    public ChatService(
        ISystemConfigRepository configRepository,
        ILogger<ChatService> logger,
        ApiMetrics apiMetrics,
        CacheMetrics cacheMetrics)
    {
        _configRepository = configRepository;
        _logger = logger;
        _apiMetrics = apiMetrics;
        _cacheMetrics = cacheMetrics;
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lookupResult = await GetOrCreateClientAsync(feature, cancellationToken);
        if (lookupResult == null)
        {
            _logger.LogDebug("Feature {Feature} is not configured, skipping AI call", feature);
            return null;
        }

        var clientInfo = lookupResult.Client;
        var featureConfig = lookupResult.FeatureConfig;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var effectiveOptions = ApplyFeatureConfigDefaults(options, featureConfig);
            var chatOptions = CreateChatOptions(effectiveOptions);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            stopwatch.Stop();
            var result = CreateResult(response, clientInfo.ModelId);
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                result?.PromptTokens ?? 0,
                result?.CompletionTokens ?? 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: true);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                0, 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: false);
            _logger.LogError(ex, "Error getting chat completion from {Model} for feature {Feature}",
                clientInfo.ModelId, feature);
            throw; // Let caller handle the exception
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetVisionCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        byte[] imageData,
        string mimeType,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lookupResult = await GetOrCreateClientAsync(feature, cancellationToken);
        if (lookupResult == null)
        {
            _logger.LogDebug("Feature {Feature} is not configured, skipping AI vision call", feature);
            return null;
        }

        var clientInfo = lookupResult.Client;
        var featureConfig = lookupResult.FeatureConfig;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, [new TextContent(userPrompt), new DataContent(imageData, mimeType)])
            };

            var effectiveOptions = ApplyFeatureConfigDefaults(options, featureConfig);
            var chatOptions = CreateChatOptions(effectiveOptions);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            stopwatch.Stop();
            var result = CreateResult(response, clientInfo.ModelId);
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                result?.PromptTokens ?? 0,
                result?.CompletionTokens ?? 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: true);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                0, 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: false);
            _logger.LogError(ex, "Error getting vision completion from {Model} for feature {Feature}",
                clientInfo.ModelId, feature);
            throw; // Let caller handle the exception
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetVisionCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<ImageInput> images,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lookupResult = await GetOrCreateClientAsync(feature, cancellationToken);
        if (lookupResult == null)
        {
            _logger.LogDebug("Feature {Feature} is not configured, skipping AI multi-image vision call", feature);
            return null;
        }

        var clientInfo = lookupResult.Client;
        var featureConfig = lookupResult.FeatureConfig;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var contents = new List<AIContent> { new TextContent(userPrompt) };
            foreach (var image in images)
            {
                contents.Add(new DataContent(image.Data, image.MimeType));
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, contents)
            };

            var effectiveOptions = ApplyFeatureConfigDefaults(options, featureConfig);
            var chatOptions = CreateChatOptions(effectiveOptions);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            stopwatch.Stop();
            var result = CreateResult(response, clientInfo.ModelId);
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                result?.PromptTokens ?? 0,
                result?.CompletionTokens ?? 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: true);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _apiMetrics.RecordOpenAiCall(
                feature.ToString(),
                clientInfo.ModelId,
                0, 0,
                stopwatch.Elapsed.TotalMilliseconds,
                success: false);
            _logger.LogError(ex, "Error getting multi-image vision completion from {Model} for feature {Feature}",
                clientInfo.ModelId, feature);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsFeatureAvailableAsync(AIFeatureType feature, CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null) return false;

        if (!config.Features.TryGetValue(feature, out var featureConfig) || featureConfig.ConnectionId == null)
            return false;

        var connection = config.Connections.SingleOrDefault(c => c.Id == featureConfig.ConnectionId);
        if (connection == null || !connection.Enabled)
            return false;

        // Check API key for non-local providers
        if (connection.Provider != AIProviderType.OpenAICompatible || connection.LocalRequiresApiKey)
        {
            var apiKeys = await _configRepository.GetApiKeysAsync(cancellationToken);
            var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public void InvalidateCache(string? connectionId = null)
    {
        if (connectionId == null)
        {
            foreach (var entry in ClientCache.Values)
            {
                entry.Client.Dispose();
            }
            ClientCache.Clear();
            _logger.LogDebug("Cleared all cached AI chat clients");
        }
        else
        {
            // Remove all cache entries for this connection (keys are delimited with "|")
            var keysToRemove = ClientCache.Keys.Where(k => k.StartsWith(connectionId + "|")).ToList();
            foreach (var key in keysToRemove)
            {
                if (ClientCache.TryRemove(key, out var removed))
                {
                    removed.Client.Dispose();
                }
            }
            _logger.LogDebug("Invalidated cached AI chat client for connection {ConnectionId}", connectionId);
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> TestCompletionAsync(
        string connectionId,
        string model,
        string? azureDeploymentName,
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var clientInfo = await GetOrCreateTestClientAsync(connectionId, model, azureDeploymentName, cancellationToken);
        if (clientInfo == null)
        {
            _logger.LogDebug("Test client not available for connection {ConnectionId}, model {Model}",
                connectionId, model);
            return null;
        }

        try
        {
            _logger.LogDebug("Making test completion call to {Model}", clientInfo.ModelId);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var chatOptions = CreateChatOptions(options);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            _logger.LogDebug("MEAI Response - Text: '{Text}', ModelId: {ModelId}, FinishReason: {FinishReason}",
                response.Text.Length > QueryConstants.DefaultLogTruncationLength
                    ? response.Text[..QueryConstants.DefaultLogTruncationLength]
                    : response.Text,
                response.ModelId ?? "(null)",
                response.FinishReason?.ToString() ?? "(null)");

            var result = CreateResult(response, clientInfo.ModelId);
            _logger.LogDebug("Test completion returned: Content={HasContent}, Tokens={Tokens}",
                !string.IsNullOrEmpty(result?.Content), result?.TotalTokens);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test completion failed for {Model}: {Message}",
                clientInfo.ModelId, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> TestVisionCompletionAsync(
        string connectionId,
        string model,
        string? azureDeploymentName,
        string systemPrompt,
        string userPrompt,
        byte[] imageData,
        string mimeType,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var clientInfo = await GetOrCreateTestClientAsync(connectionId, model, azureDeploymentName, cancellationToken);
        if (clientInfo == null)
        {
            _logger.LogDebug("Test client not available for vision call, connection {ConnectionId}, model {Model}",
                connectionId, model);
            return null;
        }

        try
        {
            _logger.LogDebug("Making test vision call to {Model} with {ImageSize} bytes",
                clientInfo.ModelId, imageData.Length);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, [new TextContent(userPrompt), new DataContent(imageData, mimeType)])
            };

            var chatOptions = CreateChatOptions(options);

            var response = await clientInfo.Client.GetResponseAsync(messages, chatOptions, cancellationToken);

            var result = CreateResult(response, clientInfo.ModelId);
            _logger.LogDebug("Test vision returned: Content={HasContent}, Tokens={Tokens}",
                !string.IsNullOrEmpty(result?.Content), result?.TotalTokens);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test vision call failed for {Model}: {Message}",
                clientInfo.ModelId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Get or create a client for testing a specific connection+model combo.
    /// Does not use feature config - uses provided model/deployment directly.
    /// </summary>
    private async Task<CachedClient?> GetOrCreateTestClientAsync(
        string connectionId,
        string model,
        string? azureDeploymentName,
        CancellationToken cancellationToken)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null) return null;

        var connection = config.Connections.SingleOrDefault(c => c.Id == connectionId);
        if (connection == null || !connection.Enabled)
        {
            _logger.LogWarning("Test connection {ConnectionId} not found or disabled", connectionId);
            return null;
        }

        var apiKeys = await _configRepository.GetApiKeysAsync(cancellationToken);
        var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);

        if (connection.Provider != AIProviderType.OpenAICompatible || connection.LocalRequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("API key not configured for test connection {ConnectionId}", connection.Id);
                return null;
            }
        }

        var testFeatureConfig = new AIFeatureConfig
        {
            ConnectionId = connectionId,
            Model = model,
            AzureDeploymentName = azureDeploymentName
        };

        var cacheKey = GenerateCacheKey(connection, testFeatureConfig, apiKey);

        if (ClientCache.TryGetValue(cacheKey, out var cachedClient))
        {
            _cacheMetrics.RecordHit("chat_client");
            return cachedClient;
        }

        _cacheMetrics.RecordMiss("chat_client");
        cachedClient = ClientCache.GetOrAdd(cacheKey, _ =>
        {
            var client = BuildClient(connection, testFeatureConfig, apiKey);
            var modelId = connection.Provider == AIProviderType.AzureOpenAI
                ? azureDeploymentName ?? model
                : model;

            _logger.LogDebug("Created and cached test client for connection {ConnectionId}, model {Model}",
                connection.Id, modelId);

            return new CachedClient(client, modelId);
        });

        return cachedClient;
    }

    /// <summary>
    /// Get or create a cached IChatClient for the specified feature.
    /// </summary>
    private async Task<ClientLookupResult?> GetOrCreateClientAsync(AIFeatureType feature, CancellationToken cancellationToken)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null) return null;

        if (!config.Features.TryGetValue(feature, out var featureConfig) || featureConfig.ConnectionId == null)
            return null;

        var connection = config.Connections.SingleOrDefault(c => c.Id == featureConfig.ConnectionId);
        if (connection == null || !connection.Enabled)
            return null;

        var apiKeys = await _configRepository.GetApiKeysAsync(cancellationToken);
        var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);

        if (connection.Provider != AIProviderType.OpenAICompatible || connection.LocalRequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("API key not configured for connection {ConnectionId}", connection.Id);
                return null;
            }
        }

        var cacheKey = GenerateCacheKey(connection, featureConfig, apiKey);

        var conn = connection;
        var featConfig = featureConfig;
        var key = apiKey;

        try
        {
            if (ClientCache.TryGetValue(cacheKey, out var cachedClient))
            {
                _cacheMetrics.RecordHit("chat_client");
            }
            else
            {
                _cacheMetrics.RecordMiss("chat_client");
                cachedClient = ClientCache.GetOrAdd(cacheKey, _ =>
                {
                    var client = BuildClient(conn, featConfig, key);
                    var modelId = conn.Provider == AIProviderType.AzureOpenAI
                        ? featConfig.AzureDeploymentName ?? featConfig.Model
                        : featConfig.Model;

                    _logger.LogDebug("Created and cached client for connection {ConnectionId}, model {Model}",
                        conn.Id, modelId);

                    return new CachedClient(client, modelId);
                });
            }

            return new ClientLookupResult(cachedClient, featureConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create client for connection {ConnectionId}", connection.Id);
            throw;
        }
    }

    /// <summary>
    /// Generate a cache key that changes when relevant config changes.
    /// </summary>
    /// <remarks>
    /// MaxTokens and Temperature are intentionally NOT included - they are per-request
    /// ChatOptions, not client configuration; the client is reused across requests.
    /// </remarks>
    private static string GenerateCacheKey(AIConnection connection, AIFeatureConfig featureConfig, string? apiKey)
    {
        return string.Join("|",
            connection.Id,
            connection.Provider.ToString(),
            featureConfig.Model ?? "",
            featureConfig.AzureDeploymentName ?? "",
            connection.AzureEndpoint ?? "",
            connection.LocalEndpoint ?? "",
            apiKey ?? "");
    }

    /// <summary>
    /// Build an IChatClient for the given connection and feature config.
    /// Transport is left unset so the OpenAI SDK uses its default shared
    /// transport (HttpClientPipelineTransport.Shared), pooling the HTTP handler
    /// across all clients.
    /// </summary>
    private static IChatClient BuildClient(AIConnection connection, AIFeatureConfig featureConfig, string? apiKey)
    {
        switch (connection.Provider)
        {
            case AIProviderType.OpenAI:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("OpenAI API key is required");

                return new OpenAIClient(new ApiKeyCredential(apiKey))
                    .GetChatClient(featureConfig.Model)
                    .AsIChatClient();

            case AIProviderType.AzureOpenAI:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Azure OpenAI API key is required");
                if (string.IsNullOrWhiteSpace(connection.AzureEndpoint))
                    throw new InvalidOperationException("Azure endpoint is required");
                if (string.IsNullOrWhiteSpace(featureConfig.AzureDeploymentName))
                    throw new InvalidOperationException("Azure deployment name is required");

                return new AzureOpenAIClient(
                        new Uri(connection.AzureEndpoint),
                        new ApiKeyCredential(apiKey))
                    .GetChatClient(featureConfig.AzureDeploymentName)
                    .AsIChatClient();

            case AIProviderType.OpenAICompatible:
                if (string.IsNullOrWhiteSpace(connection.LocalEndpoint))
                    throw new InvalidOperationException("Local endpoint is required");

                // Ollama and other keyless providers - use placeholder API key
                var localApiKey = string.IsNullOrWhiteSpace(apiKey) ? "not-required" : apiKey;

                return new OpenAIClient(
                        new ApiKeyCredential(localApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(connection.LocalEndpoint) })
                    .GetChatClient(featureConfig.Model)
                    .AsIChatClient();

            case AIProviderType.OpenRouter:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("OpenRouter API key is required");

                var openRouterEndpoint = string.IsNullOrWhiteSpace(connection.LocalEndpoint)
                    ? "https://openrouter.ai/api/v1"
                    : connection.LocalEndpoint;

                return new OpenAIClient(
                        new ApiKeyCredential(apiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(openRouterEndpoint) })
                    .GetChatClient(featureConfig.Model)
                    .AsIChatClient();

            case AIProviderType.Anthropic:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Anthropic API key is required");

                return new AnthropicClient { ApiKey = apiKey }.AsIChatClient(featureConfig.Model);

            default:
                throw new InvalidOperationException($"Unsupported AI provider type: {connection.Provider}");
        }
    }

    /// <summary>
    /// Apply feature config defaults to caller-provided options.
    /// Caller-specified values take precedence over config defaults.
    /// </summary>
    private static ChatCompletionOptions ApplyFeatureConfigDefaults(ChatCompletionOptions? options, AIFeatureConfig featureConfig)
    {
        return new ChatCompletionOptions
        {
            MaxTokens = options?.MaxTokens ?? featureConfig.MaxTokens,
            Temperature = options?.Temperature ?? featureConfig.Temperature,
            JsonMode = options?.JsonMode ?? false
        };
    }

    /// <summary>
    /// Create MEAI ChatOptions from our options.
    /// </summary>
    private static ChatOptions CreateChatOptions(ChatCompletionOptions? options)
    {
        var chatOptions = new ChatOptions();

        if (options?.MaxTokens.HasValue == true)
            chatOptions.MaxOutputTokens = options.MaxTokens.Value;

        if (options?.Temperature.HasValue == true)
            chatOptions.Temperature = options.Temperature.Value;

        if (options?.JsonMode == true)
            chatOptions.ResponseFormat = ChatResponseFormat.Json;

        return chatOptions;
    }

    /// <summary>
    /// Create result from the MEAI ChatResponse.
    /// </summary>
    private static ChatCompletionResult? CreateResult(ChatResponse response, string fallbackModelId)
    {
        var content = response.Text;
        if (string.IsNullOrEmpty(content))
            return null;

        return new ChatCompletionResult
        {
            Content = content,
            Model = response.ModelId ?? fallbackModelId,
            TotalTokens = response.Usage?.TotalTokenCount,
            PromptTokens = response.Usage?.InputTokenCount,
            CompletionTokens = response.Usage?.OutputTokenCount,
            FinishReason = response.FinishReason?.ToString()
        };
    }

    /// <summary>
    /// Cached IChatClient with its resolved model id.
    /// </summary>
    private sealed record CachedClient(IChatClient Client, string ModelId);

    /// <summary>
    /// Client lookup result including feature config defaults for ChatOptions.
    /// </summary>
    private sealed record ClientLookupResult(CachedClient Client, AIFeatureConfig FeatureConfig);
}
