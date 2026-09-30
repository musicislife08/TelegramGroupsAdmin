# Single Spam Verdict Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One place decides whether a message is spam (a pure classifier called by the only verdict writer), and one place answers it (the `message_verdicts` view). This retires `training_labels`, the media sample tables and the `net_score > 0` rule.

**Architecture:** `detection_results` becomes an append-only verdict-event log. Each row has a `source` (what caused it) and a `classification` (the 2×3 grid of spam/ham × explicit/implicit/untrained), both decided once at write time by `VerdictClassifier`. A `message_verdicts` view picks each message's latest event. Every "what is this message now?" reader goes through repositories that compose that view. Detector analytics read `ContentScan` rows directly.

**Tech Stack:** .NET 10 / C# 14, EF Core 10 + Npgsql (PostgreSQL 18), Blazor Server + MudBlazor 9, NUnit + NSubstitute, Testcontainers-backed integration tests with canonical golden data.

**Spec:** `docs/superpowers/specs/2026-09-27-single-spam-verdict-design.md`

## Global Constraints

- Branch `feat/single-spam-verdict` (already checked out). Conventional commits. Every commit message ends with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.
- Layering: **UI → service → repository → Data.** Only repositories touch `AppDbContext` or reference `TelegramGroupsAdmin.Data.Models`. Components call services, never repositories or the `DbContext`.
- Data DTOs store enums as plain `int`. The enum types exist only in Core. Repository `ToModel()`/`ToDto()` mappings cast `(int)` ↔ `(Enum)`. Core verdict enums carry explicit numeric values that are never renumbered.
- Scan spam rule: `score >= ReviewQueueThreshold` on every path, the AI-confirmed path included. Hard block → spam. AI veto → ham.
- Integration/E2E tests use canonical golden data only (`.claude/rules/integration-test-data.md`). Never seed preconditions with SUT writes, `ctx.X.Add` or raw `INSERT`. A SUT write appears only when it is the assertion subject. Migration fixtures (empty template) may insert synthetic legacy rows.
- NSubstitute matchers: `Arg.Is<T>(x => x!.Prop == y)`. Never use `?.` in a matcher.
- No `[Obsolete]`. No warning suppressions. Real fixes over suppressions. The build has 0 warnings, and `slopwatch` is clean before the final commit.
- New migration: `dotnet ef migrations add <Name> --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`. Apply locally with `dotnet run --project TelegramGroupsAdmin -- --migrate-only`.
- Test commands: unit `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~<Name>"`, integration `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~<Name>"`, component `dotnet test TelegramGroupsAdmin.ComponentTests --filter ...`.

## Deliberate deviations from the spec (flagged for review)

1. **Two schema migrations instead of one.** `AddVerdictEvents` (additive: new columns, backfill, view) lands first. Writers and readers then move over task by task, and `DropLegacyVerdictColumns` (destructive) lands last. The final schema is exactly the spec's. The split keeps every task buildable and green.
2. **`MediaFeatures` carries hashes only** (`PhotoFeatures(Hash)`, `VideoFeatures(Keyframes)`). The spec listed width/height/size/duration/has_audio, but no code reads them (verified by grep), and computing `has_audio` would add an ffprobe call to the scan path. The JSON contract tolerates adding them later.
3. **Media backfill runs in the existing idempotent startup `PhotoHashRehashService`**, not a one-time Quartz job. It already runs every start and fills only NULL hashes. That also covers admin decisions on media that was never scanned (e.g. a trusted user's post).
4. **The Training Data dialogs lose their free-text "Source" dropdown.** It wrote arbitrary strings into `detection_source`; source is now an enum (`TrainingDataPage`).

## Review Focus

1. **Two verdict rows with the same `detected_at` for one message** (rapid edits): the view must pick deterministically, with the higher `id` winning. Test in Task 4 (migration fixture).
2. **Mark as Ham twice / Dismiss after Mark as Ham:** the decision is appended with no error, and the view shows the latest (`ImplicitHam` after Dismiss). Test in Task 6.
3. **Remove from training on an `Unscanned` message** (no verdict rows): writes `TrainingExclude` with `isSpam = false` → `UntrainedHam`, and the message leaves implicit ham. Test in Task 7.
4. **Image scanned while its file is missing** (`photoFullPath == null`): the scan is recorded with no `media_features` and no exception. Test in Task 15.
5. **Admin marks a media message spam that was never scanned:** the decision is recorded, and the next startup rehash fills `media_features` so Layer 1 learns it. Test in Task 15.

---

### Task 1: Core verdict types

**Files:**
- Create: `TelegramGroupsAdmin.Core/Models/VerdictSource.cs`
- Create: `TelegramGroupsAdmin.Core/Models/VerdictClassification.cs`
- Create: `TelegramGroupsAdmin.Core/Models/VerdictClassifications.cs`
- Create: `TelegramGroupsAdmin.Core/Models/VerdictSources.cs`
- Create: `TelegramGroupsAdmin.Core/Models/MessageVerdict.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Core/Models/VerdictTypesTests.cs`

**Interfaces:**
- Produces: `VerdictSource` (12 members, pinned values), `VerdictClassification` (7 members, pinned values), `VerdictClassifications.{Spam,TrainingSpam,TrainingHam,Curated}` (enum arrays) and `...Values` (`int[]` twins for EF LINQ over DTO ints), extension `VerdictClassification.IsSpam()`, `IsExplicit()`; extension `VerdictSource.IsScan()`, `IsDecision()`; `record MessageVerdict(long ChatId, int MessageId, VerdictClassification Classification, bool IsSpam, VerdictSource? Source, DateTimeOffset? DetectedAt, long? VerdictId)`.

- [ ] **Step 1: Write the failing test**

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core.Models;

/// <summary>
/// Pins the stored numeric values of the verdict enums. Data stores these as int, so a
/// renumbering would silently change the meaning of every existing detection_results row.
/// </summary>
[TestFixture]
public class VerdictTypesTests
{
    [TestCase(VerdictSource.ContentScan, 0)]
    [TestCase(VerdictSource.FileScan, 1)]
    [TestCase(VerdictSource.AutoBan, 10)]
    [TestCase(VerdictSource.WebMarkSpam, 11)]
    [TestCase(VerdictSource.WebMarkHam, 12)]
    [TestCase(VerdictSource.SpamCommand, 13)]
    [TestCase(VerdictSource.ReviewSpam, 14)]
    [TestCase(VerdictSource.ReviewDismiss, 15)]
    [TestCase(VerdictSource.TrainingDataPage, 16)]
    [TestCase(VerdictSource.TrainingExclude, 17)]
    [TestCase(VerdictSource.Import, 18)]
    [TestCase(VerdictSource.LegacyManual, 99)]
    public void VerdictSource_HasPinnedValue(VerdictSource source, int expected)
        => Assert.That((int)source, Is.EqualTo(expected));

    [Test]
    public void VerdictSource_EveryMemberIsPinned()
        => Assert.That(Enum.GetValues<VerdictSource>(), Has.Length.EqualTo(12));

    [TestCase(VerdictClassification.ExplicitSpam, 0)]
    [TestCase(VerdictClassification.ExplicitHam, 1)]
    [TestCase(VerdictClassification.ImplicitSpam, 2)]
    [TestCase(VerdictClassification.ImplicitHam, 3)]
    [TestCase(VerdictClassification.UntrainedSpam, 4)]
    [TestCase(VerdictClassification.UntrainedHam, 5)]
    [TestCase(VerdictClassification.Unscanned, 6)]
    public void VerdictClassification_HasPinnedValue(VerdictClassification classification, int expected)
        => Assert.That((int)classification, Is.EqualTo(expected));

    [Test]
    public void VerdictClassification_EveryMemberIsPinned()
        => Assert.That(Enum.GetValues<VerdictClassification>(), Has.Length.EqualTo(7));

    [TestCase(VerdictClassification.ExplicitSpam, true)]
    [TestCase(VerdictClassification.ImplicitSpam, true)]
    [TestCase(VerdictClassification.UntrainedSpam, true)]
    [TestCase(VerdictClassification.ExplicitHam, false)]
    [TestCase(VerdictClassification.ImplicitHam, false)]
    [TestCase(VerdictClassification.UntrainedHam, false)]
    [TestCase(VerdictClassification.Unscanned, false)]
    public void IsSpam_MatchesSpamRow(VerdictClassification classification, bool expected)
        => Assert.That(classification.IsSpam(), Is.EqualTo(expected));

    [Test]
    public void IntSets_MirrorEnumSets()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.SpamValues, Is.EquivalentTo(VerdictClassifications.Spam.Select(c => (int)c)));
            Assert.That(VerdictClassifications.TrainingSpamValues, Is.EquivalentTo(VerdictClassifications.TrainingSpam.Select(c => (int)c)));
            Assert.That(VerdictClassifications.TrainingHamValues, Is.EquivalentTo(VerdictClassifications.TrainingHam.Select(c => (int)c)));
            Assert.That(VerdictClassifications.CuratedValues, Is.EquivalentTo(VerdictClassifications.Curated.Select(c => (int)c)));
        }
    }

    [Test]
    public void TrainingSets_AreDisjointAndExcludeUntrained()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(VerdictClassifications.TrainingSpam, Is.EquivalentTo(new[] { VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam }));
            Assert.That(VerdictClassifications.TrainingHam, Is.EquivalentTo(new[] { VerdictClassification.ExplicitHam, VerdictClassification.ImplicitHam, VerdictClassification.Unscanned }));
            Assert.That(VerdictClassifications.Curated, Is.EquivalentTo(new[] { VerdictClassification.ExplicitSpam, VerdictClassification.ExplicitHam, VerdictClassification.ImplicitSpam }));
        }
    }

    [TestCase(VerdictSource.ContentScan, true)]
    [TestCase(VerdictSource.FileScan, true)]
    [TestCase(VerdictSource.AutoBan, false)]
    [TestCase(VerdictSource.TrainingExclude, false)]
    [TestCase(VerdictSource.LegacyManual, false)]
    public void IsScan_OnlyForScanSources(VerdictSource source, bool expected)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.IsScan(), Is.EqualTo(expected));
            Assert.That(source.IsDecision(), Is.EqualTo(!expected));
        }
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~VerdictTypesTests"`
Expected: build FAIL, `The type or namespace name 'VerdictSource' could not be found`.

- [ ] **Step 3: Implement the types**

`TelegramGroupsAdmin.Core/Models/VerdictSource.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// What caused a verdict event (one detection_results row). Stored as int by the Data
/// layer: values are explicit and must never be renumbered.
/// </summary>
public enum VerdictSource
{
    /// <summary>Content detection engine scan of a message version (new message or edit).</summary>
    ContentScan = 0,
    /// <summary>Attachment scan. Never used as the message's verdict.</summary>
    FileScan = 1,
    /// <summary>High-confidence auto-ban or hard block.</summary>
    AutoBan = 10,
    WebMarkSpam = 11,
    WebMarkHam = 12,
    SpamCommand = 13,
    ReviewSpam = 14,
    /// <summary>Review queue "Dismiss": ham, but not an admin-grade label.</summary>
    ReviewDismiss = 15,
    TrainingDataPage = 16,
    /// <summary>"Remove from training": keeps spam/ham, drops training membership.</summary>
    TrainingExclude = 17,
    /// <summary>Bulk import (tg-spam).</summary>
    Import = 18,
    /// <summary>Migration only: a legacy manual row whose origin cannot be recovered.</summary>
    LegacyManual = 99
}
```

`TelegramGroupsAdmin.Core/Models/VerdictClassification.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// The single stored decision for a verdict event: spam/ham × explicit/implicit/untrained.
/// Stored as int by the Data layer; values are explicit and must never be renumbered.
/// </summary>
public enum VerdictClassification
{
    ExplicitSpam = 0,
    ExplicitHam = 1,
    ImplicitSpam = 2,
    ImplicitHam = 3,
    UntrainedSpam = 4,
    UntrainedHam = 5,
    /// <summary>View-only: the message has no verdict event. Never stored on a row.</summary>
    Unscanned = 6
}
```

`TelegramGroupsAdmin.Core/Models/VerdictClassifications.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Fixed groupings of <see cref="VerdictClassification"/>. The int twins exist for EF LINQ
/// over Data DTOs, which store the classification as int.
/// The database's generated is_spam column uses the same Spam set (pinned by a parity test).
/// </summary>
public static class VerdictClassifications
{
    public static readonly VerdictClassification[] Spam =
        [VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam, VerdictClassification.UntrainedSpam];

    public static readonly VerdictClassification[] TrainingSpam =
        [VerdictClassification.ExplicitSpam, VerdictClassification.ImplicitSpam];

    public static readonly VerdictClassification[] TrainingHam =
        [VerdictClassification.ExplicitHam, VerdictClassification.ImplicitHam, VerdictClassification.Unscanned];

    /// <summary>
    /// The curated training set: explicit labels plus confident implicit spam. Retention keeps
    /// these messages, and the Training Data page lists them. (Implicit ham is too plentiful for either.)
    /// </summary>
    public static readonly VerdictClassification[] Curated =
        [VerdictClassification.ExplicitSpam, VerdictClassification.ExplicitHam, VerdictClassification.ImplicitSpam];

    public static readonly int[] SpamValues = [.. Spam.Select(c => (int)c)];
    public static readonly int[] TrainingSpamValues = [.. TrainingSpam.Select(c => (int)c)];
    public static readonly int[] TrainingHamValues = [.. TrainingHam.Select(c => (int)c)];
    public static readonly int[] CuratedValues = [.. Curated.Select(c => (int)c)];

    extension(VerdictClassification classification)
    {
        public bool IsSpam() => Spam.Contains(classification);

        public bool IsExplicit() =>
            classification is VerdictClassification.ExplicitSpam or VerdictClassification.ExplicitHam;
    }
}
```

`TelegramGroupsAdmin.Core/Models/VerdictSources.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Models;

public static class VerdictSources
{
    extension(VerdictSource source)
    {
        public bool IsScan() => source is VerdictSource.ContentScan or VerdictSource.FileScan;

        public bool IsDecision() => !source.IsScan();
    }
}
```

`TelegramGroupsAdmin.Core/Models/MessageVerdict.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// A message's current verdict, as resolved by the message_verdicts view (latest event wins).
/// Source/DetectedAt/VerdictId are null when the message is Unscanned.
/// </summary>
public sealed record MessageVerdict(
    long ChatId,
    int MessageId,
    VerdictClassification Classification,
    bool IsSpam,
    VerdictSource? Source,
    DateTimeOffset? DetectedAt,
    long? VerdictId);
```

- [ ] **Step 4: Run the test and verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~VerdictTypesTests"`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Core/Models/Verdict*.cs TelegramGroupsAdmin.Core/Models/MessageVerdict.cs TelegramGroupsAdmin.UnitTests/Core/Models/VerdictTypesTests.cs
git commit -F- <<'EOF'
feat(core): add verdict source and classification types

Refs #547

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 2: VerdictClassifier (the one decider)

**Files:**
- Create: `TelegramGroupsAdmin.ContentDetection/Services/VerdictClassifier.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Constants/ContentDetectionConstants.cs` (add two constants)
- Modify: `TelegramGroupsAdmin.Telegram/Constants/SpamDetectionConstants.cs` (remove `OpenAIConfidentThreshold`, `TrainingConfidenceThreshold`)
- Modify: `TelegramGroupsAdmin.Telegram/Handlers/ContentDetectionOrchestrator.cs` (`DetermineIfTrainingWorthy` → call the classifier)
- Test: `TelegramGroupsAdmin.UnitTests/ContentDetection/VerdictClassifierTests.cs`

**Interfaces:**
- Consumes: Task 1 types; `ContentDetectionResult`, `ContentCheckResponseV2`, `CheckName`.
- Produces: `static class VerdictClassifier` with `VerdictClassification ClassifyScan(ContentDetectionResult scan)`, `VerdictClassification ClassifyFileScan(bool infected)`, `VerdictClassification ClassifyDecision(VerdictSource source, bool? isSpam = null)`, `bool IsTrainingWorthy(ContentDetectionResult scan)`. Constants `ContentDetectionConstants.OpenAIConfidentThreshold = 4.25`, `TrainingConfidenceThreshold = 4.0`.

- [ ] **Step 1: Write the failing test**

```csharp
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

[TestFixture]
public class VerdictClassifierTests
{
    private static ContentCheckResponseV2 Check(CheckName name, double score, bool abstained = false) =>
        new() { CheckName = name, Score = score, Abstained = abstained, Details = name.ToString() };

    private static ContentDetectionResult Scan(bool isSpam, double total, params ContentCheckResponseV2[] checks) =>
        new() { IsSpam = isSpam, TotalScore = total, CheckResults = [.. checks] };

    [Test]
    public void ClassifyScan_SpamWithConfidentAI_IsImplicitSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, 4.5, Check(CheckName.StopWords, 3), Check(CheckName.OpenAI, 4.5))),
            Is.EqualTo(VerdictClassification.ImplicitSpam));

    [Test]
    public void ClassifyScan_SpamWithUnconfidentAI_IsUntrainedSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, 3.0, Check(CheckName.StopWords, 3), Check(CheckName.OpenAI, 3.0))),
            Is.EqualTo(VerdictClassification.UntrainedSpam));

    [Test]
    public void ClassifyScan_PipelineSpamAboveTrainingThreshold_IsImplicitSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, 4.1, Check(CheckName.Bayes, 4.1))),
            Is.EqualTo(VerdictClassification.ImplicitSpam));

    [Test]
    public void ClassifyScan_PipelineSpamAtTrainingThreshold_IsUntrainedSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, 4.0, Check(CheckName.Bayes, 4.0))),
            Is.EqualTo(VerdictClassification.UntrainedSpam));

    [Test]
    public void ClassifyScan_HardBlock_IsImplicitSpam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(true, ContentDetectionConstants.MaxScore, Check(CheckName.UrlBlocklist, ContentDetectionConstants.MaxScore))),
            Is.EqualTo(VerdictClassification.ImplicitSpam));

    [Test]
    public void ClassifyScan_AIReviewBelowThreshold_IsUntrainedHam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(false, 2.3, Check(CheckName.Similarity, 3.5), Check(CheckName.OpenAI, 2.3))),
            Is.EqualTo(VerdictClassification.UntrainedHam));

    [Test]
    public void ClassifyScan_AIVeto_IsImplicitHam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(false, 0, Check(CheckName.StopWords, 3), Check(CheckName.OpenAI, 0))),
            Is.EqualTo(VerdictClassification.ImplicitHam));

    [Test]
    public void ClassifyScan_AIAbstainedAndPipelineHam_IsImplicitHam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(false, 0.8, Check(CheckName.ChannelReply, 0.8), Check(CheckName.OpenAI, 0, abstained: true))),
            Is.EqualTo(VerdictClassification.ImplicitHam));

    [Test]
    public void ClassifyScan_PipelineOnlyLowScore_IsImplicitHam()
        => Assert.That(VerdictClassifier.ClassifyScan(Scan(false, 0.8, Check(CheckName.ChannelReply, 0.8))),
            Is.EqualTo(VerdictClassification.ImplicitHam));

    [TestCase(true, VerdictClassification.UntrainedSpam)]
    [TestCase(false, VerdictClassification.UntrainedHam)]
    public void ClassifyFileScan_IsAlwaysUntrained(bool infected, VerdictClassification expected)
        => Assert.That(VerdictClassifier.ClassifyFileScan(infected), Is.EqualTo(expected));

    [TestCase(VerdictSource.AutoBan, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.WebMarkSpam, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.SpamCommand, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.ReviewSpam, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.WebMarkHam, VerdictClassification.ExplicitHam)]
    [TestCase(VerdictSource.ReviewDismiss, VerdictClassification.ImplicitHam)]
    public void ClassifyDecision_FixedSources(VerdictSource source, VerdictClassification expected)
        => Assert.That(VerdictClassifier.ClassifyDecision(source), Is.EqualTo(expected));

    [TestCase(VerdictSource.TrainingDataPage, true, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.TrainingDataPage, false, VerdictClassification.ExplicitHam)]
    [TestCase(VerdictSource.Import, true, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.LegacyManual, false, VerdictClassification.ExplicitHam)]
    [TestCase(VerdictSource.TrainingExclude, true, VerdictClassification.UntrainedSpam)]
    [TestCase(VerdictSource.TrainingExclude, false, VerdictClassification.UntrainedHam)]
    public void ClassifyDecision_ArgumentSources(VerdictSource source, bool isSpam, VerdictClassification expected)
        => Assert.That(VerdictClassifier.ClassifyDecision(source, isSpam), Is.EqualTo(expected));

    [TestCase(VerdictSource.TrainingDataPage)]
    [TestCase(VerdictSource.Import)]
    [TestCase(VerdictSource.LegacyManual)]
    [TestCase(VerdictSource.TrainingExclude)]
    public void ClassifyDecision_ArgumentSourceWithoutIsSpam_Throws(VerdictSource source)
        => Assert.Throws<ArgumentException>(() => VerdictClassifier.ClassifyDecision(source));

    [TestCase(VerdictSource.ContentScan)]
    [TestCase(VerdictSource.FileScan)]
    public void ClassifyDecision_ScanSource_Throws(VerdictSource source)
        => Assert.Throws<ArgumentOutOfRangeException>(() => VerdictClassifier.ClassifyDecision(source, true));
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~VerdictClassifierTests"`
Expected: build FAIL, `VerdictClassifier` not found.

- [ ] **Step 3: Implement**

Add to `ContentDetectionConstants` (same file, same XML-doc style as `MaxScore`):

```csharp
    /// <summary>OpenAI score at or above which an auto scan is trustworthy enough to train on.</summary>
    public const double OpenAIConfidentThreshold = 4.25;

    /// <summary>Total score above which a scan with no OpenAI check is trustworthy enough to train on.</summary>
    public const double TrainingConfidenceThreshold = 4.0;
```

Delete `OpenAIConfidentThreshold` and `TrainingConfidenceThreshold` (and their XML docs) from `TelegramGroupsAdmin.Telegram/Constants/SpamDetectionConstants.cs`.

Create `TelegramGroupsAdmin.ContentDetection/Services/VerdictClassifier.cs`:

```csharp
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// The single place that decides a verdict event's classification. Pure and static: every
/// verdict write goes through DetectionResultsRepository, which calls this.
/// </summary>
public static class VerdictClassifier
{
    public static VerdictClassification ClassifyScan(ContentDetectionResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        if (scan.IsSpam)
        {
            return IsTrainingWorthy(scan)
                ? VerdictClassification.ImplicitSpam
                : VerdictClassification.UntrainedSpam;
        }

        // A positive, non-abstained AI score on a ham verdict means the AI said "review"/"spam"
        // below the review threshold: allowed, but too uncertain to learn ham from.
        var ai = scan.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
        return ai is { Abstained: false, Score: > 0 }
            ? VerdictClassification.UntrainedHam
            : VerdictClassification.ImplicitHam;
    }

    public static VerdictClassification ClassifyFileScan(bool infected) =>
        infected ? VerdictClassification.UntrainedSpam : VerdictClassification.UntrainedHam;

    public static VerdictClassification ClassifyDecision(VerdictSource source, bool? isSpam = null) => source switch
    {
        VerdictSource.AutoBan or VerdictSource.WebMarkSpam or VerdictSource.SpamCommand or VerdictSource.ReviewSpam
            => VerdictClassification.ExplicitSpam,
        VerdictSource.WebMarkHam => VerdictClassification.ExplicitHam,
        VerdictSource.ReviewDismiss => VerdictClassification.ImplicitHam,
        VerdictSource.TrainingDataPage or VerdictSource.Import or VerdictSource.LegacyManual
            => RequireIsSpam(source, isSpam) ? VerdictClassification.ExplicitSpam : VerdictClassification.ExplicitHam,
        VerdictSource.TrainingExclude
            => RequireIsSpam(source, isSpam) ? VerdictClassification.UntrainedSpam : VerdictClassification.UntrainedHam,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source,
            "Scan sources are classified with ClassifyScan / ClassifyFileScan")
    };

    /// <summary>
    /// A scan is training-worthy when OpenAI was confident, or, with no OpenAI check, when the
    /// total score is very high.
    /// </summary>
    public static bool IsTrainingWorthy(ContentDetectionResult scan)
    {
        var ai = scan.CheckResults.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
        return ai != null
            ? ai.Score >= ContentDetectionConstants.OpenAIConfidentThreshold
            : scan.TotalScore > ContentDetectionConstants.TrainingConfidenceThreshold;
    }

    private static bool RequireIsSpam(VerdictSource source, bool? isSpam) =>
        isSpam ?? throw new ArgumentException($"{source} requires an explicit isSpam value", nameof(isSpam));
}
```

In `ContentDetectionOrchestrator`, delete the private `DetermineIfTrainingWorthy` method (and its XML doc). Replace its one call site:

```csharp
        var isTrainingWorthy = VerdictClassifier.IsTrainingWorthy(spamResult);
```

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~VerdictClassifierTests"`, then `dotnet build` (0 warnings).
Expected: PASS; build clean.

- [ ] **Step 5: Commit**

```bash
git add -A TelegramGroupsAdmin.ContentDetection TelegramGroupsAdmin.Telegram/Constants TelegramGroupsAdmin.Telegram/Handlers/ContentDetectionOrchestrator.cs TelegramGroupsAdmin.UnitTests/ContentDetection/VerdictClassifierTests.cs
git commit -F- <<'EOF'
feat(detection): add VerdictClassifier as the single verdict decider

Refs #547

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 3: One spam rule — engine AI path and action service

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs:185-205` (AI-confirmed branch)
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/DetectionActionService.cs:70,136`
- Test: `TelegramGroupsAdmin.UnitTests/ContentDetection/ContentDetectionEngineV2Tests.cs` (add tests in `#region AI Veto - Confirms Spam`)
- Test: Create `TelegramGroupsAdmin.UnitTests/Telegram/Services/BackgroundServices/DetectionActionServiceTests.cs`

**Interfaces:**
- Produces: `ContentDetectionResult.IsSpam == TotalScore >= ReviewQueueThreshold` on the AI-confirmed path; `DetectionActionService` queues a review at exactly `ReviewQueueThreshold`.

- [ ] **Step 1: Write the failing tests**

Add to `ContentDetectionEngineV2Tests` inside `#region AI Veto - Confirms Spam`:

```csharp
    [Test]
    public async Task CheckMessageAsync_AIScoreBelowReviewThreshold_IsNotSpam()
    {
        // Pipeline 3.0 triggers the veto; AI answers "review" at 2.0 (< ReviewQueueThreshold 2.5).
        // One rule: the verdict follows the threshold, so this is allowed ham, not "spam but allowed".
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 3.0, abstained: false);
        var aiCheck = BuildAICheck(score: 2.0, abstained: false, details: "AI: Review - borderline");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;
        _configService
            .GetEffectiveContentDetectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);
        SetupEnabledAIInfrastructure();

        var result = await BuildEngine([pipelineCheck, aiCheck]).CheckMessageAsync(BuildRequest("Borderline content"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSpam, Is.False);
            Assert.That(result.TotalScore, Is.EqualTo(2.0));
            Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.Allow));
        }
    }

    [Test]
    public async Task CheckMessageAsync_AIScoreAtReviewThreshold_IsSpam()
    {
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 3.0, abstained: false);
        var aiCheck = BuildAICheck(score: 2.5, abstained: false, details: "AI: Review");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;
        _configService
            .GetEffectiveContentDetectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);
        SetupEnabledAIInfrastructure();

        var result = await BuildEngine([pipelineCheck, aiCheck]).CheckMessageAsync(BuildRequest("Borderline content"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSpam, Is.True);
            Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.ReviewQueue));
        }
    }
```

