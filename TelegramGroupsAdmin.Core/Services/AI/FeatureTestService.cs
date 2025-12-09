using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Tests AI feature configurations by making actual API calls
/// </summary>
public class FeatureTestService(
    ISystemConfigRepository configRepository,
    ILoggerFactory loggerFactory,
    ILogger<FeatureTestService> logger) : IFeatureTestService
{
    // Minimal 1x1 red PNG for vision tests (68 bytes)
    private static readonly byte[] TestImagePng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
        0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE, 0xD4, 0xEF, 0x00, 0x00,
        0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    public async Task<FeatureTestResult> TestFeatureAsync(
        AIFeatureType featureType,
        string connectionId,
        string model,
        string? azureDeploymentName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(model);

        try
        {
            // Get connection (Single because dropdown ensures it exists and IDs are unique)
            var config = await configRepository.GetAIProviderConfigAsync(ct);
            var connection = config!.Connections.Single(c => c.Id == connectionId);

            if (!connection.Enabled)
            {
                return FeatureTestResult.Fail($"Connection '{connectionId}' is disabled");
            }

            // Get API key
            var apiKeys = await configRepository.GetApiKeysAsync(ct);
            var apiKey = apiKeys?.GetAIConnectionKey(connectionId);

            if (connection.Provider != AIProviderType.LocalOpenAI || connection.LocalRequiresApiKey)
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return FeatureTestResult.Fail("API key not configured for this connection");
                }
            }

            // Create temporary feature config for testing
            var testConfig = new AIFeatureConfig
            {
                ConnectionId = connectionId,
                Model = model,
                AzureDeploymentName = azureDeploymentName,
                MaxTokens = 100, // Small for testing
                Temperature = 0.1
            };

            // Create chat service
            var chatServiceLogger = loggerFactory.CreateLogger<SemanticKernelChatService>();
            IChatService chatService;

            try
            {
                chatService = new SemanticKernelChatService(connection, testConfig, apiKey, chatServiceLogger);
            }
            catch (Exception ex)
            {
                return FeatureTestResult.Fail("Failed to initialize chat service", ex.Message);
            }

            // Run feature-specific test
            return featureType switch
            {
                AIFeatureType.SpamDetection => await TestSpamDetectionAsync(chatService, ct),
                AIFeatureType.Translation => await TestTranslationAsync(chatService, ct),
                AIFeatureType.ImageAnalysis => await TestVisionAsync(chatService, "image", ct),
                AIFeatureType.VideoAnalysis => await TestVisionAsync(chatService, "video frame", ct),
                AIFeatureType.PromptBuilder => await TestPromptBuilderAsync(chatService, ct),
                _ => FeatureTestResult.Fail($"Unknown feature type: {featureType}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feature test failed for {Feature} with connection {ConnectionId}",
                featureType, connectionId);
            return FeatureTestResult.Fail("Test failed unexpectedly", ex.Message);
        }
    }

    private static async Task<FeatureTestResult> TestSpamDetectionAsync(IChatService chatService, CancellationToken ct)
    {
        const string systemPrompt = "You are a test assistant. Follow instructions exactly.";
        const string userPrompt = "Respond with only 'OK'.";

        var result = await chatService.GetCompletionAsync(systemPrompt, userPrompt, new ChatCompletionOptions
        {
            MaxTokens = 10
        }, ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Content))
        {
            return FeatureTestResult.Fail("No response received from model");
        }

        return FeatureTestResult.Ok($"Model responded successfully ({result.TotalTokens ?? 0} tokens)");
    }

    private static async Task<FeatureTestResult> TestTranslationAsync(IChatService chatService, CancellationToken ct)
    {
        const string systemPrompt = "You are a translator. Respond with only the translation, nothing else.";
        const string userPrompt = "Translate 'Hello' to Spanish.";

        var result = await chatService.GetCompletionAsync(systemPrompt, userPrompt, new ChatCompletionOptions
        {
            MaxTokens = 20
        }, ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Content))
        {
            return FeatureTestResult.Fail("No response received from model");
        }

        return FeatureTestResult.Ok($"Translation test passed ({result.TotalTokens ?? 0} tokens)");
    }

    private static async Task<FeatureTestResult> TestVisionAsync(IChatService chatService, string mediaType, CancellationToken ct)
    {
        const string systemPrompt = "You are an image analyzer. Describe what you see briefly.";
        const string userPrompt = "What color is this image? Respond with one word.";

        var result = await chatService.GetVisionCompletionAsync(
            systemPrompt,
            userPrompt,
            TestImagePng,
            "image/png",
            new ChatCompletionOptions { MaxTokens = 20 },
            ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Content))
        {
            return FeatureTestResult.Fail(
                $"No response received. This model may not support vision capabilities required for {mediaType} analysis.");
        }

        return FeatureTestResult.Ok($"Vision test passed - model can analyze {mediaType}s ({result.TotalTokens ?? 0} tokens)");
    }

    private static async Task<FeatureTestResult> TestPromptBuilderAsync(IChatService chatService, CancellationToken ct)
    {
        const string systemPrompt = "You are a helpful assistant.";
        const string userPrompt = "Say 'ready' if you can help build prompts.";

        var result = await chatService.GetCompletionAsync(systemPrompt, userPrompt, new ChatCompletionOptions
        {
            MaxTokens = 10
        }, ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Content))
        {
            return FeatureTestResult.Fail("No response received from model");
        }

        return FeatureTestResult.Ok($"Prompt builder test passed ({result.TotalTokens ?? 0} tokens)");
    }
}
