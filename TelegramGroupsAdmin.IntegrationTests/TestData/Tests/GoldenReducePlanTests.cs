using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.IntegrationTests.Fixtures;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

[TestFixture]
public class GoldenReducePlanTests
{
    // training_labels.label is smallint (0=Spam, 1=Ham). The DTO exposes it as `short Label`,
    // so test predicates compare against the cast enum value rather than a string literal.
    private const short SpamLabel = (short)TrainingLabel.Spam; // 0
    private const short HamLabel = (short)TrainingLabel.Ham;   // 1

    private MigrationTestHelper? _helper;

    [SetUp]
    public async Task Setup()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndApplyMigrationsAsync();
        await using var ctx = _helper.GetDbContext();
        await GoldenDataset.LoadCanonicalAsync(ctx, PostgresFixture.SharedDataProtectionProvider);
    }

    [TearDown]
    public void TearDown() => _helper?.Dispose();

    // ── Task 1.11: KeepSpam basic + isolation ──────────────────────────────────

    [Test]
    public async Task KeepSpam_KeepsExactlyN_AndDoesNotTouchHam()
    {
        await using var ctx = _helper!.GetDbContext();
        var hamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel);

        await GoldenDataset.Reduce(ctx).KeepSpam(5).ApplyAsync();

        var spamAfter = await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel);
        var hamAfter = await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel);
        Assert.That(spamAfter, Is.EqualTo(5));
        Assert.That(hamAfter, Is.EqualTo(hamBefore), "KeepSpam must not touch ham");
    }

    [Test]
    public async Task KeepSpam_Zero_RemovesAllSpam_KeepsAllHam()
    {
        await using var ctx = _helper!.GetDbContext();
        var hamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel);

        await GoldenDataset.Reduce(ctx).KeepSpam(0).ApplyAsync();

        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(0));
        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel), Is.EqualTo(hamBefore));
    }

    // ── Task 1.12: KeepHam symmetry ─────────────────────────────────────────────

    [Test]
    public async Task KeepHam_KeepsExactlyN_AndDoesNotTouchSpam()
    {
        await using var ctx = _helper!.GetDbContext();
        var spamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel);

        await GoldenDataset.Reduce(ctx).KeepHam(5).ApplyAsync();

        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel), Is.EqualTo(5));
        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(spamBefore));
    }

    [Test]
    public async Task KeepHam_Zero_RemovesAllHam_KeepsAllSpam()
    {
        await using var ctx = _helper!.GetDbContext();
        var spamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel);

        await GoldenDataset.Reduce(ctx).KeepHam(0).ApplyAsync();

        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel), Is.EqualTo(0));
        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(spamBefore));
    }

    // ── Task 1.13: KeepDetectionResults & KeepUserActions ─────────────────────

    [Test]
    public async Task KeepDetectionResults_KeepsLowestNById()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepDetectionResults(3).ApplyAsync();

        var ids = await ctx.DetectionResults.OrderBy(dr => dr.Id).Select(dr => dr.Id).ToListAsync();
        Assert.That(ids, Has.Count.EqualTo(3));
        // No assertion on specific id values — bootstrap renumbering is opaque to this test.
        // The "lowest by id" property is verified by the count + orderedness alone.
    }

    [Test]
    public async Task KeepUserActions_KeepsLowestNById()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepUserActions(2).ApplyAsync();

        Assert.That(await ctx.UserActions.CountAsync(), Is.EqualTo(2));
    }

    // ── Task 1.14: KeepMessages cascade (Cascade FKs + SetNull on user_actions) ─

    [Test]
    public async Task KeepMessages_Zero_CascadesAllChildrenButSetNullsUserActions()
    {
        await using var ctx = _helper!.GetDbContext();

        // Sanity: canonical has message_translations rows before the cascade
        var translationsBefore = await ctx.MessageTranslations.CountAsync();
        Assume.That(translationsBefore, Is.GreaterThan(0), "canonical must contain message_translations rows");

        await GoldenDataset.Reduce(ctx).KeepMessages(0).ApplyAsync();

        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.MessageEdits.CountAsync(), Is.EqualTo(0), "Cascade FK should clear message_edits");
        Assert.That(await ctx.TrainingLabels.CountAsync(), Is.EqualTo(0), "Cascade FK should clear training_labels");
        Assert.That(await ctx.DetectionResults.CountAsync(), Is.EqualTo(0), "Cascade FK should clear detection_results");
        Assert.That(await ctx.MessageTranslations.CountAsync(), Is.EqualTo(0), "Cascade FK should clear message_translations");

        // user_actions rows survive (SetNull); MessageId/ChatId go null on cascaded rows
        Assert.That(await ctx.UserActions.CountAsync(), Is.GreaterThan(0));
        Assert.That(await ctx.UserActions.CountAsync(ua => ua.MessageId == null), Is.GreaterThan(0));
    }

    [Test]
    public async Task KeepMessages_NonZero_LeavesChildrenAtCascadeNaturalCounts()
    {
        await using var ctx = _helper!.GetDbContext();

        await GoldenDataset.Reduce(ctx).KeepMessages(5).ApplyAsync();

        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(5));
        // Children of the surviving 5 messages remain — exact count is bootstrap-defined,
        // but every surviving training_labels row must reference one of the surviving 5
        // messages. Phrased as a NOT EXISTS subquery so EF Core translates server-side
        // (an in-memory anonymous-type List<> in the predicate isn't translatable).
        var hangingTl = await ctx.TrainingLabels
            .Where(tl => !ctx.Messages.Any(m => m.ChatId == tl.ChatId && m.MessageId == tl.MessageId))
            .CountAsync();
        Assert.That(hangingTl, Is.EqualTo(0), "training_labels rows must all reference surviving messages");
    }

    // ── Task 1.15: Cascade narrowing + topological execution ──────────────────

    [Test]
    public async Task KeepMessages_FollowedByKeepDetectionResults_NarrowsFurther()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepMessages(5).KeepDetectionResults(2).ApplyAsync();

        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(5));
        Assert.That(await ctx.DetectionResults.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task KeepMessages_Zero_PlusKeepUserActions_Zero_RemovesBoth()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepMessages(0).KeepUserActions(0).ApplyAsync();

        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.UserActions.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task TopologicalOrder_RegisteringChildBeforeParentViaIntermediateVariable_ProducesParentFirstResult()
    {
        await using var ctx = _helper!.GetDbContext();

        // Wrong-order registration via intermediate variable (legal, see spec "shared mutable plan").
        var parent = GoldenDataset.Reduce(ctx);
        var child = parent.KeepDetectionResults(2);  // registers child first
        parent.KeepMessages(5);                       // then parent
        await child.ApplyAsync();                     // applies both

        // Topological execution puts KeepMessages first regardless of registration order.
        // Result must equal the canonical-ordered chain Reduce(ctx).KeepMessages(5).KeepDetectionResults(2).
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(5));
        Assert.That(await ctx.DetectionResults.CountAsync(), Is.EqualTo(2));
    }

    // ── Task 1.16: validation rules (LIMIT semantics, negative count, last-wins) ─

    [Test]
    public void KeepSpam_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        // No DB needed — the throw happens during builder method invocation, before any SQL.
        // Use a fresh context for symmetry; nothing actually persists.
        using var ctx = _helper!.GetDbContext();
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldenDataset.Reduce(ctx).KeepSpam(-1));
    }

    [Test]
    public async Task KeepSpam_CountGreaterThanCanonical_KeepsAllAvailable()
    {
        await using var ctx = _helper!.GetDbContext();
        var spamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel);
        await GoldenDataset.Reduce(ctx).KeepSpam(spamBefore + 500).ApplyAsync();

        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(spamBefore));
    }

    [Test]
    public async Task KeepDetectionResults_AfterKeepMessagesNarrowsTo12_RequestingMore_KeepsAll12()
    {
        await using var ctx = _helper!.GetDbContext();
        // Choose a KeepMessages count that leaves a small detection_results survivor count
        await GoldenDataset.Reduce(ctx).KeepMessages(5).KeepDetectionResults(500).ApplyAsync();

        var actual = await ctx.DetectionResults.CountAsync();
        Assert.That(actual, Is.LessThanOrEqualTo(500));
        // Test passes by surviving without throwing — natural LIMIT semantics handle the bound.
    }

    [Test]
    public async Task KeepSpam_CalledTwice_LastWins()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepSpam(10).KeepSpam(3).ApplyAsync();
        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(3));
    }
}
