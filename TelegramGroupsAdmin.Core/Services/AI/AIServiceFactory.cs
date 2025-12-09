using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Factory implementation for creating AI chat services based on feature configuration
/// Handles configuration lookup, API key resolution, and service instantiation
/// </summary>
public class AIServiceFactory : IAIServiceFactory
{
    private readonly ISystemConfigRepository _configRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AIServiceFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // JSON options for API responses
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AIServiceFactory(
        ISystemConfigRepository configRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<AIServiceFactory> logger,
        ILoggerFactory loggerFactory)
    {
        _configRepository = configRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public async Task<IChatService?> GetChatServiceAsync(AIFeatureType feature, CancellationToken ct = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(ct);
        if (config == null)
        {
            _logger.LogDebug("AI provider config not found, feature {Feature} not available", feature);
            return null;
        }

        // Get feature config
        if (!config.Features.TryGetValue(feature, out var featureConfig) || featureConfig.ConnectionId == null)
        {
            _logger.LogDebug("Feature {Feature} not configured", feature);
            return null;
        }

        // Find the connection
        var connection = config.Connections.FirstOrDefault(c => c.Id == featureConfig.ConnectionId);
        if (connection == null)
        {
            _logger.LogWarning("Connection {ConnectionId} not found for feature {Feature}",
                featureConfig.ConnectionId, feature);
            return null;
        }

        if (!connection.Enabled)
        {
            _logger.LogDebug("Connection {ConnectionId} is disabled for feature {Feature}",
                connection.Id, feature);
            return null;
        }

        // Get API key for this specific connection
        var apiKeys = await _configRepository.GetApiKeysAsync(ct);
        var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);

        // Validate API key if required (local providers may not need one)
        if (connection.Provider != AIProviderType.LocalOpenAI || connection.LocalRequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("API key not configured for connection {ConnectionId}", connection.Id);
                return null;
            }
        }