Create `TelegramGroupsAdmin.UnitTests/Telegram/Services/BackgroundServices/DetectionActionServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BackgroundServices;

[TestFixture]
public class DetectionActionServiceTests
{
    private IReportService _reportService = null!;
    private DetectionActionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var configService = Substitute.For<IConfigService>();
        configService.GetEffectiveContentDetectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ContentDetectionConfig()); // defaults: ReviewQueue 2.5, AutoBan 4.0

        _reportService = Substitute.For<IReportService>();

        var services = new ServiceCollection();
        services.AddScoped(_ => configService);
        services.AddScoped(_ => _reportService);
        services.AddScoped(_ => Substitute.For<IBotModerationService>());

        _service = new DetectionActionService(
            services.BuildServiceProvider(), new PipelineMetrics(), NullLogger<DetectionActionService>.Instance);
    }

    [Test]
    public async Task HandleSpamDetectionActionsAsync_ScoreExactlyAtReviewThreshold_CreatesReport()
    {
        var message = new Message
        {
            Id = 42,
            Chat = new Chat { Id = -1001, Type = ChatType.Supergroup, Title = "Test" },
            From = new User { Id = 7, FirstName = "Tester" }
        };
        var spamResult = new ContentDetectionResult
        {
            IsSpam = true,
            TotalScore = 2.5,
            CheckResults = [new ContentCheckResponseV2 { CheckName = CheckName.StopWords, Score = 2.5, Abstained = false, Details = "x" }]
        };
        var record = new DetectionResultRecord { AddedBy = Actor.AutoDetection, Reason = "Borderline" };

        await _service.HandleSpamDetectionActionsAsync(message, spamResult, record);

        await _reportService.Received(1).CreateReportAsync(
            Arg.Any<Report>(), message, Actor.AutoDetection, Arg.Any<CancellationToken>());
    }
}
```

