using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Hosted service that migrates existing OpenAI configuration to the new multi-provider format.
/// Runs once at startup if ai_provider_config is null but openai_config exists.
/// </summary>
public class AIProviderMigrationService : IHostedService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<AIProviderMigrationService> _logger;

    public AIProviderMigrationService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AIProviderMigrationService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

            // Step 1: Migrate legacy API keys to new dictionary format
            await MigrateLegacyApiKeysAsync(configRepo, cancellationToken);

            // Step 2: Check if AI provider config migration is needed
            var existingAiProviderConfig = await configRepo.GetAIProviderConfigAsync(cancellationToken);
            if (existingAiProviderConfig != null)
            {
                _logger.LogDebug("AI provider config already exists, skipping config migration");
                return;
            }

            // Step 3: Create connections based on which API keys exist
            var apiKeys = await configRepo.GetApiKeysAsync(cancellationToken);
            var connections = new List<AIConnection>();
            string? primaryConnectionId = null;

            // Check for OpenAI key (must be non-empty)
            if (apiKeys?.AIConnectionKeys.TryGetValue("openai", out var openaiKey) == true &&
                !string.IsNullOrWhiteSpace(openaiKey))
            {
                connections.Add(new AIConnection
                {
                    Id = "openai",
                    Provider = AIProviderType.OpenAI,
                    Enabled = true
                });
                primaryConnectionId ??= "openai";
                _logger.LogInformation("Creating 'openai' connection from migrated key");
            }

            // Check for Azure OpenAI key (must be non-empty)
            if (apiKeys?.AIConnectionKeys.TryGetValue("azure-openai", out var azureKey) == true &&
                !string.IsNullOrWhiteSpace(azureKey))
            {
                connections.Add(new AIConnection
                {
                    Id = "azure-openai",
                    Provider = AIProviderType.AzureOpenAI,
                    Enabled = false // Disabled by default - needs endpoint configuration
                });
                _logger.LogInformation("Creating 'azure-openai' connection from migrated key (disabled - needs endpoint)");
            }

            // Check for Local AI key (must be non-empty)
            if (apiKeys?.AIConnectionKeys.TryGetValue("local-ai", out var localKey) == true &&
                !string.IsNullOrWhiteSpace(localKey))
            {
                connections.Add(new AIConnection
                {
                    Id = "local-ai",
                    Provider = AIProviderType.LocalOpenAI,
                    Enabled = false, // Disabled by default - needs endpoint configuration
                    LocalRequiresApiKey = true
                });
                _logger.LogInformation("Creating 'local-ai' connection from migrated key (disabled - needs endpoint)");
            }

            // Load existing OpenAI config for model/params
            var oldOpenAiConfig = await configRepo.GetOpenAIConfigAsync(cancellationToken);
            var model = oldOpenAiConfig?.Model ?? "gpt-4o-mini";
            var maxTokens = oldOpenAiConfig?.MaxTokens ?? 500;
            var temperature = oldOpenAiConfig?.Temperature ?? 0.2;

            // Create features config
            var features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = primaryConnectionId,
                    Model = model,
                    MaxTokens = maxTokens,
                    Temperature = temperature
                },
                [AIFeatureType.Translation] = new AIFeatureConfig
                {
                    ConnectionId = primaryConnectionId,
                    Model = model,
                    MaxTokens = maxTokens,
                    Temperature = temperature
                },
                [AIFeatureType.ImageAnalysis] = new AIFeatureConfig
                {
                    ConnectionId = primaryConnectionId,
                    Model = model,
                    MaxTokens = maxTokens,
                    Temperature = temperature,
                    RequiresVision = true
                },
                [AIFeatureType.VideoAnalysis] = new AIFeatureConfig
                {
                    ConnectionId = primaryConnectionId,
                    Model = model,
                    MaxTokens = maxTokens,
                    Temperature = temperature,
                    RequiresVision = true
                },
                [AIFeatureType.PromptBuilder] = new AIFeatureConfig
                {
                    ConnectionId = primaryConnectionId,
                    Model = model,
                    MaxTokens = 1000,
                    Temperature = 0.3
                }
            };

            var newConfig = new AIProviderConfig
            {
                Connections = connections,
                Features = features
            };

            await configRepo.SaveAIProviderConfigAsync(newConfig, cancellationToken);

            if (primaryConnectionId != null)
            {
                _logger.LogInformation(
                    "Successfully migrated to multi-provider format. Primary connection: {ConnectionId}, Model: {Model}",
                    primaryConnectionId, model);
            }
            else
            {
                _logger.LogInformation("Initialized empty AI provider config (no API keys configured)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI provider config migration. AI features may not work correctly.");
            // Don't throw - let the app start, the user can configure manually
        }
    }

    /// <summary>
    /// Migrates legacy API key properties (OpenAI, AzureOpenAI, LocalAI) to the new AIConnectionKeys dictionary
    /// </summary>
    private async Task MigrateLegacyApiKeysAsync(ISystemConfigRepository configRepo, CancellationToken ct)
    {
        var apiKeys = await configRepo.GetApiKeysAsync(ct);
        if (apiKeys == null)
        {
            return;
        }

        if (apiKeys.MigrateLegacyKeys())
        {
            await configRepo.SaveApiKeysAsync(apiKeys, ct);
            _logger.LogInformation("Migrated legacy API keys to new connection-based format");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
