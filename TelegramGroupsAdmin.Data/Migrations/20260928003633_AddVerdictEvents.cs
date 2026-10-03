using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <summary>
    /// Turns the legacy detection_results columns into verdict events (source + classification) and
    /// creates the message_verdicts view. The SQL below is frozen, so it uses the numeric codes:
    /// <list type="bullet">
    /// <item>source (VerdictSource): 0 ContentScan, 1 FileScan, 10 AutoBan, 11 WebMarkSpam, 12 WebMarkHam,
    /// 13 SpamCommand, 14 ReviewSpam, 15 ReviewDismiss (retired by ReplaceReviewDismissWithReviewClean),
    /// 16 TrainingDataPage, 17 TrainingExclude, 18 Import, 99 LegacyManual; 19 ReviewClean arrives later.</item>
    /// <item>classification (VerdictClassification): 0 ExplicitSpam, 1 ExplicitHam, 2 ImplicitSpam,
    /// 3 ImplicitHam, 4 UntrainedSpam, 5 UntrainedHam; 6 Unscanned exists only in message_verdicts.</item>
    /// <item>CheckName inside check_results_json: 6 OpenAI, 8 UrlBlocklist.</item>
    /// </list>
    /// </summary>
    public partial class AddVerdictEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "media_features",
                table: "messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "audit_log_id",
                table: "detection_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "classification",
                table: "detection_results",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "properties",
                table: "detection_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "detection_results",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_detection_results_classification",
                table: "detection_results",
                column: "classification");

            migrationBuilder.CreateIndex(
                name: "ix_detection_results_source",
                table: "detection_results",
                column: "source");

            migrationBuilder.CreateIndex(
                name: "ix_detection_results_verdict_latest",
                table: "detection_results",
                columns: new[] { "chat_id", "message_id", "detected_at", "id" },
                descending: new[] { false, false, true, true });

            // ── 1. source: what caused each legacy row ────────────────────────────────
            // Every detection_source the app ever wrote is known; anything else would be silently turned
            // into an explicit training decision below, so stop instead.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM detection_results
                               WHERE detection_source IS NULL
                                  OR detection_source NOT IN ('auto', 'auto_detection', 'file_scan', 'tg-spam-import', 'manual')) THEN
                        RAISE EXCEPTION 'detection_results has an unknown detection_source; map it in AddVerdictEvents before migrating';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                UPDATE detection_results SET source = CASE
                    WHEN detection_source IN ('auto', 'auto_detection') THEN 0
                    WHEN detection_source = 'file_scan' THEN 1
                    WHEN detection_source = 'tg-spam-import' THEN 18
                    WHEN chat_id = 0 THEN 16
                    WHEN reason LIKE 'Manually marked as spam by admin via UI%' THEN 11
                    WHEN reason LIKE 'Manually marked as ham%'
                      OR reason LIKE 'Manually added as ham training sample%' THEN 12
                    WHEN reason LIKE 'Spam detected via /spam%' THEN 13
                    WHEN reason LIKE 'Report #%' THEN 14
                    ELSE 99
                END;
                """);

            // ── 1b. repair converted OpenAI clean answers ──────────────────────────────
            // RemoveV1ContentDetectionBridge (2026-03-06) converted V1 check results with
            // Abstained = (Result == Clean) and Score = Confidence / 20. For OpenAI a V1 "clean" was an
            // answer (the veto), not "no evidence", so every clean answer before that migration now reads
            // as abstained. Restore the veto encoding (Abstained=false, Score=0), recognised by the answer
            // text, so the classification below, the classifier and the veto analytics read them as vetoes.
            // Real abstentions (too short, API errors, no key) have other text and stay abstained. Checks
            // go through the same array guard as step 2a, so a non-array value is read as no checks.
            // Other checks' converted "clean" results also read as abstained with a leftover score; no
            // reader counts an abstained non-OpenAI check, so they are left as they are.
            migrationBuilder.Sql("""
                WITH repaired AS (
                    SELECT d.id,
                           jsonb_agg(CASE
                               WHEN (e.c->>'CheckName')::int = 6
                                    AND (e.c->>'Abstained')::boolean
                                    AND e.c->>'Details' ~ '^(OpenAI vetoed spam|OpenAI: Clean -|AI: Clean -)'
                               THEN e.c || '{"Abstained": false, "Score": 0}'::jsonb
                               ELSE e.c END ORDER BY e.ord) AS checks,
                           bool_or((e.c->>'CheckName')::int = 6
                                   AND (e.c->>'Abstained')::boolean
                                   AND e.c->>'Details' ~ '^(OpenAI vetoed spam|OpenAI: Clean -|AI: Clean -)') AS changed
                    FROM detection_results d
                    CROSS JOIN LATERAL jsonb_array_elements(
                        CASE WHEN jsonb_typeof(d.check_results_json->'Checks') = 'array'
                             THEN d.check_results_json->'Checks' ELSE '[]'::jsonb END) WITH ORDINALITY AS e(c, ord)
                    WHERE d.source = 0
                    GROUP BY d.id)
                UPDATE detection_results d SET
                    check_results_json = jsonb_set(d.check_results_json, '{Checks}', r.checks),
                    properties = COALESCE(d.properties, '{}'::jsonb) || jsonb_build_object('repaired_legacy_veto', true)
                FROM repaired r
                WHERE d.id = r.id AND r.changed;
                """);

            // ── 2a. classification: content scans (one rule: score >= ReviewQueueThreshold) ─
            migrationBuilder.Sql("""
                UPDATE detection_results dr SET
                    classification = CASE
                        WHEN x.is_spam AND dr.used_for_training THEN 2   -- ImplicitSpam
                        WHEN x.is_spam THEN 4                            -- UntrainedSpam
                        WHEN x.ai_positive THEN 5                        -- UntrainedHam (AI review below threshold)
                        ELSE 3                                           -- ImplicitHam
                    END,
                    properties = COALESCE(dr.properties, '{}'::jsonb) || jsonb_build_object('backfilled', true)
                FROM (
                    SELECT d.id,
                        CASE
                            WHEN ai.c IS NOT NULL AND NOT (ai.c->>'Abstained')::boolean
                                 AND (ai.c->>'Score')::double precision = 0 THEN false   -- AI veto
                            WHEN hb.is_hard_block THEN true
                            ELSE d.net_score >= COALESCE(chat_cfg.rq, global_cfg.rq, 2.5)
                        END AS is_spam,
                        COALESCE(ai.c IS NOT NULL AND NOT (ai.c->>'Abstained')::boolean
                                 AND (ai.c->>'Score')::double precision > 0, false) AS ai_positive
                    FROM detection_results d
                    LEFT JOIN LATERAL (
                        SELECT c FROM jsonb_array_elements((CASE WHEN jsonb_typeof(d.check_results_json->'Checks') = 'array' THEN d.check_results_json->'Checks' ELSE '[]'::jsonb END)) c
                        WHERE (c->>'CheckName')::int = 6          -- CheckName.OpenAI
                        LIMIT 1) ai ON true
                    LEFT JOIN LATERAL (
                        SELECT jsonb_array_length((CASE WHEN jsonb_typeof(d.check_results_json->'Checks') = 'array' THEN d.check_results_json->'Checks' ELSE '[]'::jsonb END)) = 1
                           AND EXISTS (
                               SELECT 1 FROM jsonb_array_elements((CASE WHEN jsonb_typeof(d.check_results_json->'Checks') = 'array' THEN d.check_results_json->'Checks' ELSE '[]'::jsonb END)) c
                               WHERE (c->>'CheckName')::int = 8   -- CheckName.UrlBlocklist (hard block)
                                 AND (c->>'Score')::double precision >= 5) AS is_hard_block) hb ON true
                    LEFT JOIN LATERAL (
                        SELECT (cfg.config_json->>'ReviewQueueThreshold')::double precision AS rq
                        FROM content_detection_configs cfg WHERE cfg.chat_id = d.chat_id) chat_cfg ON true
                    LEFT JOIN LATERAL (
                        SELECT (cfg.config_json->>'ReviewQueueThreshold')::double precision AS rq
                        FROM content_detection_configs cfg WHERE cfg.chat_id = 0 OR cfg.chat_id IS NULL
                        ORDER BY cfg.chat_id NULLS LAST LIMIT 1) global_cfg ON true
                    WHERE d.source = 0
                ) x
                WHERE dr.id = x.id;
                """);

            // ── 2b–2d. classification: file scans, imports, other decisions ────────────
            migrationBuilder.Sql("""
                UPDATE detection_results SET classification = CASE WHEN net_score > 0 THEN 4 ELSE 5 END
                WHERE source = 1;

                UPDATE detection_results SET classification = CASE
                    WHEN reason ILIKE '%label - ham%' THEN 1
                    WHEN reason ILIKE '%label - spam%' THEN 0
                    WHEN net_score > 0 THEN 0 ELSE 1 END
                WHERE source = 18;

                -- Fixed-verdict sources take their verdict from the source (matches the CHECK
                -- constraint added by DropLegacyVerdictColumns); the rest use the score sign.
                UPDATE detection_results SET classification = CASE
                    WHEN source IN (10, 11, 13, 14) THEN 0
                    WHEN source = 12 THEN 1
                    WHEN net_score > 0 THEN 0 ELSE 1 END
                WHERE source NOT IN (0, 1, 18);
                """);

            // ── 3. past exclusions: chat-0 page/import rows an admin removed from training ─
            migrationBuilder.Sql("""
                INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method,
                    score, net_score, reason, web_user_id, telegram_user_id, system_identifier, used_for_training,
                    edit_version, source, classification, properties)
                SELECT message_id, chat_id, detected_at + interval '1 microsecond', detection_source, 'TrainingExclude',
                    score, net_score, 'Removed from training (backfilled from used_for_training = false)',
                    web_user_id, telegram_user_id, system_identifier, false,
                    0, 17, CASE WHEN classification = 0 THEN 4 ELSE 5 END,
                    jsonb_build_object('backfilled', true, 'from', 'used_for_training')
                FROM detection_results
                WHERE source IN (16, 18) AND used_for_training = false;
                """);

            // ── 4. fold training_labels into decision events ──────────────────────────
            migrationBuilder.Sql("""
                INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method,
                    score, net_score, reason, telegram_user_id, system_identifier, used_for_training,
                    edit_version, source, classification, audit_log_id, properties)
                SELECT tl.message_id, tl.chat_id, tl.labeled_at, 'manual',
                    CASE WHEN tl.label = 0 AND tl.labeled_by_user_id IS NULL THEN 'AutoBan' ELSE 'Manual' END,
                    5.0, CASE WHEN tl.label = 0 THEN 5.0 ELSE -5.0 END,
                    COALESCE(tl.reason, 'Migrated training label'),
                    tl.labeled_by_user_id,
                    CASE WHEN tl.labeled_by_user_id IS NULL
                         THEN CASE WHEN tl.label = 0 THEN 'auto_detection' ELSE 'unknown' END END,
                    false, 0,
                    CASE WHEN tl.label = 0 AND tl.labeled_by_user_id IS NULL THEN 10 ELSE 99 END,
                    tl.label::int,                              -- 0 = ExplicitSpam, 1 = ExplicitHam
                    tl.audit_log_id,
                    jsonb_build_object('backfilled', true, 'from', 'training_labels')
                FROM training_labels tl
                WHERE NOT EXISTS (
                    SELECT 1 FROM detection_results d
                    WHERE d.message_id = tl.message_id AND d.chat_id = tl.chat_id
                      AND d.source NOT IN (0, 1, 17)
                      AND d.classification IN (0, 1)
                      AND (d.classification = 0) = (tl.label = 0));
                """);

            // ── 5. the view (transitional form; DropLegacyVerdictColumns recreates it) ──
            migrationBuilder.Sql("""
                CREATE VIEW message_verdicts AS
                SELECT m.chat_id, m.message_id,
                       COALESCE(v.classification, 6) AS classification,
                       COALESCE(v.classification IN (0, 2, 4), false) AS is_spam,
                       v.source, v.detected_at, v.id AS verdict_id
                FROM messages m
                LEFT JOIN LATERAL (
                    SELECT d.classification, d.source, d.detected_at, d.id
                    FROM detection_results d
                    WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id
                      AND d.source <> 1
                      AND d.classification IS NOT NULL
                    ORDER BY d.detected_at DESC, d.id DESC
                    LIMIT 1) v ON true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS message_verdicts;");
            migrationBuilder.Sql("DELETE FROM detection_results WHERE properties ? 'from';");

            migrationBuilder.DropIndex(
                name: "ix_detection_results_classification",
                table: "detection_results");

            migrationBuilder.DropIndex(
                name: "ix_detection_results_source",
                table: "detection_results");

            migrationBuilder.DropIndex(
                name: "ix_detection_results_verdict_latest",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "media_features",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "audit_log_id",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "classification",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "properties",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "source",
                table: "detection_results");
        }
    }
}
