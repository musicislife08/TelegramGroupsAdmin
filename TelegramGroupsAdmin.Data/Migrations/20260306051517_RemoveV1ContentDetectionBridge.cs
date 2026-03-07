using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveV1ContentDetectionBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ──────────────────────────────────────────────────────────────
            // Step 1: Drop ALL views that depend on detection_results
            // (is_spam column can't be dropped while views reference it)
            // ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);

            // ──────────────────────────────────────────────────────────────
            // Step 2: Drop indexes on is_spam, then drop the computed column
            // Indexes are dependent objects that block column drop
            // ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_detection_results_is_spam");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_detection_results_is_spam_detected_at");
            migrationBuilder.Sql("ALTER TABLE detection_results DROP COLUMN IF EXISTS is_spam");

            // ──────────────────────────────────────────────────────────────
            // Step 3: Add new double columns, migrate data, drop old int columns
            // Conversion: V1 (0-100 int) / 20.0 = V2 (0.0-5.0 double)
            // ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                ALTER TABLE detection_results ADD COLUMN score double precision NOT NULL DEFAULT 0;
                ALTER TABLE detection_results ADD COLUMN net_score double precision NOT NULL DEFAULT 0;

                UPDATE detection_results SET
                    score = confidence / 20.0,
                    net_score = net_confidence / 20.0;

                ALTER TABLE detection_results DROP COLUMN confidence;
                ALTER TABLE detection_results DROP COLUMN net_confidence;
                """);

            // ──────────────────────────────────────────────────────────────
            // Step 4: Recreate is_spam computed column with new formula
            // ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                ALTER TABLE detection_results
                ADD COLUMN is_spam boolean GENERATED ALWAYS AS (net_score > 0) STORED
                """);

            // Recreate indexes on is_spam
            migrationBuilder.Sql("""
                CREATE INDEX ix_detection_results_is_spam
                ON detection_results (is_spam);

                CREATE INDEX ix_detection_results_is_spam_detected_at
                ON detection_results (is_spam, detected_at);
                """);

            // ──────────────────────────────────────────────────────────────
            // Step 5: Migrate JSONB check_results_json from V1 to V2 format
            //
            // V1 format per check: { CheckName, Result (enum int), Confidence (int 0-100), Reason }
            // V2 format per check: { CheckName, Score (double 0-5), Abstained (bool), Details, ProcessingTimeMs }
            //
            // Conversion rules:
            //   Score = Confidence / 20.0
            //   Abstained = (Result == 0)  -- CheckResultType.Clean = 0 meant "no evidence"
            //   Details = Reason
            //   ProcessingTimeMs = preserved if present (was added late in V1)
            // ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                UPDATE detection_results
                SET check_results_json = COALESCE(
                    (SELECT jsonb_build_object('Checks',
                        jsonb_agg(
                            jsonb_build_object(
                                'CheckName', elem->>'CheckName',
                                'Score', COALESCE((elem->>'Confidence')::int, 0) / 20.0,
                                'Abstained', COALESCE((elem->>'Result')::int, 0) = 0,
                                'Details', COALESCE(elem->>'Reason', ''),
                                'ProcessingTimeMs', COALESCE((elem->>'ProcessingTimeMs')::double precision, 0)
                            )
                        )
                    )
                    FROM jsonb_array_elements(check_results_json->'Checks') AS elem),
                    '{"Checks": []}'::jsonb
                )
                WHERE check_results_json IS NOT NULL
                  AND check_results_json ? 'Checks'
                  AND check_results_json->'Checks' != '[]'::jsonb
                  AND jsonb_typeof(check_results_json->'Checks') = 'array'
                  AND (check_results_json->'Checks'->0) ? 'Confidence'
                """);

            // ──────────────────────────────────────────────────────────────
            // Step 6: Migrate JSONB config thresholds from V1 int to V2 double
            // V1: AutoBanThreshold=80, ReviewQueueThreshold=50, MaxConfidenceVetoThreshold=85
            // V2: AutoBanThreshold=4.0, ReviewQueueThreshold=2.5, MaxConfidenceVetoThreshold=4.25
            // ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(
                    jsonb_set(
                        jsonb_set(
                            config_json,
                            '{AutoBanThreshold}',
                            to_jsonb(COALESCE((config_json->>'AutoBanThreshold')::double precision, 80) / 20.0)
                        ),
                        '{ReviewQueueThreshold}',
                        to_jsonb(COALESCE((config_json->>'ReviewQueueThreshold')::double precision, 50) / 20.0)
                    ),
                    '{MaxConfidenceVetoThreshold}',
                    to_jsonb(COALESCE((config_json->>'MaxConfidenceVetoThreshold')::double precision, 85) / 20.0)
                )
                WHERE config_json IS NOT NULL
                  AND config_json ? 'AutoBanThreshold'
                  AND (config_json->>'AutoBanThreshold')::double precision > 5.0
                """);

            // ──────────────────────────────────────────────────────────────
            // Step 6b: Migrate nested sub-config thresholds from V1 (0-100) to V2 (0.0-5.0)
            // Only migrate values > 5.0 (indicating they're still on V1 scale)
            // Paths: {SubConfig,ConfidenceThreshold} or {SubConfig,OcrConfidenceThreshold}
            // ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{StopWords,ConfidenceThreshold}',
                    to_jsonb((config_json #>> '{StopWords,ConfidenceThreshold}')::double precision / 20.0))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{StopWords,ConfidenceThreshold}' IS NOT NULL
                  AND (config_json #>> '{StopWords,ConfidenceThreshold}')::double precision > 5.0
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{Bayes,ConfidenceThreshold}',
                    to_jsonb((config_json #>> '{Bayes,ConfidenceThreshold}')::double precision / 20.0))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{Bayes,ConfidenceThreshold}' IS NOT NULL
                  AND (config_json #>> '{Bayes,ConfidenceThreshold}')::double precision > 5.0
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{AIVeto,ConfidenceThreshold}',
                    to_jsonb((config_json #>> '{AIVeto,ConfidenceThreshold}')::double precision / 20.0))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{AIVeto,ConfidenceThreshold}' IS NOT NULL
                  AND (config_json #>> '{AIVeto,ConfidenceThreshold}')::double precision > 5.0
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{Translation,ConfidenceThreshold}',
                    to_jsonb((config_json #>> '{Translation,ConfidenceThreshold}')::double precision / 20.0))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{Translation,ConfidenceThreshold}' IS NOT NULL
                  AND (config_json #>> '{Translation,ConfidenceThreshold}')::double precision > 5.0
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{Spacing,ConfidenceThreshold}',
                    to_jsonb((config_json #>> '{Spacing,ConfidenceThreshold}')::double precision / 20.0))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{Spacing,ConfidenceThreshold}' IS NOT NULL
                  AND (config_json #>> '{Spacing,ConfidenceThreshold}')::double precision > 5.0
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{ImageContent,OcrConfidenceThreshold}',
                    to_jsonb((config_json #>> '{ImageContent,OcrConfidenceThreshold}')::double precision / 20.0))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{ImageContent,OcrConfidenceThreshold}' IS NOT NULL
                  AND (config_json #>> '{ImageContent,OcrConfidenceThreshold}')::double precision > 5.0
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{VideoContent,OcrConfidenceThreshold}',
                    to_jsonb((config_json #>> '{VideoContent,OcrConfidenceThreshold}')::double precision / 20.0))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{VideoContent,OcrConfidenceThreshold}' IS NOT NULL
                  AND (config_json #>> '{VideoContent,OcrConfidenceThreshold}')::double precision > 5.0
                """);

            // ──────────────────────────────────────────────────────────────
            // Step 7: Recreate views with V2 column names
            // ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql(EnrichedDetectionView.CreateViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.CreateViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop views first (depend on columns)
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);

            // Drop indexes and computed column
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_detection_results_is_spam");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_detection_results_is_spam_detected_at");
            migrationBuilder.Sql("ALTER TABLE detection_results DROP COLUMN IF EXISTS is_spam");

            // Reverse column migration: V2 → V1
            migrationBuilder.Sql("""
                ALTER TABLE detection_results ADD COLUMN confidence integer NOT NULL DEFAULT 0;
                ALTER TABLE detection_results ADD COLUMN net_confidence integer NOT NULL DEFAULT 0;

                UPDATE detection_results SET
                    confidence = (score * 20)::integer,
                    net_confidence = (net_score * 20)::integer;

                ALTER TABLE detection_results DROP COLUMN score;
                ALTER TABLE detection_results DROP COLUMN net_score;
                """);

            // Recreate is_spam with V1 formula
            migrationBuilder.Sql("""
                ALTER TABLE detection_results
                ADD COLUMN is_spam boolean GENERATED ALWAYS AS (net_confidence > 0) STORED
                """);

            // Reverse JSONB check_results_json: V2 → V1
            migrationBuilder.Sql("""
                UPDATE detection_results
                SET check_results_json = COALESCE(
                    (SELECT jsonb_build_object('Checks',
                        jsonb_agg(
                            jsonb_build_object(
                                'CheckName', elem->>'CheckName',
                                'Result', CASE WHEN (elem->>'Abstained')::boolean THEN 0 ELSE 1 END,
                                'Confidence', ((elem->>'Score')::double precision * 20)::integer,
                                'Reason', COALESCE(elem->>'Details', ''),
                                'ProcessingTimeMs', COALESCE((elem->>'ProcessingTimeMs')::double precision, 0)
                            )
                        )
                    )
                    FROM jsonb_array_elements(check_results_json->'Checks') AS elem),
                    '{"Checks": []}'::jsonb
                )
                WHERE check_results_json IS NOT NULL
                  AND check_results_json ? 'Checks'
                  AND check_results_json->'Checks' != '[]'::jsonb
                  AND jsonb_typeof(check_results_json->'Checks') = 'array'
                """);

            // Reverse config thresholds: V2 → V1
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(
                    jsonb_set(
                        jsonb_set(
                            config_json,
                            '{AutoBanThreshold}',
                            to_jsonb(((config_json->>'AutoBanThreshold')::double precision * 20)::integer)
                        ),
                        '{ReviewQueueThreshold}',
                        to_jsonb(((config_json->>'ReviewQueueThreshold')::double precision * 20)::integer)
                    ),
                    '{MaxConfidenceVetoThreshold}',
                    to_jsonb(((config_json->>'MaxConfidenceVetoThreshold')::double precision * 20)::integer)
                )
                WHERE config_json IS NOT NULL
                  AND config_json ? 'AutoBanThreshold'
                """);

            // Reverse nested sub-config thresholds: V2 → V1
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{StopWords,ConfidenceThreshold}',
                    to_jsonb(((config_json #>> '{StopWords,ConfidenceThreshold}')::double precision * 20)::integer))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{StopWords,ConfidenceThreshold}' IS NOT NULL
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{Bayes,ConfidenceThreshold}',
                    to_jsonb(((config_json #>> '{Bayes,ConfidenceThreshold}')::double precision * 20)::integer))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{Bayes,ConfidenceThreshold}' IS NOT NULL
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{AIVeto,ConfidenceThreshold}',
                    to_jsonb(((config_json #>> '{AIVeto,ConfidenceThreshold}')::double precision * 20)::integer))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{AIVeto,ConfidenceThreshold}' IS NOT NULL
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{Translation,ConfidenceThreshold}',
                    to_jsonb(((config_json #>> '{Translation,ConfidenceThreshold}')::double precision * 20)::integer))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{Translation,ConfidenceThreshold}' IS NOT NULL
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{Spacing,ConfidenceThreshold}',
                    to_jsonb(((config_json #>> '{Spacing,ConfidenceThreshold}')::double precision * 20)::integer))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{Spacing,ConfidenceThreshold}' IS NOT NULL
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{ImageContent,OcrConfidenceThreshold}',
                    to_jsonb(((config_json #>> '{ImageContent,OcrConfidenceThreshold}')::double precision * 20)::integer))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{ImageContent,OcrConfidenceThreshold}' IS NOT NULL
                """);

            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{VideoContent,OcrConfidenceThreshold}',
                    to_jsonb(((config_json #>> '{VideoContent,OcrConfidenceThreshold}')::double precision * 20)::integer))
                WHERE config_json IS NOT NULL
                  AND config_json #>> '{VideoContent,OcrConfidenceThreshold}' IS NOT NULL
                """);

            // Note: Down does NOT recreate views with old SQL since the view consts now have V2 SQL.
            // A rollback past this migration requires manually restoring the old view definitions.
        }
    }
}