(If a `using` does not resolve, find the type's namespace with `grep -rn "interface IReportService\|class Report\b\|interface IBotModerationService" --include=*.cs TelegramGroupsAdmin.Telegram` and fix the `using`. Don't change the test's shape.)

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ContentDetectionEngineV2Tests|FullyQualifiedName~DetectionActionServiceTests"`
Expected: `CheckMessageAsync_AIScoreBelowReviewThreshold_IsNotSpam` FAILS (IsSpam is True). `HandleSpamDetectionActionsAsync_ScoreExactlyAtReviewThreshold_CreatesReport` FAILS (0 calls received).

- [ ] **Step 3: Implement**

In `ContentDetectionEngineV2`, replace the AI-confirmed block (starting at the comment `// AI confirmed spam - AI score is the sole authority`):

```csharp
                // AI verdict: the AI score replaces the pipeline aggregate and follows the same
                // threshold rule as the pipeline, so the verdict always agrees with the action.
                LogAIConfirmedSpam(_logger, request.User.ToLogDebug(), vetoResultV2.Score);

                var aiIsSpam = vetoResultV2.Score >= config.ReviewQueueThreshold;
                var confirmedResult = pipelineResult with
                {
                    CheckResults = updatedCheckResults,
                    IsSpam = aiIsSpam,
                    TotalScore = vetoResultV2.Score,
                    PrimaryReason = aiIsSpam
                        ? $"AI confirmed spam: {vetoResultV2.Details} (score: {vetoResultV2.Score:F1})"
                        : $"AI below review threshold: {vetoResultV2.Details} (score: {vetoResultV2.Score:F1})",
                    RecommendedAction = DetermineActionFromScore(vetoResultV2.Score, config.AutoBanThreshold, config.ReviewQueueThreshold),
                    RequiresAIConfirmation = false
                };
```

In `DetectionActionService.HandleSpamDetectionActionsAsync`, change the early return and the review branch to `>=` semantics:

```csharp
            if (!spamResult.IsSpam || spamResult.TotalScore < config.ReviewQueueThreshold)
            {
                return;
            }
```

```csharp
            else if (spamResult.TotalScore >= config.ReviewQueueThreshold)
```

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ContentDetectionEngineV2Tests|FullyQualifiedName~DetectionActionServiceTests"`
Expected: PASS, including all pre-existing engine tests.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs TelegramGroupsAdmin.Telegram/Services/BackgroundServices/DetectionActionService.cs TelegramGroupsAdmin.UnitTests/ContentDetection/ContentDetectionEngineV2Tests.cs TelegramGroupsAdmin.UnitTests/Telegram/Services/BackgroundServices/DetectionActionServiceTests.cs
git commit -F- <<'EOF'
fix(detection): apply the review threshold to AI verdicts and actions alike

An AI "review" below ReviewQueueThreshold is now ham (allowed), not
"spam but allowed". The action service queues at >= the threshold,
matching the engine.

Refs #547, #521

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 4: Additive migration `AddVerdictEvents` (columns, backfill, view)

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/DetectionResultRecord.cs` (4 new properties)
- Modify: `TelegramGroupsAdmin.Data/Models/MessageRecordDto.cs` (1 new property)
- Create: `TelegramGroupsAdmin.Data/Models/MessageVerdictView.cs`
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs` (DbSet, keyless view mapping, indexes)
- Create (generated, then edited): `TelegramGroupsAdmin.Data/Migrations/<timestamp>_AddVerdictEvents.cs`
- Test: Create `TelegramGroupsAdmin.IntegrationTests/Migrations/AddVerdictEventsMigrationTests.cs`

**Interfaces:**
- Produces: `DetectionResultRecordDto.Source (int?)`, `.Classification (int?)`, `.Properties (string? jsonb)`, `.AuditLogId (long?)`; `MessageRecordDto.MediaFeatures (string? jsonb, column media_features)`; `MessageVerdictView { long ChatId; int MessageId; int Classification; bool IsSpam; int? Source; DateTimeOffset? DetectedAt; long? VerdictId; }`; `AppDbContext.MessageVerdicts` (`DbSet<MessageVerdictView>`); DB view `message_verdicts`. Legacy columns (`is_spam`, `net_score`, `used_for_training`, `detection_source`) and `training_labels` are untouched here; Task 16 removes them.

- [ ] **Step 1: Change the model**

Add to `DetectionResultRecordDto` (after `ChatId`):

```csharp
    /// <summary>Core VerdictSource value. Nullable until DropLegacyVerdictColumns.</summary>
    [Column("source")]
    public int? Source { get; set; }

    /// <summary>Core VerdictClassification value. Nullable until DropLegacyVerdictColumns.</summary>
    [Column("classification")]
    public int? Classification { get; set; }

    /// <summary>Explanation only (e.g. backfill provenance). Never an input to any rule.</summary>
    [Column("properties", TypeName = "jsonb")]
    public string? Properties { get; set; }

    [Column("audit_log_id")]
    public long? AuditLogId { get; set; }
```

Add to `MessageRecordDto`:

```csharp
    /// <summary>Perceptual-hash features of the message's photo/video (MediaFeaturesDto JSON).</summary>
    [Column("media_features", TypeName = "jsonb")]
    public string? MediaFeatures { get; set; }
```

Create `TelegramGroupsAdmin.Data/Models/MessageVerdictView.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Keyless mapping of the message_verdicts view: each message's latest non-FileScan verdict
/// event (detected_at DESC, id DESC), or Unscanned (6) when it has none. The view selects;
/// it applies no spam rules.
/// </summary>
public class MessageVerdictView
{
    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("message_id")]
    public int MessageId { get; set; }

    [Column("classification")]
    public int Classification { get; set; }

    [Column("is_spam")]
    public bool IsSpam { get; set; }

    [Column("source")]
    public int? Source { get; set; }

    [Column("detected_at")]
    public DateTimeOffset? DetectedAt { get; set; }

    [Column("verdict_id")]
    public long? VerdictId { get; set; }
}
```

In `AppDbContext`, add next to the other view DbSets:

```csharp
    public DbSet<MessageVerdictView> MessageVerdicts => Set<MessageVerdictView>();
```

Next to the other `.ToView(...)` mappings:

```csharp
        // message_verdicts: one current verdict per message (latest event wins)
        modelBuilder.Entity<MessageVerdictView>()
            .HasNoKey()
            .ToView("message_verdicts");
```

Next to the existing `// DetectionResults indexes`:

```csharp
        // message_verdicts picks the latest row per (chat_id, message_id)
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => new { dr.ChatId, dr.MessageId, dr.DetectedAt, dr.Id })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("ix_detection_results_verdict_latest");
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.Classification)
            .HasDatabaseName("ix_detection_results_classification");
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.Source)
            .HasDatabaseName("ix_detection_results_source");
```

- [ ] **Step 2: Generate the migration**

Run: `dotnet ef migrations add AddVerdictEvents --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`
Expected: a migration that adds 5 columns and 3 indexes. No view operations (keyless views are not migrated by EF).

- [ ] **Step 3: Add the backfill and view SQL to the migration**

At the **end** of `Up` (after the generated `AddColumn`/`CreateIndex` calls), append:

```csharp
            // ── 1. source: what caused each legacy row ────────────────────────────────
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

            // ── 2a. classification: content scans (one rule: score >= ReviewQueueThreshold) ─
            migrationBuilder.Sql("""
                UPDATE detection_results dr SET
                    classification = CASE
                        WHEN x.is_spam AND dr.used_for_training THEN 2   -- ImplicitSpam
                        WHEN x.is_spam THEN 4                            -- UntrainedSpam
                        WHEN x.ai_positive THEN 5                        -- UntrainedHam (AI review below threshold)
                        ELSE 3                                           -- ImplicitHam
                    END,
                    properties = jsonb_build_object('backfilled', true)
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
                        SELECT c FROM jsonb_array_elements(COALESCE(d.check_results_json->'Checks', '[]'::jsonb)) c
                        WHERE (c->>'CheckName')::int = 6          -- CheckName.OpenAI
                        LIMIT 1) ai ON true
                    LEFT JOIN LATERAL (
                        SELECT jsonb_array_length(COALESCE(d.check_results_json->'Checks', '[]'::jsonb)) = 1
                           AND EXISTS (
                               SELECT 1 FROM jsonb_array_elements(COALESCE(d.check_results_json->'Checks', '[]'::jsonb)) c
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
```

At the **start** of `Down`, prepend:

```csharp
            migrationBuilder.Sql("DROP VIEW IF EXISTS message_verdicts;");
            migrationBuilder.Sql("DELETE FROM detection_results WHERE properties ? 'from';");
```

- [ ] **Step 4: Write the migration fixture test**

Create `TelegramGroupsAdmin.IntegrationTests/Migrations/AddVerdictEventsMigrationTests.cs`. It uses the empty template (migration fixtures may insert synthetic legacy rows). One database per fixture:

```csharp
using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TelegramGroupsAdmin.Data.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// Backfill correctness for AddVerdictEvents: one synthetic legacy row per backfill branch,
/// migrated from the previous migration. Synthetic rows are allowed here (migration fixture).
/// </summary>
[TestFixture]
public class AddVerdictEventsMigrationTests
{
    private const string PreviousMigration = "20260925190056_AddBanCelebrationSubscribers";
    private MigrationTestHelper _helper = null!;

    // ids >= 100000 so the identity sequence (used by the migration's INSERTs) never collides
    private const string Seed = """
        INSERT INTO telegram_users (telegram_user_id, is_trusted, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at)
        VALUES (9000000000900, false, false, now(), now(), now(), now());

        INSERT INTO content_detection_configs (id, chat_id, config_json, last_updated)
        VALUES (100001, -1002, '{"ReviewQueueThreshold": 3.0}', now());

        INSERT INTO messages (message_id, user_id, chat_id, "timestamp")
        SELECT g, 9000000000900, -1001, now() - interval '1 day' FROM generate_series(1, 40) g;
        INSERT INTO messages (message_id, user_id, chat_id, "timestamp") VALUES
            (1, 9000000000900, -1002, now()),
            (-1, 0, 0, now()), (-2, 0, 0, now()), (-3, 0, 0, now()), (-4, 0, 0, now());

        INSERT INTO detection_results (id, message_id, chat_id, detected_at, detection_source, detection_method,
            score, net_score, reason, system_identifier, web_user_id, telegram_user_id, used_for_training, edit_version, check_results_json)
        VALUES
        -- scans
        (100001, 1, -1001, '2026-01-01T00:00:00Z', 'auto', 'ChannelReply', 0.8, 0.8, 'No spam detected', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":13,"Score":0.8,"Abstained":false}]}'),
        (100002, 2, -1001, '2026-01-01T00:00:00Z', 'auto', 'Similarity, OpenAI', 2.3, 2.3, 'AI confirmed spam: AI: Review - borderline', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":2,"Score":3.5,"Abstained":false},{"CheckName":6,"Score":2.3,"Abstained":false}]}'),
        (100003, 3, -1001, '2026-01-01T00:00:00Z', 'auto', 'StopWords, OpenAI', 4.5, 4.5, 'AI confirmed spam', 'auto_detection', NULL, NULL, true, 0,
            '{"Checks":[{"CheckName":0,"Score":3,"Abstained":false},{"CheckName":6,"Score":4.5,"Abstained":false}]}'),
        (100004, 4, -1001, '2026-01-01T00:00:00Z', 'auto', 'StopWords, OpenAI', 3.0, 3.0, 'AI confirmed spam', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":0,"Score":3,"Abstained":false},{"CheckName":6,"Score":3.0,"Abstained":false}]}'),
        (100005, 5, -1001, '2026-01-01T00:00:00Z', 'auto', 'StopWords, OpenAI', 0, 0, 'OpenAI vetoed spam', 'auto_detection', NULL, NULL, true, 0,
            '{"Checks":[{"CheckName":0,"Score":3,"Abstained":false},{"CheckName":6,"Score":0,"Abstained":false}]}'),
        (100006, 6, -1001, '2026-01-01T00:00:00Z', 'auto', 'UrlBlocklist', 5, 5, 'Hard block policy violation', 'auto_detection', NULL, NULL, true, 0,
            '{"Checks":[{"CheckName":8,"Score":5,"Abstained":false}]}'),
        (100007, 1, -1002, '2026-01-01T00:00:00Z', 'auto', 'Bayes', 2.8, 2.8, 'Additive score', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":3,"Score":2.8,"Abstained":false}]}'),
        -- file scans
        (100008, 7, -1001, '2026-01-01T00:00:00Z', 'file_scan', 'FileScanningCheck', 5, 5, 'Malware', 'file_scanner', NULL, NULL, false, 0, NULL),
        (100009, 8, -1001, '2026-01-01T00:00:00Z', 'file_scan', 'FileScanningCheck', 0, 0, 'Clean', 'file_scanner', NULL, NULL, false, 0, NULL),
        -- manual decisions (source recovered from reason)
        (100010, 9,  -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Manually marked as spam by admin via UI', NULL, NULL, 9000000000900, false, 0, NULL),
        (100011, 10, -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 0, -5, 'Manually marked as ham (not spam) by admin - false positive correction', NULL, NULL, 9000000000900, false, 0, NULL),
        (100012, 11, -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Spam detected via /spam command in chat X', NULL, NULL, 9000000000900, false, 0, NULL),
        (100013, 12, -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Report #7 - spam/abuse', NULL, NULL, 9000000000900, false, 0, NULL),
        (100014, 13, -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Marked as spam by moderator', 'auto_detection', NULL, NULL, false, 0, NULL),
        -- chat-0 page + imports
        (100015, -1, 0, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Manually added training sample', 'System', NULL, NULL, true, 0, NULL),
        (100016, -2, 0, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, -5, 'Manually added training sample', 'System', NULL, NULL, false, 0, NULL),
        (100017, -3, 0, '2026-01-01T00:00:00Z', 'tg-spam-import', 'Manual', 5, 5, 'Manual label - spam', 'System', NULL, NULL, true, 0, NULL),
        (100018, -4, 0, '2026-01-01T00:00:00Z', 'tg-spam-import', 'Manual', 5, 5, 'Manual label - ham', 'System', NULL, NULL, true, 0, NULL),
        -- view: equal detected_at, higher id wins
        (100030, 30, -1001, '2026-02-01T00:00:00Z', 'auto', 'Bayes', 4.5, 4.5, 'x', 'auto_detection', NULL, NULL, true, 0, '{"Checks":[{"CheckName":3,"Score":4.5,"Abstained":false}]}'),
        (100031, 30, -1001, '2026-02-01T00:00:00Z', 'auto', 'Bayes', 0, 0, 'x', 'auto_detection', NULL, NULL, false, 1, '{"Checks":[{"CheckName":3,"Score":0,"Abstained":true}]}'),
        -- view: FileScan newer than the scan is ignored
        (100040, 40, -1001, '2026-02-01T00:00:00Z', 'auto', 'Bayes', 0, 0, 'x', 'auto_detection', NULL, NULL, false, 0, '{"Checks":[]}'),
        (100041, 40, -1001, '2026-02-02T00:00:00Z', 'file_scan', 'FileScanningCheck', 5, 5, 'Malware', 'file_scanner', NULL, NULL, false, 0, NULL);

        INSERT INTO training_labels (message_id, chat_id, label, labeled_by_user_id, labeled_at, reason) VALUES
        (3,  -1001, 0, NULL,           '2026-01-02T00:00:00Z', 'Auto-detected spam'),        -- auto-ban: no manual row
        (10, -1001, 1, 9000000000900, '2026-01-02T00:00:00Z', 'Marked ham'),                 -- matches manual row 100011
        (14, -1001, 0, 9000000000900, '2026-01-02T00:00:00Z', 'Marked spam');                -- no manual row
        """;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndMigrateToAsync(PreviousMigration);
        await _helper.ExecuteSqlAsync(Seed);
        await _helper.ApplyNextMigrationAsync(typeof(AddVerdictEvents).GetCustomAttribute<MigrationAttribute>()!.Id);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _helper.Dispose();

    private async Task<(int Source, int Classification)> RowAsync(long id)
    {
        await using var conn = new NpgsqlConnection(_helper.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT source, classification FROM detection_results WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, $"row {id} missing");
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    [TestCase(100001, 0, 3, TestName = "Pipeline 0.8 → ContentScan/ImplicitHam")]
    [TestCase(100002, 0, 5, TestName = "AI review 2.3 → ContentScan/UntrainedHam")]
    [TestCase(100003, 0, 2, TestName = "AI 4.5 trained → ContentScan/ImplicitSpam")]
    [TestCase(100004, 0, 4, TestName = "AI 3.0 untrained → ContentScan/UntrainedSpam")]
    [TestCase(100005, 0, 3, TestName = "AI veto → ContentScan/ImplicitHam")]
    [TestCase(100006, 0, 2, TestName = "Hard block → ContentScan/ImplicitSpam")]
    [TestCase(100007, 0, 3, TestName = "Per-chat threshold 3.0 makes 2.8 ham")]
    [TestCase(100008, 1, 4, TestName = "Infected file → FileScan/UntrainedSpam")]
    [TestCase(100009, 1, 5, TestName = "Clean file → FileScan/UntrainedHam")]
    [TestCase(100010, 11, 0, TestName = "Web mark spam")]
    [TestCase(100011, 12, 1, TestName = "Web mark ham")]
    [TestCase(100012, 13, 0, TestName = "/spam command")]
    [TestCase(100013, 14, 0, TestName = "Review spam")]
    [TestCase(100014, 99, 0, TestName = "Unrecoverable manual → LegacyManual")]
    [TestCase(100015, 16, 0, TestName = "Training Data page spam")]
    [TestCase(100016, 16, 1, TestName = "Training Data page ham")]
    [TestCase(100017, 18, 0, TestName = "Import spam")]
    [TestCase(100018, 18, 1, TestName = "Import label text wins over score sign")]
    public async Task Backfill_ClassifiesLegacyRow(long id, int expectedSource, int expectedClassification)
    {
        var (source, classification) = await RowAsync(id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Is.EqualTo(expectedSource));
            Assert.That(classification, Is.EqualTo(expectedClassification));
        }
    }

    [Test]
    public async Task Backfill_ExcludedPageRow_GainsTrainingExcludeEvent()
    {
        var count = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE message_id = -2 AND chat_id = 0 AND source = 17 AND classification = 5");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task Backfill_AutoBanLabel_BecomesAutoBanDecision()
    {
        var count = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE message_id = 3 AND chat_id = -1001 AND source = 10 AND classification = 0 AND system_identifier = 'auto_detection'");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task Backfill_LabelWithMatchingManualRow_IsNotDuplicated()
    {
        var count = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE message_id = 10 AND chat_id = -1001 AND source <> 0");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task Backfill_UserLabelWithoutManualRow_BecomesLegacyManualDecision()
    {
        var count = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE message_id = 14 AND chat_id = -1001 AND source = 99 AND classification = 0 AND telegram_user_id = 9000000000900");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task View_EqualDetectedAt_HigherIdWins()
    {
        var verdictId = await _helper.ExecuteScalarAsync<long>(
            "SELECT verdict_id FROM message_verdicts WHERE chat_id = -1001 AND message_id = 30");
        Assert.That(verdictId, Is.EqualTo(100031));
    }

    [Test]
    public async Task View_IgnoresNewerFileScan()
    {
        var verdictId = await _helper.ExecuteScalarAsync<long>(
            "SELECT verdict_id FROM message_verdicts WHERE chat_id = -1001 AND message_id = 40");
        Assert.That(verdictId, Is.EqualTo(100040));
    }

    [Test]
    public async Task View_LabelNewerThanScan_Wins()
    {
        var source = await _helper.ExecuteScalarAsync<int>(
            "SELECT source FROM message_verdicts WHERE chat_id = -1001 AND message_id = 3");
        Assert.That(source, Is.EqualTo(10));
    }

    [Test]
    public async Task View_MessageWithoutRows_IsUnscannedHam()
    {
        await using var conn = new NpgsqlConnection(_helper.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT classification, is_spam, verdict_id IS NULL FROM message_verdicts WHERE chat_id = -1001 AND message_id = 20", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(6));
            Assert.That(reader.GetBoolean(1), Is.False);
            Assert.That(reader.GetBoolean(2), Is.True);
        }
    }
}
```

(`MigrationTestHelper.ConnectionString` is public, as used by the existing repository tests. If `telegram_users` or `content_detection_configs` rejects the seed insert because of a NOT NULL column added by a later migration, add that column with a neutral value. Don't change the assertions.)

- [ ] **Step 5: Run the fixture and the full migration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~AddVerdictEventsMigrationTests|FullyQualifiedName~Migrations"`
Expected: all PASS. If a backfill case fails, fix the SQL, not the expectation. The expectations are the spec.

- [ ] **Step 6: Apply locally**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: exits 0. `docker exec tga-db psql -U tgadmin -d telegram_groups_admin -c "SELECT source, classification, count(*) FROM detection_results GROUP BY 1,2 ORDER BY 1,2"` shows no NULL `classification`.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Data TelegramGroupsAdmin.IntegrationTests/Migrations/AddVerdictEventsMigrationTests.cs
git commit -F- <<'EOF'
feat(data): add verdict events columns, backfill, and message_verdicts view

Additive step: source/classification/properties/audit_log_id on
detection_results, media_features on messages, legacy rows classified
by the single rule, training_labels folded into decision events, and
a view that picks each message's latest event.

Refs #547, #549, #548

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 5: Canonical data in the new shape + independent slice oracle

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/tools/convert-canonical-to-verdict-events.sql` (provenance; not embedded)
- Modify (regenerated): `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/32_detection_results.sql`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/33_training_labels.sql` (remove msg 7796's label only)
- Create (generated): `TelegramGroupsAdmin.IntegrationTests/TestData/CanonicalSlices.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDatasetConstants.cs` (new `Verdicts` class)
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/LoadCanonicalAsyncTests.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (counts + Part 2 recipe)
- Test: Create `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/CanonicalVerdictOracleTests.cs`

**Interfaces:**
- Consumes: Task 4 schema.
- Produces: canonical `detection_results` rows with `source`/`classification` set. `GoldenDatasetConstants.Verdicts` anchors (below). `CanonicalSlices.All` (frozen old-model slice per message). Flag-edited anchors used by Tasks 6–15.

Canonical must be written **independently of the migration**. The conversion script is its own, simpler rule set for the shapes canonical contains, and the slice oracle (computed from the *old* model's predicates before conversion) checks it.

- [ ] **Step 1: Build a scratch copy of canonical at the current schema**

```bash
docker exec tga-db createdb -U tgadmin canonical_convert
ConnectionStrings__PostgreSQL="Host=localhost;Port=5432;Database=canonical_convert;Username=tgadmin;Password=changeme" \
  dotnet run --project TelegramGroupsAdmin -- --migrate-only
for f in $(ls TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/*.sql | sort); do
  docker exec -i tga-db psql -q -v ON_ERROR_STOP=1 -U tgadmin -d canonical_convert < "$f"
done
```

Expected: all files load with no error. (`AddVerdictEvents` backfilled nothing, because the DB was empty when it ran, so every `classification` is NULL.)

- [ ] **Step 2: Freeze the old model's slices (the oracle) before touching anything**

This query applies the **old** predicates (training_labels and `is_spam AND used_for_training`), i.e. the model canonical was sampled with. A label counts only if no scan is newer than it, because an edit supersedes it under the new rule.

```bash
docker exec -i tga-db psql -At -U tgadmin -d canonical_convert > /tmp/slices.txt <<'SQL'
SELECT format('        (%sL, %s, VerdictClassification.%s),', m.chat_id, m.message_id, s.slice)
FROM messages m
CROSS JOIN LATERAL (SELECT CASE
    WHEN tl.label = 0 AND NOT EXISTS (SELECT 1 FROM detection_results d WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id
                                      AND d.detection_source = 'auto' AND d.detected_at > tl.labeled_at) THEN 'ExplicitSpam'
    WHEN tl.label = 1 AND NOT EXISTS (SELECT 1 FROM detection_results d WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id
                                      AND d.detection_source = 'auto' AND d.detected_at > tl.labeled_at) THEN 'ExplicitHam'
    WHEN tl.label IS NULL AND EXISTS (SELECT 1 FROM detection_results d WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id
                                      AND d.is_spam AND d.used_for_training) THEN 'ImplicitSpam'
    WHEN tl.label IS NULL AND m.deleted_at IS NULL AND NOT EXISTS (SELECT 1 FROM detection_results d WHERE d.chat_id = m.chat_id
                                      AND d.message_id = m.message_id AND d.is_spam) THEN 'ImplicitHam'
    END AS slice
    FROM (SELECT 1) one LEFT JOIN training_labels tl ON tl.chat_id = m.chat_id AND tl.message_id = m.message_id) s
WHERE s.slice IS NOT NULL
  AND (m.chat_id, m.message_id) NOT IN ((-100065252085265, 82837), (<chat of 7796>, 7796), (<chat of 216684>, 216684), (<chat of the Step 4 UntrainedHam anchor>, <its message_id>))
ORDER BY m.chat_id, m.message_id;
SQL
```

Replace the `<chat of …>` tokens with the chat ids from `SELECT chat_id FROM messages WHERE message_id IN (7796, 216684)`. The Step 4 anchor is chosen in Step 4d, so run this after 4d's selection query but **before** 4d's update. Flag-edited messages are excluded because their verdicts are edited on purpose.

Write `TelegramGroupsAdmin.IntegrationTests/TestData/CanonicalSlices.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Frozen oracle: each canonical message's training slice under the OLD model
/// (training_labels + is_spam/used_for_training), computed before canonical was converted to
/// verdict events (canonical edit 2026-09-27). Independent of the new classifier/migration, so
/// CanonicalVerdictOracleTests catches a wrong conversion. Flag-edited verdict anchors are excluded.
/// Implicit ham is expected as ImplicitHam or Unscanned (both are training ham).
/// </summary>
internal static class CanonicalSlices
{
    public static readonly (long ChatId, int MessageId, VerdictClassification Slice)[] All =
    [
        // paste /tmp/slices.txt here
    ];
}
```

Paste every line of `/tmp/slices.txt` in place of the comment.

- [ ] **Step 3: Write and run the standalone conversion**

Create `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/tools/convert-canonical-to-verdict-events.sql`:

```sql
-- One-off conversion of canonical detection_results to verdict events (canonical edit 2026-09-27).
-- Deliberately independent of the AddVerdictEvents migration; checked by CanonicalVerdictOracleTests.
BEGIN;

UPDATE detection_results SET source = CASE
    WHEN detection_source = 'auto' THEN 0
    WHEN detection_source = 'file_scan' THEN 1
    WHEN detection_source = 'tg-spam-import' THEN 18
    WHEN chat_id = 0 THEN 16
    WHEN reason LIKE 'Manually marked as spam by admin via UI%' THEN 11
    WHEN reason LIKE 'Manually marked as ham%' OR reason LIKE 'Manually added as ham training sample%' THEN 12
    WHEN reason LIKE 'Spam detected via /spam%' THEN 13
    WHEN reason LIKE 'Report #%' THEN 14
    ELSE 99 END;

-- canonical scans: no sub-threshold or AI-review rows exist, so the sign + training flag is exact
UPDATE detection_results SET classification = CASE
    WHEN net_score > 0 AND used_for_training THEN 2
    WHEN net_score > 0 THEN 4
    ELSE 3 END
WHERE source = 0;

UPDATE detection_results SET classification = CASE
    WHEN reason ILIKE '%label - ham%' THEN 1 ELSE 0 END
WHERE source = 18;

UPDATE detection_results SET classification = CASE
    WHEN source IN (10, 11, 13, 14) THEN 0
    WHEN source = 12 THEN 1
    WHEN net_score > 0 THEN 0 ELSE 1 END
WHERE source NOT IN (0, 1, 18);

INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, net_score,
    reason, web_user_id, telegram_user_id, system_identifier, used_for_training, edit_version, source, classification, properties)
SELECT message_id, chat_id, detected_at + interval '1 microsecond', detection_source, 'TrainingExclude', score, net_score,
    'Removed from training', web_user_id, telegram_user_id, system_identifier, false, 0, 17,
    CASE WHEN classification = 0 THEN 4 ELSE 5 END, NULL
FROM detection_results WHERE source IN (16, 18) AND used_for_training = false
ORDER BY chat_id, message_id;

INSERT INTO detection_results (message_id, chat_id, detected_at, detection_source, detection_method, score, net_score,
    reason, telegram_user_id, system_identifier, used_for_training, edit_version, source, classification, audit_log_id)
SELECT tl.message_id, tl.chat_id, tl.labeled_at, 'manual',
    CASE WHEN tl.label = 0 AND tl.labeled_by_user_id IS NULL THEN 'AutoBan' ELSE 'Manual' END,
    5.0, CASE WHEN tl.label = 0 THEN 5.0 ELSE -5.0 END, COALESCE(tl.reason, 'Training label'),
    tl.labeled_by_user_id,
    CASE WHEN tl.labeled_by_user_id IS NULL THEN CASE WHEN tl.label = 0 THEN 'auto_detection' ELSE 'unknown' END END,
    false, 0,
    CASE WHEN tl.label = 0 AND tl.labeled_by_user_id IS NULL THEN 10 ELSE 99 END,
    tl.label::int, tl.audit_log_id
FROM training_labels tl
WHERE NOT EXISTS (SELECT 1 FROM detection_results d
                  WHERE d.chat_id = tl.chat_id AND d.message_id = tl.message_id
                    AND d.source NOT IN (0, 1, 17) AND (d.classification = 0) = (tl.label = 0))
ORDER BY tl.chat_id, tl.message_id;

-- flag-edits: appended in Step 4
COMMIT;
```

- [ ] **Step 4: Flag-edit the new-model anchors (append to the script before COMMIT)**

Before each edit, confirm the row is unreferenced: `grep -rn <id> TelegramGroupsAdmin.*Tests docs` must show only canonical SQL. Each edit copies a real prod shape, scrubbed like the rest of canonical (no real names or text, and `canonical-spam.test` for any URL).

```sql
-- 4a. edit rescan flips a message to spam (msg 82837 @financerope): the v1 rescan became spam
UPDATE detection_results SET score = 4.5, net_score = 4.5, used_for_training = true, classification = 2,
    reason = '[Edit #1] AI confirmed spam: AI: Spam - promotional contact request (score: 4.5)'
WHERE id = 1334;

-- 4b. spam among @mouthsafeguard's last three (msg 7796): an untrained spam scan, no admin label
UPDATE detection_results SET score = 3.0, net_score = 3.0, used_for_training = false, classification = 4,
    reason = 'AI confirmed spam: AI: Spam - unsolicited offer (score: 3.0)'
WHERE id = 1339;
DELETE FROM detection_results WHERE message_id = 7796 AND source IN (12, 99) AND classification = 1;

-- 4c. an attachment scan beside a content scan (msg 216684): the manual row becomes a clean file scan
UPDATE detection_results SET detection_source = 'file_scan', detection_method = 'FileScanningCheck',
    source = 1, classification = 5, score = 0, net_score = 0, used_for_training = false,
    reason = 'No threats detected', web_user_id = NULL, telegram_user_id = NULL,
    system_identifier = 'file_scanner', check_results_json = NULL,
    detected_at = (SELECT max(detected_at) + interval '1 minute' FROM detection_results WHERE message_id = 216684)
WHERE id = 2535;
```

**4d. `UntrainedHam` anchor.** It needs an unreferenced message with **no** `training_labels` row and exactly one detection row, an auto scan with an OpenAI check. Find candidates, then pick the first one that is unreferenced by the grep:

```sql
SELECT d.id, d.message_id, d.chat_id, m.user_id FROM detection_results d
JOIN messages m USING (chat_id, message_id)
WHERE d.detection_source = 'auto' AND d.check_results_json::text LIKE '%"CheckName": 6%'
  AND NOT EXISTS (SELECT 1 FROM training_labels tl WHERE tl.chat_id = d.chat_id AND tl.message_id = d.message_id)
  AND (SELECT count(*) FROM detection_results x WHERE x.chat_id = d.chat_id AND x.message_id = d.message_id) = 1
ORDER BY d.id;
```

Then append (with `<id>` the chosen detection row):

```sql
UPDATE detection_results SET score = 2.0, net_score = 2.0, used_for_training = false, classification = 5,
    reason = 'AI below review threshold: AI: Review - friendly greeting, could be a lead-in (score: 2.0)',
    check_results_json = '{"Checks":[{"CheckName":2,"Score":3.5,"IsSpam":true,"Details":"High similarity to spam sample","Abstained":false,"ProcessingTimeMs":1.2},{"CheckName":6,"Score":2.0,"IsSpam":true,"Details":"AI: Review - friendly greeting, could be a lead-in","Abstained":false,"ProcessingTimeMs":2100.0}]}'
WHERE id = <id>;
```

Also remove message 7796's row from `33_training_labels.sql` (delete that one INSERT line). The legacy readers that still use `training_labels` until Task 16 then see the same edit.

Run: `docker exec -i tga-db psql -v ON_ERROR_STOP=1 -U tgadmin -d canonical_convert < TelegramGroupsAdmin.IntegrationTests/TestData/SQL/tools/convert-canonical-to-verdict-events.sql`, then `DELETE FROM training_labels WHERE message_id = 7796;` in the scratch DB.
Expected: COMMIT. `SELECT count(*) FROM detection_results WHERE classification IS NULL` returns 0.

- [ ] **Step 5: Dump the converted table back into canonical**

```bash
docker exec tga-db pg_dump -U tgadmin -d canonical_convert --data-only --column-inserts -t detection_results \
  | grep -E '^(INSERT INTO|SELECT pg_catalog.setval)' \
  > TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/32_detection_results.sql
docker exec tga-db psql -At -U tgadmin -d canonical_convert -c "SELECT count(*) FROM detection_results"
docker exec tga-db dropdb -U tgadmin canonical_convert
```

Record the printed count (the new canonical `detection_results` total).

- [ ] **Step 6: Pin anchors and counts**

Add to `GoldenDatasetConstants` a nested class (same XML-doc-per-constant style as `Retention`):

```csharp
    /// <summary>
    /// Verdict-event anchors (canonical edit 2026-09-27). See IntegrationTests/CLAUDE.md Part 2
    /// "Verdict events". Edited rows are guarded by read-back assertions in their tests.
    /// </summary>
    public static class Verdicts
    {
        /// <summary>Scan then WebMarkHam correction → ExplicitHam. @dinnersnazzy.</summary>
        public const int CorrectedToHamMsgId = 213409;
        /// <summary>Auto-ban decision (migrated label, no user) → ExplicitSpam. @AndrewLong6.</summary>
        public const int AutoBanMsgId = 220384;
        /// <summary>Auto-banned message on Land Owners used as the Mark as Ham subject.</summary>
        public const int MarkAsHamSubjectMsgId = 8646;
        /// <summary>Edited: v1 rescan flipped to ImplicitSpam (dr 1334). @financerope, 5 edits.</summary>
        public const int EditFlipMsgId = 82837;
        public const long EditFlipChatId = -100065252085265L;
        /// <summary>Edited: UntrainedSpam scan, label removed (dr 1339). @mouthsafeguard's third message.</summary>
        public const int SpamInTrustWindowMsgId = 7796;
        /// <summary>@mouthsafeguard.</summary>
        public const long SpamInTrustWindowUserId = 9917295586642L;
        /// <summary>Edited: newest row is a clean FileScan (dr 2535) that the view must ignore.</summary>
        public const int FileScanBesideScanMsgId = 216684;
        public const long FileScanRowId = 2535;
        /// <summary>Edited in Step 4d: UntrainedHam (AI review 2.0 below threshold).</summary>
        public const int UntrainedHamMsgId = <message_id from 4d>;
        public const long UntrainedHamChatId = <chat_id from 4d>;
        /// <summary>Unscanned message (no verdict rows). @unhelpfulgrab.</summary>
        public const int UnscannedMsgId = 219219;
        /// <summary>User whose three latest messages are all training ham.</summary>
        public const long AllHamUserId = 9184102838760L;
        /// <summary>Only a training label keeps this old message (all its rows used_for_training = false). @arisepacifism.</summary>
        public const int LabeledOnlyRetentionMsgId = 7974;
    }
```

Replace `<message_id from 4d>` / `<chat_id from 4d>` with the values chosen in 4d. They are literal values, so the file compiles.

In `LoadCanonicalAsyncTests`, update the pinned `detection_results` count (if one exists) to the Step 5 total. Leave `training_labels` at 199 (200 minus msg 7796).

In `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`:
- Update the table rows for `32 detection_results` (new count; "source/classification set by `tools/convert-canonical-to-verdict-events.sql`") and `33 training_labels` (199).
- Add a Part 2 recipe "Verdict events (canonical edit 2026-09-27)" listing each `Verdicts` anchor and the 4a–4d edits.

- [ ] **Step 7: Write the oracle test**

Create `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/CanonicalVerdictOracleTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

/// <summary>
/// The converted canonical must agree with the old model's slices (frozen before conversion).
/// </summary>
[TestFixture]
public class CanonicalVerdictOracleTests
{
    private MigrationTestHelper _helper = null!;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    [Test]
    public async Task EveryCanonicalSlice_ResolvesToTheMatchingClassification()
    {
        await using var ctx = _helper.GetDbContext();
        var verdicts = await ctx.MessageVerdicts.AsNoTracking()
            .ToDictionaryAsync(v => (v.ChatId, v.MessageId), v => (VerdictClassification)v.Classification);

        var mismatches = CanonicalSlices.All
            .Where(s => !Matches(s.Slice, verdicts.GetValueOrDefault((s.ChatId, s.MessageId), VerdictClassification.Unscanned)))
            .Select(s => $"{s.ChatId}/{s.MessageId}: slice {s.Slice}, view {verdicts.GetValueOrDefault((s.ChatId, s.MessageId))}")
            .ToList();

        Assert.That(mismatches, Is.Empty);
    }

    [Test]
    public async Task EveryStoredRow_HasAClassification()
    {
        await using var ctx = _helper.GetDbContext();
        Assert.That(await ctx.DetectionResults.CountAsync(d => d.Classification == null || d.Source == null), Is.Zero);
    }

    private static bool Matches(VerdictClassification slice, VerdictClassification actual) => slice switch
    {
        VerdictClassification.ImplicitHam => actual is VerdictClassification.ImplicitHam or VerdictClassification.Unscanned,
        _ => actual == slice
    };
}
```

- [ ] **Step 8: Run the integration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`
Expected: all PASS. Pre-existing tests that asserted on msg 7796's label or on the edited rows' `net_score` must not exist (the rows were chosen unreferenced). If one does fail, stop and report it; don't change the assertion.

- [ ] **Step 9: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests
git commit -F- <<'EOF'
test(canonical): convert detection_results to verdict events with a slice oracle

Canonical is converted by a standalone script (not the migration) and
checked against the old model's slices, frozen before conversion.
Adds flag-edited anchors for edit flips, the trust window, file scans
and AI-review-below-threshold.

Refs #547

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 6: Write side — every verdict goes through `Record*Async`

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Models/DetectionResultRecord.cs`
- Create: `TelegramGroupsAdmin.ContentDetection/Repositories/Mappings/MessageVerdictMappings.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/Mappings/DetectionResultMappings.cs` (ToModel; delete ToDto)
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/IDetectionResultsRepository.cs`, `DetectionResultsRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Handlers/ContentDetectionOrchestrator.cs` (store via `RecordScanAsync`; drop insert-time SimHash dedup)
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IMessageHistoryRepository.cs`, `MessageHistoryRepository.cs` (delete `HasSimilarTrainingHashAsync`)
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Jobs/FileScanJob.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Intents/SpamBanIntent.cs` (+ `Source`), all 4 production creators, `BotModerationService.cs:148`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/ITrainingHandler.cs`, `TrainingHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ContentReportHandler.cs` (Dismiss)
- Modify: `TelegramGroupsAdmin/Components/Pages/Messages.razor` (Mark as Ham → handler; closes #386)
- Modify: `TelegramGroupsAdmin.Telegram/Models/MessageWithDetectionHistory.cs` (+ `LatestScan`), `Handlers/NotificationHandler.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/DetectionResultsRepositoryTests.cs` (new `#region Verdict writes`)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Handlers/TrainingHandlerTests.cs`, `.../BotModerationServiceTests.cs`, `TelegramGroupsAdmin.UnitTests/Services/ReportActions/ContentReportHandlerTests.cs`, and every test that builds a `DetectionResultRecord` with `IsSpam =` or a `SpamBanIntent`

**Interfaces:**
- Consumes: Task 1 types, Task 2 `VerdictClassifier`, Task 4 columns and view.
- Produces:
  - `DetectionResultRecord.Source (VerdictSource)`, `.Classification (VerdictClassification)`, `.Properties (string?)`, `.AuditLogId (long?)`; `.IsSpam` becomes get-only `=> Classification.IsSpam()`.
  - `IDetectionResultsRepository`:
    - `Task<DetectionResultRecord> RecordScanAsync(int messageId, long chatId, ContentDetectionResult scan, int editVersion, CancellationToken ct = default)`
    - `Task RecordFileScanAsync(int messageId, long chatId, bool infected, double score, string details, CancellationToken ct = default)`
    - `Task<long> RecordDecisionAsync(int messageId, long chatId, VerdictSource source, Actor actor, string reason, bool? isSpam = null, long? auditLogId = null, CancellationToken ct = default)`
    - `Task<MessageVerdict?> GetCurrentVerdictAsync(int messageId, long chatId, CancellationToken ct = default)` (null only if the message does not exist)
    - `InsertAsync` is **removed**.
  - `MessageVerdictView.ToModel()` → `MessageVerdict` (public extension in ContentDetection mappings, reused by other projects' repositories).
  - `SpamBanIntent.Source` (`required VerdictSource`).
  - `ITrainingHandler.CreateSpamSampleAsync(int messageId, ChatIdentity chat, Actor executor, VerdictSource source, string reason, CancellationToken ct = default)` and `ITrainingHandler.CreateHamSampleAsync(int messageId, ChatIdentity chat, Actor executor, VerdictSource source, string reason, CancellationToken ct = default)` (source ∈ {WebMarkHam, ReviewDismiss}).
  - `MessageWithDetectionHistory.LatestScan` (latest `ContentScan` row).

Until Task 16 drops them, `Record*Async` also fills the legacy columns (`detection_source`, `net_score` with sign = `IsSpam()`, `used_for_training`), so the not-yet-migrated readers keep working. `TrainingHandler` keeps upserting `training_labels` for explicit decisions until Task 16 removes the table.

- [ ] **Step 1: Write the failing integration tests**

Add to `DetectionResultsRepositoryTests` (it already builds the repository over a golden-template clone in `SetUp`; add `using TelegramGroupsAdmin.ContentDetection.Constants; using TelegramGroupsAdmin.ContentDetection.Models; using TelegramGroupsAdmin.ContentDetection.Services; using TelegramGroupsAdmin.Core.Models; using TelegramGroupsAdmin.IntegrationTests.TestData;`):

```csharp
    #region Verdict writes

    // A canonical message with no detection rows (existing insert target of this fixture).
    private const int EmptyTargetMessageId = InvalidateTargetMessageId;

    [Test]
    public async Task RecordScanAsync_AIReviewBelowThreshold_StoresUntrainedHam_AndViewAgrees()
    {
        var scan = new ContentDetectionResult
        {
            IsSpam = false,
            TotalScore = 2.0,
            PrimaryReason = "AI below review threshold",
            CheckResults =
            [
                new ContentCheckResponseV2 { CheckName = CheckName.Similarity, Score = 3.5, Abstained = false, Details = "sim" },
                new ContentCheckResponseV2 { CheckName = CheckName.OpenAI, Score = 2.0, Abstained = false, Details = "AI: Review" }
            ]
        };

        var record = await _repository!.RecordScanAsync(EmptyTargetMessageId, MainChatId, scan, editVersion: 0);
        var verdict = await _repository.GetCurrentVerdictAsync(EmptyTargetMessageId, MainChatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Source, Is.EqualTo(VerdictSource.ContentScan));
            Assert.That(record.Classification, Is.EqualTo(VerdictClassification.UntrainedHam));
            Assert.That(verdict!.Classification, Is.EqualTo(VerdictClassification.UntrainedHam));
            Assert.That(verdict.IsSpam, Is.False);
        }
    }

    [Test]
    public async Task RecordDecisionAsync_WebMarkHamOnAutoBannedMessage_ViewBecomesExplicitHam()
    {
        var chatId = GoldenDatasetConstants.Chats.LandOwnersChatId;
        var msgId = GoldenDatasetConstants.Verdicts.MarkAsHamSubjectMsgId;
        var before = await _repository!.GetCurrentVerdictAsync(msgId, chatId);
        Assert.That(before!.Classification, Is.EqualTo(VerdictClassification.ExplicitSpam), "canonical precondition");

        await _repository.RecordDecisionAsync(msgId, chatId,
            VerdictSource.WebMarkHam, Actor.FromWebUser(GoldenDatasetConstants.WebUsers.OwnerId), "false positive");

        var after = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        Assert.That(after!.Classification, Is.EqualTo(VerdictClassification.ExplicitHam));
    }

    [Test]
    public async Task RecordDecisionAsync_DismissAfterMarkAsHam_AppendsAndViewShowsLatest()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId;
        Assert.That((await _repository!.GetCurrentVerdictAsync(msgId, chatId))!.Classification,
            Is.EqualTo(VerdictClassification.ExplicitHam), "canonical precondition");

        await _repository.RecordDecisionAsync(msgId, chatId, VerdictSource.ReviewDismiss,
            Actor.FromWebUser(GoldenDatasetConstants.WebUsers.OwnerId), "Report dismissed");

        var after = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        Assert.That(after!.Classification, Is.EqualTo(VerdictClassification.ImplicitHam));
    }

    [Test]
    public async Task RecordFileScanAsync_DoesNotChangeTheMessageVerdict()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.AutoBanMsgId;
        var before = await _repository!.GetCurrentVerdictAsync(msgId, chatId);

        await _repository.RecordFileScanAsync(msgId, chatId, infected: true, score: 5.0, details: "Malware");

        var after = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        Assert.That(after!.VerdictId, Is.EqualTo(before!.VerdictId));
    }

    [Test]
    public async Task GetCurrentVerdictAsync_MessageWithoutRows_IsUnscanned()
    {
        var verdict = await _repository!.GetCurrentVerdictAsync(
            GoldenDatasetConstants.Verdicts.UnscannedMsgId, GoldenDatasetConstants.Chats.MainChatId);
        Assert.That(verdict!.Classification, Is.EqualTo(VerdictClassification.Unscanned));
    }

    #endregion
```

(`GoldenDatasetConstants.Chats.MainChatId` is -100026957614982. If a `Verdicts` anchor turns out to live in a different chat, use the chat printed by `SELECT chat_id FROM messages WHERE message_id = <id>` and add it as a constant next to the anchor.)

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~DetectionResultsRepositoryTests"`
Expected: build FAIL (`RecordScanAsync` etc. not defined).

- [ ] **Step 3: Domain model and mappings**

In `TelegramGroupsAdmin.ContentDetection/Models/DetectionResultRecord.cs`, replace `public bool IsSpam { get; set; }` with:

```csharp
    public VerdictSource Source { get; set; }
    public VerdictClassification Classification { get; set; }
    public bool IsSpam => Classification.IsSpam();
    public string? Properties { get; set; }
    public long? AuditLogId { get; set; }
```

(`using TelegramGroupsAdmin.Core.Models;` is already present.) Change the `NetScore` trailing comment to `// Legacy: removed with DropLegacyVerdictColumns`.

Create `TelegramGroupsAdmin.ContentDetection/Repositories/Mappings/MessageVerdictMappings.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

public static class MessageVerdictMappings
{
    extension(DataModels.MessageVerdictView view)
    {
        public MessageVerdict ToModel() => new(
            view.ChatId,
            view.MessageId,
            (VerdictClassification)view.Classification,
            view.IsSpam,
            view.Source is { } source ? (VerdictSource)source : null,
            view.DetectedAt,
            view.VerdictId);
    }
}
```

In `DetectionResultMappings.ToModel`, replace `IsSpam = data.IsSpam,` with:

```csharp
                Source = (VerdictSource)(data.Source ?? throw new InvalidOperationException($"detection_results {data.Id} has no source")),
                Classification = (VerdictClassification)(data.Classification ?? throw new InvalidOperationException($"detection_results {data.Id} has no classification")),
                Properties = data.Properties,
                AuditLogId = data.AuditLogId,
```

(Add `using TelegramGroupsAdmin.Core.Models;`.) Delete the `extension(UiModels.DetectionResultRecord ui)` block (`ToDto`); its only caller, `InsertAsync`, is removed below.

In `DetectionResultsRepository.WithActorJoins`, replace `IsSpam = x.dr.IsSpam,` with:

```csharp
                Source = (VerdictSource)x.dr.Source!.Value,
                Classification = (VerdictClassification)x.dr.Classification!.Value,
                Properties = x.dr.Properties,
                AuditLogId = x.dr.AuditLogId,
```

In `GetRecentNonSpamResultsForUserAsync`, the projected `IsSpam` is no longer translatable. Filter on the DTO before `WithActorJoins` (Task 9 replaces this query entirely):

```csharp
        var query = WithActorJoins(
                context.DetectionResults.AsNoTracking()
                    .Where(dr => dr.Classification != null && !VerdictClassifications.SpamValues.Contains(dr.Classification.Value)),
                context)
            .Where(x => x.UserId == userId);
```

- [ ] **Step 4: Repository write API**

In `IDetectionResultsRepository`, delete `InsertAsync` and add:

```csharp
    /// <summary>Records a content-detection scan. The classification is decided by VerdictClassifier.</summary>
    Task<DetectionResultRecord> RecordScanAsync(int messageId, long chatId, ContentDetectionResult scan, int editVersion, CancellationToken cancellationToken = default);

    /// <summary>Records an attachment scan. Never becomes the message's verdict.</summary>
    Task RecordFileScanAsync(int messageId, long chatId, bool infected, double score, string details, CancellationToken cancellationToken = default);

    /// <summary>Records a decision (admin, auto-ban, dismiss, training page, exclusion). Latest event wins.</summary>
    Task<long> RecordDecisionAsync(int messageId, long chatId, VerdictSource source, Actor actor, string reason,
        bool? isSpam = null, long? auditLogId = null, CancellationToken cancellationToken = default);

    /// <summary>The message's current verdict from message_verdicts; null only if the message does not exist.</summary>
    Task<MessageVerdict?> GetCurrentVerdictAsync(int messageId, long chatId, CancellationToken cancellationToken = default);
```

(Add `using TelegramGroupsAdmin.ContentDetection.Services; using TelegramGroupsAdmin.Core.Models;`.)

In `DetectionResultsRepository`, delete `InsertAsync` and add:

```csharp
    public async Task<DetectionResultRecord> RecordScanAsync(int messageId, long chatId, ContentDetectionResult scan,
        int editVersion, CancellationToken cancellationToken = default)
    {
        var classification = VerdictClassifier.ClassifyScan(scan);
        var reasonPrefix = editVersion > 0 ? $"[Edit #{editVersion}] " : "";
        var method = scan.CheckResults.Count > 0 ? string.Join(", ", scan.CheckResults.Select(c => c.CheckName)) : "Unknown";

        var row = NewRow(messageId, chatId, VerdictSource.ContentScan, classification, Actor.AutoDetection,
            scan.TotalScore, $"{reasonPrefix}{scan.PrimaryReason}", method);
        row.CheckResultsJson = CheckResultsSerializer.Serialize(scan.CheckResults);
        row.EditVersion = editVersion;

        await SaveAsync(row, cancellationToken);
        return row.ToModel();
    }

    public async Task RecordFileScanAsync(int messageId, long chatId, bool infected, double score, string details,
        CancellationToken cancellationToken = default)
    {
        var row = NewRow(messageId, chatId, VerdictSource.FileScan, VerdictClassifier.ClassifyFileScan(infected),
            Actor.FileScanner, score, details, "FileScanningCheck");
        await SaveAsync(row, cancellationToken);
    }

    public async Task<long> RecordDecisionAsync(int messageId, long chatId, VerdictSource source, Actor actor, string reason,
        bool? isSpam = null, long? auditLogId = null, CancellationToken cancellationToken = default)
    {
        var row = NewRow(messageId, chatId, source, VerdictClassifier.ClassifyDecision(source, isSpam), actor,
            score: 5.0, reason, method: source.ToString());
        row.AuditLogId = auditLogId;
        await SaveAsync(row, cancellationToken);
        return row.Id;
    }

    public async Task<MessageVerdict?> GetCurrentVerdictAsync(int messageId, long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var view = await context.MessageVerdicts.AsNoTracking()
            .FirstOrDefaultAsync(v => v.MessageId == messageId && v.ChatId == chatId, cancellationToken);
        return view?.ToModel();
    }

    private static DataModels.DetectionResultRecordDto NewRow(int messageId, long chatId, VerdictSource source,
        VerdictClassification classification, Actor actor, double score, string reason, string method)
    {
        ActorMappings.SetActorColumns(actor, out var webUserId, out var telegramUserId, out var systemIdentifier);
        var isSpam = classification.IsSpam();
        return new DataModels.DetectionResultRecordDto
        {
            MessageId = messageId,
            ChatId = chatId,
            DetectedAt = DateTimeOffset.UtcNow,
            Source = (int)source,
            Classification = (int)classification,
            DetectionMethod = method,
            Score = score,
            Reason = reason,
            WebUserId = webUserId,
            TelegramUserId = telegramUserId,
            SystemIdentifier = systemIdentifier,
            // Legacy columns, kept consistent until DropLegacyVerdictColumns removes them.
            DetectionSource = source switch
            {
                VerdictSource.ContentScan => "auto",
                VerdictSource.FileScan => "file_scan",
                VerdictSource.Import => "tg-spam-import",
                _ => "manual"
            },
            NetScore = isSpam ? Math.Abs(score) : -Math.Abs(score),
            UsedForTraining = classification == VerdictClassification.ImplicitSpam
                || (source is VerdictSource.TrainingDataPage or VerdictSource.Import && classification.IsExplicit())
        };
    }

    private async Task SaveAsync(DataModels.DetectionResultRecordDto row, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.DetectionResults.Add(row);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Recorded {Source} verdict {Classification} for message {MessageId} in chat {ChatId} (score {Score:F2})",
            (VerdictSource)row.Source!.Value, (VerdictClassification)row.Classification!.Value, row.MessageId, row.ChatId, row.Score);
    }
```

(Add `using TelegramGroupsAdmin.ContentDetection.Services; using TelegramGroupsAdmin.Core.Models;`. `ActorMappings` is already imported via `TelegramGroupsAdmin.Core.Repositories.Mappings`.)

- [ ] **Step 5: Run the repository tests and verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~DetectionResultsRepositoryTests"`
Expected: new tests PASS. (Other projects fail to build until Steps 6–9 are done. Use `dotnet build TelegramGroupsAdmin.ContentDetection` for an earlier check.)

- [ ] **Step 6: Scan writers**

`ContentDetectionOrchestrator`: replace `StoreDetectionResultAsync` with:

```csharp
    /// <summary>
    /// Store the scan as a verdict event. The classification is decided once, by VerdictClassifier,
    /// inside the repository.
    /// </summary>
    private async Task<DetectionResultRecord> StoreDetectionResultAsync(
        IDetectionResultsRepository detectionResultsRepo,
        Message message,
        ContentDetectionResult spamResult,
        int editVersion,
        CancellationToken cancellationToken)
    {
        var detectionResult = await detectionResultsRepo.RecordScanAsync(
            message.MessageId, message.Chat.Id, spamResult, editVersion, cancellationToken);

        _logger.LogDebug(
            "Stored {Classification} verdict for message {MessageId} (edit {EditVersion}, score {Score:F2})",
            detectionResult.Classification, message.MessageId, editVersion, spamResult.TotalScore);

        return detectionResult;
    }
```

Update its call site to drop the `text` argument. Remove the now-unused `SimHashService` constructor parameter and field from the orchestrator. Delete `HasSimilarTrainingHashAsync` from `IMessageHistoryRepository` and `MessageHistoryRepository`. Training-time `DeduplicateSamples` already removes near-duplicates. Delete that method's tests in `MessageHistoryRepositoryTests` (`grep -n HasSimilarTrainingHash`) together with any fixture constants used only by them.

`FileScanJob`: replace the `var detectionRecord = new DetectionResultRecord { ... }; await detectionResultsRepository.InsertAsync(...)` block with:

```csharp
                await detectionResultsRepository.RecordFileScanAsync(
                    payload.MessageId, payload.Chat.Id, isInfected, scanResult.Score, scanResult.Details, cancellationToken);
```

- [ ] **Step 7: Decision writers**

`SpamBanIntent`: add

```csharp
    /// <summary>What caused this ban's verdict event (AutoBan, WebMarkSpam, SpamCommand, ReviewSpam).</summary>
    public required VerdictSource Source { get; init; }
```

Set it at each creator: `DetectionActionService` (both) → `VerdictSource.AutoBan`; `Messages.razor` `HandleMarkAsSpam` → `VerdictSource.WebMarkSpam`; `SpamCommand` → `VerdictSource.SpamCommand`; `ContentReportHandler.SpamAsync` → `VerdictSource.ReviewSpam`. In `BotModerationService` line 148:

```csharp
            () => _trainingHandler.CreateSpamSampleAsync(intent.MessageId, intent.Chat, intent.Executor, intent.Source, intent.Reason, cancellationToken),
```

`ITrainingHandler`:

```csharp
    /// <summary>
    /// Records a spam decision (ExplicitSpam) and triggers retraining. Every executor records
    /// a decision, System included; auto-bans are explicit by design.
    /// </summary>
    Task CreateSpamSampleAsync(int messageId, ChatIdentity chat, Actor executor, VerdictSource source, string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a ham decision: WebMarkHam (ExplicitHam) or ReviewDismiss (ImplicitHam), and
    /// triggers retraining. The newer event supersedes older spam events everywhere.
    /// </summary>
    Task CreateHamSampleAsync(int messageId, ChatIdentity chat, Actor executor, VerdictSource source, string reason,
        CancellationToken cancellationToken = default);
```

`TrainingHandler.CreateSpamSampleAsync`: change the signature to match. Replace the `if (executor.Type != ActorType.System) { ... InsertAsync ... }` block with an unconditional:

```csharp
        await _detectionResultsRepository.RecordDecisionAsync(
            messageId, chat.Id, source, executor, reason, cancellationToken: cancellationToken);
```

Keep the existing `training_labels` upsert, retraining trigger, media download and sample saves exactly as they are (Task 15 removes the sample saves, and Task 16 removes the label upserts).

Add `CreateHamSampleAsync`:

```csharp
    /// <inheritdoc />
    public async Task CreateHamSampleAsync(int messageId, ChatIdentity chat, Actor executor, VerdictSource source,
        string reason, CancellationToken cancellationToken = default)
    {
        if (source is not (VerdictSource.WebMarkHam or VerdictSource.ReviewDismiss))
            throw new ArgumentOutOfRangeException(nameof(source), source, "Ham decisions are WebMarkHam or ReviewDismiss");

        await _detectionResultsRepository.RecordDecisionAsync(
            messageId, chat.Id, source, executor, reason, cancellationToken: cancellationToken);

        // Legacy explicit label for readers not yet on message_verdicts (removed by DropLegacyVerdictColumns).
        if (source == VerdictSource.WebMarkHam)
        {
            await _trainingLabelsRepository.UpsertLabelAsync(
                messageId, chat.Id, label: TrainingLabel.Ham, actor: executor, reason: reason,
                auditLogId: null, cancellationToken: cancellationToken);
        }

        await _jobTriggerService.TriggerNowAsync(
            BackgroundJobNames.ClassifierRetraining, payload: new { }, cancellationToken: cancellationToken);

        _logger.LogInformation("Recorded {Source} ham decision for message {MessageId} by {Executor}",
            source, messageId, executor.GetDisplayText());
    }
```

`Messages.razor` `HandleMarkAsHam`: replace steps 1–3 (the `new DetectionResultRecord`, `DetectionResultsRepository.InsertAsync`, `TrainingLabelsRepository.UpsertLabelAsync`, `JobTriggerService.TriggerNowAsync`) and the misleading "atomic invalidation" comment with:

```csharp
            await TrainingHandler.CreateHamSampleAsync(
                message.MessageId, message.Chat, executor, VerdictSource.WebMarkHam, reason);
```

Add `@inject ITrainingHandler TrainingHandler`. Remove the `TrainingLabelsRepository` and `JobTriggerService` injections if nothing else in the page uses them (`grep -n` first; line ~1079 still reads `TrainingLabelsRepository.GetByMessageIdAsync`, which Task 13 replaces, so keep that injection for now). Keep the unban-on-false-positive logic. Change the final `else` log text to `"Message {MessageId} marked as ham"`.

`ContentReportHandler`: add `ITrainingHandler trainingHandler` to the primary constructor. In `DismissAsync`, after the status update succeeds and before `CleanupContentReportAsync`:

```csharp
        await trainingHandler.CreateHamSampleAsync(
            report.MessageId, report.Chat, executor, VerdictSource.ReviewDismiss,
            reason is null ? $"Report #{reportId} dismissed" : $"Report #{reportId} dismissed: {reason}",
            cancellationToken);
```

`MessageWithDetectionHistory`: add

```csharp
    /// <summary>Latest content-detection scan (detector evidence), ignoring decisions and file scans.</summary>
    public DetectionResultRecord? LatestScan => DetectionResults.FirstOrDefault(d => d.Source == VerdictSource.ContentScan);
```

`NotificationHandler.NotifyAdminsSpamBanAsync`: keep `bannedBy: detection?.AddedBy` from `LatestDetection`, and take evidence from the scan:

```csharp
        var detection = enrichedMessage.LatestDetection;
        var evidence = enrichedMessage.LatestScan ?? detection;
```

Use `evidence?.Reason` for `detectionReason`, `netScore: evidence?.Score ?? 0` and `score: evidence?.Score ?? 0`.

- [ ] **Step 8: Update unit tests**

- `TrainingHandlerTests`: every `CreateSpamSampleAsync(messageId, chat, executor)` call gains `VerdictSource.WebMarkSpam, "Marked as spam by moderator"` (tests with a System executor use `VerdictSource.AutoBan`). Replace assertions on `InsertAsync(...)` with `await _mockDetectionResultsRepo.Received(1).RecordDecisionAsync(messageId, -100, VerdictSource.WebMarkSpam, executor, Arg.Any<string>(), null, null, Arg.Any<CancellationToken>());` (use the existing substitute's field name). Rename `CreateSpamSampleAsync_SystemActor_SkipsDetectionResultInsert` to `CreateSpamSampleAsync_SystemActor_RecordsAutoBanDecision` and assert `Received(1).RecordDecisionAsync(..., VerdictSource.AutoBan, ...)`. Add:

```csharp
    [Test]
    public async Task CreateHamSampleAsync_ReviewDismiss_RecordsImplicitHamDecisionWithoutLabel()
    {
        var executor = Actor.FromWebUser("admin-1");
        await _handler.CreateHamSampleAsync(4242, ChatIdentity.FromId(-100), executor, VerdictSource.ReviewDismiss, "Report #1 dismissed");

        await _mockDetectionResultsRepo.Received(1).RecordDecisionAsync(
            4242, -100, VerdictSource.ReviewDismiss, executor, "Report #1 dismissed", null, null, Arg.Any<CancellationToken>());
        await _mockTrainingLabelsRepo.DidNotReceiveWithAnyArgs().UpsertLabelAsync(default, default, default, default!, default, default, default);
    }

    [Test]
    public void CreateHamSampleAsync_NonHamSource_Throws()
        => Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _handler.CreateHamSampleAsync(1, ChatIdentity.FromId(-100), Actor.AutoDetection, VerdictSource.WebMarkSpam, "x"));
```

(Match the substitute field names already in the file; `grep -n "Substitute.For<I" TrainingHandlerTests.cs`.)
- `BotModerationServiceTests`: every `CreateSpamSampleAsync(...)` expectation gains two arguments (`Arg.Any<VerdictSource>(), Arg.Any<string>()`, or the exact values where the test pins them). Every `new SpamBanIntent { ... }` gains `Source = VerdictSource.WebMarkSpam` (or `AutoBan` for AutoDetection executors).
- `ContentReportHandlerTests`: add the `ITrainingHandler` substitute to the constructor call in `SetUp`, and add:

```csharp
    [Test]
    public async Task DismissAsync_RecordsReviewDismissHamDecision()
    {
        // Arrange with the file's existing pending-report helper, then:
        var result = await _handler.DismissAsync(reportId, executor, "not actionable", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        await _trainingHandler.Received(1).CreateHamSampleAsync(
            report.MessageId, report.Chat, executor, VerdictSource.ReviewDismiss,
            $"Report #{reportId} dismissed: not actionable", Arg.Any<CancellationToken>());
    }
```

(Reuse the arrange code of the existing `DismissAsync_*` success test for `reportId`, `report` and `executor`.)
- Every test object initializer that sets `IsSpam = …` on a `DetectionResultRecord` (`NotificationHandlerTests.cs:267`, `UserAutoTrustServiceTests.cs:264`, any component test; find them with `grep -rn "new DetectionResultRecord" TelegramGroupsAdmin.*Tests`) becomes `Source = VerdictSource.ContentScan, Classification = VerdictClassification.ImplicitSpam` (was `true`) or `VerdictClassification.ImplicitHam` (was `false`).

- [ ] **Step 9: Build and run the affected suites**

Run: `dotnet build` (0 warnings), then `dotnet test TelegramGroupsAdmin.UnitTests`, then `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~DetectionResultsRepositoryTests|FullyQualifiedName~MessageHistoryRepositoryTests"`, then `dotnet test TelegramGroupsAdmin.ComponentTests`.
Expected: all PASS. `grep -rn "InsertAsync(detection\|\.InsertAsync(detectionResult" --include=*.cs --include=*.razor .` returns nothing.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(detection): route every verdict write through Record*Async

Scans, file scans and decisions are classified once by VerdictClassifier
inside DetectionResultsRepository; the generic InsertAsync is gone.
Auto-bans now record an AutoBan decision, review Dismiss records a
ReviewDismiss ham decision, and Mark as Ham moves out of Messages.razor
into TrainingHandler.CreateHamSampleAsync. Insert-time SimHash dedup is
removed (training-time dedup already covers it).

Closes #386
Refs #547, #549

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 7: Training Data page through a service; "Remove from training" becomes `TrainingExclude`

**Files:**
- Create: `TelegramGroupsAdmin/Services/TrainingData/ITrainingDataService.cs`, `TrainingDataService.cs`
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` (register the service)
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/IDetectionResultsRepository.cs`, `DetectionResultsRepository.cs` (`AddManualTrainingSampleAsync` signature; `GetAllTrainingDataAsync`/`GetTrainingDataStatsAsync` over the view; delete `ExcludeFromTrainingAsync`, `InvalidateTrainingDataForMessageAsync`)
- Modify: `TelegramGroupsAdmin.Telegram/Models/TrainingSampleDto.cs` (+`ChatId`; `DetectionSource` → `Source`), `TelegramGroupsAdmin.Telegram/Services/TrainingDataDeduplicationService.cs`
- Modify: `TelegramGroupsAdmin/Components/Shared/ContentDetection/TrainingData.razor`, `TrainingDataDuplicates.razor`
- Modify: `TelegramGroupsAdmin/Components/Shared/AddTrainingSampleDialog.razor`, `EditTrainingSampleDialog.razor`, `TelegramGroupsAdmin/Models/Dialogs/AddTrainingSampleData.cs`, `EditTrainingSampleData.cs`
- Test: Create `TelegramGroupsAdmin.IntegrationTests/Services/TrainingDataServiceTests.cs`; update `TelegramGroupsAdmin.ComponentTests/Components/EditTrainingSampleDialogTests.cs`; delete the `InvalidateTrainingDataForMessageAsync` tests in `DetectionResultsRepositoryTests`

**Interfaces:**
- Consumes: Task 6 `RecordDecisionAsync`, `GetCurrentVerdictAsync`.
- Produces: `ITrainingDataService` with `Task<IReadOnlyList<DetectionResultRecord>> GetSamplesAsync(CancellationToken ct = default)`, `Task<TrainingDataStats> GetStatsAsync(CancellationToken ct = default)`, `Task AddSampleAsync(string messageText, bool isSpam, Actor actor, string? translatedText, string? detectedLanguage, CancellationToken ct = default)`, `Task ReplaceSampleAsync(int oldMessageId, long oldChatId, string messageText, bool isSpam, Actor actor, CancellationToken ct = default)`, `Task ExcludeAsync(int messageId, long chatId, Actor actor, CancellationToken ct = default)`. The repository gets `Task<long> AddManualTrainingSampleAsync(string messageText, bool isSpam, Actor actor, string? translatedText = null, string? detectedLanguage = null, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing integration tests**

Create `TelegramGroupsAdmin.IntegrationTests/Services/TrainingDataServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Services.TrainingData;

namespace TelegramGroupsAdmin.IntegrationTests.Services;

[TestFixture]
public class TrainingDataServiceTests
{
    private MigrationTestHelper _helper = null!;
    private ServiceProvider _provider = null!;
    private ITrainingDataService _service = null!;
    private IDetectionResultsRepository _repository = null!;
    private static readonly Actor Owner = Actor.FromWebUser(GoldenDatasetConstants.WebUsers.OwnerId);

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_helper.ConnectionString));
        services.AddLogging();
        services.AddScoped<IDetectionResultsRepository, DetectionResultsRepository>();
        services.AddScoped(_ => Substitute.For<IJobTriggerService>());
        services.AddScoped<ITrainingDataService, TrainingDataService>();
        _provider = services.BuildServiceProvider();
        _service = _provider.GetRequiredService<ITrainingDataService>();
        _repository = _provider.GetRequiredService<IDetectionResultsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _helper.Dispose();
    }

    [Test]
    public async Task ExcludeAsync_ExplicitSpam_BecomesUntrainedSpam_AndLeavesTheList()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.AutoBanMsgId;
        Assert.That((await _service.GetSamplesAsync()).Any(s => s.MessageId == msgId && s.ChatId == chatId), Is.True,
            "canonical precondition: auto-banned message is a curated sample");

        await _service.ExcludeAsync(msgId, chatId, Owner);

        var verdict = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict!.Classification, Is.EqualTo(VerdictClassification.UntrainedSpam));
            Assert.That(verdict.IsSpam, Is.True, "exclusion keeps the verdict");
            Assert.That((await _service.GetSamplesAsync()).Any(s => s.MessageId == msgId && s.ChatId == chatId), Is.False);
        }
    }

    [Test]
    public async Task ExcludeAsync_UnscannedMessage_BecomesUntrainedHam()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.UnscannedMsgId;

        await _service.ExcludeAsync(msgId, chatId, Owner);

        Assert.That((await _repository.GetCurrentVerdictAsync(msgId, chatId))!.Classification,
            Is.EqualTo(VerdictClassification.UntrainedHam));
    }

    [Test]
    public async Task AddSampleAsync_CreatesExplicitSampleOnChatZero()
    {
        await _service.AddSampleAsync("Buy cheap followers at canonical-spam.test now", isSpam: true, Owner, null, null);

        var added = (await _service.GetSamplesAsync())
            .Single(s => s.ChatId == 0 && s.MessageText == "Buy cheap followers at canonical-spam.test now");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Source, Is.EqualTo(VerdictSource.TrainingDataPage));
            Assert.That(added.Classification, Is.EqualTo(VerdictClassification.ExplicitSpam));
            Assert.That(added.AddedBy.Type, Is.EqualTo(ActorType.WebUser));
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TrainingDataServiceTests"`
Expected: build FAIL (`ITrainingDataService` missing).

