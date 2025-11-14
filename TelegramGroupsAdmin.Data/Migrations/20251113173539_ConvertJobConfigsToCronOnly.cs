using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertJobConfigsToCronOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop TickerQ tables (Phase 1: Remove TickerQ infrastructure)
            migrationBuilder.DropTable(
                name: "CronTickerOccurrences",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TimeTickers",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "CronTickers",
                schema: "ticker");

            // Data migration: Convert old job configs (ScheduleType + IntervalDuration) to new format (CronExpression only)
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    config_record RECORD;
                    jobs_json jsonb;
                    job_key text;
                    job_config jsonb;
                    schedule_type text;
                    interval_duration text;
                    cron_expression text;
                    updated_job jsonb;
                    updated_jobs jsonb;
                BEGIN
                    -- Loop through all config records with background_jobs_config
                    FOR config_record IN
                        SELECT id, background_jobs_config
                        FROM configs
                        WHERE background_jobs_config IS NOT NULL
                    LOOP
                        jobs_json := config_record.background_jobs_config->'Jobs';

                        IF jobs_json IS NOT NULL THEN
                            updated_jobs := '{}'::jsonb;

                            -- Loop through each job in the Jobs dictionary
                            FOR job_key IN SELECT jsonb_object_keys(jobs_json)
                            LOOP
                                job_config := jobs_json->job_key;
                                schedule_type := job_config->>'ScheduleType';
                                interval_duration := job_config->>'IntervalDuration';
                                cron_expression := job_config->>'CronExpression';

                                -- Convert interval schedules to cron expressions
                                IF schedule_type = 'interval' AND interval_duration IS NOT NULL AND cron_expression IS NULL THEN
                                    -- Convert common intervals to cron (handled by C# code in next step)
                                    -- For now, mark for conversion by setting a placeholder
                                    cron_expression := 'CONVERT:' || interval_duration;
                                END IF;

                                -- Ensure cron_expression is not null (use Quartz format: 6 fields with seconds)
                                IF cron_expression IS NULL OR cron_expression = '' THEN
                                    cron_expression := '0 0 2 * * ?'; -- Default: daily at 2 AM (Quartz format)
                                END IF;

                                -- Remove old fields and ensure CronExpression exists
                                updated_job := job_config;
                                updated_job := updated_job - 'ScheduleType';
                                updated_job := updated_job - 'IntervalDuration';
                                updated_job := jsonb_set(updated_job, '{CronExpression}', to_jsonb(cron_expression));

                                -- Add to updated jobs
                                updated_jobs := jsonb_set(updated_jobs, ARRAY[job_key], updated_job);
                            END LOOP;

                            -- Update the config record with Jobs wrapper intact
                            UPDATE configs
                            SET background_jobs_config = jsonb_build_object('Jobs', updated_jobs),
                            updated_at = NOW()
                            WHERE id = config_record.id;
                        END IF;
                    END LOOP;
                END $$;
            ");

            // C# post-migration: Convert 'CONVERT:' placeholders to actual cron expressions
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION convert_interval_to_cron(interval_str text) RETURNS text AS $$
                BEGIN
                    -- Convert common interval patterns to Quartz cron expressions (6 fields: sec min hour day month dow)
                    CASE
                        WHEN interval_str ~ '^\d+m$' OR interval_str ~ '^\d+min' THEN
                            -- Minutes: not directly supported in cron, default to hourly
                            RETURN '0 0 * * * ?';
                        WHEN interval_str ~ '^\d+h$' OR interval_str ~ '^\d+hr' THEN
                            -- Hours: extract number and create */N pattern
                            RETURN '0 0 */' || regexp_replace(interval_str, '[^0-9]', '', 'g') || ' * * ?';
                        WHEN interval_str ~ '^\d+d$' OR interval_str ~ '^\d+day' THEN
                            -- Days: daily at 2 AM (Quartz format)
                            RETURN '0 0 2 * * ?';
                        WHEN interval_str ~ '^\d+w$' OR interval_str ~ '^\d+week' THEN
                            -- Weeks: weekly on Sunday at 2 AM (Quartz format)
                            RETURN '0 0 2 ? * SUN';
                        ELSE
                            -- Unknown format: default to daily at 2 AM (Quartz format)
                            RETURN '0 0 2 * * ?';
                    END CASE;
                END;
                $$ LANGUAGE plpgsql;

                UPDATE configs
                SET background_jobs_config = jsonb_build_object(
                    'Jobs',
                    (
                        SELECT jsonb_object_agg(
                            job_key,
                            CASE
                                WHEN job_config->>'CronExpression' LIKE 'CONVERT:%'
                                THEN jsonb_set(
                                    job_config,
                                    '{CronExpression}',
                                    to_jsonb(convert_interval_to_cron(substring(job_config->>'CronExpression' from 9)))
                                )
                                ELSE job_config
                            END
                        )
                        FROM jsonb_each(background_jobs_config->'Jobs') AS jobs(job_key, job_config)
                    )
                ),
                updated_at = NOW()
                WHERE background_jobs_config->'Jobs' IS NOT NULL
                  AND EXISTS (
                      SELECT 1
                      FROM jsonb_each(background_jobs_config->'Jobs') AS jobs(job_key, job_config)
                      WHERE job_config->>'CronExpression' LIKE 'CONVERT:%'
                  );

                DROP FUNCTION convert_interval_to_cron(text);
            ");

            // Rename job keys from TickerQ-style to Quartz-style (e.g., BlocklistSync → BlocklistSyncJob)
            // Then remove any old keys that weren't renamed (keep only Quartz job names)
            migrationBuilder.Sql(@"
                -- First pass: Rename old job keys to new Quartz-style names
                UPDATE configs
                SET background_jobs_config = jsonb_build_object(
                    'Jobs',
                    (
                        SELECT jsonb_object_agg(
                            CASE job_key
                                WHEN 'BlocklistSync' THEN 'BlocklistSyncJob'
                                WHEN 'message_cleanup' THEN 'DatabaseMaintenanceJob'
                                WHEN 'scheduled_backup' THEN 'ScheduledBackupJob'
                                WHEN 'chat_health_check' THEN 'ChatHealthCheckJob'
                                WHEN 'refresh_user_photos' THEN 'RefreshUserPhotosJob'
                                WHEN 'database_maintenance' THEN 'DatabaseMaintenanceJob'
                                ELSE job_key
                            END,
                            job_config
                        )
                        FROM jsonb_each(background_jobs_config->'Jobs') AS jobs(job_key, job_config)
                    )
                ),
                updated_at = NOW()
                WHERE background_jobs_config->'Jobs' IS NOT NULL;

                -- Second pass: Keep only valid Quartz job names (removes duplicates/old keys)
                UPDATE configs
                SET background_jobs_config = jsonb_build_object(
                    'Jobs',
                    (
                        SELECT jsonb_object_agg(job_key, job_config)
                        FROM jsonb_each(background_jobs_config->'Jobs') AS jobs(job_key, job_config)
                        WHERE job_key IN (
                            'BlocklistSyncJob',
                            'ChatHealthCheckJob',
                            'DatabaseMaintenanceJob',
                            'DeleteMessageJob',
                            'DeleteUserMessagesJob',
                            'FetchUserPhotoJob',
                            'FileScanJob',
                            'RefreshUserPhotosJob',
                            'RotateBackupPassphraseJob',
                            'ScheduledBackupJob',
                            'TempbanExpiryJob',
                            'WelcomeTimeoutJob'
                        )
                    )
                ),
                updated_at = NOW()
                WHERE background_jobs_config->'Jobs' IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ticker");

            migrationBuilder.CreateTable(
                name: "CronTickers",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Expression = table.Column<string>(type: "text", nullable: true),
                    Function = table.Column<string>(type: "text", nullable: true),
                    InitIdentifier = table.Column<string>(type: "text", nullable: true),
                    Request = table.Column<byte[]>(type: "bytea", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: false),
                    RetryIntervals = table.Column<int[]>(type: "integer[]", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronTickers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeTickers",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchParent = table.Column<Guid>(type: "uuid", nullable: true),
                    BatchRunCondition = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ElapsedTime = table.Column<long>(type: "bigint", nullable: false),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Function = table.Column<string>(type: "text", nullable: true),
                    InitIdentifier = table.Column<string>(type: "text", nullable: true),
                    LockHolder = table.Column<string>(type: "text", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Request = table.Column<byte[]>(type: "bytea", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    RetryIntervals = table.Column<int[]>(type: "integer[]", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeTickers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeTickers_TimeTickers_BatchParent",
                        column: x => x.BatchParent,
                        principalSchema: "ticker",
                        principalTable: "TimeTickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CronTickerOccurrences",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CronTickerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElapsedTime = table.Column<long>(type: "bigint", nullable: false),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockHolder = table.Column<string>(type: "text", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronTickerOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CronTickerOccurrences_CronTickers_CronTickerId",
                        column: x => x.CronTickerId,
                        principalSchema: "ticker",
                        principalTable: "CronTickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences",
                column: "CronTickerId");

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                column: "ExecutionTime");

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "UQ_CronTickerId_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "CronTickerId", "ExecutionTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_Expression",
                schema: "ticker",
                table: "CronTickers",
                column: "Expression");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                column: "ExecutionTime");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTickers_BatchParent",
                schema: "ticker",
                table: "TimeTickers",
                column: "BatchParent");
        }
    }
}
