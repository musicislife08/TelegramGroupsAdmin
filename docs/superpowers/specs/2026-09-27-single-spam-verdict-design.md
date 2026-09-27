# Single Spam Verdict — Design

**Date:** 2026-09-27
**Issues:** Closes #547, #549, #548, #546, #527, #386. Addresses #521 Defect B (Defect A ships separately).
**Related:** #551 (AI label/score contract, follow-up), #521 Defect A (self-exclusion, separate PR).

## Summary

The app has no single answer to "is this message spam?". The engine decides with a per-chat
threshold, the database stores `is_spam = net_score > 0`, the action service recomputes with `>`,
the training system has four levels spread across two tables, and admin decisions are written
twice (a ±5 `detection_results` row **and** a `training_labels` row), inconsistently. Every reader
picks its own combination, and most of the open ML bugs are those combinations disagreeing.

This design makes **one place decide** and **one place answer**:

- **Decide (write time, in code):** a pure `VerdictClassifier` turns each event (a scan or a
  decision) into exactly one **classification**. `DetectionResultsRepository` is the only writer,
  and it calls the classifier.
- **Answer (read time, in the database):** a `message_verdicts` view picks each message's
  **latest** event and exposes its classification. Every "what is this message now?" reader uses
  the view. The app filters by classification; it never re-derives spam.

`training_labels` and the two media sample tables are removed. `detection_results` becomes the
single verdict-event log.

## Background: why `training_labels` existed

- `4ee62441` (2025-11-21): reclassifying a message appended a new `detection_results` row without
  cancelling the old one, so Bayes trained on messages as both spam and ham. The fix,
  `InvalidateTrainingDataForMessageAsync`, relied on every writer invalidating first, and added the
  Training Data page's "remove from training" soft delete.