- [ ] **Step 3: Repository changes**

`AddManualTrainingSampleAsync` becomes:

```csharp
    Task<long> AddManualTrainingSampleAsync(string messageText, bool isSpam, Actor actor,
        string? translatedText = null, string? detectedLanguage = null, CancellationToken cancellationToken = default);
```

In the implementation, keep the synthetic chat-0 message and translation creation unchanged. Replace the `new DataModels.DetectionResultRecordDto { ... }` block with:

```csharp
        var detectionResult = NewRow(message.MessageId, 0, VerdictSource.TrainingDataPage,
            VerdictClassifier.ClassifyDecision(VerdictSource.TrainingDataPage, isSpam), actor,
            score: 5.0, reason: "Manually added training sample", method: nameof(VerdictSource.TrainingDataPage));
        context.DetectionResults.Add(detectionResult);
        await context.SaveChangesAsync(cancellationToken);
```

Update the final log call to drop `source`/`addedBy` and log `actor.GetDisplayText()`.

Replace `GetAllTrainingDataAsync` and `GetTrainingDataStatsAsync` bodies with view-based versions (curated current verdicts):

```csharp
    public async Task<List<DetectionResultRecord>> GetAllTrainingDataAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var currentCuratedIds = context.MessageVerdicts
            .Where(v => v.VerdictId != null && VerdictClassifications.CuratedValues.Contains(v.Classification))
            .Select(v => v.VerdictId!.Value);

        return await WithActorJoins(
                context.DetectionResults.AsNoTracking().Where(dr => currentCuratedIds.Contains(dr.Id)),
                context)
            .OrderByDescending(x => x.DetectedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<TrainingDataStats> GetTrainingDataStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.MessageVerdicts.AsNoTracking()
            .Where(v => v.VerdictId != null && VerdictClassifications.CuratedValues.Contains(v.Classification))
            .Select(v => new { v.IsSpam, v.Source })
            .ToListAsync(cancellationToken);

        var total = rows.Count;
        var spam = rows.Count(r => r.IsSpam);
        return new TrainingDataStats
        {
            TotalSamples = total,
            SpamSamples = spam,
            HamSamples = total - spam,
            SpamPercentage = total > 0 ? (double)spam / total * 100 : 0,
            SamplesBySource = rows.GroupBy(r => ((VerdictSource)r.Source!.Value).ToString()).ToDictionary(g => g.Key, g => g.Count())
        };
    }
```

Delete `ExcludeFromTrainingAsync` and `InvalidateTrainingDataForMessageAsync` from the interface and the class, together with the latter's tests in `DetectionResultsRepositoryTests` (`#region InvalidateTrainingDataForMessageAsync`). Update the fixture's XML summary to drop them.

- [ ] **Step 4: The service**

`TelegramGroupsAdmin/Services/TrainingData/ITrainingDataService.cs`:

```csharp
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.TrainingData;

/// <summary>Training Data page operations. Every change is a verdict event; the latest wins.</summary>
public interface ITrainingDataService
{
    Task<IReadOnlyList<DetectionResultRecord>> GetSamplesAsync(CancellationToken cancellationToken = default);
    Task<TrainingDataStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task AddSampleAsync(string messageText, bool isSpam, Actor actor, string? translatedText, string? detectedLanguage,
        CancellationToken cancellationToken = default);
    Task ReplaceSampleAsync(int oldMessageId, long oldChatId, string messageText, bool isSpam, Actor actor,
        CancellationToken cancellationToken = default);
    Task ExcludeAsync(int messageId, long chatId, Actor actor, CancellationToken cancellationToken = default);
}
```

`TrainingDataService.cs`:

```csharp
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.Services.TrainingData;

public sealed class TrainingDataService(
    IDetectionResultsRepository detectionResultsRepository,
    IJobTriggerService jobTriggerService,
    ILogger<TrainingDataService> logger) : ITrainingDataService
{
    public async Task<IReadOnlyList<DetectionResultRecord>> GetSamplesAsync(CancellationToken cancellationToken = default)
        => await detectionResultsRepository.GetAllTrainingDataAsync(cancellationToken);

    public Task<TrainingDataStats> GetStatsAsync(CancellationToken cancellationToken = default)
        => detectionResultsRepository.GetTrainingDataStatsAsync(cancellationToken);

    public async Task AddSampleAsync(string messageText, bool isSpam, Actor actor, string? translatedText,
        string? detectedLanguage, CancellationToken cancellationToken = default)
    {
        await detectionResultsRepository.AddManualTrainingSampleAsync(
            messageText, isSpam, actor, translatedText, detectedLanguage, cancellationToken);
        await RetrainAsync(cancellationToken);
    }

    public async Task ReplaceSampleAsync(int oldMessageId, long oldChatId, string messageText, bool isSpam, Actor actor,
        CancellationToken cancellationToken = default)
    {
        await RecordExclusionAsync(oldMessageId, oldChatId, actor, cancellationToken);
        await detectionResultsRepository.AddManualTrainingSampleAsync(messageText, isSpam, actor, cancellationToken: cancellationToken);
        await RetrainAsync(cancellationToken);
    }

    public async Task ExcludeAsync(int messageId, long chatId, Actor actor, CancellationToken cancellationToken = default)
    {
        await RecordExclusionAsync(messageId, chatId, actor, cancellationToken);
        await RetrainAsync(cancellationToken);
    }

    private async Task RecordExclusionAsync(int messageId, long chatId, Actor actor, CancellationToken cancellationToken)
    {
        var current = await detectionResultsRepository.GetCurrentVerdictAsync(messageId, chatId, cancellationToken)
            ?? throw new InvalidOperationException($"Message {messageId} in chat {chatId} not found");

        await detectionResultsRepository.RecordDecisionAsync(messageId, chatId, VerdictSource.TrainingExclude, actor,
            "Removed from training data", isSpam: current.IsSpam, cancellationToken: cancellationToken);

        logger.LogInformation("Excluded message {MessageId} in chat {ChatId} from training (was {Classification})",
            messageId, chatId, current.Classification);
    }

    private Task RetrainAsync(CancellationToken cancellationToken) =>
        jobTriggerService.TriggerNowAsync(BackgroundJobNames.ClassifierRetraining, payload: new { }, cancellationToken: cancellationToken);
}
```

Register it in `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` next to the other app services:

```csharp
            services.AddScoped<TelegramGroupsAdmin.Services.TrainingData.ITrainingDataService, TelegramGroupsAdmin.Services.TrainingData.TrainingDataService>();
```

- [ ] **Step 5: UI and dialogs**

- `TrainingData.razor`: replace `@inject IDetectionResultsRepository DetectionResultsRepository` and the `IJobTriggerService` injection with `@inject ITrainingDataService TrainingDataService` (`@using TelegramGroupsAdmin.Services.TrainingData`). Then:
  - Loading: `TrainingDataService.GetSamplesAsync()` / `GetStatsAsync()`.
  - Add: `await TrainingDataService.AddSampleAsync(data.MessageText, data.IsSpam, WebUser.ToActor(), data.TranslatedText, data.DetectedLanguage);`
  - Edit: `await TrainingDataService.ReplaceSampleAsync(sample.MessageId, sample.ChatId, data.MessageText, data.IsSpam, WebUser.ToActor());`
  - Delete: `await TrainingDataService.ExcludeAsync(sample.MessageId, sample.ChatId, WebUser.ToActor());`
  - Remove `TriggerRetrainingAsync` (the service retrains). The Source column/search/filter use `context.Source.ToString()` / `s.Source.ToString()` instead of `DetectionSource`.
- `TrainingDataDuplicates.razor`: inject `ITrainingDataService` instead of `IDetectionResultsRepository`. The three `ExcludeFromTrainingAsync(sample.Id)` calls become `TrainingDataService.ExcludeAsync(sample.MessageId, sample.ChatId, WebUser!.ToActor())`. Add `[CascadingParameter] private WebUserIdentity? WebUser { get; set; }` if the component lacks it (same declaration as `TrainingData.razor:166`). `@sample.DetectionSource` becomes `@sample.Source`.
- `TrainingSampleDto`: add `public long ChatId { get; set; }` and replace `public string DetectionSource` with `public VerdictSource Source { get; set; }`. `TrainingDataDeduplicationService` maps `ChatId = s.ChatId, Source = s.Source`.
- `AddTrainingSampleDialog.razor` / `EditTrainingSampleDialog.razor`: remove the Source `MudSelect` and the Score field, their backing fields, and `Source`/`Score` from `AddTrainingSampleData`/`EditTrainingSampleData` (decisions carry a fixed score, and the source is always `TrainingDataPage`). The page's edit handler already has the `DetectionResultRecord sample`, so it passes `sample.MessageId`/`sample.ChatId` to `ReplaceSampleAsync`.
- `EditTrainingSampleDialogTests`: remove assertions and interactions on the Source/Score inputs.

