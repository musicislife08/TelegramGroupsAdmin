using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Implementation of IChatService using Microsoft Semantic Kernel
/// Supports OpenAI, Azure OpenAI, and OpenAI-compatible local endpoints
/// </summary>
public class SemanticKernelChatService : IChatService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly string _modelId;
    private readonly ILogger<SemanticKernelChatService> _logger;

    /// <summary>
    /// Create a Semantic Kernel chat service for the given connection and feature config
    /// </summary>
    public SemanticKernelChatService(
        AIConnection connection,
        AIFeatureConfig featureConfig,
        string? apiKey,
        ILogger<SemanticKernelChatService> logger)
    {
        _logger = logger;

        var builder = Kernel.CreateBuilder();

        // Determine model ID based on provider type
        _modelId = connection.Provider == AIProviderType.AzureOpenAI
            ? featureConfig.AzureDeploymentName ?? featureConfig.Model
            : featureConfig.Model;

        switch (connection.Provider)
        {
            case AIProviderType.OpenAI:
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new ArgumentException("OpenAI API key is required for OpenAI provider");
                }
                builder.AddOpenAIChatCompletion(
                    modelId: featureConfig.Model,
                    apiKey: apiKey);
                break;

            case AIProviderType.AzureOpenAI:
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new ArgumentException("Azure OpenAI API key is required for Azure provider");
                }
                if (string.IsNullOrWhiteSpace(connection.AzureEndpoint))
                {
                    throw new ArgumentException("Azure endpoint is required for Azure provider");
                }
                if (string.IsNullOrWhiteSpace(featureConfig.AzureDeploymentName))
                {
                    throw new ArgumentException("Azure deployment name is required for Azure provider");
                }
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: featureConfig.AzureDeploymentName,
                    endpoint: connection.AzureEndpoint,
                    apiKey: apiKey,
                    apiVersion: connection.AzureApiVersion);
                break;

            case AIProviderType.LocalOpenAI:
                if (string.IsNullOrWhiteSpace(connection.LocalEndpoint))
                {
                    throw new ArgumentException("Local endpoint is required for local provider");
                }
                // Use OpenAI connector with custom endpoint
                // For providers that require API key
                if (connection.LocalRequiresApiKey && !string.IsNullOrWhiteSpace(apiKey))
                {
                    builder.AddOpenAIChatCompletion(
                        modelId: featureConfig.Model,
                        apiKey: apiKey,
                        endpoint: new Uri(connection.LocalEndpoint));
                }
                else
                {
                    // Ollama and other keyless providers - use empty string as API key
                    builder.AddOpenAIChatCompletion(
                        modelId: featureConfig.Model,
                        apiKey: "not-required",  // SK requires non-empty, but Ollama ignores it
                        endpoint: new Uri(connection.LocalEndpoint));
                }
                break;

            default:
                throw new ArgumentException($"Unsupported AI provider type: {connection.Provider}");
        }

        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetCompletionAsync(
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default)
    {
        try
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);
            chatHistory.AddUserMessage(userPrompt);

            var executionSettings = CreateExecutionSettings(options);

            var response = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: _kernel,
                cancellationToken: ct);

            return CreateResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chat completion from {Model}", _modelId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult?> GetVisionCompletionAsync(
        string systemPrompt,
        string userPrompt,
        byte[] imageData,
        string mimeType,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default)
    {
        try
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);

            // Create message with both text and image
            var imageContent = new ImageContent(imageData, mimeType);
            var textContent = new TextContent(userPrompt);

            chatHistory.AddUserMessage([textContent, imageContent]);

            var executionSettings = CreateExecutionSettings(options);

            var response = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: _kernel,
                cancellationToken: ct);

            return CreateResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting vision completion from {Model}", _modelId);
            return null;
        }
    }

    /// <summary>
    /// Create execution settings from options
    /// </summary>
    private static OpenAIPromptExecutionSettings CreateExecutionSettings(ChatCompletionOptions? options)
    {
        var settings = new OpenAIPromptExecutionSettings();

        if (options?.MaxTokens.HasValue == true)
        {
            settings.MaxTokens = options.MaxTokens.Value;
        }

        if (options?.Temperature.HasValue == true)
        {
            settings.Temperature = options.Temperature.Value;
        }

        if (options?.JsonMode == true)
        {
            settings.ResponseFormat = "json_object";
        }

        return settings;
    }

    /// <summary>
    /// Create result from SK response
    /// </summary>
    private ChatCompletionResult? CreateResult(ChatMessageContent response)
    {
        var content = response.Content;
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        // Extract token usage from metadata if available
        int? totalTokens = null;
        int? promptTokens = null;
        int? completionTokens = null;
        string? finishReason = null;

        if (response.Metadata != null)
        {
            if (response.Metadata.TryGetValue("Usage", out var usageObj) && usageObj is OpenAI.Chat.ChatTokenUsage usage)
            {
                totalTokens = usage.TotalTokenCount;
                promptTokens = usage.InputTokenCount;
                completionTokens = usage.OutputTokenCount;
            }

            if (response.Metadata.TryGetValue("FinishReason", out var finishObj) && finishObj is OpenAI.Chat.ChatFinishReason reason)
            {
                finishReason = reason.ToString();
            }
        }

        return new ChatCompletionResult
        {
            Content = content,
            Model = response.ModelId ?? _modelId,
            TotalTokens = totalTokens,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            FinishReason = finishReason
        };
    }
}