- `ba9ab30a` (2025-12-13, PR #161): `training_labels` was added to "separate detection history from ML
  training intent". One row per message plus an upsert made the conflict structurally impossible,
  **but only for the ML trainers**. Every other reader kept reading `detection_results`, and the
  invalidate method later lost its callers (#549).
- `4e4ea063` (2026-03-12): label provenance (auto vs. manual) was encoded in the reason string.

Each requirement is kept: one answer per message (the view, now for **every** reader), history vs.
intent (events vs. the view), race-free writes (append-only, ordered by `detected_at, id`),
provenance (`source`), the audit link (`audit_log_id`), and "remove from training"
(`TrainingExclude`).

## Goals and success criteria

- Exactly one rule decides spam vs. ham for a scan, and it is shared by the verdict and the action.
- Exactly one place resolves a message's current status, and "the latest event wins" across
  edits, admin decisions, auto-bans and Dismiss.
- The four training levels (explicit/implicit × spam/ham) are one stored value, not a join.
- Admin corrections supersede earlier verdicts everywhere (training, AI history, auto-trust, stop
  words, prompt examples, Training Data page, Layer 1 media), with no per-writer cleanup.
- Admin-labeled and training-relevant messages survive retention.
- Layer 1 media hash similarity has a corpus, spam and ham, that follows corrections.

## Decisions

| Topic | Decision |
|---|---|
| Data model | One verdict-event table (`detection_results`). `training_labels`, `image_training_samples` and `video_training_samples` are dropped. |
| What a row records | `source` (fine-grained enum, what caused it) + `classification` (2×3 grid). Replaces `detection_source`, `used_for_training`, `net_score`. `is_spam` stays, but is generated from `classification` only. |
| Current status | `message_verdicts` view: latest non-FileScan row per message by `detected_at DESC, id DESC`, or `Unscanned`. |
| Edit rescan vs. earlier admin decision | The edit's rescan is newer and wins. An edit is a new content event. |
| Scan spam rule | `score >= ReviewQueueThreshold` on **every** path, AI-confirmed included. Hard block → spam. AI veto → ham. |
| AI "review" below threshold | `UntrainedHam`: allowed, "ok" in AI history, not a ham training sample. |
| Auto-bans | Write an `AutoBan` decision (`ExplicitSpam`). Intended; auto-ban requires score ≥ 4.0 **and** OpenAI ≥ 4.0. |
| Review "Dismiss" | Writes a `ReviewDismiss` decision classified `ImplicitHam` (ham, but not admin-grade). Mark as Ham remains the only source of explicit ham. |
| Auto-trust | Counts any current verdict that is ham, `UntrainedHam` included. |
| "Remove from training" | A `TrainingExclude` decision: keeps the message's current spam/ham, classifies it `Untrained*`. Replaces the `used_for_training = false` soft delete. |
| Media features | `messages.media_features` jsonb, captured at scan time. Layer 1 = view ⋈ features. |
| Where the rule lives | Static `VerdictClassifier` + typed repository writes. No new service (nothing to coordinate). |
| Old backups | `3.0` backups migrate on restore via a backup-only C# step, removed one year after release. |

## Data model

### `detection_results` (verdict events)

| Column | Change | Notes |
|---|---|---|
| `id`, `message_id`, `chat_id`, `detected_at`, `edit_version` | kept | |
| `source` | **new** int | Core `VerdictSource` value, replaces `detection_source` text |
| `classification` | **new** int | Core `VerdictClassification` value (6 stored values) |
| `properties` | **new** jsonb, nullable | Explanation only, never an input to any rule (e.g. `review_threshold`, AI label, `backfilled: true`) |
| `audit_log_id` | **moved in** from `training_labels` | nullable |
| `score` | kept | engine total / AI score / ±5 manual |
| `reason`, `detection_method`, `check_results_json`, actor arc | kept | |
| `is_spam` | computed from `net_score` → **generated from `classification`** | `GENERATED ALWAYS AS (classification IN (ExplicitSpam, ImplicitSpam, UntrainedSpam)) STORED`. The coarse spam/ham split, defined once in SQL. Never written by code. |
| `used_for_training`, `net_score`, `detection_source` | **dropped** | |

**`VerdictSource`**

| Group | Values |
|---|---|
| Scans | `ContentScan`, `FileScan` |
| Decisions | `AutoBan` (includes hard block; reason says which), `WebMarkSpam`, `WebMarkHam`, `SpamCommand`, `ReviewSpam`, `ReviewDismiss`, `TrainingDataPage`, `TrainingExclude`, `Import` |
| Migration only | `LegacyManual` |

`VerdictSourceExtensions.IsDecision()` / `IsScan()` is the fixed mapping. There is no separate
"kind" column; two columns encoding overlapping facts would eventually disagree.

**`VerdictClassification`** (2×3 grid, plus one view-only value)

| | Explicit | Implicit | Untrained |
|---|---|---|---|
| **Spam** | `ExplicitSpam` | `ImplicitSpam` | `UntrainedSpam` |
| **Ham** | `ExplicitHam` | `ImplicitHam` | `UntrainedHam` |

`Unscanned` exists only in the view (message with no verdict row).

Two resolutions of the same decision:

- **Fine (`classification`)**: for readers that care about explicit/implicit/untrained (training,
  retention, Layer 1). C# sets `VerdictClassifications.TrainingSpam` (`ExplicitSpam`, `ImplicitSpam`)
  and `.TrainingHam` (`ExplicitHam`, `ImplicitHam`, `Unscanned`), usable in LINQ via `.Contains(...)`.
- **Coarse (`is_spam`)**: for readers that only need spam vs. ham (auto-trust, AI history, badges,
  stop words, detector analytics). Read from the generated column / view, never re-derived.

An in-memory `IsSpam()` extension exists only for values not yet stored (e.g. the classifier's
result). A parity test pins it to the generated column for every enum value.

**CHECK constraints**

- `classification` ∈ the six stored values.
- Decision sources pin their classification: `AutoBan`, `WebMarkSpam`, `SpamCommand`, `ReviewSpam` → `ExplicitSpam`; `WebMarkHam` → `ExplicitHam`; `ReviewDismiss` → `ImplicitHam`; `TrainingDataPage`, `Import`, `LegacyManual` → `ExplicitSpam` or `ExplicitHam`.
- `FileScan`, `TrainingExclude` → `UntrainedSpam` or `UntrainedHam`.
- `ContentScan` → any non-explicit value.

**Indexes:** `(chat_id, message_id, detected_at DESC, id DESC)` for the view; `classification`;
`source`. The existing `is_spam` / `(is_spam, detected_at)` indexes are recreated on the new generated column.

### `message_verdicts` view

```sql
SELECT m.chat_id, m.message_id,
       COALESCE(v.classification, <Unscanned>) AS classification,
       COALESCE(v.is_spam, false) AS is_spam,
       v.source, v.detected_at, v.id AS verdict_id
FROM messages m
LEFT JOIN LATERAL (
    SELECT d.classification, d.is_spam, d.source, d.detected_at, d.id
    FROM detection_results d
    WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id
      AND d.source <> <FileScan>
    ORDER BY d.detected_at DESC, d.id DESC
    LIMIT 1) v ON true;
```

Mapped as a keyless EF entity (the codebase already maps views such as `enriched_detections`).
The view **selects** and passes `is_spam` through; it applies no spam rules.

### `messages.media_features` (jsonb, nullable)

Designed so a C# 15 `union` (or closed hierarchy) can replace the base type with no data change.

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PhotoFeatures), "photo")]
[JsonDerivedType(typeof(VideoFeatures), "video")]
public abstract record MediaFeatures;   // empty marker, no shared members