- [ ] **Step 6: Run and verify**

Run: `dotnet build` (0 warnings), then `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TrainingDataServiceTests|FullyQualifiedName~DetectionResultsRepositoryTests"`, then `dotnet test TelegramGroupsAdmin.ComponentTests`.
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(training): Training Data page records verdict events via a service

Add/edit/remove on the Training Data and Duplicates pages go through
TrainingDataService. "Remove from training" writes a TrainingExclude
decision that keeps the verdict and drops training membership. The page
lists curated current verdicts from message_verdicts.

Refs #549

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 8: Training readers use the view — ML/Bayes samples and AI prompt examples

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/MLTrainingDataRepository.cs` (`GetSpamSamplesAsync`, the explicit/implicit ham queries in `GetHamSamplesAsync`)
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/DetectionResultsRepository.cs` (`GetTrainingSamplesAsync`; delete `GetSpamSamplesForSimilarityAsync` and its interface member)
- Modify: `TelegramGroupsAdmin.ContentDetection/Models/TrainingSampleSource.cs` (doc comments only: "explicit = ExplicitSpam/ExplicitHam verdict")
- Test: Create `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/MLTrainingDataRepositoryVerdictTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.MessageVerdicts`, `VerdictClassifications.*Values`, Task 5 anchors.
- Produces: unchanged public signatures. `TrainingSample.Source` means explicit ⇔ `ExplicitSpam`/`ExplicitHam`, implicit ⇔ `ImplicitSpam`/`ImplicitHam`/`Unscanned`. `Untrained*` never becomes a sample.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// The four training levels come from message_verdicts: the latest event wins, and Untrained* never trains.
/// Anchors: GoldenDatasetConstants.Verdicts (canonical edit 2026-09-27).
/// </summary>
[TestFixture]
public class MLTrainingDataRepositoryVerdictTests
{
    private MigrationTestHelper _helper = null!;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private async Task<(List<TrainingSample> Spam, List<TrainingSample> Ham)> LoadAsync()
    {
        await using var ctx = _helper.GetDbContext();
        var repo = new MLTrainingDataRepository(ctx, new SimHashService(), NullLogger<MLTrainingDataRepository>.Instance);
        var spam = await repo.GetSpamSamplesAsync();
        var ham = await repo.GetHamSamplesAsync(spamCount: 10_000); // cap high enough to include every ham candidate
        return (spam, ham);
    }

    [Test]
    public async Task CorrectedToHam_IsExplicitHam_NotSpam()
    {
        var (spam, ham) = await LoadAsync();
        var id = GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spam.Any(s => s.MessageId == id), Is.False);
            Assert.That(ham.Single(s => s.MessageId == id).Source, Is.EqualTo(TrainingSampleSource.Explicit));
        }
    }

    [Test]
    public async Task EditFlippedToSpam_IsImplicitSpam_NotHam()
    {
        var (spam, ham) = await LoadAsync();
        var id = GoldenDatasetConstants.Verdicts.EditFlipMsgId;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spam.Single(s => s.MessageId == id).Source, Is.EqualTo(TrainingSampleSource.Implicit));
            Assert.That(ham.Any(s => s.MessageId == id), Is.False);
        }
    }

    [TestCase(GoldenDatasetConstants.Verdicts.SpamInTrustWindowMsgId, TestName = "UntrainedSpam never trains")]
    [TestCase(GoldenDatasetConstants.Verdicts.UntrainedHamMsgId, TestName = "UntrainedHam never trains")]
    public async Task UntrainedVerdicts_AreNotSamples(int messageId)
    {
        var (spam, ham) = await LoadAsync();
        Assert.That(spam.Concat(ham).Any(s => s.MessageId == messageId), Is.False);
    }
}
```

(Use the fixture's real constructor. If `SimHashService` needs arguments, copy how `MLTextClassifierServiceTests` builds the repository.)

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MLTrainingDataRepositoryVerdictTests"`
Expected: `EditFlippedToSpam_IsImplicitSpam_NotHam` and `UntrainedVerdicts_AreNotSamples(UntrainedHam…)` FAIL, because the old queries read `training_labels`/`is_spam` per row.

- [ ] **Step 3: Rewrite the spam query**

Replace the two queries in `GetSpamSamplesAsync` (`explicitSpam`, `implicitSpam`) with one view-based query. Keep the `TrainingSample` construction, logging and `DeduplicateSamples` call:

```csharp
        // Current verdict per message (latest event wins): ExplicitSpam and ImplicitSpam train as spam.
        var spamRows = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where VerdictClassifications.TrainingSpamValues.Contains(v.Classification)
            join dr in context.DetectionResults.AsNoTracking() on v.VerdictId equals (long?)dr.Id
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            let mt = context.MessageTranslations
                .Where(t => t.MessageId == m.MessageId && t.ChatId == m.ChatId && t.EditId == null)
                .OrderByDescending(t => t.TranslatedAt)
                .FirstOrDefault()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > MLConstants.MinTextLength
            select new { Text = text, m.MessageId, m.ChatId, v.Classification, dr.TelegramUserId, dr.DetectedAt }
        ).ToListAsync(cancellationToken);

        List<TrainingSample> samples = [.. spamRows.Select(x => new TrainingSample
        {
            Text = x.Text!,
            Label = TrainingLabel.Spam,
            Source = x.Classification == (int)VerdictClassification.ExplicitSpam ? TrainingSampleSource.Explicit : TrainingSampleSource.Implicit,
            MessageId = x.MessageId,
            ChatId = x.ChatId,
            LabeledByUserId = x.Classification == (int)VerdictClassification.ExplicitSpam ? x.TelegramUserId : null,
            LabeledAt = x.Classification == (int)VerdictClassification.ExplicitSpam ? x.DetectedAt : null
        })];

        logger.LogInformation(
            "Loaded {Count} spam training samples ({Explicit} explicit + {Implicit} implicit)",
            samples.Count,
            samples.Count(s => s.Source == TrainingSampleSource.Explicit),
            samples.Count(s => s.Source == TrainingSampleSource.Implicit));

        return DeduplicateSamples(samples, "spam");
```

- [ ] **Step 4: Rewrite the ham queries**

In `GetHamSamplesAsync`, replace the `explicitHamRaw` query with the same shape filtered to `v.Classification == (int)VerdictClassification.ExplicitHam` (label `TrainingLabel.Ham`, `LabeledByUserId = dr.TelegramUserId`, `LabeledAt = dr.DetectedAt`). Replace the `implicitHamRaw` query's source and predicate. Keep its ordering, `Take(maxImplicitHam * 3)`, dedupe and cap:

```csharp
        var implicitHamRaw = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where v.Classification == (int)VerdictClassification.ImplicitHam
               || v.Classification == (int)VerdictClassification.Unscanned
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            where m.DeletedAt == null  // Message-level filter (better signal than user-level ban)
            from mt in context.MessageTranslations
                .Where(mt => mt.MessageId == m.MessageId && mt.ChatId == m.ChatId && mt.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > MLConstants.MinTextLength
            orderby text.Length descending  // Sort by LENGTH (uses expression index)
            select new { Text = text, m.MessageId, m.ChatId }
        ).Take(maxImplicitHam * 3).ToListAsync(cancellationToken);
```

Remove every remaining reference to `context.TrainingLabels` and `dr.IsSpam`/`dr.UsedForTraining` in this file (`grep -n "TrainingLabels\|UsedForTraining\|\.IsSpam" MLTrainingDataRepository.cs` must be empty).

- [ ] **Step 5: Prompt examples**

`DetectionResultsRepository.GetTrainingSamplesAsync` returns the curated current verdicts:

```csharp
    public async Task<List<(string MessageText, bool IsSpam)>> GetTrainingSamplesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Curated current verdicts (explicit labels + confident implicit spam); translated text when available.
        var results = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where VerdictClassifications.CuratedValues.Contains(v.Classification)
            join m in context.Messages on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            from mt in context.MessageTranslations
                .Where(t => t.MessageId == m.MessageId && t.ChatId == m.ChatId && t.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text != ""
            orderby v.IsSpam descending
            select new { MessageText = text, v.IsSpam }
        ).ToListAsync(cancellationToken);

        return results.Select(r => (r.MessageText!, r.IsSpam)).ToList();
    }
```

Delete `GetSpamSamplesForSimilarityAsync` (no callers) from the interface and the class.

- [ ] **Step 6: Run the ML suites**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MLTrainingDataRepositoryVerdictTests|FullyQualifiedName~MLTextClassifierServiceTests|FullyQualifiedName~BayesClassifierServiceTests|FullyQualifiedName~PromptBuilder"`
Expected: all PASS. A pre-existing test that derived its expected counts from `ctx.TrainingLabels` should now derive them from `ctx.MessageVerdicts` by classification (the same meaning under the new source of truth). If a count differs for any other reason, stop and report it; don't loosen the assertion.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(ml): train from message_verdicts (latest event wins)

Explicit/implicit spam and ham come from each message's current
classification; Untrained* verdicts never train and a newer event
(edit rescan, admin correction) supersedes older ones for SDCA, Bayes
and AI prompt examples alike.

Refs #547, #549

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 9: Auto-trust counts messages, latest version each, consecutively (#546)

**Files:**
- Create: `TelegramGroupsAdmin.ContentDetection/Models/UserMessageVerdict.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/IDetectionResultsRepository.cs`, `DetectionResultsRepository.cs` (replace `GetRecentNonSpamResultsForUserAsync`)
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserAutoTrustService.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Services/UserAutoTrustServiceTests.cs`; add `#region GetRecentMessageVerdictsForUserAsync` to `TelegramGroupsAdmin.IntegrationTests/Repositories/DetectionResultsRepositoryTests.cs`

**Interfaces:**
- Produces: `record UserMessageVerdict(int MessageId, long ChatId, VerdictClassification Classification, bool IsSpam, int TextLength)`; `Task<IReadOnlyList<UserMessageVerdict>> GetRecentMessageVerdictsForUserAsync(long userId, int limit, CancellationToken ct = default)`. It returns the user's latest `limit` **scanned** messages across all chats (`Unscanned` excluded), one row per message, ordered by the message's post time, newest first. Each row is judged on its current verdict and its current text.
- Rule (in the service): trust iff there are exactly `FirstMessagesCount` rows, none `IsSpam`, and every `TextLength >= AutoTrustMinMessageLength`. `UntrainedHam` counts as ham.

- [ ] **Step 1: Write the failing integration tests**

```csharp
    #region GetRecentMessageVerdictsForUserAsync

    [Test]
    public async Task GetRecentMessageVerdictsForUserAsync_OneMessageEditedManyTimes_CountsOnce()
    {
        var rows = await _repository!.GetRecentMessageVerdictsForUserAsync(9468093502025L, limit: 10); // @financerope
        Assert.That(rows.Count(r => r.MessageId == GoldenDatasetConstants.Verdicts.EditFlipMsgId), Is.EqualTo(1));
    }

    [Test]
    public async Task GetRecentMessageVerdictsForUserAsync_EditFlippedToSpam_IsJudgedOnLatestVersion()
    {
        var rows = await _repository!.GetRecentMessageVerdictsForUserAsync(9468093502025L, limit: 10);
        Assert.That(rows.Single(r => r.MessageId == GoldenDatasetConstants.Verdicts.EditFlipMsgId).IsSpam, Is.True);
    }

    [Test]
    public async Task GetRecentMessageVerdictsForUserAsync_SpamInWindow_IsReturned()
    {
        var rows = await _repository!.GetRecentMessageVerdictsForUserAsync(GoldenDatasetConstants.Verdicts.SpamInTrustWindowUserId, limit: 3);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(3));
            Assert.That(rows.Count(r => r.IsSpam), Is.EqualTo(1));
            Assert.That(rows.Single(r => r.IsSpam).MessageId, Is.EqualTo(GoldenDatasetConstants.Verdicts.SpamInTrustWindowMsgId));
        }
    }

    [Test]
    public async Task GetRecentMessageVerdictsForUserAsync_AllHamUser_ReturnsThreeHam()
    {
        var rows = await _repository!.GetRecentMessageVerdictsForUserAsync(GoldenDatasetConstants.Verdicts.AllHamUserId, limit: 3);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(3));
            Assert.That(rows.Any(r => r.IsSpam), Is.False);
        }
    }

    #endregion
```

(If @financerope's user id differs from `9468093502025`, read it with `SELECT user_id FROM messages WHERE message_id = 82837`, add it to `GoldenDatasetConstants.Verdicts` as `EditFlipUserId`, and use the constant.)

- [ ] **Step 2: Update the unit tests (they fail to compile until Step 3)**

In `UserAutoTrustServiceTests`, replace every `GetRecentNonSpamResultsForUserAsync(...)` stub with `GetRecentMessageVerdictsForUserAsync(TestUserId, 3, Arg.Any<CancellationToken>())` returning `UserMessageVerdict` lists. Add a helper and three tests:

```csharp
    private static UserMessageVerdict Verdict(int id, VerdictClassification c, int length = 40) =>
        new(id, TestChatId, c, c.IsSpam(), length);

    [Test]
    public async Task CheckAndApplyAutoTrust_SpamAmongLastN_DoesNotTrust()
    {
        // Arrange with the same eligible-user setup as CheckAndApplyAutoTrust_BothConditionsMet_TrustsUser
        _detectionResultsRepo.GetRecentMessageVerdictsForUserAsync(TestUserId, 3, Arg.Any<CancellationToken>())
            .Returns([Verdict(1, VerdictClassification.ImplicitHam), Verdict(2, VerdictClassification.UntrainedSpam), Verdict(3, VerdictClassification.ImplicitHam)]);

        await _service.CheckAndApplyAutoTrustAsync(_tgUser, _chat);

        await _userRepo.DidNotReceive().TrustUserAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAndApplyAutoTrust_ShortMessageAmongLastN_DoesNotTrust()
    {
        _detectionResultsRepo.GetRecentMessageVerdictsForUserAsync(TestUserId, 3, Arg.Any<CancellationToken>())
            .Returns([Verdict(1, VerdictClassification.ImplicitHam), Verdict(2, VerdictClassification.ImplicitHam, length: 5), Verdict(3, VerdictClassification.ImplicitHam)]);

        await _service.CheckAndApplyAutoTrustAsync(_tgUser, _chat);

        await _userRepo.DidNotReceive().TrustUserAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAndApplyAutoTrust_UntrainedHamCounts_Trusts()
    {
        _detectionResultsRepo.GetRecentMessageVerdictsForUserAsync(TestUserId, 3, Arg.Any<CancellationToken>())
            .Returns([Verdict(1, VerdictClassification.UntrainedHam), Verdict(2, VerdictClassification.ImplicitHam), Verdict(3, VerdictClassification.ExplicitHam)]);

        await _service.CheckAndApplyAutoTrustAsync(_tgUser, _chat);

        await _userRepo.Received(1).TrustUserAsync(TestUserId, Arg.Any<CancellationToken>());
    }
```

(`_tgUser`/`_chat` stand for whatever user/chat objects and eligible-user arrangement the existing `BothConditionsMet` test uses. Copy that arrangement into each new test.)

- [ ] **Step 3: Implement**

`TelegramGroupsAdmin.ContentDetection/Models/UserMessageVerdict.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>One message's current verdict and current text length, for auto-trust.</summary>
public sealed record UserMessageVerdict(int MessageId, long ChatId, VerdictClassification Classification, bool IsSpam, int TextLength);
```

Replace `GetRecentNonSpamResultsForUserAsync` (interface and class) with:

```csharp
    public async Task<IReadOnlyList<UserMessageVerdict>> GetRecentMessageVerdictsForUserAsync(
        long userId, int limit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // One row per message (the view), latest version each; edits never add units.
        var rows = await (
            from m in context.Messages.AsNoTracking()
            where m.UserId == userId
            join v in context.MessageVerdicts.AsNoTracking() on new { m.MessageId, m.ChatId } equals new { v.MessageId, v.ChatId }
            where v.Classification != (int)VerdictClassification.Unscanned
            orderby m.Timestamp descending
            select new { m.MessageId, m.ChatId, v.Classification, v.IsSpam, TextLength = m.MessageText == null ? 0 : m.MessageText.Length }
        ).Take(limit).ToListAsync(cancellationToken);

        return rows.Select(r => new UserMessageVerdict(r.MessageId, r.ChatId, (VerdictClassification)r.Classification, r.IsSpam, r.TextLength)).ToList();
    }
```

In `UserAutoTrustService`, replace the `recentResults` block with:

```csharp
            // Last N messages (latest version each), consecutively: any spam or short message in the
            // window blocks trust. Edits never add messages. UntrainedHam counts as ham.
            var recent = await _detectionResultsRepository.GetRecentMessageVerdictsForUserAsync(
                userId, config.FirstMessagesCount, cancellationToken);

            if (recent.Count < config.FirstMessagesCount
                || recent.Any(r => r.IsSpam || r.TextLength < config.AutoTrustMinMessageLength))
            {
                _logger.LogDebug(
                    "{User} not eligible for auto-trust: {Count}/{Threshold} messages, {Spam} spam, {Short} shorter than {MinLength}",
                    tgUser.ToLogDebug(), recent.Count, config.FirstMessagesCount,
                    recent.Count(r => r.IsSpam), recent.Count(r => r.TextLength < config.AutoTrustMinMessageLength),
                    config.AutoTrustMinMessageLength);
                return;
            }
```

Update the trust `Reason` text to `$"Auto-trusted after {config.FirstMessagesCount} consecutive non-spam messages"`.

- [ ] **Step 4: Run and verify**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserAutoTrustServiceTests"` and `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~DetectionResultsRepositoryTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
fix(security): auto-trust counts messages, latest version each, consecutively

Edits no longer inflate the count, a spam verdict anywhere in the last
N messages blocks trust, and ham/spam comes from the single verdict.

Closes #546

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 10: AI history and message badges read the current verdict (#521 Defect B)

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Models/RecentMessageVerdict.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IMessageHistoryRepository.cs`, `MessageHistoryRepository.cs` (two new query methods)
- Modify: `TelegramGroupsAdmin.Telegram/Services/MessageQueryService.cs` (`GetContentChecksForMessagesAsync` delegates; no `DbContext` query for verdicts)
- Modify: `TelegramGroupsAdmin/Services/MessageContextAdapter.cs`
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` only if `MessageContextAdapter`'s constructor change needs it (it is resolved by DI)
- Test: add `#region Current verdict reads` to `TelegramGroupsAdmin.IntegrationTests/Repositories/MessageHistoryRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `record RecentMessageVerdict(int MessageId, long UserId, string? Username, string? MessageText, DateTimeOffset Timestamp, bool IsSpam)`
  - `IMessageHistoryRepository.GetRecentMessagesWithVerdictAsync(long chatId, int count, CancellationToken ct = default)` → `IReadOnlyList<RecentMessageVerdict>` (newest first)
  - `IMessageHistoryRepository.GetCurrentContentChecksAsync(long chatId, IReadOnlyCollection<int> messageIds, CancellationToken ct = default)` → `Dictionary<int, ContentCheckRecord>` built from each message's current verdict row (never a FileScan)

- [ ] **Step 1: Write the failing integration tests**

```csharp
    #region Current verdict reads

    [Test]
    public async Task GetRecentMessagesWithVerdictAsync_CorrectedToHam_IsNotShownAsSpam()
    {
        // Previously WasSpam = Any(row.IsSpam): an admin-corrected message stayed "spam" in the AI history.
        var rows = await _repository!.GetRecentMessagesWithVerdictAsync(GoldenDatasetConstants.Chats.MainChatId, count: 1000);
        Assert.That(rows.Single(r => r.MessageId == GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId).IsSpam, Is.False);
    }

    [Test]
    public async Task GetRecentMessagesWithVerdictAsync_AutoBanned_IsSpam()
    {
        var rows = await _repository!.GetRecentMessagesWithVerdictAsync(GoldenDatasetConstants.Chats.MainChatId, count: 1000);
        Assert.That(rows.Single(r => r.MessageId == GoldenDatasetConstants.Verdicts.AutoBanMsgId).IsSpam, Is.True);
    }

    [Test]
    public async Task GetCurrentContentChecksAsync_UsesCurrentVerdict_NotANewerFileScan()
    {
        var checks = await _repository!.GetCurrentContentChecksAsync(
            GoldenDatasetConstants.Verdicts.FileScanBesideScanChatId, [GoldenDatasetConstants.Verdicts.FileScanBesideScanMsgId]);

        Assert.That(checks[GoldenDatasetConstants.Verdicts.FileScanBesideScanMsgId].Reason, Is.Not.EqualTo("No threats detected"));
    }

    #endregion
```

(Add `public const long FileScanBesideScanChatId = <chat_id of message 216684>;` to `GoldenDatasetConstants.Verdicts`, read from `SELECT chat_id FROM messages WHERE message_id = 216684`.)

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MessageHistoryRepositoryTests"`
Expected: build FAIL (new methods missing).

- [ ] **Step 3: Implement**

`TelegramGroupsAdmin.Telegram/Models/RecentMessageVerdict.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>A recent chat message with its current verdict (AI veto history context).</summary>
public sealed record RecentMessageVerdict(int MessageId, long UserId, string? Username, string? MessageText, DateTimeOffset Timestamp, bool IsSpam);
```

`IMessageHistoryRepository` gets the two members from Interfaces above, with XML docs. `MessageHistoryRepository`:

```csharp
    public async Task<IReadOnlyList<UiModels.RecentMessageVerdict>> GetRecentMessagesWithVerdictAsync(
        long chatId, int count, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
            from m in context.Messages.AsNoTracking()
            where m.ChatId == chatId
            join v in context.MessageVerdicts.AsNoTracking() on new { m.MessageId, m.ChatId } equals new { v.MessageId, v.ChatId }
            join tu in context.TelegramUsers.AsNoTracking() on m.UserId equals tu.TelegramUserId into users
            from tu in users.DefaultIfEmpty()
            orderby m.Timestamp descending
            select new { m.MessageId, m.UserId, Username = tu != null ? tu.Username : null, m.MessageText, m.Timestamp, v.IsSpam }
        ).Take(count).ToListAsync(cancellationToken);

        return rows.Select(r => new UiModels.RecentMessageVerdict(r.MessageId, r.UserId, r.Username, r.MessageText, r.Timestamp, r.IsSpam)).ToList();
    }

    public async Task<Dictionary<int, UiModels.ContentCheckRecord>> GetCurrentContentChecksAsync(
        long chatId, IReadOnlyCollection<int> messageIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where v.ChatId == chatId && messageIds.Contains(v.MessageId) && v.VerdictId != null
            join dr in context.DetectionResults.AsNoTracking() on v.VerdictId equals (long?)dr.Id
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            select new { dr.DetectedAt, m.UserId, v.IsSpam, dr.Score, dr.Reason, dr.DetectionMethod, v.MessageId }
        ).ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.MessageId,
            r => new UiModels.ContentCheckRecord(
                CheckTimestamp: r.DetectedAt,
                UserId: r.UserId,
                IsSpam: r.IsSpam,
                Score: r.Score,
                Reason: r.Reason ?? $"{r.DetectionMethod}: {(r.IsSpam ? "spam" : "ham")}",
                MatchedMessageId: r.MessageId));
    }
```

`MessageQueryService.GetContentChecksForMessagesAsync`: replace the body with a delegation (inject `IMessageHistoryRepository` via the constructor):

```csharp
        => _messageHistoryRepository.GetCurrentContentChecksAsync(chatId, messageIds.ToArray(), cancellationToken);
```

`MessageContextAdapter`: depend on `IMessageHistoryRepository` instead of `IMessageQueryService`:

```csharp
            var messages = await _messageHistoryRepository.GetRecentMessagesWithVerdictAsync(chat.Id, count, cancellationToken);

            return messages.Select(m => new ContentDetectionServices.HistoryMessage
            {
                UserId = m.UserId.ToString(),
                UserName = m.Username ?? "Unknown",
                Message = m.MessageText ?? string.Empty,
                Timestamp = m.Timestamp.UtcDateTime,
                WasSpam = m.IsSpam
            }).ToList();
```

- [ ] **Step 4: Run and verify**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MessageHistoryRepositoryTests"` and `dotnet build` (0 warnings).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
fix(ai): AI history and message badges show the current verdict

WasSpam and the message badge come from message_verdicts, so allowed
sub-threshold messages and admin-corrected messages are no longer shown
to the AI veto as spam exemplars. The verdict queries move behind
MessageHistoryRepository.

Refs #521, #215

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 11: Retention keeps curated messages (#548)

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/MessageHistoryRepository.cs` (`CleanupExpiredAsync` keep condition + comment)
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDatasetConstants.cs` (`Retention` docs and expected counts if they change)
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/MessageHistoryRepositoryTests.cs`

**Interfaces:**
- Produces: an expired message is kept iff its current classification ∈ `VerdictClassifications.Curated` (ExplicitSpam, ExplicitHam, ImplicitSpam).

- [ ] **Step 1: Write the failing test**

Next to the existing `CleanupExpiredAsync_*` tests, and following their pattern for aging messages (they shift timestamps with `GoldenDataset.Mutate(ctx)…` or the retention mutator; reuse exactly what `CleanupExpiredAsync_WithOldMessages_DeletesExpiredAndPreservesTrainingData` does):

```csharp
    [Test]
    public async Task CleanupExpiredAsync_LabeledOnlyMessage_IsPreserved()
    {
        var msgId = GoldenDatasetConstants.Verdicts.LabeledOnlyRetentionMsgId;
        await using (var ctx = _testHelper!.GetDbContext())
        {
            // Guard the canonical precondition: every row for this message has used_for_training = false,
            // so the old keep-condition would delete it; its current verdict is an explicit label.
            Assert.That(await ctx.DetectionResults.AnyAsync(d => d.MessageId == msgId && d.ChatId == GoldenDatasetConstants.Chats.LandOwnersChatId && d.UsedForTraining), Is.False);
            Assert.That((await ctx.MessageVerdicts.SingleAsync(v => v.MessageId == msgId && v.ChatId == GoldenDatasetConstants.Chats.LandOwnersChatId)).Classification,
                Is.EqualTo((int)VerdictClassification.ExplicitHam));
        }

        // message 7974 is dated 2025-12-18, well past a 30-day retention window
        await _repository!.CleanupExpiredAsync(TimeSpan.FromDays(30));

        await using var after = _testHelper.GetDbContext();
        Assert.That(await after.Messages.AnyAsync(m => m.MessageId == msgId && m.ChatId == GoldenDatasetConstants.Chats.LandOwnersChatId), Is.True);
    }
```

(Task 16 removes the `UsedForTraining` line from this guard when the column is dropped.)

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~CleanupExpiredAsync"`
Expected: `CleanupExpiredAsync_LabeledOnlyMessage_IsPreserved` FAILS (message deleted).

- [ ] **Step 3: Implement**

In `CleanupExpiredAsync`, replace the comment and the `.Where`:

```csharp
        // Retention: keep messages within the retention period, and any older message whose current
        // verdict is curated training data (explicit labels, confident implicit spam). Media features
        // live on the message row, so they are kept with it.
        var retentionCutoff = DateTimeOffset.UtcNow - retention;

        var expiredData = await context.Messages
            .Where(m => m.Timestamp < retentionCutoff
                && !context.MessageVerdicts.Any(v => v.MessageId == m.MessageId && v.ChatId == m.ChatId
                    && VerdictClassifications.CuratedValues.Contains(v.Classification)))
```

(`using TelegramGroupsAdmin.Core.Models;`.) Keep the rest of the method unchanged.

- [ ] **Step 4: Run the retention suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~CleanupExpiredAsync|FullyQualifiedName~DataCleanupJob|FullyQualifiedName~Retention"`
Expected: all PASS. If `GoldenDatasetConstants.Retention.ExpectedDeletionsWith30DayRetention` changes because labeled messages are now kept, verify the new number by listing the deleted ids. They must be exactly the messages whose current classification is not curated. Update the constant and its doc comment with the reason.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
fix(retention): keep messages whose current verdict is curated training data

Admin-labeled messages were deleted because retention only honored
used_for_training; it now keeps explicit labels and confident implicit
spam via message_verdicts.

Closes #548

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 12: Stop-word recommendations read through a repository

**Files:**
- Create: `TelegramGroupsAdmin.ContentDetection/Repositories/IStopWordCorpusRepository.cs`, `StopWordCorpusRepository.cs`
- Create: `TelegramGroupsAdmin.ContentDetection/Models/StopWordCorpusCounts.cs`, `ScanCheckResults.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/ML/StopWordRecommendationService.cs` (no `DbContext`; uses the repository)
- Modify: `TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs` (register the repository)
- Test: Create `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/StopWordCorpusRepositoryTests.cs`; update `TelegramGroupsAdmin.IntegrationTests/ML/StopWordRecommendationServiceMalformedJsonTests.cs` DI setup

**Interfaces:**
- Produces:
  - `record StopWordCorpusCounts(int SpamSamples, int LegitMessages, int ScanResults)`
  - `record ScanCheckResults(long Id, bool IsSpam, string CheckResultsJson, DateTimeOffset DetectedAt)`
  - `IStopWordCorpusRepository`:
    - `Task<StopWordCorpusCounts> GetCountsAsync(DateTimeOffset since, CancellationToken ct = default)`
    - `Task<IReadOnlyList<string>> GetSpamTextsAsync(DateTimeOffset since, CancellationToken ct = default)`: current verdict ∈ TrainingSpam, message posted ≥ since, translated text preferred
    - `Task<IReadOnlyList<string>> GetLegitTextsAsync(DateTimeOffset since, CancellationToken ct = default)`: current verdict not spam (includes Unscanned)
    - `Task<IReadOnlyList<ScanCheckResults>> GetScanCheckResultsAsync(DateTimeOffset since, CancellationToken ct = default)`: `ContentScan` rows since, with JSON

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

[TestFixture]
public class StopWordCorpusRepositoryTests
{
    private MigrationTestHelper _helper = null!;
    private ServiceProvider _provider = null!;
    private IStopWordCorpusRepository _repository = null!;
    private static readonly DateTimeOffset Always = DateTimeOffset.MinValue;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_helper.ConnectionString));
        services.AddScoped<IStopWordCorpusRepository, StopWordCorpusRepository>();
        _provider = services.BuildServiceProvider();
        _repository = _provider.GetRequiredService<IStopWordCorpusRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _helper.Dispose();
    }

    [Test]
    public async Task Corpora_FollowTheCurrentVerdict()
    {
        await using var ctx = _helper.GetDbContext();
        var correctedText = (await ctx.Messages.SingleAsync(m => m.MessageId == GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId
            && m.ChatId == GoldenDatasetConstants.Chats.MainChatId)).MessageText!;

        var spam = await _repository.GetSpamTextsAsync(Always);
        var legit = await _repository.GetLegitTextsAsync(Always);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(spam, Does.Not.Contain(correctedText), "an admin ham correction supersedes the spam scan");
            Assert.That(legit, Does.Contain(correctedText));
        }
    }

    [Test]
    public async Task ScanCheckResults_ExcludeDecisions()
    {
        var scans = await _repository.GetScanCheckResultsAsync(Always);
        await using var ctx = _helper.GetDbContext();
        var decisionIds = await ctx.DetectionResults.Where(d => d.Source != 0).Select(d => d.Id).ToListAsync();
        Assert.That(scans.Select(s => s.Id), Has.None.AnyOf(decisionIds));
    }
}
```

(If `CorrectedToHamMsgId`'s text has a translation, compare against the translated text, which is what the corpus returns. Read it from `MessageTranslations` with the same keys.)

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~StopWordCorpusRepositoryTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement the repository**

`StopWordCorpusRepository` (primary constructor `IDbContextFactory<AppDbContext> contextFactory`). Each method opens a context, queries with `AsNoTracking()`, and returns plain models:

```csharp
    public async Task<StopWordCorpusCounts> GetCountsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var spam = await (from v in context.MessageVerdicts
                          where VerdictClassifications.TrainingSpamValues.Contains(v.Classification)
                          join m in context.Messages on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
                          where m.Timestamp >= since
                          select v.MessageId).CountAsync(cancellationToken);
        var legit = await (from v in context.MessageVerdicts
                           where !v.IsSpam
                           join m in context.Messages on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
                           where m.Timestamp >= since
                           select v.MessageId).CountAsync(cancellationToken);
        var scans = await context.DetectionResults
            .CountAsync(d => d.Source == (int)VerdictSource.ContentScan && d.DetectedAt >= since, cancellationToken);
        return new StopWordCorpusCounts(spam, legit, scans);
    }

    public Task<IReadOnlyList<string>> GetSpamTextsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        => GetTextsAsync(since, spam: true, cancellationToken);

    public Task<IReadOnlyList<string>> GetLegitTextsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        => GetTextsAsync(since, spam: false, cancellationToken);

    private async Task<IReadOnlyList<string>> GetTextsAsync(DateTimeOffset since, bool spam, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var texts = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where spam ? VerdictClassifications.TrainingSpamValues.Contains(v.Classification) : !v.IsSpam
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            where m.Timestamp >= since
            from mt in context.MessageTranslations
                .Where(t => t.MessageId == m.MessageId && t.ChatId == m.ChatId && t.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text != ""
            select text
        ).ToListAsync(cancellationToken);
        return texts!;
    }

    public async Task<IReadOnlyList<ScanCheckResults>> GetScanCheckResultsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.DetectionResults.AsNoTracking()
            .Where(d => d.Source == (int)VerdictSource.ContentScan && d.DetectedAt >= since && d.CheckResultsJson != null)
            .Select(d => new { d.Id, d.Classification, d.CheckResultsJson, d.DetectedAt })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ScanCheckResults(r.Id, ((VerdictClassification)r.Classification!.Value).IsSpam(), r.CheckResultsJson!, r.DetectedAt)).ToList();
    }
