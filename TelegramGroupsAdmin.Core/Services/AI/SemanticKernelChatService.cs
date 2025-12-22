using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Implementation of IChatService using Microsoft Semantic Kernel
/// Supports OpenAI, Azure OpenAI, and OpenAI-compatible local endpoints
/// Static kernel cache persists across scoped instances for efficiency
/// </summary>
public class SemanticKernelChatService : IChatService
{
    // Static cache - persists across scoped instances for kernel reuse.
    // Thread safety: ConcurrentDictionary + GetOrAdd provides atomic access even when
    // multiple scoped instances (from different HTTP requests) access simultaneously.
    // Cache bounds: Expected <10 entries in homelab use (connections × models configured).
    // Unbounded growth is not a concern - entries only added when new connection/model combos
    // are configured, and InvalidateCache() clears entries when connections are modified.
    private static readonly ConcurrentDictionary<string, CachedKernel> KernelCache = new();
    private readonly ISystemConfigRepository _configRepository;
    private readonly ILogger<SemanticKernelChatService> _logger;

    public SemanticKernelChatService(
        ISystemConfigRepository configRepository,
        ILogger<SemanticKernelChatService> logger)
    {
        _configRepository = configRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lookupResult = await GetOrCreateKernelAsync(feature, cancellationToken);
        if (lookupResult == null)
        {
            _logger.LogDebug("Feature {Feature} is not configured, skipping AI call", feature);
            return null;
        }

        var kernelInfo = lookupResult.Kernel;
        var featureConfig = lookupResult.FeatureConfig;

        try
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);
            chatHistory.AddUserMessage(userPrompt);

            // Apply feature config defaults for settings not specified by caller
            var effectiveOptions = ApplyFeatureConfigDefaults(options, featureConfig);
            var executionSettings = CreateExecutionSettings(effectiveOptions);

