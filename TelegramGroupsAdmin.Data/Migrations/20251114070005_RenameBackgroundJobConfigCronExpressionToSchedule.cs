using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameBackgroundJobConfigCronExpressionToSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename CronExpression → Schedule in JSONB background_jobs_config column
            // Uses jsonb_set to copy the value, then removes the old key
            migrationBuilder.Sql(@"
                UPDATE configs
                SET background_jobs_config = (
                    SELECT jsonb_object_agg(
                        job_key,
                        CASE
                            WHEN job_value ? 'CronExpression' THEN
                                job_value - 'CronExpression' || jsonb_build_object('Schedule', job_value->'CronExpression')
                            ELSE
                                job_value
                        END
                    )
                    FROM jsonb_each(background_jobs_config->'Jobs') AS entries(job_key, job_value)
                )
                WHERE background_jobs_config IS NOT NULL
                  AND background_jobs_config ? 'Jobs';

                -- Wrap the result back in the Jobs object
                UPDATE configs
                SET background_jobs_config = jsonb_build_object('Jobs', background_jobs_config)
                WHERE background_jobs_config IS NOT NULL
                  AND NOT (background_jobs_config ? 'Jobs');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rename Schedule → CronExpression in JSONB background_jobs_config column (reverse migration)
            migrationBuilder.Sql(@"
                UPDATE configs
                SET background_jobs_config = (
                    SELECT jsonb_object_agg(
                        job_key,
                        CASE
                            WHEN job_value ? 'Schedule' THEN
                                job_value - 'Schedule' || jsonb_build_object('CronExpression', job_value->'Schedule')
                            ELSE
                                job_value
                        END
                    )
                    FROM jsonb_each(background_jobs_config->'Jobs') AS entries(job_key, job_value)
                )
                WHERE background_jobs_config IS NOT NULL
                  AND background_jobs_config ? 'Jobs';

                -- Wrap the result back in the Jobs object
                UPDATE configs
                SET background_jobs_config = jsonb_build_object('Jobs', background_jobs_config)
                WHERE background_jobs_config IS NOT NULL
                  AND NOT (background_jobs_config ? 'Jobs');
            ");
        }
    }
}