```

This also fixes a latent bug: the old translation lookup was keyed by `MessageId` alone and could throw on a duplicate id across chats.

- [ ] **Step 4: Point the service at the repository**

In `StopWordRecommendationService`, replace the `IDbContextFactory<AppDbContext>` dependency with `IStopWordCorpusRepository`:
- `ValidateDataAvailabilityAsync` → `var counts = await _corpusRepository.GetCountsAsync(since, ct);` (use `counts.SpamSamples`, `counts.LegitMessages`, `counts.ScanResults` where the three locals were).
- `GenerateAdditionRecommendationsAsync` → `var spamTexts = await _corpusRepository.GetSpamTextsAsync(since, ct);` and `var legitTexts = await _corpusRepository.GetLegitTextsAsync(since, ct);` (delete the manual translation lookups).
- `GenerateRemovalRecommendationsAsync` → iterate `await _corpusRepository.GetScanCheckResultsAsync(since, ct)` (`result.IsSpam`, `result.CheckResultsJson`).
- `GetAverageStopWordsExecutionTimeAsync`: delete the unused `CreateDbContextAsync` line and keep its documented `return null;`.

`grep -n "DbContext\|AppDbContext" StopWordRecommendationService.cs` must return nothing. Register `services.AddScoped<IStopWordCorpusRepository, StopWordCorpusRepository>();` next to `IMLTrainingDataRepository`. Add the same registration to `StopWordRecommendationServiceMalformedJsonTests`' DI setup.

- [ ] **Step 5: Run and verify**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~StopWord"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(ml): stop-word corpora come from a repository over message_verdicts

StopWordRecommendationService no longer queries the DbContext; spam and
legit corpora follow each message's current verdict.

Refs #547, #484

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 13: Detector analytics read scans; stats move behind a repository

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/HourlyDetectionStatsView.cs`, `EnrichedDetectionView.cs`, `DetectionAccuracyView.cs` (new SQL constants + columns)
- Create (generated, then edited): `TelegramGroupsAdmin.Data/Migrations/<timestamp>_UpdateDetectionAnalyticsViews.cs`
- Modify: `TelegramGroupsAdmin/Repositories/AnalyticsRepository.cs`
- Create: `TelegramGroupsAdmin/Repositories/IMessageStatsRepository.cs`, `MessageStatsRepository.cs` (moved query code); Modify `TelegramGroupsAdmin/Repositories/MessageStatsService.cs` (thin), `IMessageStatsService.cs` (+1 method)
- Modify: `TelegramGroupsAdmin/Repositories/Mappings/EnrichedDetectionMappings.cs`, `TelegramGroupsAdmin/Models/Analytics/RecentDetection.cs`
- Modify: `TelegramGroupsAdmin/Components/Shared/ContentDetection/ContentDetectionAnalytics.razor`
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/DetectionResultsRepository.cs` (veto analytics: scans only; file-scan queries by `Source`)
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Repositories/AnalyticsRepositoryTests.cs`, `MessageHistoryRepositoryTests.cs` (DI registration), new `#region Detection accuracy` test

**Interfaces:**
- Produces: views read `source`/`classification`. `EnrichedDetectionView` gains `Source (int)` and `Classification (int)`, keeps `IsSpam` (computed in the view), and drops `DetectionSource`/`NetScore`. `RecentDetection.Source (VerdictSource)` replaces `DetectionSource`, and `NetScore` is removed. `IMessageStatsService.GetCuratedTrainingCountsAsync(CancellationToken ct = default)` → `(int Total, int Spam)`. `IMessageStatsRepository` has the same members as `IMessageStatsService`.

- [ ] **Step 1: Write the failing test (accuracy uses decisions on the same message and chat)**

Add to `AnalyticsRepositoryTests` (it already has the canonical FP anchors `DrId_FpAuto = 2012` / `DrId_FpManual = 2013` for message 213325):

```csharp
    [Test]
    public async Task DetectionAccuracy_FlagsFalsePositive_FromCorrectionDecision_AndIgnoresDecisionRows()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var rows = await ctx.DetectionAccuracy.AsNoTracking().ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Single(r => r.Id == GoldenDatasetConstants.Analytics.DrId_FpAuto).IsFalsePositive, Is.True);
            Assert.That(rows.Any(r => r.Id == GoldenDatasetConstants.Analytics.DrId_FpManual), Is.False, "decision rows are not detector output");
            Assert.That(await ctx.DetectionResults.Where(d => rows.Select(r => r.Id).Contains(d.Id)).AllAsync(d => d.Source == 0), Is.True);
        }
    }
```