            var response = await kernelInfo.ChatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: kernelInfo.Kernel,
                cancellationToken: cancellationToken);

            return CreateResult(response, kernelInfo.ModelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chat completion from {Model} for feature {Feature}",
                kernelInfo.ModelId, feature);
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
        var lookupResult = await GetOrCreateKernelAsync(feature, cancellationToken);
        if (lookupResult == null)
        {
            _logger.LogDebug("Feature {Feature} is not configured, skipping AI vision call", feature);
            return null;
        }

        var kernelInfo = lookupResult.Kernel;
        var featureConfig = lookupResult.FeatureConfig;

        try
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);

            // Create message with both text and image
            var imageContent = new ImageContent(imageData, mimeType);
            var textContent = new TextContent(userPrompt);
            chatHistory.AddUserMessage([textContent, imageContent]);

            // Apply feature config defaults for settings not specified by caller
            var effectiveOptions = ApplyFeatureConfigDefaults(options, featureConfig);
            var executionSettings = CreateExecutionSettings(effectiveOptions);

            var response = await kernelInfo.ChatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: kernelInfo.Kernel,
                cancellationToken: cancellationToken);

            return CreateResult(response, kernelInfo.ModelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting vision completion from {Model} for feature {Feature}",
                kernelInfo.ModelId, feature);
            throw; // Let caller handle the exception
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
        if (connection.Provider != AIProviderType.LocalOpenAI || connection.LocalRequiresApiKey)
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
            KernelCache.Clear();
            _logger.LogDebug("Cleared all cached AI kernels");
        }
        else
        {
            // Remove all cache entries for this connection (keys are delimited with "|")
            var keysToRemove = KernelCache.Keys.Where(k => k.StartsWith(connectionId + "|")).ToList();
            foreach (var key in keysToRemove)
            {
                KernelCache.TryRemove(key, out _);
            }
            _logger.LogDebug("Invalidated cached AI kernel for connection {ConnectionId}", connectionId);
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
        var kernelInfo = await GetOrCreateTestKernelAsync(connectionId, model, azureDeploymentName, cancellationToken);
        if (kernelInfo == null)
        {
            // Debug level - specific reason already logged at Warning level by GetOrCreateTestKernelAsync
            _logger.LogDebug("Test kernel not available for connection {ConnectionId}, model {Model}",
                connectionId, model);
            return null;
        }

        try
        {
            _logger.LogDebug("Making test completion call to {Model}", kernelInfo.ModelId);

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);
            chatHistory.AddUserMessage(userPrompt);

            var executionSettings = CreateExecutionSettings(options);

            var response = await kernelInfo.ChatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: kernelInfo.Kernel,
                cancellationToken: cancellationToken);

            // Detailed response logging for debugging
            _logger.LogDebug("SK Response - Content: '{Content}', ModelId: {ModelId}, Role: {Role}, ItemCount: {ItemCount}",
                response.Content ?? "(null)",
                response.ModelId ?? "(null)",
                response.Role.ToString(),
                response.Items.Count);

            if (response.Items.Count > 0)
            {
                foreach (var item in response.Items)
                {
                    _logger.LogDebug("SK Response Item - Type: {Type}, ToString: {Value}",
                        item.GetType().Name, item.ToString()?[..Math.Min(item.ToString()?.Length ?? 0, 200)]);
                }
            }

            // Log metadata to find hidden errors
            if (response.Metadata != null)
            {
                foreach (var kvp in response.Metadata)
                {
                    _logger.LogDebug("SK Metadata - {Key}: {Value}",
                        kvp.Key, kvp.Value?.ToString()?[..Math.Min(kvp.Value?.ToString()?.Length ?? 0, 500)] ?? "(null)");
                }
            }
            else
            {
                _logger.LogDebug("SK Response has no metadata");
            }

            var result = CreateResult(response, kernelInfo.ModelId);
            _logger.LogDebug("Test completion returned: Content={HasContent}, Tokens={Tokens}",
                !string.IsNullOrEmpty(result?.Content), result?.TotalTokens);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test completion failed for {Model}: {Message}",
                kernelInfo.ModelId, ex.Message);
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
        var kernelInfo = await GetOrCreateTestKernelAsync(connectionId, model, azureDeploymentName, cancellationToken);
        if (kernelInfo == null)
        {
            // Debug level - specific reason already logged at Warning level by GetOrCreateTestKernelAsync
            _logger.LogDebug("Test kernel not available for vision call, connection {ConnectionId}, model {Model}",
                connectionId, model);
            return null;
        }

        try
        {
            _logger.LogDebug("Making test vision call to {Model} with {ImageSize} bytes",
                kernelInfo.ModelId, imageData.Length);

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);

            // Create message with both text and image
            var imageContent = new ImageContent(imageData, mimeType);
            var textContent = new TextContent(userPrompt);
            chatHistory.AddUserMessage([textContent, imageContent]);

            var executionSettings = CreateExecutionSettings(options);

            var response = await kernelInfo.ChatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: kernelInfo.Kernel,
                cancellationToken: cancellationToken);

            var result = CreateResult(response, kernelInfo.ModelId);
            _logger.LogDebug("Test vision returned: Content={HasContent}, Tokens={Tokens}",
                !string.IsNullOrEmpty(result?.Content), result?.TotalTokens);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test vision call failed for {Model}: {Message}",
                kernelInfo.ModelId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Get or create a kernel for testing a specific connection+model combo
    /// Does not use feature config - uses provided model/deployment directly
    /// </summary>
    private async Task<CachedKernel?> GetOrCreateTestKernelAsync(
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

        // Validate API key if required
        if (connection.Provider != AIProviderType.LocalOpenAI || connection.LocalRequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("API key not configured for test connection {ConnectionId}", connection.Id);
                return null;
            }
        }

        // Create a temporary feature config for the test
        var testFeatureConfig = new AIFeatureConfig
        {
            ConnectionId = connectionId,
            Model = model,
            AzureDeploymentName = azureDeploymentName
        };

        // Generate cache key for test kernel
        var cacheKey = GenerateCacheKey(connection, testFeatureConfig, apiKey);

        // Use GetOrAdd for thread-safe atomic cache access
        var cachedKernel = KernelCache.GetOrAdd(cacheKey, _ =>
        {
            var kernel = BuildKernel(connection, testFeatureConfig, apiKey);
            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            var modelId = connection.Provider == AIProviderType.AzureOpenAI
                ? azureDeploymentName ?? model
                : model;

            _logger.LogDebug("Created and cached test kernel for connection {ConnectionId}, model {Model}",
                connection.Id, modelId);

            return new CachedKernel(kernel, chatService, modelId);
        });

        return cachedKernel;
    }

    /// <summary>
    /// Get or create a cached Kernel for the specified feature
    /// </summary>
    private async Task<KernelLookupResult?> GetOrCreateKernelAsync(AIFeatureType feature, CancellationToken cancellationToken)
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

        // Validate API key if required
        if (connection.Provider != AIProviderType.LocalOpenAI || connection.LocalRequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("API key not configured for connection {ConnectionId}", connection.Id);
                return null;
            }
        }

        // Generate cache key based on connection + model + relevant config
        var cacheKey = GenerateCacheKey(connection, featureConfig, apiKey);

        // Use GetOrAdd for thread-safe atomic cache access
        // Capture variables for the closure
        var conn = connection;
        var featConfig = featureConfig;
        var key = apiKey;

        try
        {
            var cachedKernel = KernelCache.GetOrAdd(cacheKey, _ =>
            {
                var kernel = BuildKernel(conn, featConfig, key);
                var chatService = kernel.GetRequiredService<IChatCompletionService>();
                var modelId = conn.Provider == AIProviderType.AzureOpenAI
                    ? featConfig.AzureDeploymentName ?? featConfig.Model
                    : featConfig.Model;

                _logger.LogDebug("Created and cached kernel for connection {ConnectionId}, model {Model}",
                    conn.Id, modelId);

                return new CachedKernel(kernel, chatService, modelId);
            });

            // Return kernel with feature config so caller can use config defaults (Temperature, MaxTokens)
            return new KernelLookupResult(cachedKernel, featureConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create kernel for connection {ConnectionId}", connection.Id);
            throw;
        }
    }

    /// <summary>
    /// Generate a cache key that changes when relevant config changes.
    /// Uses full key string to avoid hash collisions - cache size is small (typically &lt;10 entries).
    /// </summary>
    /// <remarks>
    /// MaxTokens and Temperature are intentionally NOT included in the cache key because they are
    /// per-request execution settings passed to GetChatMessageContentAsync(), not kernel configuration.
    /// The kernel/client can be reused across requests with different token limits and temperatures.
    /// </remarks>
    private static string GenerateCacheKey(AIConnection connection, AIFeatureConfig featureConfig, string? apiKey)
    {
        // Use full key to avoid hash collisions - cache has few entries
        // API key included so kernel is rebuilt if key changes
        return string.Join("|",
            connection.Id,
            connection.Provider.ToString(),
            featureConfig.Model ?? "",
            featureConfig.AzureDeploymentName ?? "",
            connection.AzureEndpoint ?? "",
            connection.AzureApiVersion ?? "",
            connection.LocalEndpoint ?? "",
            apiKey ?? "");
    }

    /// <summary>
    /// Build a Semantic Kernel for the given connection and feature config
    /// </summary>
    private static Kernel BuildKernel(AIConnection connection, AIFeatureConfig featureConfig, string? apiKey)
    {
        var builder = Kernel.CreateBuilder();

        switch (connection.Provider)
        {
            case AIProviderType.OpenAI:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("OpenAI API key is required");

                builder.AddOpenAIChatCompletion(
                    modelId: featureConfig.Model,
                    apiKey: apiKey);
                break;

            case AIProviderType.AzureOpenAI:
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Azure OpenAI API key is required");
                if (string.IsNullOrWhiteSpace(connection.AzureEndpoint))
                    throw new InvalidOperationException("Azure endpoint is required");
                if (string.IsNullOrWhiteSpace(featureConfig.AzureDeploymentName))
                    throw new InvalidOperationException("Azure deployment name is required");

                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: featureConfig.AzureDeploymentName,
                    endpoint: connection.AzureEndpoint,
                    apiKey: apiKey,
                    apiVersion: connection.AzureApiVersion);
                break;

            case AIProviderType.LocalOpenAI:
                if (string.IsNullOrWhiteSpace(connection.LocalEndpoint))
                    throw new InvalidOperationException("Local endpoint is required");

                // Ollama and other keyless providers - use placeholder API key
                var localApiKey = string.IsNullOrWhiteSpace(apiKey) ? "not-required" : apiKey;

                builder.AddOpenAIChatCompletion(
                    modelId: featureConfig.Model,
                    apiKey: localApiKey,
                    endpoint: new Uri(connection.LocalEndpoint));
                break;

            default:
                throw new InvalidOperationException($"Unsupported AI provider type: {connection.Provider}");
        }

        return builder.Build();
    }

    /// <summary>
    /// Apply feature config defaults to caller-provided options.
    /// Caller-specified values take precedence over config defaults.
    /// </summary>
    private static ChatCompletionOptions ApplyFeatureConfigDefaults(ChatCompletionOptions? options, AIFeatureConfig featureConfig)
    {
        return new ChatCompletionOptions
        {
            // Use caller value if specified, otherwise use feature config default
            MaxTokens = options?.MaxTokens ?? featureConfig.MaxTokens,
            Temperature = options?.Temperature ?? featureConfig.Temperature,
            JsonMode = options?.JsonMode ?? false
        };
    }

    /// <summary>
    /// Create execution settings from options
    /// </summary>
    private static OpenAIPromptExecutionSettings CreateExecutionSettings(ChatCompletionOptions? options)
    {
        var settings = new OpenAIPromptExecutionSettings();

        if (options?.MaxTokens.HasValue == true)
            settings.MaxTokens = options.MaxTokens.Value;

        if (options?.Temperature.HasValue == true)
            settings.Temperature = options.Temperature.Value;

        if (options?.JsonMode == true)
            settings.ResponseFormat = "json_object";

        return settings;
    }

    /// <summary>
    /// Create result from SK response
    /// </summary>
    private static ChatCompletionResult? CreateResult(ChatMessageContent response, string fallbackModelId)
    {
        var content = response.Content;
        if (string.IsNullOrEmpty(content))
            return null;

        int? totalTokens = null;
        int? promptTokens = null;
        int? completionTokens = null;
        string? finishReason = null;

        if (response.Metadata != null)
        {
            if (response.Metadata.TryGetValue("Usage", out var usageObj) &&
                usageObj is OpenAI.Chat.ChatTokenUsage usage)
            {
                totalTokens = usage.TotalTokenCount;
                promptTokens = usage.InputTokenCount;
                completionTokens = usage.OutputTokenCount;
            }

            if (response.Metadata.TryGetValue("FinishReason", out var finishObj) &&
                finishObj is OpenAI.Chat.ChatFinishReason reason)
            {
                finishReason = reason.ToString();
            }
        }

        return new ChatCompletionResult
        {
            Content = content,
            Model = response.ModelId ?? fallbackModelId,
            TotalTokens = totalTokens,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            FinishReason = finishReason
        };
    }

    /// <summary>
    /// Cached kernel with associated services
    /// </summary>
    private sealed record CachedKernel(Kernel Kernel, IChatCompletionService ChatService, string ModelId);

    /// <summary>
    /// Kernel lookup result including feature config defaults for execution settings
    /// </summary>
    private sealed record KernelLookupResult(CachedKernel Kernel, AIFeatureConfig FeatureConfig);
}