public sealed record PhotoFeatures(
    [property: JsonRequired] byte[] Hash, int Width, int Height, long SizeBytes) : MediaFeatures;

public sealed record VideoFeatures(
    [property: JsonRequired] IReadOnlyList<KeyframeHash> Keyframes,
    double DurationSeconds, bool HasAudio, int Width, int Height, long SizeBytes) : MediaFeatures;
```

Rules that keep a union migration code-only:

1. The base is an empty marker; case types are self-contained sealed records.
2. Stored JSON always carries `type` and never depends on CLR type names.
3. Each case has a unique `[JsonRequired]` property (`hash` vs. `keyframes`), so .NET 11's
   structural union classifier can tell them apart.
4. Readers `switch` over case types; they never use base members.

The records above are the **persistence shape** and live in Data as `MediaFeaturesDto` /
`PhotoFeaturesDto` / `VideoFeaturesDto`, with the JSON contract (`type` discriminator,
`[JsonRequired]`) and an EF value converter to `jsonb`. Core has the domain `MediaFeatures`
(`PhotoFeatures` / `VideoFeatures`), and the owning repository maps with `ToModel()` / `ToDto()`.

This split makes the union migration easier: the JSON contract is pinned by the Data DTOs, so the
**Core** model can become a C# 15 `union` without touching System.Text.Json's union serialization
at all. Only the mapping `switch` changes.

## Write side: one decider

### `VerdictClassifier` (pure, static, `TelegramGroupsAdmin.ContentDetection`)

`Classify(VerdictSource source, ContentDetectionResult? scan, bool? isSpam = null)`:

- **`ContentScan`**
  - Engine `IsSpam` → spam. Training-worthy (today's `DetermineIfTrainingWorthy`, moved here: OpenAI ≥ 4.25, or no OpenAI and total > 4.0) → `ImplicitSpam`, else `UntrainedSpam`.
  - Not spam, and a non-abstained AI check scored > 0 (AI "review"/"spam" below threshold) → `UntrainedHam`.
  - Otherwise (pipeline ham, AI veto) → `ImplicitHam`.
- **`FileScan`**: infected → `UntrainedSpam`, else `UntrainedHam`.
- **Decisions**: fixed by source; `TrainingDataPage` / `Import` / `LegacyManual` take the explicit `isSpam` argument.
- **`TrainingExclude`**: `isSpam` is the message's **current** spam/ham (read from the view by the caller) → `UntrainedSpam` / `UntrainedHam`. The verdict is preserved; only training membership changes.

### Engine and action alignment

- `ContentDetectionEngineV2` AI-confirmed path: `IsSpam = aiScore >= ReviewQueueThreshold` (today any
  AI score > 0 sets `IsSpam = true`). Veto and hard-block paths are unchanged.
- `DetectionActionService`: `> ReviewQueueThreshold` → `>=`, matching the engine.

### `DetectionResultsRepository`: the only writer

A domain-shaped command object over EF (EF's `DbContext` is already the generic repository):

- `RecordScanAsync(message, ContentDetectionResult)`
- `RecordFileScanAsync(message, FileScanResult)`
- `RecordDecisionAsync(message, VerdictSource, Actor, reason, bool? isSpam = null, long? auditLogId = null)`

Each calls `VerdictClassifier`. The generic `InsertAsync(DetectionResultRecord)` is **removed**, so
nothing can write a verdict row without the classifier. If the rule ever needs I/O or other data,
that is the point to introduce a service.

### Caller changes

| Caller | Today | After |
|---|---|---|
| `ContentDetectionOrchestrator` | builds a row with `NetScore` | `RecordScanAsync`; also saves `media_features` in the same unit of work |
| `FileScanJob` | sets `IsSpam` (silently ignored) | `RecordFileScanAsync` |
| `TrainingHandler.CreateSpamSampleAsync` | ±5 row (skipped for System) + label + image/video samples | `RecordDecisionAsync` with the caller's source (`AutoBan` for System); sample-saving block removed |
| Messages.razor Mark as Spam / Mark as Ham | row + label written from the component | a handler method (#386); `WebMarkSpam` / `WebMarkHam` |
| `SpamCommand` / `ContentReportHandler` spam | via `TrainingHandler` | `SpamCommand` / `ReviewSpam` source |
| `ContentReportHandler.DismissAsync` | writes nothing | `RecordDecisionAsync(ReviewDismiss)` |
| Training Data page (`AddManualTrainingSampleAsync`) | ±5 row, `used_for_training = true` | `RecordDecisionAsync(TrainingDataPage, isSpam)` |
| Training Data page delete, Duplicates page removals (`ExcludeFromTrainingAsync`, 5 call sites) | sets `used_for_training = false` on a row | `RecordDecisionAsync(TrainingExclude, isSpam: current)`; `ExcludeFromTrainingAsync` removed |
| Edit Training Sample | exclude old row + add new chat-0 sample | `TrainingExclude` on the old message + `TrainingDataPage` on the new one |

`TrainingLabelsRepository`, `ImageTrainingSamplesRepository`, `VideoTrainingSamplesRepository`,
`InvalidateTrainingDataForMessageAsync`, `GetSpamSamplesForSimilarityAsync` and the insert-time
SimHash dedup (`MessageHistoryRepository.HasSimilarTrainingHashAsync`) are removed. Training-time
`DeduplicateSamples` already covers dedup.

## Project placement and layering

Layering rule: **UI → service → repository → Data.** Data is pure schema (entities, `DbContext`,
Fluent configuration; no logic). **Only repositories touch the `DbContext` or reference
`TelegramGroupsAdmin.Data.Models`**; they accept and return Core models and do every Data↔Core
conversion (the #397 lesson: a service casting a Data enum let two enums diverge). Repositories do
query work; services do the rest. Components call services, never repositories or the `DbContext`.

Project chain: `Data ← Core ← Configuration ← AI ← ContentDetection ← Telegram ← BackgroundJobs ← App`.

| Piece | Project | Notes |
|---|---|---|
| `VerdictSource`, `VerdictClassification` **domain enums**, `VerdictClassifications` sets (`Spam`, `TrainingSpam`, `TrainingHam`), `IsSpam()`, `MessageVerdict` model, `MediaFeatures` records | Core | What services, the classifier and components use. |
| `DetectionResultRecordDto` columns (`Source`, `Classification` as plain `int`; Data has no verdict enums), `MessageVerdictView` keyless entity, `MediaFeaturesDto` hierarchy (`PhotoFeaturesDto`, `VideoFeaturesDto`) mapped to `media_features` jsonb with an EF value converter, generated `is_spam`, CHECK constraints | Data | Schema only. The generated column and CHECK constraints use literal ints. |
| Core ↔ Data mapping | `Repositories/Mappings/` (`ToModel()` / `ToDto()` extensions) | Casts enum ↔ int, so adding an enum member needs no mapper change. Core enums carry **explicit, stable numeric values** that are never renumbered. |
| `VerdictClassifier` | ContentDetection | Pure static rule over Core types; needs `ContentDetectionResult`. |
| Writes (`Record*Async`) | `DetectionResultsRepository` (ContentDetection) | The only verdict writer; takes Core types, maps to Data. |
| Reads | Repositories, composing `MessageVerdicts` inside their own queries | Joins stay in SQL; results returned as Core models. |
| UI entry points (Mark as Spam/Ham, Training Data add/edit/exclude, Duplicates, analytics) | Services | Mark as Spam/Ham → the moderation/training handler (#386); Training Data and Duplicates pages → a `TrainingDataService`; analytics component → an analytics service. Components never call `DetectionResultsRepository` directly. |

**Readers moved behind repositories** (they query the `DbContext` from a non-repository today, and
this change rewrites their verdict queries anyway). Partial overlap with #215, #484, #490, noted in
the PR:

| Today | After |
|---|---|
| `MessageQueryService` (Telegram service) — AI history | Query moves to `MessageHistoryRepository`; the service calls it. |
| `StopWordRecommendationService` (ContentDetection) | Corpus queries move to a repository method (`DetectionResultsRepository` or a new `StopWordCorpusRepository` if it grows). |
| `MessageStatsService` (App, a service) | Queries move to `AnalyticsRepository`. |
| `ContentDetectionAnalytics.razor` | Queries move to `AnalyticsRepository`; the component calls an analytics service. |

`TelegramUserRepository` also reads `is_spam` and switches to the view in place.

## Read side

Readers split by the question they ask.

### "What is this message now?" → `message_verdicts`

| Reader | Rule |
|---|---|
| `MLTrainingDataRepository` (SDCA + Bayes) | Explicit spam/ham: `ExplicitSpam` / `ExplicitHam`. Implicit spam: `ImplicitSpam`. Implicit ham: `ImplicitHam` or `Unscanned` (trusted users' unscanned messages stay the main ham source). Existing length, dedup, cap and translation logic unchanged. |
| Auto-trust (#546) — `DetectionResultsRepository` | User's last N **scanned** messages from the view (`Unscanned` excluded; latest version each, current text length). Trust only if there are N and none has `is_spam`. Method renamed from `GetRecentNonSpamResultsForUserAsync`. |
| AI history `WasSpam` (#521B) — `MessageHistoryRepository` | View `is_spam`. |
| Prompt examples, stop-word recommendations, Training Data page list/stats | View, filtered to training classifications. |
| Layer 1 image/video | View rows whose `classification` is in `TrainingSpam` or `TrainingHam`, joined to `messages.media_features` of the right case type, most recent N. Ham matches finally exist (`ImageContentCheckV2` already abstains on them). |
| Retention (#548) — `MessageHistoryRepository` | Keep an expired message if its current classification is `ExplicitSpam`, `ExplicitHam` or `ImplicitSpam`. |
| Message UI badges, history dialog, user detail (`TelegramUserRepository`, `DetectionResultsRepository`) | Current status from the view; timeline from all rows with `source` labels. |

### "What did the detector say?" → `ContentScan` rows directly

| Reader | Rule |
|---|---|
| Detection analytics (daily counts, response times, hourly stats view) — `AnalyticsRepository` | `ContentScan` rows' `is_spam` (table, not the view). Corrections must not rewrite detector history. The SQL views `hourly_detection_stats`, `enriched_detections`, `detection_accuracy` keep reading `is_spam` with minimal edits. |
| False positive/negative, `detection_accuracy` view | A scan row followed by a contradicting decision on the same `(chat_id, message_id)`. Fixes today's `message_id`-only joins. |
| Veto analytics | Keyed on the OpenAI entry in `check_results_json`, not `!is_spam`. |

## Migration

One EF migration, backfill in SQL. Prod data (read-only survey, 2026-09-27) informs each branch:
every chat uses 2.5 / 4.0, so the threshold backfill is exact.

1. Add `source`, `classification`, `properties`, `audit_log_id`; add `messages.media_features`.
2. **`source`**: `auto` / `auto_detection` → `ContentScan`; `file_scan` → `FileScan`; `tg-spam-import` → `Import`; `manual` by reason prefix: `Manually marked as spam by admin via UI` → `WebMarkSpam`; `Manually marked as ham` / `Manually added as ham training sample` → `WebMarkHam`; `Spam detected via /spam` → `SpamCommand`; `Report #` → `ReviewSpam`; `Manually added training sample` → `TrainingDataPage`; anything else (e.g. `Marked as spam by moderator`, NULL) → `LegacyManual`.
3. **`classification`**:
   - `ContentScan`: AI vetoed (OpenAI check present, not abstained, score 0) → `ImplicitHam`. Hard block → spam. Otherwise spam iff `net_score >=` the chat's `ReviewQueueThreshold` (default 2.5). Spam + `used_for_training` → `ImplicitSpam`, other spam → `UntrainedSpam`. Ham with a positive non-abstained AI score → `UntrainedHam`, other ham → `ImplicitHam`. `properties.backfilled = true`.
   - `FileScan`: sign of `net_score`.
   - `Import`: **trust the reason text** (`Manual label - spam|ham`), not the score sign. 11 prod rows contradict; the migration logs the count.
   - Other decisions: sign of `net_score` → `ExplicitSpam` / `ExplicitHam`.
   - **Past exclusions:** `TrainingDataPage` and `Import` rows (chat 0) with `used_for_training = false` were removed from training by an admin (4 in prod). Each gets a following `TrainingExclude` row (`detected_at` + 1 µs) so the exclusion survives. Other `manual` rows defaulted to `used_for_training = false` as "history only", so for them the flag carries no exclusion meaning and is ignored.
