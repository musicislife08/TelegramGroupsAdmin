using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.Fixtures;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

[TestFixture]
public class GoldenMutatePlanTests
{
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

    [Test]
    public async Task ShiftDetectionResultTimestamps_SetsDetectedAtRelativeToNow()
    {
        // dr_id 2952 is a known auto-spam row on canonical msg 220017 (MainChat).
        const long DrId = 2952;
        await using var ctx = _helper!.GetDbContext();

        var before = await ctx.DetectionResults.Where(d => d.Id == DrId).Select(d => d.DetectedAt).SingleAsync();

        await GoldenDataset.Mutate(ctx)
            .ShiftDetectionResultTimestamps(new[] { new TimestampShift(DrId, TimeSpan.FromSeconds(-3)) })
            .ApplyAsync();

        var after = await ctx.DetectionResults.Where(d => d.Id == DrId).Select(d => d.DetectedAt).SingleAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Is.Not.EqualTo(before), "mutator must change the timestamp");
            // 3 seconds before NOW() — server-side NOW() drifts slightly during round-trip, so allow ±2s slop.
            var delta = (DateTimeOffset.UtcNow - after).TotalSeconds;
            Assert.That(delta, Is.InRange(1.0, 5.0),
                $"shifted timestamp should be ~3 seconds before NOW(); actual delta {delta:F2}s");
        }
    }

    [Test]
    public async Task ShiftDetectionResultTimestamps_DoesNotTouchUnshiftedRows()
    {
        await using var ctx = _helper!.GetDbContext();
        const long ShiftedId = 2952;
        const long UntouchedId = 2955;

        var untouchedBefore = await ctx.DetectionResults.Where(d => d.Id == UntouchedId).Select(d => d.DetectedAt).SingleAsync();

        await GoldenDataset.Mutate(ctx)
            .ShiftDetectionResultTimestamps(new[] { new TimestampShift(ShiftedId, TimeSpan.FromSeconds(-1)) })
            .ApplyAsync();

        var untouchedAfter = await ctx.DetectionResults.Where(d => d.Id == UntouchedId).Select(d => d.DetectedAt).SingleAsync();
        Assert.That(untouchedAfter, Is.EqualTo(untouchedBefore));
    }

    [Test]
    public async Task ShiftWelcomeResponseTimestamps_SetsRespondedAtAndCreatedAt()
    {
        // canonical welcome_response 73 is an Accepted row in MainChat.
        const long WrId = 73;
        await using var ctx = _helper!.GetDbContext();

        await GoldenDataset.Mutate(ctx)
            .ShiftWelcomeResponseTimestamps(new[] { new TimestampShift(WrId, TimeSpan.FromSeconds(-2)) })
            .ApplyAsync();

        var row = await ctx.WelcomeResponses.Where(w => w.Id == WrId)
            .Select(w => new { w.RespondedAt, w.CreatedAt })
            .SingleAsync();

        using (Assert.EnterMultipleScope())
        {
            var respondedDelta = (DateTimeOffset.UtcNow - row.RespondedAt).TotalSeconds;
            Assert.That(respondedDelta, Is.InRange(0.0, 4.0),
                $"responded_at should be ~2 seconds before NOW(); actual {respondedDelta:F2}s");

            // created_at is responded_at - 1 minute by the mutator's convention.
            var createdDelta = (row.RespondedAt - row.CreatedAt).TotalSeconds;
            Assert.That(createdDelta, Is.InRange(59.0, 61.0),
                $"created_at should be ~60s before responded_at; actual delta {createdDelta:F2}s");
        }
    }

    [Test]
    public async Task ApplyAsync_ChainsBothShiftVerbsInOneCall()
    {
        await using var ctx = _helper!.GetDbContext();

        await GoldenDataset.Mutate(ctx)
            .ShiftDetectionResultTimestamps(new[] { new TimestampShift(2952, TimeSpan.FromSeconds(-1)) })
            .ShiftWelcomeResponseTimestamps(new[] { new TimestampShift(73, TimeSpan.FromSeconds(-1)) })
            .ApplyAsync();

        var drDelta = (DateTimeOffset.UtcNow - await ctx.DetectionResults
            .Where(d => d.Id == 2952).Select(d => d.DetectedAt).SingleAsync()).TotalSeconds;
        var wrDelta = (DateTimeOffset.UtcNow - await ctx.WelcomeResponses
            .Where(w => w.Id == 73).Select(w => w.RespondedAt).SingleAsync()).TotalSeconds;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(drDelta, Is.InRange(0.0, 3.0));
            Assert.That(wrDelta, Is.InRange(0.0, 3.0));
        }
    }
}
