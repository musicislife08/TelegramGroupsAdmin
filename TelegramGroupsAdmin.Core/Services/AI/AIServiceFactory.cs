using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Service for AI configuration management (used by Settings UI)
/// For making AI calls, use IChatService directly
/// </summary>
public class AIServiceFactory : IAIServiceFactory
{
    private readonly ISystemConfigRepository _configRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AIServiceFactory> _logger;

    // JSON options for API responses
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AIServiceFactory(
        ISystemConfigRepository configRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<AIServiceFactory> logger)
    {
        _configRepository = configRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AIFeatureStatus> GetFeatureStatusAsync(AIFeatureType feature, CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null)
        {
            return new AIFeatureStatus(false, false, false, null, null);
        }

        if (!config.Features.TryGetValue(feature, out var featureConfig) || featureConfig.ConnectionId == null)
        {
            return new AIFeatureStatus(false, false, featureConfig?.RequiresVision ?? false, null, null);
        }

        var connection = config.Connections.SingleOrDefault(c => c.Id == featureConfig.ConnectionId);
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
    public async Task<IReadOnlyList<AIConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        return config?.Connections ?? [];
    }

    /// <inheritdoc />
    public async Task<AIFeatureConfig?> GetFeatureConfigAsync(AIFeatureType feature, CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null)
        {
            return null;
        }

        config.Features.TryGetValue(feature, out var featureConfig);
        return featureConfig;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AIModelInfo>> RefreshModelsAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetAIProviderConfigAsync(cancellationToken);
        if (config == null)
        {
            _logger.LogWarning("Cannot refresh models: AI provider config not found");
            return [];
        }

        var connection = config.Connections.SingleOrDefault(c => c.Id == connectionId);
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
            var models = await FetchModelsAsync(connection, cancellationToken);

            // Update connection with fetched models
            connection.AvailableModels = models.ToList();
            connection.ModelsLastFetched = DateTimeOffset.UtcNow;

            // Save updated config
            await _configRepository.SaveAIProviderConfigAsync(config, cancellationToken);

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
    private async Task<IReadOnlyList<AIModelInfo>> FetchModelsAsync(AIConnection connection, CancellationToken cancellationToken)
    {
        var apiKeys = await _configRepository.GetApiKeysAsync(cancellationToken);
        var apiKey = apiKeys?.GetAIConnectionKey(connection.Id);

        // Determine endpoint based on provider
        var endpoint = connection.Provider == AIProviderType.OpenAI
            ? "https://api.openai.com"
            : connection.LocalEndpoint!;

        return await FetchOpenAICompatibleModelsAsync(endpoint, apiKey, cancellationToken);
    }

    /// <summary>
    /// Fetch models from any OpenAI-compatible endpoint (including OpenAI itself and Ollama)
    /// Returns ALL models - filtering by capability is done at the UI layer
    /// </summary>
    private async Task<IReadOnlyList<AIModelInfo>> FetchOpenAICompatibleModelsAsync(
        string endpoint,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        var baseUri = new Uri(endpoint.TrimEnd('/'));

        // Try Ollama's /api/tags endpoint first if URL suggests Ollama
        if (endpoint.Contains("11434") || endpoint.Contains("ollama", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var ollamaModels = await FetchOllamaModelsAsync(client, baseUri, cancellationToken);
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

            var response = await client.GetAsync(modelsUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch models from {Endpoint}: {StatusCode}", endpoint, response.StatusCode);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<OpenAIModelsResponse>(content, JsonOptions);

            // Return ALL models - no filtering (UI handles capability filtering)
            return modelsResponse?.Data?
                .Select(m => new AIModelInfo { Id = m.Id })
                .OrderBy(m => m.Id)
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch models from {Endpoint}", endpoint);
            return [];
        }
    }

    /// <summary>
    /// Fetch models from Ollama API (/api/tags endpoint)
    /// </summary>
    private async Task<IReadOnlyList<AIModelInfo>> FetchOllamaModelsAsync(
        HttpClient client,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        var ollamaUrl = $"{baseUri.Scheme}://{baseUri.Host}:{baseUri.Port}/api/tags";

        var response = await client.GetAsync(ollamaUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var ollamaResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(content, JsonOptions);

        return ollamaResponse?.Models?
            .Select(m => new AIModelInfo
            {
                Id = m.Name,
                SizeBytes = m.Size
            })
            .OrderBy(m => m.Id)
            .ToList() ?? [];
    }

    // Response models for API parsing
    private record OpenAIModelsResponse(OpenAIModelData[]? Data);
    private record OpenAIModelData(string Id, string? OwnedBy);
    private record OllamaModelsResponse(OllamaModelData[]? Models);
    private record OllamaModelData(string Name, long Size, DateTimeOffset ModifiedAt);
}