(Use the fixture's existing test-helper field name.)

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~AnalyticsRepositoryTests.DetectionAccuracy"`
Expected: FAIL, because the current view joins on `message_id` only and keys on `detection_source`. (If it happens to pass, it must still pass after Step 3; the new SQL is required regardless for Task 16's column drops.)

- [ ] **Step 3: New view SQL and migration**

Replace `CreateViewSql` in the three view classes (keep `DropViewSql`). Classification literals `(0, 2, 4)` are the Spam set (pinned by the parity test in Task 16):

`HourlyDetectionStatsView.CreateViewSql`:

```sql
CREATE VIEW hourly_detection_stats AS
SELECT date(detected_at) AS detection_date,
       EXTRACT(hour FROM detected_at)::integer AS detection_hour,
       count(*) FILTER (WHERE source = 0) AS total_count,
       count(*) FILTER (WHERE source = 0 AND classification IN (0, 2, 4)) AS spam_count,
       count(*) FILTER (WHERE source = 0 AND classification NOT IN (0, 2, 4)) AS ham_count,
       count(*) FILTER (WHERE source IN (11, 12, 13, 14, 15, 16, 17, 99)) AS manual_count,
       avg(score) FILTER (WHERE source = 0) AS avg_score
FROM detection_results
GROUP BY date(detected_at), EXTRACT(hour FROM detected_at);
```

`EnrichedDetectionView.CreateViewSql`: the same SELECT as today with `dr.detection_source`, `dr.is_spam`, `dr.net_score` replaced by `dr.source`, `dr.classification`, `(dr.classification IN (0, 2, 4)) AS is_spam`. Class: add `[Column("source")] public int Source`, `[Column("classification")] public int Classification`; delete `DetectionSource`, `NetScore`.

`DetectionAccuracyView.CreateViewSql`:

```sql
CREATE VIEW detection_accuracy AS
WITH corrections AS (
    SELECT DISTINCT ON (chat_id, message_id)
        chat_id, message_id, classification IN (0, 2, 4) AS corrected_to_spam
    FROM detection_results
    WHERE source IN (11, 12, 13, 14, 15, 99)   -- human decisions that can contradict a scan
    ORDER BY chat_id, message_id, detected_at DESC, id DESC
)
SELECT dr.id, dr.message_id, dr.detected_at, date(dr.detected_at) AS detection_date,
       dr.classification IN (0, 2, 4) AS original_classification,
       COALESCE(c.message_id IS NOT NULL AND dr.classification IN (0, 2, 4) AND NOT c.corrected_to_spam, false) AS is_false_positive,
       COALESCE(c.message_id IS NOT NULL AND dr.classification NOT IN (0, 2, 4) AND c.corrected_to_spam, false) AS is_false_negative
FROM detection_results dr
LEFT JOIN corrections c ON c.chat_id = dr.chat_id AND c.message_id = dr.message_id
WHERE dr.source = 0;
```

Generate an empty migration `dotnet ef migrations add UpdateDetectionAnalyticsViews --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`. In `Up`, call the three `DropViewSql` and then the three `CreateViewSql` (the same pattern as `RemoveV1ContentDetectionBridge`). In `Down`, drop the three views and recreate the **previous** definitions inline, as literal SQL copied from `pg_get_viewdef` (`docker exec tga-db psql -U tgadmin -d telegram_groups_admin -At -c "SELECT pg_get_viewdef('<view>'::regclass, true)"`, run **before** applying this migration locally).

- [ ] **Step 4: Repositories**

`AnalyticsRepository`, detector-performance queries only (scan rows):
- Response times: `where dr.Source == (int)VerdictSource.ContentScan && VerdictClassifications.SpamValues.Contains(dr.Classification!.Value)` and join `UserActions` on `ua.MessageId == dr.MessageId && ua.ChatId == dr.ChatId`.
- Detection method comparison: replace `.Where(dr => dr.DetectionSource != "manual")` with `.Where(dr => dr.Source == (int)VerdictSource.ContentScan)` and select `IsSpam = VerdictClassifications.SpamValues.Contains(dr.Classification!.Value)`.
- Daily detection trends and spam trend comparison: add `dr.Source == (int)VerdictSource.ContentScan`, and use the same `SpamValues.Contains` expression in place of `dr.IsSpam`.

`DetectionResultsRepository`:
- `GetOpenAIVetoAnalyticsAsync` / `GetRecentVetoedMessagesAsync`: replace `!dr.IsSpam` with `dr.Source == (int)VerdictSource.ContentScan && !VerdictClassifications.SpamValues.Contains(dr.Classification!.Value)`. The JSON check that follows already requires an OpenAI clean result, so AI-review-below-threshold rows (positive AI score) are not counted as vetoes.
- The three file-scan methods: `dr.DetectionSource == "file_scan"` → `dr.Source == (int)VerdictSource.FileScan`, and `dr.IsSpam == true` → `dr.Classification == (int)VerdictClassification.UntrainedSpam`.

Move `MessageStatsService`'s entire implementation into `MessageStatsRepository : IMessageStatsRepository` (same file content with the class renamed; `IMessageStatsRepository` mirrors `IMessageStatsService`). Then change inside it:
- `GetDetectionStatsAsync`: both queries filter `dr.Source == (int)VerdictSource.ContentScan` and compute spam with the `SpamValues.Contains` expression.
- The message-trends queries (the three `LeftJoin(context.DetectionResults, m => m.MessageId, dr => dr.MessageId, …)` blocks) join `context.MessageVerdicts` on `new { m.MessageId, m.ChatId }` and use `IsSpam = v != null && v.IsSpam`. This answers "is this message spam now?" and fixes the `message_id`-only join.
- Add `GetCuratedTrainingCountsAsync`: count `MessageVerdicts` with `CuratedValues.Contains(v.Classification)`, total and `v.IsSpam`.

`MessageStatsService` becomes a thin service over `IMessageStatsRepository` (every member delegates). Register `IMessageStatsRepository` in `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` and in `MessageHistoryRepositoryTests`' DI setup.

`ContentDetectionAnalytics.razor`: remove `@inject IDbContextFactory<AppDbContext>` and the EF usings. `LoadTrainingCount` becomes:

```csharp
            (_totalTrainingSamples, _totalSpamSamples) = await MessageStatsService.GetCuratedTrainingCountsAsync();
```

`EnrichedDetectionMappings` / `RecentDetection`: map `Source = (VerdictSource)view.Source`; remove `DetectionSource` and `NetScore` from `RecentDetection` and fix its readers (`grep -rn "\.NetScore\|DetectionSource" TelegramGroupsAdmin/Components TelegramGroupsAdmin/Models/Analytics`): display `Source.ToString()` and `Score`.

- [ ] **Step 5: Run the analytics suites**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only` then `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~Analytics|FullyQualifiedName~MessageHistoryRepositoryTests|FullyQualifiedName~Migrations"` and `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~Analytics"`.
Expected: all PASS. If a pre-existing analytics expectation changes because manual rows no longer count as detector output, that's the intended semantics. Confirm by listing the ids that moved, and update the expectation's comment and value together. If a count changes for any other reason, stop and report it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(analytics): detector analytics read scans; stats behind a repository

Views and analytics measure what the detector said (ContentScan rows),
corrections come from human decisions on the same (chat, message), and
message trends ask message_verdicts. MessageStatsService's queries move
into MessageStatsRepository; the analytics component stops querying the
DbContext.

Refs #547, #490

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 14: Retire legacy fields from the domain model and UI

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Models/DetectionResultRecord.cs` (remove `DetectionSource`, `NetScore`, `UsedForTraining`)
- Modify: `TelegramGroupsAdmin.Core/Models/VerdictClassifications.cs` (+`IsTrainingSample()`)
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/Mappings/DetectionResultMappings.cs`, `DetectionResultsRepository.cs` (`WithActorJoins`)
- Modify: `TelegramGroupsAdmin/Components/Shared/DetectionHistoryDialog.razor`, `MessageBubbleTelegram.razor`, `TelegramGroupsAdmin/Components/Pages/Messages.razor`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs` (detection-history join on the composite key)
- Test: `TelegramGroupsAdmin.ComponentTests/Components/DetectionHistoryDialogTests.cs`, `TelegramGroupsAdmin.UnitTests/Core/Models/VerdictTypesTests.cs`

**Interfaces:**
- Produces: `VerdictClassification.IsTrainingSample()` (ExplicitSpam, ExplicitHam, ImplicitSpam, ImplicitHam). `DetectionHistoryDialog` takes `[Parameter] public MessageVerdict? CurrentVerdict` instead of `TrainingLabelRecord? TrainingLabel`.

- [ ] **Step 1: Write the failing tests**

`VerdictTypesTests`:

```csharp
    [TestCase(VerdictClassification.ExplicitSpam, true)]
    [TestCase(VerdictClassification.ExplicitHam, true)]
    [TestCase(VerdictClassification.ImplicitSpam, true)]
    [TestCase(VerdictClassification.ImplicitHam, true)]
    [TestCase(VerdictClassification.UntrainedSpam, false)]
    [TestCase(VerdictClassification.UntrainedHam, false)]
    [TestCase(VerdictClassification.Unscanned, false)]
    public void IsTrainingSample_OnlyTrainedClassifications(VerdictClassification c, bool expected)
        => Assert.That(c.IsTrainingSample(), Is.EqualTo(expected));
```

`DetectionHistoryDialogTests`: replace the `TrainingLabel` parameter setup with `CurrentVerdict = new MessageVerdict(-100, 1, VerdictClassification.ExplicitHam, false, VerdictSource.WebMarkHam, DateTimeOffset.UtcNow, 7)` and assert the banner shows "Explicit ham" and "WebMarkHam". Replace any `NetScore =`/`UsedForTraining =`/`DetectionSource =` initializers with `Score =`/`Classification =`/`Source =`.

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~VerdictTypesTests"` and `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~DetectionHistoryDialogTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement**

`VerdictClassifications`: add inside the extension block:

```csharp
        /// <summary>True when the verdict trains the classifiers (explicit or implicit, spam or ham).</summary>
        public bool IsTrainingSample() => classification is VerdictClassification.ExplicitSpam or VerdictClassification.ExplicitHam
            or VerdictClassification.ImplicitSpam or VerdictClassification.ImplicitHam;
```

`DetectionResultRecord`: delete `DetectionSource`, `NetScore`, `UsedForTraining`. Remove their assignments in `DetectionResultMappings.ToModel` and `WithActorJoins`. Fix every compile error (`dotnet build`) by showing `Source`, `Score` and `Classification` instead:
- `DetectionHistoryDialog.razor`: `Net: @result.NetScore` → `Score: @result.Score.ToString("F1")`; `@result.DetectionSource` → `@result.Source`; `Score: @Math.Abs(result.NetScore)` → `Score: @result.Score.ToString("F1")`; `@if (result.UsedForTraining)` → `@if (result.Classification.IsTrainingSample())`. Replace the `TrainingLabel` parameter and banner with:

```razor
        @if (CurrentVerdict is { Source: not null } verdict)
        {
            <MudAlert Severity="@(verdict.IsSpam ? Severity.Error : Severity.Success)" Class="mb-4" Icon="@Icons.Material.Filled.Gavel">
                <MudText Typo="Typo.body2">
                    <strong>Current verdict:</strong> @verdict.Classification.ToDisplayText() (@verdict.Source)
                    @if (verdict.DetectedAt is { } at)
                    {
                        <text> · <LocalTimestamp Value="at" /></text>
                    }
                </MudText>
            </MudAlert>
        }
```

  with `[Parameter] public MessageVerdict? CurrentVerdict { get; set; }`. Add to `VerdictClassifications`: `public string ToDisplayText() => classification switch { VerdictClassification.ExplicitSpam => "Explicit spam", VerdictClassification.ExplicitHam => "Explicit ham", VerdictClassification.ImplicitSpam => "Implicit spam", VerdictClassification.ImplicitHam => "Implicit ham", VerdictClassification.UntrainedSpam => "Spam (not trained)", VerdictClassification.UntrainedHam => "Ham (not trained)", _ => "Unscanned" };` inside the extension block, with a unit test case per value in `VerdictTypesTests`. (Check the `LocalTimestamp` parameter name in `Components/Shared/LocalTimestamp.razor` and match it.)
- `Messages.razor` `HandleViewDetections`: replace the `TrainingLabelsRepository.GetByMessageIdAsync` line with `var verdict = await DetectionResultsRepository.GetCurrentVerdictAsync(message.MessageId, message.Chat.Id);` and pass `{ x => x.CurrentVerdict, verdict }`. Remove the `ITrainingLabelsRepository` injection.
- `MessageBubbleTelegram.razor`: line ~561 `latest is { DetectionSource: "manual", IsSpam: false }` → `latest is { Source: VerdictSource.WebMarkHam or VerdictSource.ReviewDismiss }`. In the tooltip, `latest.NetScore:F2` → `latest.Score:F2` and `Source: {latest.DetectionSource}` → `Source: {latest.Source}`.
- `TelegramUserRepository` detection history: `join m in context.Messages on new { dr.MessageId, dr.ChatId } equals new { m.MessageId, m.ChatId }` (was `message_id` only).

- [ ] **Step 4: Run and verify**

Run: `dotnet build` (0 warnings), `dotnet test TelegramGroupsAdmin.UnitTests`, `dotnet test TelegramGroupsAdmin.ComponentTests`.
Expected: all PASS; `grep -rn "\.NetScore\|\.UsedForTraining\|\.DetectionSource" --include=*.cs --include=*.razor TelegramGroupsAdmin TelegramGroupsAdmin.Telegram TelegramGroupsAdmin.ContentDetection TelegramGroupsAdmin.BackgroundJobs` shows only the Data DTO and repository internals (the `NewRow` legacy writes, removed in Task 16).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(ui): show verdict source and classification instead of legacy fields

The detection history dialog shows the message's current verdict
instead of the training_labels row; badges and tooltips use score and
source. The user detail detection join uses the composite key.

Refs #547

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 15: Media features on the message; Layer 1 reads verdicts ⋈ features (#527)

**Files:**
- Create: `TelegramGroupsAdmin.Core/Models/MediaFeatures.cs` (domain)
- Create: `TelegramGroupsAdmin.Data/Models/MediaFeaturesDto.cs` (persistence + JSON contract), `TelegramGroupsAdmin.Data/MediaFeaturesJson.cs` (serializer options)
- Modify: `TelegramGroupsAdmin.Data/Models/MessageRecordDto.cs` (`MediaFeatures` → `MediaFeaturesDto?`), `TelegramGroupsAdmin.Data/AppDbContext.cs` (value converter)
- Create: `TelegramGroupsAdmin.ContentDetection/Repositories/Mappings/MediaFeaturesMappings.cs`
- Create: `TelegramGroupsAdmin.ContentDetection/Services/IMediaFeatureExtractor.cs`, `MediaFeatureExtractor.cs`
- Create: `TelegramGroupsAdmin.ContentDetection/Repositories/IMediaSampleRepository.cs`, `MediaSampleRepository.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Models/ContentCheckResponseV2.cs` (+`MediaFeatures`)
- Modify: `TelegramGroupsAdmin.ContentDetection/Checks/ImageContentCheckV2.cs`, `VideoContentCheckV2.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IMessageHistoryRepository.cs`, `MessageHistoryRepository.cs` (+`SetMediaFeaturesAsync`, `GetMediaFeatureBackfillCandidatesAsync`)
- Modify: `TelegramGroupsAdmin.Telegram/Handlers/ContentDetectionOrchestrator.cs` (store features)
- Modify: `TelegramGroupsAdmin.Telegram/Services/Hashing/PhotoHashRehashService.cs` (image-sample step → media backfill)
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/TrainingHandler.cs` (drop sample saves)
- Modify: `TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs` (register extractor + repository)
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/19_messages.sql` (append one `UPDATE`, canonical edit)
- Test: Create `TelegramGroupsAdmin.UnitTests/Data/MediaFeaturesJsonTests.cs`, `TelegramGroupsAdmin.UnitTests/ContentDetection/MediaFeatureExtractorTests.cs`, `TelegramGroupsAdmin.IntegrationTests/ContentDetection/Repositories/MediaSampleRepositoryTests.cs`; update `TelegramGroupsAdmin.IntegrationTests/Services/PhotoHashRehashServiceTests.cs`, `TrainingHandlerTests.cs`

**Interfaces:**
- Produces:
  - Core: `abstract record MediaFeatures`; `sealed record PhotoFeatures(byte[] Hash) : MediaFeatures`; `sealed record VideoFeatures(IReadOnlyList<KeyframeFeature> Keyframes) : MediaFeatures`; `sealed record KeyframeFeature(double Position, byte[] Hash)`.
  - Data: `MediaFeaturesDto` (`[JsonPolymorphic("type")]`, `"photo"`/`"video"`), `PhotoFeaturesDto(byte[] Hash)`, `VideoFeaturesDto(IReadOnlyList<KeyframeFeatureDto> Keyframes)`, `KeyframeFeatureDto(double Position, byte[] Hash)`; `MediaFeaturesJson.Options`.
  - `MediaFeatures.ToDto()` / `MediaFeaturesDto.ToModel()` (public, ContentDetection mappings).
  - `IMediaFeatureExtractor`: `Task<PhotoFeatures?> ExtractPhotoAsync(string absolutePath)`, `Task<VideoFeatures?> ExtractVideoAsync(string absolutePath, CancellationToken ct = default)`, `Task<VideoFeatures?> FromFramesAsync(IReadOnlyList<ExtractedFrame> frames)`.
  - `IMediaSampleRepository`: `Task<IReadOnlyList<(PhotoFeatures Features, bool IsSpam)>> GetRecentPhotoSamplesAsync(int limit, CancellationToken ct = default)`, `Task<IReadOnlyList<(VideoFeatures Features, bool IsSpam)>> GetRecentVideoSamplesAsync(int limit, CancellationToken ct = default)`: messages whose current verdict `IsTrainingSample()` and that carry features, newest verdict first.
  - `IMessageHistoryRepository.SetMediaFeaturesAsync(int messageId, long chatId, MediaFeatures features, CancellationToken ct = default)`; `GetMediaFeatureBackfillCandidatesAsync(int limit, CancellationToken ct = default)` → `IReadOnlyList<MediaBackfillCandidate>` with `record MediaBackfillCandidate(int MessageId, long ChatId, string? PhotoLocalPath, string? MediaLocalPath, MediaType? MediaType)` (Telegram models): curated current verdict, `media_features IS NULL`, and has a photo or video/animation/video-note path.
  - `ContentCheckResponseV2.MediaFeatures` (never serialized into `check_results_json`; `CheckResultsSerializer` copies named fields only).

- [ ] **Step 1: Write the failing JSON-contract and extractor tests**

`TelegramGroupsAdmin.UnitTests/Data/MediaFeaturesJsonTests.cs`:

```csharp
using System.Text.Json;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.UnitTests.Data;

/// <summary>
/// Pins the persisted media_features JSON contract. The Core model may later become a C# 15
/// union; this DTO contract must not change when it does.
/// </summary>
[TestFixture]
public class MediaFeaturesJsonTests
{
    private static readonly byte[] Hash = [1, 2, 3, 4, 5, 6, 7, 8];

    [Test]
    public void Photo_RoundTrips_WithTypeDiscriminator()
    {
        var json = JsonSerializer.Serialize<MediaFeaturesDto>(new PhotoFeaturesDto(Hash), MediaFeaturesJson.Options);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.StartWith("""{"type":"photo","""));
            Assert.That(JsonSerializer.Deserialize<MediaFeaturesDto>(json, MediaFeaturesJson.Options),
                Is.TypeOf<PhotoFeaturesDto>().With.Property(nameof(PhotoFeaturesDto.Hash)).EqualTo(Hash));
        }
    }

    [Test]
    public void Video_RoundTrips()
    {
        MediaFeaturesDto video = new VideoFeaturesDto([new KeyframeFeatureDto(0.1, Hash), new KeyframeFeatureDto(0.5, Hash)]);
        var back = JsonSerializer.Deserialize<MediaFeaturesDto>(JsonSerializer.Serialize(video, MediaFeaturesJson.Options), MediaFeaturesJson.Options);
        Assert.That(((VideoFeaturesDto)back!).Keyframes, Has.Count.EqualTo(2));
    }

    [Test]
    public void Photo_WithoutHash_IsRejected()
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MediaFeaturesDto>("""{"type":"photo"}""", MediaFeaturesJson.Options));

    [Test]
    public void UnknownType_IsRejected()
        => Assert.Catch(() => JsonSerializer.Deserialize<MediaFeaturesDto>("""{"type":"audio","hash":"AQ=="}""", MediaFeaturesJson.Options));
}
```

`TelegramGroupsAdmin.UnitTests/ContentDetection/MediaFeatureExtractorTests.cs`:

```csharp
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

[TestFixture]
public class MediaFeatureExtractorTests
{
    [Test]
    public async Task ExtractPhotoAsync_MissingFile_ReturnsNullWithoutThrowing()
    {
        var hashes = Substitute.For<IPhotoHashService>();
        var extractor = new MediaFeatureExtractor(hashes, Substitute.For<IVideoFrameExtractionService>());

        var features = await extractor.ExtractPhotoAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jpg"));

        Assert.That(features, Is.Null);
        await hashes.DidNotReceiveWithAnyArgs().ComputePhotoHashAsync(default!);
    }

    [Test]
    public async Task FromFramesAsync_HashesEveryFrame_KeepsPositions()
    {
        var hashes = Substitute.For<IPhotoHashService>();
        hashes.ComputePhotoHashAsync(Arg.Any<string>()).Returns(new byte[8]);
        var extractor = new MediaFeatureExtractor(hashes, Substitute.For<IVideoFrameExtractionService>());

        var features = await extractor.FromFramesAsync([new ExtractedFrame("a.jpg", 0.1, 50, false), new ExtractedFrame("b.jpg", 0.9, 50, false)]);

        Assert.That(features!.Keyframes.Select(k => k.Position), Is.EqualTo(new[] { 0.1, 0.9 }));
    }
}
```

(Match `ExtractedFrame`'s full positional constructor. Add arguments if it has more than the four shown.)

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~MediaFeaturesJsonTests|FullyQualifiedName~MediaFeatureExtractorTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement the types, contract, mappings and extractor**

`TelegramGroupsAdmin.Core/Models/MediaFeatures.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Perceptual-hash features of a message's media. Case types are self-contained so this base can
/// later become a C# 15 union without touching persistence (Data owns the JSON contract).
/// Readers switch over the case types; they never use base members.
/// </summary>
public abstract record MediaFeatures;

public sealed record PhotoFeatures(byte[] Hash) : MediaFeatures;

public sealed record VideoFeatures(IReadOnlyList<KeyframeFeature> Keyframes) : MediaFeatures;

public sealed record KeyframeFeature(double Position, byte[] Hash);
```

`TelegramGroupsAdmin.Data/Models/MediaFeaturesDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Persisted media_features JSON contract: always carries "type"; each case has a unique required
/// property ("hash" / "keyframes"). Do not change field names: stored rows depend on them.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PhotoFeaturesDto), "photo")]
[JsonDerivedType(typeof(VideoFeaturesDto), "video")]
public abstract record MediaFeaturesDto;

public sealed record PhotoFeaturesDto(
    [property: JsonPropertyName("hash"), JsonRequired] byte[] Hash) : MediaFeaturesDto;

public sealed record VideoFeaturesDto(
    [property: JsonPropertyName("keyframes"), JsonRequired] IReadOnlyList<KeyframeFeatureDto> Keyframes) : MediaFeaturesDto;

public sealed record KeyframeFeatureDto(
    [property: JsonPropertyName("position")] double Position,
    [property: JsonPropertyName("hash"), JsonRequired] byte[] Hash);
```

`TelegramGroupsAdmin.Data/MediaFeaturesJson.cs`:

```csharp
using System.Text.Json;

namespace TelegramGroupsAdmin.Data;

public static class MediaFeaturesJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
}
```

`MessageRecordDto.MediaFeatures` changes type to `MediaFeaturesDto?` (keep the `[Column("media_features", TypeName = "jsonb")]`). In `AppDbContext`:

```csharp
        modelBuilder.Entity<MessageRecordDto>()
            .Property(m => m.MediaFeatures)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, MediaFeaturesJson.Options),
                s => s == null ? null : JsonSerializer.Deserialize<MediaFeaturesDto>(s, MediaFeaturesJson.Options));
```

Run `dotnet ef migrations has-pending-model-changes --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`. Expected: no changes (same column, same type). If EF reports a change, generate it as `MediaFeaturesConverter` and confirm it contains no DDL.

`TelegramGroupsAdmin.ContentDetection/Repositories/Mappings/MediaFeaturesMappings.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

public static class MediaFeaturesMappings
{
    extension(MediaFeatures features)
    {
        public DataModels.MediaFeaturesDto ToDto() => features switch
        {
            PhotoFeatures p => new DataModels.PhotoFeaturesDto(p.Hash),
            VideoFeatures v => new DataModels.VideoFeaturesDto([.. v.Keyframes.Select(k => new DataModels.KeyframeFeatureDto(k.Position, k.Hash))]),
            _ => throw new ArgumentOutOfRangeException(nameof(features), features.GetType().Name, "Unknown media features type")
        };
    }

    extension(DataModels.MediaFeaturesDto dto)
    {
        public MediaFeatures ToModel() => dto switch
        {
            DataModels.PhotoFeaturesDto p => new PhotoFeatures(p.Hash),
            DataModels.VideoFeaturesDto v => new VideoFeatures([.. v.Keyframes.Select(k => new KeyframeFeature(k.Position, k.Hash))]),
            _ => throw new ArgumentOutOfRangeException(nameof(dto), dto.GetType().Name, "Unknown media features type")
        };
    }
}
```

`IMediaFeatureExtractor` / `MediaFeatureExtractor` (ContentDetection/Services):

```csharp
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.ContentDetection.Services;

public sealed class MediaFeatureExtractor(
    IPhotoHashService photoHashService,
    IVideoFrameExtractionService frameExtractionService,
    ILogger<MediaFeatureExtractor>? logger = null) : IMediaFeatureExtractor
{
    public async Task<PhotoFeatures?> ExtractPhotoAsync(string absolutePath)
    {
        if (!File.Exists(absolutePath))
            return null;
        var hash = await photoHashService.ComputePhotoHashAsync(absolutePath);
        return hash is null ? null : new PhotoFeatures(hash);
    }

    public async Task<VideoFeatures?> ExtractVideoAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(absolutePath) || !frameExtractionService.IsAvailable)
            return null;
        var frames = await frameExtractionService.ExtractKeyframesAsync(absolutePath, cancellationToken);
        try
        {
            return await FromFramesAsync(frames);
        }
        finally
        {
            foreach (var frame in frames)
                File.Delete(frame.FramePath);
        }
    }

    public async Task<VideoFeatures?> FromFramesAsync(IReadOnlyList<ExtractedFrame> frames)
    {
        var keyframes = new List<KeyframeFeature>(frames.Count);
        foreach (var frame in frames)
        {
            var hash = await photoHashService.ComputePhotoHashAsync(frame.FramePath);
            if (hash is not null)
                keyframes.Add(new KeyframeFeature(frame.PositionPercent, hash));
        }

        if (keyframes.Count == 0)
            logger?.LogWarning("No keyframe hashes computed from {Count} frames", frames.Count);
        return keyframes.Count == 0 ? null : new VideoFeatures(keyframes);
    }
}
```

(In production DI resolves the logger; the unit test passes none.) Add `public MediaFeatures? MediaFeatures { get; init; }` to `ContentCheckResponseV2` (`using TelegramGroupsAdmin.Core.Models;`).

- [ ] **Step 4: Run the unit tests and verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~MediaFeaturesJsonTests|FullyQualifiedName~MediaFeatureExtractorTests"`
Expected: PASS.

- [ ] **Step 5: Canonical edit + failing Layer 1 repository test**

Append to `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/19_messages.sql` (canonical edit 2026-09-27; message 222818 is an explicit-spam photo, unreferenced):

```sql
UPDATE messages SET media_features = '{"type":"photo","hash":"8J8PDw8PH/8="}' WHERE message_id = 222818 AND chat_id = -100026957614982;
```

Add `public const int PhotoFeaturesMsgId = 222818;` (with XML doc) to `GoldenDatasetConstants.Verdicts` and a line to the CLAUDE.md "Verdict events" recipe.

`MediaSampleRepositoryTests`:

```csharp
    [Test]
    public async Task GetRecentPhotoSamplesAsync_ReturnsExplicitSpamPhotoWithFeatures()
    {
        await using (var ctx = _helper.GetDbContext())
        {
            // guard the canonical edit
            Assert.That((await ctx.Messages.SingleAsync(m => m.MessageId == GoldenDatasetConstants.Verdicts.PhotoFeaturesMsgId
                && m.ChatId == GoldenDatasetConstants.Chats.MainChatId)).MediaFeatures, Is.TypeOf<PhotoFeaturesDto>());
        }

        var samples = await _repository.GetRecentPhotoSamplesAsync(limit: 100);

        Assert.That(samples, Has.Some.Matches<(PhotoFeatures Features, bool IsSpam)>(
            s => s.IsSpam && s.Features.Hash.SequenceEqual(Convert.FromBase64String("8J8PDw8PH/8="))));
    }
```

(Use the same `SetUp`/`TearDown` shape as `StopWordCorpusRepositoryTests`, registering `IMediaSampleRepository, MediaSampleRepository`.)

- [ ] **Step 6: Implement the repositories**

`MediaSampleRepository` (ContentDetection; primary constructor `IDbContextFactory<AppDbContext> contextFactory`):

```csharp
    private static readonly int[] TrainingSampleValues =
        [.. VerdictClassifications.TrainingSpamValues, (int)VerdictClassification.ExplicitHam, (int)VerdictClassification.ImplicitHam];

    public async Task<IReadOnlyList<(PhotoFeatures Features, bool IsSpam)>> GetRecentPhotoSamplesAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = await LoadAsync(photo: true, limit, cancellationToken);
        return rows.Select(r => (Features: r.Features.ToModel(), r.IsSpam))
            .Where(r => r.Features is PhotoFeatures)
            .Select(r => ((PhotoFeatures)r.Features, r.IsSpam)).ToList();
    }

    public async Task<IReadOnlyList<(VideoFeatures Features, bool IsSpam)>> GetRecentVideoSamplesAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = await LoadAsync(photo: false, limit, cancellationToken);
        return rows.Select(r => (Features: r.Features.ToModel(), r.IsSpam))
            .Where(r => r.Features is VideoFeatures)
            .Select(r => ((VideoFeatures)r.Features, r.IsSpam)).ToList();
    }

    private async Task<List<(DataModels.MediaFeaturesDto Features, bool IsSpam)>> LoadAsync(bool photo, int limit, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where TrainingSampleValues.Contains(v.Classification)
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            where m.MediaFeatures != null && (photo ? m.PhotoFileId != null : m.PhotoFileId == null)
            orderby v.DetectedAt descending
            select new { m.MediaFeatures, v.IsSpam }
        ).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(r => (r.MediaFeatures!, r.IsSpam)).ToList();
    }
```

In `MessageHistoryRepository`, add:

```csharp
    public async Task SetMediaFeaturesAsync(int messageId, long chatId, MediaFeatures features, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var message = await context.Messages.FirstOrDefaultAsync(m => m.MessageId == messageId && m.ChatId == chatId, cancellationToken);
        if (message is null)
            return;
        message.MediaFeatures = features.ToDto();
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UiModels.MediaBackfillCandidate>> GetMediaFeatureBackfillCandidatesAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        int[] videoTypes = [(int)MediaType.Video, (int)MediaType.Animation, (int)MediaType.VideoNote];
        var rows = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where VerdictClassifications.CuratedValues.Contains(v.Classification)
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            where m.MediaFeatures == null
               && (m.PhotoLocalPath != null || (m.MediaLocalPath != null && m.MediaType != null && videoTypes.Contains((int)m.MediaType.Value)))
            select new { m.MessageId, m.ChatId, m.PhotoLocalPath, m.MediaLocalPath, m.MediaType }
        ).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(r => new UiModels.MediaBackfillCandidate(r.MessageId, r.ChatId, r.PhotoLocalPath, r.MediaLocalPath, (MediaType?)r.MediaType)).ToList();
    }
```

(Match `MessageRecordDto.MediaType`'s real type. If it is an `int?` column, compare ints directly and cast in the projection.) Add the record `TelegramGroupsAdmin.Telegram/Models/MediaBackfillCandidate.cs`.

- [ ] **Step 7: Capture at scan time; Layer 1 via the repository**

`ImageContentCheckV2`: replace the `IImageTrainingSamplesRepository` dependency with `IMediaSampleRepository mediaSamples` and `IMediaFeatureExtractor extractor`. Move the hash computation to the top of `CheckAsync`, before Layer 1, whenever `req.PhotoLocalPath` is set:

```csharp
            var photoFeatures = !string.IsNullOrEmpty(req.PhotoLocalPath)
                ? await extractor.ExtractPhotoAsync(req.PhotoLocalPath)
                : null;
```

Layer 1 uses `photoFeatures.Hash` against `await mediaSamples.GetRecentPhotoSamplesAsync(imageConfig.MaxTrainingSamplesToCompare, req.CancellationToken)` (tuple `Features.Hash`, `IsSpam`). The ham-match abstain branch is unchanged. Rename the existing body to `private async Task<ContentCheckResponseV2> CheckCoreAsync(ImageCheckRequest req, long startTimestamp, PhotoFeatures? photoFeatures)`, and make `CheckAsync` attach the features to whatever it returns:

```csharp
        var response = await CheckCoreAsync(req, startTimestamp, photoFeatures);
        return response with { MediaFeatures = photoFeatures };
```

`VideoContentCheckV2`: same pattern. Right after `ExtractKeyframesAsync`, compute `var videoFeatures = await extractor.FromFramesAsync(frames);`. Layer 1 compares against `GetRecentVideoSamplesAsync(config.MaxTrainingSamplesToCompare, ct)` keyframe hashes (replacing the JSON `KeyframeHashJson` parsing; delete `KeyframeHashJson.cs` if no longer used). Every response after that point carries `MediaFeatures = videoFeatures` via the same `CheckCoreAsync` wrap.

`ContentDetectionOrchestrator`, right after `StoreDetectionResultAsync`:

```csharp
                var mediaFeatures = result.SpamResult.CheckResults
                    .Select(c => c.MediaFeatures)
                    .FirstOrDefault(f => f is not null);
                if (mediaFeatures is not null)
                {
                    var messageHistory = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
                    await messageHistory.SetMediaFeaturesAsync(message.MessageId, message.Chat.Id, mediaFeatures, cancellationToken);
                }
```

A missing file means `photoFullPath` is null, so no features are computed and nothing is stored (Review Focus 4).

`TrainingHandler`: delete the image/video `SaveTrainingSampleAsync` calls and the two sample repository dependencies. Keep the defensive media download, so the startup backfill can hash that media on the next start (Review Focus 5). Update `TrainingHandlerTests`: remove the sample-save tests and substitutes; keep and adjust the download tests (they assert the download, not the save).

Register in `ContentDetection/Extensions/ServiceCollectionExtensions.cs`: `services.AddScoped<IMediaSampleRepository, MediaSampleRepository>();` and `services.AddSingleton<IMediaFeatureExtractor, MediaFeatureExtractor>();`. Remove the `IImageTrainingSamplesRepository`/`IVideoTrainingSamplesRepository` registrations and delete those two repositories, their interfaces, `ImageTrainingSample.cs`, and `ImageTrainingSamplesRepositoryTests` (the tables are dropped in Task 16).

- [ ] **Step 8: Startup backfill replaces the image-sample rehash**

In `PhotoHashRehashService`, replace `RehashImageTrainingSamplesAsync` with a media backfill that uses repositories (inject `IMessageHistoryRepository`, `IMediaFeatureExtractor`, `IOptions<AppOptions>`):

```csharp
    private async Task<PhotoHashRehashResult> BackfillMessageMediaFeaturesAsync(CancellationToken ct)
    {
        var candidates = await messageHistoryRepository.GetMediaFeatureBackfillCandidatesAsync(limit: 500, ct);
        int recomputed = 0, unrecoverable = 0;
        foreach (var candidate in candidates)
        {
            MediaFeatures? features = null;
            string? path = null;
            if (candidate.PhotoLocalPath is not null)
            {
                path = MediaUtilities.ToAbsolutePath(candidate.PhotoLocalPath, appOptions.Value.DataPath);
                features = await mediaFeatureExtractor.ExtractPhotoAsync(path);
            }
            else if (candidate.MediaLocalPath is not null)
            {
                MediaUtilities.ValidateMediaPath(candidate.MediaLocalPath, (int?)candidate.MediaType, appOptions.Value.DataPath, out path);
                features = await mediaFeatureExtractor.ExtractVideoAsync(path, ct);
            }

            if (features is null)
            {
                unrecoverable++;
                logger.LogWarning("Cannot compute media features for message {MessageId} in chat {ChatId}: file missing or unreadable at {Path}",
                    candidate.MessageId, candidate.ChatId, path);
                continue;
            }

            await messageHistoryRepository.SetMediaFeaturesAsync(candidate.MessageId, candidate.ChatId, features, ct);
            recomputed++;
        }
        return new PhotoHashRehashResult(recomputed, unrecoverable, 0);
    }
```

Wire it where `.Add(await RehashImageTrainingSamplesAsync(ct))` was. In `PhotoHashRehashServiceTests`, register the new dependencies (`IMessageHistoryRepository` via `MessageHistoryRepository` plus `AddCoreServices()` for `SimHashService`, `IMediaFeatureExtractor` with a substituted `IVideoFrameExtractionService`). Replace the image-training-sample tests with:

```csharp
    [Test]
    public async Task RehashAsync_CuratedPhotoMessageWithoutFeatures_GetsPhotoFeatures()
    {
        // Infrastructure fixture (empty template): seeds its own rows, as the rest of this file does.
        var messageId = Interlocked.Increment(ref _nextMessageId);
        const long chatId = -100_900_000_000_001L;
        var relative = $"full/{chatId}/{messageId}.jpg";
        WriteTestImage(Path.Combine(_dataPath!, "media", relative));

        await using (var ctx = NewContext())
        {
            ctx.Messages.Add(new MessageRecordDto { MessageId = messageId, ChatId = chatId, UserId = 0, Timestamp = DateTimeOffset.UtcNow, PhotoFileId = "f", PhotoLocalPath = relative });
            ctx.DetectionResults.Add(new DetectionResultRecordDto
            {
                MessageId = messageId, ChatId = chatId, DetectedAt = DateTimeOffset.UtcNow,
                Source = (int)VerdictSource.WebMarkSpam, Classification = (int)VerdictClassification.ExplicitSpam,
                DetectionMethod = "WebMarkSpam", Score = 5, Reason = "test", SystemIdentifier = "integration-test",
                DetectionSource = "manual", NetScore = 5, UsedForTraining = false
            });
            await ctx.SaveChangesAsync();
        }

        await _service!.RehashAsync();

        await using var after = NewContext();
        Assert.That((await after.Messages.SingleAsync(m => m.MessageId == messageId && m.ChatId == chatId)).MediaFeatures,
            Is.TypeOf<PhotoFeaturesDto>());
    }
```

(Task 16 removes `DetectionSource`/`NetScore`/`UsedForTraining` from this initializer when the columns are dropped.)

- [ ] **Step 9: Run and verify**

Run: `dotnet build` (0 warnings), `dotnet test TelegramGroupsAdmin.UnitTests`, and `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MediaSample|FullyQualifiedName~PhotoHashRehash|FullyQualifiedName~LoadCanonical"`.
Expected: all PASS.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(media): media features live on the message; Layer 1 reads verdicts

Image/video checks compute perceptual-hash features once and the scan
stores them on messages.media_features (Data owns the JSON contract,
Core owns a union-ready model). Layer 1 compares against messages whose
current verdict trains, so ham matches exist for the first time and
corrections apply automatically. The startup rehash backfills curated
media; the image/video sample repositories are retired.

Closes #527

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---
### Task 16: Destructive migration `DropLegacyVerdictColumns` + final schema

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/DetectionResultRecord.cs` (drop `NetScore`, `UsedForTraining`, `DetectionSource`; `Source`/`Classification` become `int`)
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs` (is_spam computed from classification; CHECKs; drop `TrainingLabels`/`ImageTrainingSamples`/`VideoTrainingSamples` sets and configuration; drop `used_for_training`/`detection_source` indexes)
- Delete: `TelegramGroupsAdmin.Data/Models/TrainingLabelDto.cs`, `ImageTrainingSampleDto.cs`, `VideoTrainingSampleDto.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/MessageVerdictView.cs` (add final `CreateViewSql`/`DropViewSql`)
- Create (generated, then edited): `TelegramGroupsAdmin.Data/Migrations/<timestamp>_DropLegacyVerdictColumns.cs`
- Delete: `TelegramGroupsAdmin.ContentDetection/Repositories/ITrainingLabelsRepository.cs`, `TrainingLabelsRepository.cs`, `Mappings/TrainingLabelMappings.cs` and the `TrainingLabelRecord` model if nothing else references it; `TelegramGroupsAdmin.IntegrationTests/Repositories/TrainingLabelsRepositoryTests.cs`
- Modify: `DetectionResultsRepository.cs` (`NewRow` without legacy columns; drop `!.Value` on now-non-null ints), `DetectionResultMappings.cs`, `TrainingHandler.cs` (no label upserts), `ContentDetection/Extensions/ServiceCollectionExtensions.cs`
- Modify (test data): delete `canonical/15_image_training_samples.sql`, `16_video_training_samples.sql`, `33_training_labels.sql`; regenerate `32_detection_results.sql`; `GoldenDataset.cs` fixture list; `ChildReducePlan.cs`; `GoldenDatasetConstants.cs` (`TrainingLabels` class removed if unreferenced); `LoadCanonicalAsyncTests.cs`; `IntegrationTests/CLAUDE.md`
- Test: Create `TelegramGroupsAdmin.IntegrationTests/Migrations/VerdictSchemaTests.cs`; fix tests touching removed members

**Interfaces:**
- Produces: the spec's final schema. `detection_results.is_spam GENERATED ALWAYS AS (classification IN (0, 2, 4)) STORED`; `CK_detection_results_classification`, `CK_detection_results_source_classification`; `message_verdicts` passes `is_spam` through; `DetectionResultRecordDto.Source`/`.Classification` are `int`.

- [ ] **Step 1: Write the failing schema tests**

`VerdictSchemaTests` (infrastructure fixture: empty template, synthetic rows allowed):

```csharp
using Npgsql;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

[TestFixture]
public class VerdictSchemaTests
{
    private MigrationTestHelper _helper = null!;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromEmptyTemplateAsync();
        await _helper.ExecuteSqlAsync("""
            INSERT INTO messages (message_id, user_id, chat_id, "timestamp") VALUES (1, 0, -1001, now());
            """);
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private Task InsertAsync(int source, int classification) => _helper.ExecuteSqlAsync($"""
        INSERT INTO detection_results (message_id, chat_id, detected_at, detection_method, score, reason,
            system_identifier, edit_version, source, classification)
        VALUES (1, -1001, now(), 'x', 1, 'x', 'integration-test', 0, {source}, {classification});
        """);

    [TestCase(VerdictSource.ContentScan, VerdictClassification.ImplicitSpam)]
    [TestCase(VerdictSource.ContentScan, VerdictClassification.ImplicitHam)]
    [TestCase(VerdictSource.ContentScan, VerdictClassification.UntrainedSpam)]
    [TestCase(VerdictSource.ContentScan, VerdictClassification.UntrainedHam)]
    [TestCase(VerdictSource.AutoBan, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.WebMarkHam, VerdictClassification.ExplicitHam)]
    public async Task GeneratedIsSpam_MatchesCoreIsSpam(VerdictSource source, VerdictClassification classification)
    {
        await InsertAsync((int)source, (int)classification);
        var isSpam = await _helper.ExecuteScalarAsync<bool>("SELECT is_spam FROM detection_results ORDER BY id DESC LIMIT 1");
        Assert.That(isSpam, Is.EqualTo(classification.IsSpam()));
    }

    [TestCase(VerdictSource.WebMarkHam, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.AutoBan, VerdictClassification.ImplicitSpam)]
    [TestCase(VerdictSource.ContentScan, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.FileScan, VerdictClassification.ImplicitHam)]
    [TestCase(VerdictSource.ReviewDismiss, VerdictClassification.ExplicitHam)]
    public void CheckConstraint_RejectsInconsistentSourceAndClassification(VerdictSource source, VerdictClassification classification)
        => Assert.ThrowsAsync<PostgresException>(() => InsertAsync((int)source, (int)classification));

    [Test]
    public void CheckConstraint_RejectsStoredUnscanned()
        => Assert.ThrowsAsync<PostgresException>(() => InsertAsync((int)VerdictSource.ContentScan, (int)VerdictClassification.Unscanned));

    [Test]
    public async Task View_PassesIsSpamThrough()
    {
        await InsertAsync((int)VerdictSource.ContentScan, (int)VerdictClassification.UntrainedSpam);
        Assert.That(await _helper.ExecuteScalarAsync<bool>("SELECT is_spam FROM message_verdicts WHERE message_id = 1"), Is.True);
    }

    [TestCase("training_labels")]
    [TestCase("image_training_samples")]
    [TestCase("video_training_samples")]
    public async Task LegacyTables_AreGone(string table)
        => Assert.That(await _helper.ExecuteScalarAsync<long>($"SELECT count(*) FROM information_schema.tables WHERE table_name = '{table}'"), Is.Zero);
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~VerdictSchemaTests"`
Expected: FAIL (legacy NOT NULL columns reject the insert, no CHECK exists, and the tables are still present).

- [ ] **Step 3: Final model**

`DetectionResultRecordDto`: delete `DetectionSource`, `UsedForTraining`, `NetScore`. `Source`/`Classification` become `int` (non-nullable). Keep `IsSpam` as `[DatabaseGenerated(DatabaseGeneratedOption.Computed)]`, with its doc changed to "generated from classification".

`AppDbContext`:

```csharp
        // is_spam: the coarse spam/ham split, generated from classification only (VerdictClassifications.Spam).
        modelBuilder.Entity<DetectionResultRecordDto>()
            .Property(d => d.IsSpam)
            .HasComputedColumnSql("(classification IN (0, 2, 4))", stored: true);

        modelBuilder.Entity<DetectionResultRecordDto>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_detection_results_classification", "classification IN (0, 1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_detection_results_source_classification", """
                (source IN (10, 11, 13, 14) AND classification = 0)
                OR (source = 12 AND classification = 1)
                OR (source = 15 AND classification = 3)
                OR (source IN (16, 18, 99) AND classification IN (0, 1))
                OR (source IN (1, 17) AND classification IN (4, 5))
                OR (source = 0 AND classification IN (2, 3, 4, 5))
                """);
        });
```

(Merge the `ToTable` into the existing `CK_detection_results_exclusive_actor` `ToTable` call if one already exists for this entity. EF allows only one `ToTable(Action)` builder per entity configuration pass, so put all three constraints in one lambda.) Delete the `used_for_training` and `detection_source` index configurations, and all `TrainingLabelDto`, `ImageTrainingSampleDto` and `VideoTrainingSampleDto` configuration and DbSets. Delete those three DTO files.

`MessageVerdictView`: add the final definition as constants (the M1 migration keeps its own inline SQL):

```csharp
    public const string CreateViewSql = """
        CREATE VIEW message_verdicts AS
        SELECT m.chat_id, m.message_id,
               COALESCE(v.classification, 6) AS classification,
               COALESCE(v.is_spam, false) AS is_spam,
               v.source, v.detected_at, v.id AS verdict_id
        FROM messages m
        LEFT JOIN LATERAL (
            SELECT d.classification, d.is_spam, d.source, d.detected_at, d.id
            FROM detection_results d
            WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id AND d.source <> 1
            ORDER BY d.detected_at DESC, d.id DESC
            LIMIT 1) v ON true;
        """;

    public const string DropViewSql = "DROP VIEW IF EXISTS message_verdicts";
```

- [ ] **Step 4: Generate and order the migration**

Run: `dotnet ef migrations add DropLegacyVerdictColumns --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`.

Edit `Up` so it runs in this order, with EF's generated operations kept where they fit:
1. `migrationBuilder.Sql(MessageVerdictView.DropViewSql); EnrichedDetectionView.DropViewSql; HourlyDetectionStatsView.DropViewSql; DetectionAccuracyView.DropViewSql;`
2. Replace whatever EF generated for `is_spam` with explicit SQL:

```csharp
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_detection_results_is_spam;
                DROP INDEX IF EXISTS ix_detection_results_is_spam_detected_at;
                ALTER TABLE detection_results DROP COLUMN is_spam;
                ALTER TABLE detection_results ALTER COLUMN source SET NOT NULL;
                ALTER TABLE detection_results ALTER COLUMN classification SET NOT NULL;
                ALTER TABLE detection_results ADD COLUMN is_spam boolean GENERATED ALWAYS AS (classification IN (0, 2, 4)) STORED;
                CREATE INDEX ix_detection_results_is_spam ON detection_results (is_spam);
                CREATE INDEX ix_detection_results_is_spam_detected_at ON detection_results (is_spam, detected_at);
                """);
```

   Remove EF's own `AlterColumn` calls for `is_spam`/`source`/`classification`.
3. EF's generated `DropIndex` for `IX_detection_results_used_for_training` / `ix_detection_results_detection_source`, `DropColumn` for `net_score`, `used_for_training`, `detection_source`, `DropTable` for the three tables, and `AddCheckConstraint` for the two new constraints.
4. `migrationBuilder.Sql(MessageVerdictView.CreateViewSql); EnrichedDetectionView.CreateViewSql; HourlyDetectionStatsView.CreateViewSql; DetectionAccuracyView.CreateViewSql;`

`Down`: this migration is intentionally one-way for data (dropped tables can't be restored). Implement `Down` to recreate the schema (EF-generated table and column adds, plus the old computed `is_spam` from `net_score > 0` and the M1 transitional view inline), with a comment saying data dropped by `Up` is not restored. Match the pattern and comment style of `RemoveV1ContentDetectionBridge.Down`.

- [ ] **Step 5: Remove the legacy code paths**

- `DetectionResultsRepository.NewRow`: delete the three legacy assignments and their comment. Replace `row.Source!.Value`/`row.Classification!.Value`, `dr.Classification!.Value` and `x.dr.Source!.Value` casts across the ContentDetection, Telegram and App repositories with the plain `int` (`grep -rn "Classification!\.\|Source!\.\|Classification != null" --include=*.cs`).
- `DetectionResultMappings.ToModel`: `Source = (VerdictSource)data.Source, Classification = (VerdictClassification)data.Classification`.
- `TrainingHandler`: remove `ITrainingLabelsRepository` and both label upserts (spam and ham paths). The retraining trigger stays. Update `TrainingHandlerTests` (remove label assertions and substitutes).
- Delete `ITrainingLabelsRepository`, `TrainingLabelsRepository`, `TrainingLabelMappings`, `TrainingLabelsRepositoryTests`, and their DI registration. Delete `TrainingLabelRecord` if `grep -rn TrainingLabelRecord` shows no remaining users. Keep the Core `TrainingLabel` enum (ML samples use it).
- `MessageHistoryRepositoryTests` retention guard: remove the `UsedForTraining` line (Task 11). `PhotoHashRehashServiceTests` seed: remove `DetectionSource`/`NetScore`/`UsedForTraining` (Task 15).

- [ ] **Step 6: Canonical and test-data helpers**

Regenerate `32_detection_results.sql` without the dropped columns (a mechanical column drop):

```bash
docker exec tga-db createdb -U tgadmin canonical_convert
PREV=$(ls TelegramGroupsAdmin.Data/Migrations/*_UpdateDetectionAnalyticsViews.cs | xargs -n1 basename | sed 's/\.cs$//')
# migrate to the migration BEFORE DropLegacyVerdictColumns, load the current canonical, then apply the drop
dotnet ef database update "$PREV" --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin \
  --connection "Host=localhost;Port=5432;Database=canonical_convert;Username=tgadmin;Password=changeme"
for f in $(ls TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/*.sql | sort); do
  docker exec -i tga-db psql -q -v ON_ERROR_STOP=1 -U tgadmin -d canonical_convert < "$f"
done
dotnet ef database update DropLegacyVerdictColumns --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin \
  --connection "Host=localhost;Port=5432;Database=canonical_convert;Username=tgadmin;Password=changeme"
docker exec tga-db pg_dump -U tgadmin -d canonical_convert --data-only --column-inserts -t detection_results \
  | grep -E '^(INSERT INTO|SELECT pg_catalog.setval)' \
  > TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/32_detection_results.sql
docker exec tga-db dropdb -U tgadmin canonical_convert
git rm TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/15_image_training_samples.sql \
       TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/16_video_training_samples.sql \
       TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/33_training_labels.sql
```

(If the `ef database update … --connection` form is unavailable, set `ConnectionStrings__DefaultConnection` for the design-time factory instead.) The row count must equal Task 5's total. `pg_dump` omits the generated `is_spam`.

- `GoldenDataset.LoadCanonicalAsync`: remove the three deleted fixtures from the list and fix the "36 files" comments (now 33).
- `ChildReducePlan`: `KeepLabeledMessagesOnly` keeps messages that have an **explicit decision** row (`source NOT IN (0, 1, 17) AND classification IN (0, 1)`), and `KeepSpam(n)`/ham counterparts filter on those rows' `classification`. Replace each `training_labels` SQL fragment with the equivalent `detection_results` predicate, and remove the "DELETE FROM training_labels" statements (the table no longer exists). `GoldenReducePlanTests` keep their assertions. If one fails, the predicate translation is wrong; fix the predicate.
- `GoldenDatasetConstants`: delete the `TrainingLabels` class if `grep -rn "GoldenDatasetConstants.TrainingLabels"` is empty after deleting `TrainingLabelsRepositoryTests`; otherwise keep the anchors used elsewhere.
- `LoadCanonicalAsyncTests`: remove the `training_labels` count assertion and the empty-table comment entries for the two sample tables.
- `IntegrationTests/CLAUDE.md`: update the file count (33), the table list (remove 15/16/33, detection_results columns), and the recipes that mention `training_labels`.

- [ ] **Step 7: Run everything**

Run: `dotnet build` (0 warnings), `dotnet run --project TelegramGroupsAdmin -- --migrate-only`, then the full integration suite in the background: `dotnet test TelegramGroupsAdmin.IntegrationTests`, plus `dotnet test TelegramGroupsAdmin.UnitTests` and `dotnet test TelegramGroupsAdmin.ComponentTests`.
Expected: all PASS, including `VerdictSchemaTests`, `AddVerdictEventsMigrationTests` (it migrates only to `AddVerdictEvents`, so it's unaffected) and `CanonicalVerdictOracleTests`. `git grep -n "training_labels\|used_for_training\|net_score\|detection_source\|TrainingLabelsRepository\|ImageTrainingSample\|VideoTrainingSample" -- ':!*/Migrations/*' ':!docs/*' ':!*/TestData/SQL/tools/*'` returns nothing (the backup step added in Task 17 is the one expected future exception).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(data)!: drop legacy verdict columns and tables

is_spam is generated from classification, CHECK constraints tie every
decision source to its classification, and training_labels plus the
image/video sample tables are gone. message_verdicts passes is_spam
through.

Refs #547, #549

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 17: Backup format 3.1 — restore 3.0 backups (backup-only, time-boxed)

**Files:**
- Create: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Migrations/Backup30To31VerdictMigration.cs`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs` (`CurrentVersion = "3.1"`, `ApplyBackupMigrations`), `BackupMetadata.cs` (default `"3.1"`)
- Test: Create `TelegramGroupsAdmin.UnitTests/BackgroundJobs/Services/Backup/Backup30To31VerdictMigrationTests.cs`

**Interfaces:**
- Produces: `public static class Backup30To31VerdictMigration { public static void Apply(SystemBackup backup, ILogger logger); }`. It transforms a 3.0 backup's in-memory data into the 3.1 shape. It is a **separate implementation** from the SQL migration, by decision. Remove it one year after release.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Migrations;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

[TestFixture]
public class Backup30To31VerdictMigrationTests
{
    private static object Row(object anonymous) => JsonSerializer.SerializeToElement(anonymous);

    private static SystemBackup Build() => new()
    {
        Metadata = new BackupMetadata { Version = "3.0", Tables = ["detection_results", "training_labels", "image_training_samples", "video_training_samples", "content_detection_configs"] },
        Data = new Dictionary<string, List<object>>
        {
            ["content_detection_configs"] = [Row(new { id = 1, chat_id = 0, config_json = """{"ReviewQueueThreshold":2.5}""" })],
            ["detection_results"] =
            [
                Row(new { id = 10, message_id = 1, chat_id = -1001, detected_at = "2026-01-01T00:00:00Z", detection_source = "auto", net_score = 0.8, score = 0.8, used_for_training = false, reason = "No spam", system_identifier = "auto_detection", check_results_json = """{"Checks":[{"CheckName":13,"Score":0.8,"Abstained":false}]}""" }),
                Row(new { id = 11, message_id = 2, chat_id = -1001, detected_at = "2026-01-01T00:00:00Z", detection_source = "auto", net_score = 2.3, score = 2.3, used_for_training = false, reason = "AI confirmed spam: AI: Review", system_identifier = "auto_detection", check_results_json = """{"Checks":[{"CheckName":2,"Score":3.5,"Abstained":false},{"CheckName":6,"Score":2.3,"Abstained":false}]}""" }),
                Row(new { id = 12, message_id = 3, chat_id = -1001, detected_at = "2026-01-01T00:00:00Z", detection_source = "manual", net_score = -5.0, score = 0.0, used_for_training = false, reason = "Manually marked as ham (not spam) by admin", telegram_user_id = 9L })
            ],
            ["training_labels"] =
            [
                Row(new { message_id = 3, chat_id = -1001, label = 1, labeled_by_user_id = 9L, labeled_at = "2026-01-02T00:00:00Z" }),
                Row(new { message_id = 4, chat_id = -1001, label = 0, labeled_by_user_id = (long?)null, labeled_at = "2026-01-02T00:00:00Z" })
            ],
            ["image_training_samples"] = [],
            ["video_training_samples"] = []
        }
    };

    private static JsonElement Get(SystemBackup b, long id) =>
        b.Data!["detection_results"].Cast<JsonElement>().Single(r => r.GetProperty("id").GetInt64() == id);

    [Test]
    public void Apply_ClassifiesScansWithTheSingleRule()
    {
        var backup = Build();
        Backup30To31VerdictMigration.Apply(backup, NullLogger.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Get(backup, 10).GetProperty("classification").GetInt32(), Is.EqualTo(3)); // ImplicitHam
            Assert.That(Get(backup, 11).GetProperty("classification").GetInt32(), Is.EqualTo(5)); // UntrainedHam
            Assert.That(Get(backup, 12).GetProperty("source").GetInt32(), Is.EqualTo(12));        // WebMarkHam
        }
    }

    [Test]
    public void Apply_FoldsLabels_WithoutDuplicatingMatchedOnes()
    {
        var backup = Build();
        Backup30To31VerdictMigration.Apply(backup, NullLogger.Instance);

        var rows = backup.Data!["detection_results"].Cast<JsonElement>().ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Count(r => r.GetProperty("message_id").GetInt32() == 3), Is.EqualTo(1), "matched ham label not duplicated");
            var autoBan = rows.Single(r => r.GetProperty("message_id").GetInt32() == 4);
            Assert.That(autoBan.GetProperty("source").GetInt32(), Is.EqualTo(10));
            Assert.That(autoBan.GetProperty("classification").GetInt32(), Is.EqualTo(0));
            Assert.That(autoBan.GetProperty("id").GetInt64(), Is.GreaterThan(12));
        }
    }

    [Test]
    public void Apply_RemovesLegacyTables()
    {
        var backup = Build();
        Backup30To31VerdictMigration.Apply(backup, NullLogger.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backup.Data!.Keys, Has.None.AnyOf("training_labels", "image_training_samples", "video_training_samples"));
            Assert.That(backup.Metadata.Tables, Has.None.AnyOf("training_labels", "image_training_samples", "video_training_samples"));
        }
    }
}
```

(Adjust `SystemBackup`/`BackupMetadata` initialization to their real shape. `grep -n "class SystemBackup\|class BackupMetadata" -A20` in the Backup folder.)

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~Backup30To31VerdictMigrationTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement**

`Backup30To31VerdictMigration.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Migrations;

/// <summary>
/// Backup format 3.0 → 3.1 (single spam verdict): classifies legacy detection_results rows,
/// folds training_labels into decision rows, and drops the retired tables.
/// Deliberately separate from the SQL migration AddVerdictEvents (backup-only, time-boxed).
/// Remove one year after the release that introduced backup format 3.1 (set the exact date
/// when tagging that release).
/// </summary>
public static class Backup30To31VerdictMigration
{
    private static readonly string[] RetiredTables = ["training_labels", "image_training_samples", "video_training_samples"];

    public static void Apply(SystemBackup backup, ILogger logger)
    {
        if (backup.Data is null || !backup.Data.TryGetValue("detection_results", out var detectionRows))
        {
            RemoveRetiredTables(backup);
            return;
        }

        var thresholds = ReadReviewThresholds(backup);
        var rows = detectionRows.Cast<JsonElement>().Select(e => JsonNode.Parse(e.GetRawText())!.AsObject()).ToList();

        foreach (var row in rows)
        {
            var source = SourceOf(row);
            row["source"] = source;
            row["classification"] = ClassificationOf(row, source, thresholds);
        }

        var nextId = rows.Count == 0 ? 1 : rows.Max(r => r["id"]!.GetValue<long>()) + 1;
        var added = new List<JsonObject>();

        foreach (var excluded in rows.Where(r => r["source"]!.GetValue<int>() is 16 or 18 && r["used_for_training"]?.GetValue<bool>() == false).ToList())
        {
            var exclusion = excluded.DeepClone().AsObject();
            exclusion["id"] = nextId++;
            exclusion["detected_at"] = DateTimeOffset.Parse(excluded["detected_at"]!.GetValue<string>()).AddTicks(10).ToString("O");
            exclusion["source"] = 17;
            exclusion["classification"] = excluded["classification"]!.GetValue<int>() == 0 ? 4 : 5;
            exclusion["reason"] = "Removed from training (backup migration)";
            added.Add(exclusion);
        }

        if (backup.Data.TryGetValue("training_labels", out var labels))
        {
            foreach (var label in labels.Cast<JsonElement>())
            {
                var messageId = label.GetProperty("message_id").GetInt32();
                var chatId = label.GetProperty("chat_id").GetInt64();
                var isSpamLabel = label.GetProperty("label").GetInt32() == 0;
                var labeledBy = label.TryGetProperty("labeled_by_user_id", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt64() : (long?)null;

                var matched = rows.Any(r => r["message_id"]!.GetValue<int>() == messageId && r["chat_id"]!.GetValue<long>() == chatId
                    && r["source"]!.GetValue<int>() is not (0 or 1 or 17)
                    && (r["classification"]!.GetValue<int>() == 0) == isSpamLabel);
                if (matched)
                    continue;

                var autoBan = isSpamLabel && labeledBy is null;
                added.Add(new JsonObject
                {
                    ["id"] = nextId++,
                    ["message_id"] = messageId,
                    ["chat_id"] = chatId,
                    ["detected_at"] = label.GetProperty("labeled_at").GetString(),
                    ["detection_method"] = autoBan ? "AutoBan" : "Manual",
                    ["score"] = 5.0,
                    ["reason"] = label.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : "Migrated training label",
                    ["telegram_user_id"] = labeledBy,
                    ["system_identifier"] = labeledBy is null ? (isSpamLabel ? "auto_detection" : "unknown") : null,
                    ["edit_version"] = 0,
                    ["source"] = autoBan ? 10 : 99,
                    ["classification"] = isSpamLabel ? 0 : 1,
                    ["audit_log_id"] = label.TryGetProperty("audit_log_id", out var audit) && audit.ValueKind == JsonValueKind.Number ? audit.GetInt64() : null
                });
            }
        }

        backup.Data["detection_results"] = [.. rows.Concat(added).Select(o => (object)JsonSerializer.SerializeToElement(o))];
        RemoveRetiredTables(backup);

        logger.LogInformation("Backup 3.0→3.1: classified {Rows} detection rows, added {Added} decision rows", rows.Count, added.Count);
    }

    private static void RemoveRetiredTables(SystemBackup backup)
    {
        foreach (var table in RetiredTables)
        {
            backup.Data?.Remove(table);
            backup.Metadata.Tables?.Remove(table);
        }
    }

    private static int SourceOf(JsonObject row)
    {
        var detectionSource = row["detection_source"]?.GetValue<string>() ?? "";
        var reason = row["reason"]?.GetValue<string>() ?? "";
        var chatId = row["chat_id"]!.GetValue<long>();
        return detectionSource switch
        {
            "auto" or "auto_detection" => 0,
            "file_scan" => 1,
            "tg-spam-import" => 18,
            _ when chatId == 0 => 16,
            _ when reason.StartsWith("Manually marked as spam by admin via UI", StringComparison.Ordinal) => 11,
            _ when reason.StartsWith("Manually marked as ham", StringComparison.Ordinal)
                || reason.StartsWith("Manually added as ham training sample", StringComparison.Ordinal) => 12,
            _ when reason.StartsWith("Spam detected via /spam", StringComparison.Ordinal) => 13,
            _ when reason.StartsWith("Report #", StringComparison.Ordinal) => 14,
            _ => 99
        };
    }

    private static int ClassificationOf(JsonObject row, int source, IReadOnlyDictionary<long, double> thresholds)
    {
        var netScore = row["net_score"]?.GetValue<double>() ?? 0;
        switch (source)
        {
            case 1: return netScore > 0 ? 4 : 5;
            case 18:
                var reason = row["reason"]?.GetValue<string>() ?? "";
                return reason.Contains("label - ham", StringComparison.OrdinalIgnoreCase) ? 1
                    : reason.Contains("label - spam", StringComparison.OrdinalIgnoreCase) ? 0
                    : netScore > 0 ? 0 : 1;
            case 10 or 11 or 13 or 14: return 0;
            case 12: return 1;
            case not 0: return netScore > 0 ? 0 : 1;
        }

        var checks = ParseChecks(row["check_results_json"]?.GetValue<string>());
        var ai = checks.FirstOrDefault(c => c.CheckName == 6);
        var aiVeto = ai is { Abstained: false, Score: 0 };
        var hardBlock = checks.Count == 1 && checks[0] is { CheckName: 8, Score: >= 5 };
        var chatId = row["chat_id"]!.GetValue<long>();
        var threshold = thresholds.GetValueOrDefault(chatId, thresholds.GetValueOrDefault(0, 2.5));
        var isSpam = !aiVeto && (hardBlock || netScore >= threshold);
        var trained = row["used_for_training"]?.GetValue<bool>() == true;

        if (isSpam) return trained ? 2 : 4;
        return ai is { Abstained: false, Score: > 0 } ? 5 : 3;
    }

    private sealed record Check(int CheckName, double Score, bool Abstained);

    private static List<Check> ParseChecks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Checks", out var checks) || checks.ValueKind != JsonValueKind.Array) return [];
        return [.. checks.EnumerateArray().Select(c => new Check(
            c.GetProperty("CheckName").GetInt32(),
            c.TryGetProperty("Score", out var s) ? s.GetDouble() : 0,
            c.TryGetProperty("Abstained", out var a) && a.GetBoolean()))];
    }

    private static Dictionary<long, double> ReadReviewThresholds(SystemBackup backup)
    {
        var result = new Dictionary<long, double>();
        if (backup.Data is null || !backup.Data.TryGetValue("content_detection_configs", out var configs)) return result;
        foreach (var config in configs.Cast<JsonElement>())
        {
            if (!config.TryGetProperty("chat_id", out var chat) || chat.ValueKind != JsonValueKind.Number) continue;
            if (!config.TryGetProperty("config_json", out var json) || json.ValueKind != JsonValueKind.String) continue;
            using var doc = JsonDocument.Parse(json.GetString()!);
            if (doc.RootElement.TryGetProperty("ReviewQueueThreshold", out var rq) && rq.ValueKind == JsonValueKind.Number)
                result[chat.GetInt64()] = rq.GetDouble();
        }
        return result;
    }
}
```

(Before finalizing, inspect a real 3.0 backup's JSON shape for `check_results_json`/`config_json` from `BackupServiceTests` fixtures, a string vs an embedded object. If they are embedded objects, read them with `GetRawText()` instead of `GetString()`. Adjust only that access, not the rules.)

In `BackupService`: `CurrentVersion = "3.1"`. In `ApplyBackupMigrations`, after the SCHEMA-3 block:

```csharp
        // Migration: v3.0 → v3.1 (single spam verdict). Backup-only and time-boxed: remove one year
        // after the release that introduced backup format 3.1.
        if (string.Compare(backupVersion, "3.1", StringComparison.Ordinal) < 0)
        {
            _logger.LogInformation("Applying verdict-events migration (backup v{Version} < 3.1)", backupVersion);
            Backup30To31VerdictMigration.Apply(backup, _logger);
        }
```

Set the `BackupMetadata.Version` default to `"3.1"`.

- [ ] **Step 4: Run and verify**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~Backup"` and `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~Backup"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
feat(backup): format 3.1 restores 3.0 backups into verdict events

A backup-only, time-boxed 3.0→3.1 step classifies legacy detection rows,
folds training_labels into decisions and drops the retired tables before
restore.

Refs #547

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 18: Final verification and docs

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (final counts; recipes)
- Modify: `docs/superpowers/specs/2026-09-27-single-spam-verdict-design.md` (append a "Delivered" note listing the four plan deviations)

- [ ] **Step 1: Full build and tests**

Run: `dotnet build` (expect 0 warnings); then in the background: `dotnet test` for the whole solution.
Expected: every project PASSES. Report failures verbatim. Don't paper over them.

- [ ] **Step 2: Slopwatch**

Run: `dotnet slopwatch analyze` (the repo's configured tool; see `.config/dotnet-tools.json`).
Expected: no new findings. Fix any real issue rather than suppressing it.

- [ ] **Step 3: Leftover sweep**

Run:

```bash
git grep -n "training_labels\|used_for_training\|net_score\|detection_source\|IsSpam = \(true\|false\)\|InsertAsync(detection\|HasSimilarTrainingHash\|ExcludeFromTrainingAsync\|InvalidateTrainingData" \
  -- ':!*/Migrations/*' ':!docs/*' ':!*/TestData/SQL/tools/*' ':!*Backup30To31VerdictMigration*'
```

Expected: no output (`IsSpam = …` on `ContentDetectionResult` in tests is fine; review each hit).

- [ ] **Step 4: Docs**

Append to the spec a `## Delivered` section listing the four deviations from this plan's header, one line each. Confirm `IntegrationTests/CLAUDE.md` reflects the final canonical file list and the "Verdict events" recipe.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
docs: record delivered verdict design and canonical changes

Refs #547

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```
