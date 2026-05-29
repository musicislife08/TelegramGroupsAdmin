using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class AIProviderConfigMappings
{
    extension(AIProviderConfigData data)
    {
        public AIProviderConfig ToModel() => new()
        {
            Connections = data.Connections.Select(c => c.ToModel()).ToList(),
            Features = data.Features.ToDictionary(
                kvp => Enum.Parse<AIFeatureType>(kvp.Key),
                kvp => kvp.Value.ToModel())
        };
    }

    extension(AIProviderConfig model)
    {
        public AIProviderConfigData ToData() => new()
        {
            Connections = model.Connections.Select(c => c.ToData()).ToList(),
            Features = model.Features.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => kvp.Value.ToData())
        };
    }

    extension(AIConnectionData data)
    {
        public AIConnection ToModel() => new()
        {
            Id = data.Id,
            Provider = (AIProviderType)data.Provider,
            Enabled = data.Enabled,
            AzureEndpoint = data.AzureEndpoint,
            AzureApiVersion = data.AzureApiVersion,
            LocalEndpoint = data.LocalEndpoint,
            LocalRequiresApiKey = data.LocalRequiresApiKey,
            AvailableModels = data.AvailableModels.Select(m => m.ToModel()).ToList(),
            ModelsLastFetched = data.ModelsLastFetched
        };
    }

    extension(AIConnection model)
    {
        public AIConnectionData ToData() => new()
        {
            Id = model.Id,
            Provider = (int)model.Provider,
            Enabled = model.Enabled,
            AzureEndpoint = model.AzureEndpoint,
            AzureApiVersion = model.AzureApiVersion,
            LocalEndpoint = model.LocalEndpoint,
            LocalRequiresApiKey = model.LocalRequiresApiKey,
            AvailableModels = model.AvailableModels.Select(m => m.ToData()).ToList(),
            ModelsLastFetched = model.ModelsLastFetched
        };
    }

    extension(AIFeatureConfigData data)
    {
        public AIFeatureConfig ToModel() => new()
        {
            ConnectionId = data.ConnectionId,
            Model = data.Model,
            MaxTokens = data.MaxTokens,
            Temperature = data.Temperature,
            AzureDeploymentName = data.AzureDeploymentName,
            RequiresVision = data.RequiresVision
        };
    }

    extension(AIFeatureConfig model)
    {
        public AIFeatureConfigData ToData() => new()
        {
            ConnectionId = model.ConnectionId,
            Model = model.Model,
            MaxTokens = model.MaxTokens,
            Temperature = model.Temperature,
            AzureDeploymentName = model.AzureDeploymentName,
            RequiresVision = model.RequiresVision
        };
    }

    extension(AIModelInfoData data)
    {
        public AIModelInfo ToModel() => new()
        {
            Id = data.Id,
            SizeBytes = data.SizeBytes
        };
    }

    extension(AIModelInfo model)
    {
        public AIModelInfoData ToData() => new()
        {
            Id = model.Id,
            SizeBytes = model.SizeBytes
        };
    }
}
