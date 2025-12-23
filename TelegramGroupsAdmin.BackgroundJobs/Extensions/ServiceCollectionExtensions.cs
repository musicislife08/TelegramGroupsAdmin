using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Constants;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.BackgroundJobs.Listeners;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.BackgroundJobs.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all background job services: Quartz.NET, BackgroundJobConfigService, Backup services
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        // Register BackgroundJob configuration service
        services.AddSingleton<IBackgroundJobConfigService, BackgroundJobConfigService>();

        // Register manual job trigger service
        services.AddScoped<IJobTriggerService, JobTriggerService>();

        // Register ad-hoc job scheduler (for one-time delayed jobs)
        services.AddSingleton<Core.BackgroundJobs.IJobScheduler, QuartzJobScheduler>();

        // Register Backup services
        services.AddBackupServices();

        // Register retry listener
        services.AddSingleton<RetryJobListener>();

        // Register scheduling sync service (syncs database configs to Quartz triggers)
        services.AddHostedService<QuartzSchedulingSyncService>();

        // Configure Quartz.NET
        services.AddQuartz(q =>
        {
            // Microsoft DI is now the default job factory in Quartz 3.x

            // Configure thread pool (MaxConcurrency = 4)
            q.UseDefaultThreadPool(tp =>
            {
                tp.MaxConcurrency = QuartzConstants.MaxConcurrency;
            });

            // Configure PostgreSQL persistence
            var connectionString = configuration.GetConnectionString("PostgreSQL");
            q.UsePersistentStore(store =>
            {
                store.UsePostgres(postgres =>
                {
                    postgres.ConnectionString = connectionString!;
                    postgres.TablePrefix = "quartz.qrtz_"; // Use quartz schema
                });
                store.UseSystemTextJsonSerializer(); // Modern System.Text.Json serializer
            });

            // Register retry listener globally (applies to all jobs)
            q.AddJobListener<RetryJobListener>();

            // Register all jobs
            RegisterJobs(q);
        });

        // Add Quartz hosted service
        services.AddQuartzHostedService(options =>
        {
            // Wait for jobs to complete on shutdown
            options.WaitForJobsToComplete = true;
        });

        return services;
    }

    private static void RegisterJobs(IServiceCollectionQuartzConfigurator q)
    {
        // Register each job as durable (allows triggers to be added dynamically)
        // StoreDurably() tells Quartz to keep the job definition even without triggers
        q.AddJob<BlocklistSyncJob>(opts => opts.WithIdentity("BlocklistSyncJob").StoreDurably());
        q.AddJob<ChatHealthCheckJob>(opts => opts.WithIdentity("ChatHealthCheckJob").StoreDurably());
        q.AddJob<DatabaseMaintenanceJob>(opts => opts.WithIdentity("DatabaseMaintenanceJob").StoreDurably());
        q.AddJob<DeleteMessageJob>(opts => opts.WithIdentity("DeleteMessageJob").StoreDurably());
        q.AddJob<DeleteUserMessagesJob>(opts => opts.WithIdentity("DeleteUserMessagesJob").StoreDurably());
        q.AddJob<FetchUserPhotoJob>(opts => opts.WithIdentity("FetchUserPhotoJob").StoreDurably());
        q.AddJob<FileScanJob>(opts => opts.WithIdentity("FileScanJob").StoreDurably());
        q.AddJob<RefreshUserPhotosJob>(opts => opts.WithIdentity("RefreshUserPhotosJob").StoreDurably());
        q.AddJob<RotateBackupPassphraseJob>(opts => opts.WithIdentity("RotateBackupPassphraseJob").StoreDurably());
        q.AddJob<ScheduledBackupJob>(opts => opts.WithIdentity("ScheduledBackupJob").StoreDurably());
        q.AddJob<SendChatNotificationJob>(opts => opts.WithIdentity("SendChatNotificationJob").StoreDurably());
        q.AddJob<TempbanExpiryJob>(opts => opts.WithIdentity("TempbanExpiryJob").StoreDurably());
        q.AddJob<TextClassifierRetrainingJob>(opts => opts.WithIdentity("TextClassifierRetrainingJob").StoreDurably());
        q.AddJob<WelcomeTimeoutJob>(opts => opts.WithIdentity("WelcomeTimeoutJob").StoreDurably());

        // Note: Triggers will be created dynamically by QuartzSchedulingSyncService
        // based on database configuration (BackgroundJobConfig.Schedule)
    }

    private static IServiceCollection AddBackupServices(this IServiceCollection services)
    {
        // Public interfaces
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IBackupEncryptionService, BackupEncryptionService>();
        services.AddScoped<IBackupConfigurationService, BackupConfigurationService>();
        services.AddScoped<IPassphraseManagementService, PassphraseManagementService>();

        // Internal handler services (required by BackupService)
        services.AddScoped<BackupRetentionService>();
        services.AddScoped<TableDiscoveryService>();
        services.AddScoped<TableExportService>();
        services.AddScoped<DependencyResolutionService>();

        return services;
    }
}