        try
        {
            var chatServiceLogger = _loggerFactory.CreateLogger<SemanticKernelChatService>();
            return new SemanticKernelChatService(connection, featureConfig, apiKey, chatServiceLogger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create chat service for feature {Feature}", feature);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<AIFeatureStatus> GetFeatureStatusAsync(AIFeatureType feature, CancellationToken ct = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(ct);
        if (config == null)
        {
            return new AIFeatureStatus(false, false, false, null, null);
        }

        if (!config.Features.TryGetValue(feature, out var featureConfig) || featureConfig.ConnectionId == null)
        {
            return new AIFeatureStatus(false, false, featureConfig?.RequiresVision ?? false, null, null);
        }

        var connection = config.Connections.FirstOrDefault(c => c.Id == featureConfig.ConnectionId);
        if (connection == null)
        {
            return new AIFeatureStatus(false, false, featureConfig.RequiresVision, featureConfig.ConnectionId, featureConfig.Model);
        }

        // Determine model name (Azure uses deployment name, others use model)
        var modelName = connection.Provider == AIProviderType.AzureOpenAI
            ? featureConfig.AzureDeploymentName ?? featureConfig.Model
            : featureConfig.Model;

        return new AIFeatureStatus(
            IsConfigured: true,
            ConnectionEnabled: connection.Enabled,
            RequiresVision: featureConfig.RequiresVision,
            ConnectionId: connection.Id,
            ModelName: modelName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AIConnection>> GetConnectionsAsync(CancellationToken ct = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(ct);
        return config?.Connections ?? [];
    }

    /// <inheritdoc />
    public async Task<AIFeatureConfig?> GetFeatureConfigAsync(AIFeatureType feature, CancellationToken ct = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(ct);
        if (config == null)
        {
            return null;
        }

        config.Features.TryGetValue(feature, out var featureConfig);
        return featureConfig;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AIModelInfo>> RefreshModelsAsync(string connectionId, CancellationToken ct = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(ct);
        if (config == null)
        {
            _logger.LogWarning("Cannot refresh models: AI provider config not found");
            return [];
        }

        var connection = config.Connections.FirstOrDefault(c => c.Id == connectionId);
        if (connection == null)
        {
            _logger.LogWarning("Cannot refresh models: Connection {ConnectionId} not found", connectionId);
            return [];
        }

        // Azure doesn't support model listing via API (requires ARM API)
        if (connection.Provider == AIProviderType.AzureOpenAI)
        {
            _logger.LogDebug("Skipping model refresh for Azure OpenAI connection (requires manual deployment name)");
            return connection.AvailableModels;
        }

        try
        {
            var models = await FetchModelsAsync(connection, ct);

            // Update connection with fetched models
            connection.AvailableModels = models.ToList();
            connection.ModelsLastFetched = DateTimeOffset.UtcNow;

            // Save updated config
            await _configRepository.SaveAIProviderConfigAsync(config, ct);

            _logger.LogInformation("Refreshed {Count} models for connection {ConnectionId}",
                models.Count, connectionId);

            return models;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh models for connection {ConnectionId}", connectionId);
            return connection.AvailableModels;
        }
    }

    /// <summary>
    /// Fetch models from provider API
    /// </summary>
    private async Task<IReadOnlyList<AIModelInfo>> FetchModelsAsync(AIConnection connection, CancellationToken ct)
    {
        var apiKeys = await _configRepository.GetApiKeysAsync(ct);
        var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);

        switch (connection.Provider)
        {
            case AIProviderType.OpenAI:
                return await FetchOpenAIModelsAsync(apiKey, ct);

            case AIProviderType.LocalOpenAI:
                // Try Ollama format first, fall back to OpenAI-compatible
                return await FetchLocalModelsAsync(connection.LocalEndpoint!, apiKey, ct);

            default:
                return [];
        }
    }

    /// <summary>
    /// Fetch models from OpenAI API
    /// </summary>
    private async Task<IReadOnlyList<AIModelInfo>> FetchOpenAIModelsAsync(string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Cannot fetch OpenAI models: API key not configured");
            return [];
        }

        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var response = await client.GetAsync("https://api.openai.com/v1/models", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch OpenAI models: {StatusCode}", response.StatusCode);
            return [];
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        var modelsResponse = JsonSerializer.Deserialize<OpenAIModelsResponse>(content, JsonOptions);

        return modelsResponse?.Data?
            .Where(m => IsChatModel(m.Id))
            .Select(m => new AIModelInfo
            {
                Id = m.Id,
                Description = m.OwnedBy,
                SupportsVision = InferVisionSupport(m.Id)
            })
            .OrderBy(m => m.Id)
            .ToList() ?? [];
    }

    /// <summary>
    /// Fetch models from local/OpenAI-compatible endpoint
    /// </summary>
    private async Task<IReadOnlyList<AIModelInfo>> FetchLocalModelsAsync(
        string endpoint,
        string? apiKey,
        CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        // Parse base URL and try different endpoints
        var baseUri = new Uri(endpoint.TrimEnd('/'));

        // Try Ollama's /api/tags endpoint first (if base URL contains ollama port or path)
        if (endpoint.Contains("11434") || endpoint.Contains("ollama", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var ollamaModels = await FetchOllamaModelsAsync(client, baseUri, ct);
                if (ollamaModels.Count > 0)
                {
                    return ollamaModels;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ollama endpoint not available, trying OpenAI-compatible format");
            }
        }

        // Try OpenAI-compatible /v1/models endpoint
        try
        {
            var modelsUrl = endpoint.EndsWith("/v1")
                ? $"{endpoint}/models"
                : $"{endpoint.TrimEnd('/')}/v1/models";

            var response = await client.GetAsync(modelsUrl, ct);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var modelsResponse = JsonSerializer.Deserialize<OpenAIModelsResponse>(content, JsonOptions);

                return modelsResponse?.Data?
                    .Select(m => new AIModelInfo
                    {
                        Id = m.Id,
                        Description = m.OwnedBy,
                        SupportsVision = InferVisionSupport(m.Id)
                    })
                    .OrderBy(m => m.Id)
                    .ToList() ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch models from OpenAI-compatible endpoint");
        }

        return [];
    }

    /// <summary>
    /// Fetch models from Ollama API
    /// </summary>
    private async Task<IReadOnlyList<AIModelInfo>> FetchOllamaModelsAsync(
        HttpClient client,
        Uri baseUri,
        CancellationToken ct)
    {
        // Ollama API uses /api/tags for model listing
        var ollamaUrl = $"{baseUri.Scheme}://{baseUri.Host}:{baseUri.Port}/api/tags";

        var response = await client.GetAsync(ollamaUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        var ollamaResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(content, JsonOptions);

        return ollamaResponse?.Models?
            .Select(m => new AIModelInfo
            {
                Id = m.Name,
                Description = $"Size: {FormatSize(m.Size)}, Modified: {m.ModifiedAt:g}",
                SupportsVision = InferVisionSupport(m.Name)
            })
            .OrderBy(m => m.Id)
            .ToList() ?? [];
    }

    /// <summary>
    /// Check if a model ID is likely a chat model
    /// </summary>
    private static bool IsChatModel(string modelId)
    {
        // Filter to chat-capable models
        var chatPrefixes = new[] { "gpt-", "o1-", "o3-", "chatgpt-" };
        return chatPrefixes.Any(p => modelId.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Infer vision support from model name
    /// </summary>
    private static bool? InferVisionSupport(string modelId)
    {
        var lowerModel = modelId.ToLowerInvariant();

        // Models known to support vision
        if (lowerModel.Contains("vision") ||
            lowerModel.Contains("4o") ||  // gpt-4o models support vision
            lowerModel.Contains("llava") ||  // LLaVA models
            lowerModel.Contains("bakllava") ||  // BakLLaVA models
            lowerModel.Contains("cogvlm") ||  // CogVLM models
            lowerModel.Contains("moondream"))  // Moondream models
        {
            return true;
        }

        // Models known NOT to support vision
        if (lowerModel.Contains("instruct") && !lowerModel.Contains("4o") ||
            lowerModel.StartsWith("gpt-3.5") ||
            lowerModel.StartsWith("text-"))
        {
            return false;
        }

        // Unknown - let the user decide
        return null;
    }

    /// <summary>
    /// Format byte size to human-readable string
    /// </summary>
    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F1} {suffixes[i]}";
    }

    // Response models for API parsing
    private record OpenAIModelsResponse(OpenAIModelData[]? Data);
    private record OpenAIModelData(string Id, string? OwnedBy);
    private record OllamaModelsResponse(OllamaModelData[]? Models);
    private record OllamaModelData(string Name, long Size, DateTimeOffset ModifiedAt);
}
