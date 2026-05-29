using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.AI.Services;

namespace TelegramGroupsAdmin.AI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIServices(this IServiceCollection services)
    {
        // AI services (Microsoft.Extensions.AI multi-provider support)
        // IChatService is Scoped (matches ISystemConfigRepository); the IChatClient cache is static
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IAIServiceFactory, AIServiceFactory>();
        services.AddScoped<IAITranslationService, AITranslationService>();
        services.AddScoped<IFeatureTestService, FeatureTestService>();
        return services;
    }
}