4. **Fold `training_labels`** into decision rows at `labeled_at` with actor and `audit_log_id`:
   - A label with a matching manual row (same message, same verdict) is **not** inserted again (198 in prod).
   - A spam label with no user and only `ContentScan` rows is an auto-ban → `AutoBan` (416 in prod).
   - Any other label → `LegacyManual`.
5. Replace `is_spam` (drop the `net_score` expression, re-add as generated from `classification`). Drop `used_for_training`, `net_score`, `detection_source`, their indexes, `training_labels`, `image_training_samples`, `video_training_samples` (0 rows in prod).
6. Create indexes, CHECK constraints, and the `message_verdicts` view.

Behaviour change on existing data: a label applied before a later edit no longer survives the edit.
Prod has 0 such messages.

Apply with `dotnet run --migrate-only`.

### Media backfill

A one-time Quartz job hashes messages whose current classification is `ExplicitSpam`,
`ExplicitHam` or `ImplicitSpam` and whose file is on disk (photo: `MediaUtilities.ToAbsolutePath`
over `photo_local_path`; video: `MediaUtilities.ValidateMediaPath`). A missing file logs at Warning
with the resolved path. The job is idempotent (skips rows with `media_features`).

## Backup / restore

- Backup format version `3.0` → `3.1`.
- `ApplyBackupMigrations` gains a `3.0 → 3.1` step that applies the same transformation in C# to
  the in-memory backup data (maps old `detection_results` columns, folds `training_labels`, drops the
  sample tables). This is a deliberate, separate implementation, not shared with the SQL migration.
  The method carries a plain comment: *remove one year after the release that introduced it*
  (concrete date filled in at release). No `[Obsolete]`.
