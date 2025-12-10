using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Tests AI feature configurations by making actual API calls via IChatService
/// </summary>
public class FeatureTestService(
    IChatService chatService,
    ILogger<FeatureTestService> logger) : IFeatureTestService
{
    // 64x64 solid red PNG for vision tests (289 bytes)
    private static readonly byte[] TestImagePng =
    [
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x40,
        0x08, 0x06, 0x00, 0x00, 0x00, 0xaa, 0x69, 0x71, 0xde, 0x00, 0x00, 0x00,
        0x01, 0x73, 0x52, 0x47, 0x42, 0x00, 0xae, 0xce, 0x1c, 0xe9, 0x00, 0x00,
        0x00, 0x44, 0x65, 0x58, 0x49, 0x66, 0x4d, 0x4d, 0x00, 0x2a, 0x00, 0x00,
        0x00, 0x08, 0x00, 0x01, 0x87, 0x69, 0x00, 0x04, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x1a, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0xa0, 0x01,
        0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xa0, 0x02,
        0x00, 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x40, 0xa0, 0x03,
        0x00, 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00,
        0x00, 0x00, 0x46, 0x51, 0x42, 0xb0, 0x00, 0x00, 0x00, 0x8b, 0x49, 0x44,
        0x41, 0x54, 0x78, 0x01, 0xed, 0xd5, 0x81, 0x0d, 0x00, 0x10, 0x14, 0x43,
        0xc1, 0xcf, 0xfe, 0x3b, 0x23, 0x31, 0xc6, 0x3b, 0xb1, 0x80, 0xa6, 0xa7,
        0xeb, 0xcc, 0xbc, 0xdb, 0x3d, 0xbb, 0xfb, 0xf4, 0xff, 0x72, 0x01, 0x68,
        0x40, 0x3c, 0x01, 0x04, 0xe2, 0x05, 0x18, 0x0d, 0xd0, 0x80, 0x78, 0x02,
        0x08, 0xc4, 0x0b, 0xe0, 0x13, 0x44, 0x00, 0x81, 0x78, 0x02, 0x08, 0xc4,
        0x0b, 0x60, 0x05, 0x10, 0x40, 0x20, 0x9e, 0x00, 0x02, 0xf1, 0x02, 0x58,
        0x01, 0x04, 0x10, 0x88, 0x27, 0x80, 0x40, 0xbc, 0x00, 0x56, 0x00, 0x01,
        0x04, 0xe2, 0x09, 0x20, 0x10, 0x2f, 0x80, 0x15, 0x40, 0x00, 0x81, 0x78,
        0x02, 0x08, 0xc4, 0x0b, 0x60, 0x05, 0x10, 0x40, 0x20, 0x9e, 0x00, 0x02,
        0xf1, 0x02, 0x58, 0x01, 0x04, 0x10, 0x88, 0x27, 0x80, 0x40, 0xbc, 0x00,
        0x56, 0x00, 0x01, 0x04, 0xe2, 0x09, 0x20, 0x10, 0x2f, 0x80, 0x15, 0x40,
        0x00, 0x81, 0x78, 0x02, 0x17, 0x53, 0x35, 0x02, 0x7e, 0xe2, 0x69, 0x6e,
        0x88, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60,
        0x82
    ];

    private const int DefaultMaxTokens = 500;

    public async Task<FeatureTestResult> TestFeatureAsync(
        AIFeatureType featureType,
        string connectionId,
        string model,
        string? azureDeploymentName = null,
        int? maxTokens = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(model);

        // Early validation: if azureDeploymentName was explicitly provided (not null) but is empty/whitespace,
        // return a clear error instead of letting it fail deep in the kernel builder
        if (azureDeploymentName is not null && string.IsNullOrWhiteSpace(azureDeploymentName))
        {
            return FeatureTestResult.Fail(
                "Azure deployment name is required",
                "Enter the deployment name from your Azure OpenAI resource in the Azure Portal.");
        }

        var tokens = maxTokens ?? DefaultMaxTokens;

        try
        {
            // Run feature-specific test using IChatService test methods
            return featureType switch
            {
                AIFeatureType.SpamDetection => await TestSpamDetectionAsync(connectionId, model, azureDeploymentName, tokens, ct),
                AIFeatureType.Translation => await TestTranslationAsync(connectionId, model, azureDeploymentName, tokens, ct),
                AIFeatureType.ImageAnalysis => await TestVisionAsync(connectionId, model, azureDeploymentName, "image", tokens, ct),
                AIFeatureType.VideoAnalysis => await TestVisionAsync(connectionId, model, azureDeploymentName, "video frame", tokens, ct),
                AIFeatureType.PromptBuilder => await TestPromptBuilderAsync(connectionId, model, azureDeploymentName, tokens, ct),
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

    private async Task<FeatureTestResult> TestSpamDetectionAsync(
        string connectionId, string model, string? azureDeploymentName, int maxTokens, CancellationToken ct)
    {
        const string systemPrompt = "You are a test assistant. Follow instructions exactly.";
        const string userPrompt = "Respond with only 'OK'.";

        var result = await chatService.TestCompletionAsync(
            connectionId,
            model,
            azureDeploymentName,
            systemPrompt,
            userPrompt,
            new ChatCompletionOptions { MaxTokens = maxTokens },
            ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Content))
        {
            return FeatureTestResult.Fail("No response received from model");
        }

        return FeatureTestResult.Ok($"Model responded successfully ({result.TotalTokens ?? 0} tokens)");
    }

    private async Task<FeatureTestResult> TestTranslationAsync(
        string connectionId, string model, string? azureDeploymentName, int maxTokens, CancellationToken ct)
    {
        const string systemPrompt = "You are a translator. Respond with only the translation, nothing else.";
        const string userPrompt = "Translate 'Hello' to Spanish.";

        var result = await chatService.TestCompletionAsync(
            connectionId,
            model,
            azureDeploymentName,
            systemPrompt,
            userPrompt,
            new ChatCompletionOptions { MaxTokens = maxTokens },
            ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Content))
        {
            return FeatureTestResult.Fail("No response received from model");
        }

        return FeatureTestResult.Ok($"Translation test passed ({result.TotalTokens ?? 0} tokens)");
    }

    private async Task<FeatureTestResult> TestVisionAsync(
        string connectionId, string model, string? azureDeploymentName, string mediaType, int maxTokens, CancellationToken ct)
    {
        const string systemPrompt = "You are an image analyzer. Describe what you see briefly.";
        const string userPrompt = "What color is this image? Respond with one word.";

        var result = await chatService.TestVisionCompletionAsync(
            connectionId,
            model,
            azureDeploymentName,
            systemPrompt,
            userPrompt,
            TestImagePng,
            "image/png",
            new ChatCompletionOptions { MaxTokens = maxTokens },
            ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Content))
        {
            return FeatureTestResult.Fail(
                $"No response received. This model may not support vision capabilities required for {mediaType} analysis.");
        }

        return FeatureTestResult.Ok($"Vision test passed - model can analyze {mediaType}s ({result.TotalTokens ?? 0} tokens)");
    }

    private async Task<FeatureTestResult> TestPromptBuilderAsync(
        string connectionId, string model, string? azureDeploymentName, int maxTokens, CancellationToken ct)
    {
        const string systemPrompt = "You are a helpful assistant.";
        const string userPrompt = "Say 'ready' if you can help build prompts.";

        var result = await chatService.TestCompletionAsync(
            connectionId,
            model,
            azureDeploymentName,
            systemPrompt,
            userPrompt,
            new ChatCompletionOptions { MaxTokens = maxTokens },
            ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Content))
        {
            return FeatureTestResult.Fail("No response received from model");
        }

        return FeatureTestResult.Ok($"Prompt builder test passed ({result.TotalTokens ?? 0} tokens)");
    }
}
