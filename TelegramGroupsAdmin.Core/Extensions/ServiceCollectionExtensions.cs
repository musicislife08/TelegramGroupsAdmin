using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Utility services
        services.AddSingleton<SimHashService>(); // SimHash fingerprinting for O(1) deduplication

        // Audit services
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditService, AuditService>();

        // AI services (Semantic Kernel multi-provider support)
        // IChatService is Scoped (matches ISystemConfigRepository), kernel cache is static
        services.AddScoped<IChatService, SemanticKernelChatService>();
        services.AddScoped<IAIServiceFactory, AIServiceFactory>();
        services.AddScoped<IAITranslationService, AITranslationService>();
        services.AddScoped<IFeatureTestService, FeatureTestService>();

        return services;
    }
}