- Restore of a `3.0` backup must succeed end-to-end; restore still rejects unknown tables.

## Testing

### Unit (no database)

- `VerdictClassifier`: every grid cell; AI path below / at / above `ReviewQueueThreshold`; hard block; veto; abstained AI; each decision source → its pinned classification; `isSpam` required for `TrainingDataPage` / `Import` / `LegacyManual` / `TrainingExclude`; `TrainingExclude` yields `Untrained*` with the given spam/ham.
- Engine: AI-confirmed below threshold now returns `IsSpam = false`.
- `DetectionActionService`: a score exactly at `ReviewQueueThreshold` queues for review.
- Core verdict enums: every member has an explicit value, and the values match a pinned table (guards against accidental renumbering, since stored ints depend on them).
- `MediaFeatures` JSON round-trip: `type` discriminator, `[JsonRequired]` enforcement, unknown `type` rejected.
- Backup `3.0 → 3.1` step over an in-memory legacy backup.

### Migration fixture (empty template, synthetic legacy rows allowed)

Migrate to N−1, insert one legacy row per backfill branch, migrate to N, and assert `source` and
`classification` per row: pipeline-low, AI "Review" below threshold, AI-confirmed, AI vetoed, hard
block, file scan, each `manual` reason prefix, consistent and contradicting `tg-spam-import`,
auto-ban label, label with and without a matching manual row, excluded chat-0 page/import row
(gains a `TrainingExclude` row). **Parity:** for every stored `VerdictClassification` value, the
generated `is_spam` equals the in-memory `IsSpam()`. Also assert that the CHECK constraint
rejects a `WebMarkHam` row classified as spam, and that the view returns `Unscanned` for a message
with no rows.

