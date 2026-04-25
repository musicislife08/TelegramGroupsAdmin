using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.AI.Services;

namespace TelegramGroupsAdmin.AI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIServices(this IServiceCollection services)
    {
        // AI services (Semantic Kernel multi-provider support)
        // IChatService is Scoped (matches ISystemConfigRepository), kernel cache is static
        services.AddScoped<IChatService, SemanticKernelChatService>();
        services.AddScoped<IAIServiceFactory, AIServiceFactory>();
        services.AddScoped<IAITranslationService, AITranslationService>();
        services.AddScoped<IFeatureTestService, FeatureTestService>();
        return services;
    }
}