### Canonical data

Canonical is **static new-shape SQL**, independent of the migration. It is written once (a one-off
conversion script), reviewed, and committed. No test depends on running the migration over it.

- `32_detection_results.sql` gains `source` / `classification` / `properties` / `audit_log_id` and loses the dropped columns. The 200 `training_labels` rows move into it as decision rows at their `labeled_at` (minus those already represented by a matching manual row). This is the same data in its new representation, not rows added to obtain a shape.
- `33_training_labels.sql`, `15_image_training_samples.sql`, `16_video_training_samples.sql` are deleted. `19_messages.sql` gains `media_features` (NULL except one flag-edit).
- New-shape rows and flag-edits copy **real prod row shapes** (reason formats, `check_results_json` structure, score ranges), scrubbed like the existing golden data: rotated Telegram user ids, `canonical-spam.test` hostnames, no real usernames or text.
- **Independent oracle:** a `LoadCanonicalAsyncTests` invariant asserts that every message in each of the four canonical slices (`explicit_spam`, `implicit_spam`, `explicit_ham`, `implicit_ham`, defined at snapshot time) resolves in `message_verdicts` to the matching training classification. Counts in `IntegrationTests/CLAUDE.md` and `LoadCanonicalAsyncTests` are updated.

**Flag-edits** (canonical edit 2026-09-27; each pinned in `GoldenDatasetConstants`, recorded as a
Part 2 recipe, and guarded by a read-back assertion in its test):

| Shape | Anchor | Edit |
|---|---|---|
| Edit rescan flips the verdict | msg 82837, @financerope (9468093502025) | detection row dr1334 (edit_version 1) → `ContentScan` / `ImplicitSpam` |
| Spam among a user's last 3 | msg 7796, @mouthsafeguard (9917295586642) | dr1339 → `ContentScan` / `UntrainedSpam`; its folded ham-label decision removed |
| FileScan beside a scan | msg 216684 (sender 9778846455554) | dr2535 → `FileScan` / `UntrainedHam` |
| `UntrainedHam` (AI review below threshold) | msg 222716 (sender 9058402298095) | dr3211 → reason `AI confirmed spam: AI: Review …`, score 2.0, `UntrainedHam` |
| Layer 1 photo feature | msg 222818 (sender 9777802619662) | `media_features` = a scrubbed photo feature |

### Integration tests (golden template, reduce as needed)

| Test | Anchor | Asserts |
|---|---|---|
| View: correction supersedes scan | msg 213409, @dinnersnazzy (9257421184750) | scan then `WebMarkHam` → `ExplicitHam` |
| View: auto-ban | msg 220384, @AndrewLong6 (9127536472473) | `AutoBan` decision → `ExplicitSpam` |
| View: edit replaces earlier verdict | msg 82837, @financerope | latest `edit_version` row wins |
| View: FileScan excluded | msg 216684 | classification comes from the `ContentScan` row |
| View: unscanned | msg 219219, @unhelpfulgrab (9921676191756) | `Unscanned` |
| Training levels | the four canonical slices; spam-not-trained msg 8390, @REDDOMTANYA (9038018426261) | explicit/implicit sets match the view; `UntrainedSpam` excluded |
| Auto-trust: all ham | user 9184102838760, msgs 71028/71030/71041 | N ham scanned messages → trusted |
| Auto-trust: spam in window blocks | msg 7796 (flag-edited) | trust denied |
| Auto-trust: edits don't inflate | msg 82837, @financerope (5 edits) | counts once |
| Auto-trust: `UntrainedHam` counts | msg 222716 (flag-edited) | treated as ham |
| AI history `WasSpam` | chat MainChat, msg 210743 (sender 9566750116353) + msg 222716 | spam row → `spam`; `UntrainedHam` → `ok` |
| Retention (#548) | msg 7974, @arisepacifism (9702019239117) | labeled-only message kept; an unlabeled expired message deleted |
| Dismiss (#549) | report 186, msg 70989 (read-only use) | `ReviewDismiss` decision written (write is the subject); view → `ImplicitHam` |
| Mark as Ham (#549) | msg 8646, Land Owners (sender 9550752264926), an auto-banned message | `WebMarkHam` decision written (the subject); view `ExplicitSpam` → `ExplicitHam`; message leaves every spam reader |
| Retention: unlabeled expired deleted | existing `Retention` anchors in `GoldenDatasetConstants` | unchanged behaviour for untrained messages |
| Layer 1 read | msg 222818 (flag-edited) | returned as a spam photo sample |
| Remove from training | msg 220384, @AndrewLong6 | `TrainingExclude` written (the subject); view `ExplicitSpam` → `UntrainedSpam`; absent from ML spam samples; still `is_spam` for auto-trust / AI history |
| `Record*Async` writes | any anchor above | the write is the assertion subject |

## Out of scope

- **#521 Defect A** (exclude the message under evaluation from its own AI history): separate small PR, can ship first.
- **#551** (AI label/score contract: thresholds in the prompt, label-driven bands).
- **Restore by replaying migrations** (store the migration id in backup metadata, restore into the old schema, migrate forward). Worth a follow-up issue; not needed here.
- Renaming `detection_results`.
