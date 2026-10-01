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
        await _helper.CreateDatabaseFromEmptyTemplateAsync();
        await using var ctx = _helper.GetDbContext();
        await GoldenDataset.LoadCanonicalAsync(ctx, PostgresFixture.SharedDataProtectionProvider);
    }

    [TearDown]
    public void TearDown() => _helper?.Dispose();

    // Helper: midnight UTC today, matching PostgreSQL's date_trunc('day', NOW()) anchor.
    private static DateTimeOffset MidnightTodayUtc =>
        new(DateTime.UtcNow.Date, TimeSpan.Zero);

    [Test]
    public async Task ShiftDetectionResultTimestamps_AnchorsToMidnightTodayPlusOffset()
    {
        // dr_id 2952 is a known auto-spam row on canonical msg 220017 (MainChat).
        const long DrId = 2952;
        var offset = TimeSpan.FromHours(1);  // today at 01:00 UTC

        await using var ctx = _helper!.GetDbContext();
        var before = await ctx.DetectionResults.Where(d => d.Id == DrId).Select(d => d.DetectedAt).SingleAsync();

        await GoldenDataset.Mutate(ctx)
            .ShiftDetectionResultTimestamps(new[] { new TimestampShift(DrId, offset) })
            .ApplyAsync();

        var after = await ctx.DetectionResults.Where(d => d.Id == DrId).Select(d => d.DetectedAt).SingleAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Is.Not.EqualTo(before), "mutator must change the timestamp");
            var expected = MidnightTodayUtc + offset;
            Assert.That(after, Is.EqualTo(expected).Within(TimeSpan.FromSeconds(2)),
                "shifted timestamp must equal midnight-today + offset within clock-drift tolerance");
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
            .ShiftDetectionResultTimestamps(new[] { new TimestampShift(ShiftedId, TimeSpan.FromHours(1)) })
            .ApplyAsync();

        var untouchedAfter = await ctx.DetectionResults.Where(d => d.Id == UntouchedId).Select(d => d.DetectedAt).SingleAsync();
        Assert.That(untouchedAfter, Is.EqualTo(untouchedBefore));
    }

    [Test]
    public async Task ShiftWelcomeResponseTimestamps_SetsRespondedAtAndCreatedAt()
    {
        // canonical welcome_response 73 is an Accepted row in MainChat.
        const long WrId = 73;
        var offset = TimeSpan.FromHours(12);  // today at noon UTC

        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Mutate(ctx)
            .ShiftWelcomeResponseTimestamps(new[] { new TimestampShift(WrId, offset) })
            .ApplyAsync();

        var row = await ctx.WelcomeResponses.Where(w => w.Id == WrId)
            .Select(w => new { w.RespondedAt, w.CreatedAt })
            .SingleAsync();

        using (Assert.EnterMultipleScope())
        {
            var expected = MidnightTodayUtc + offset;
            Assert.That(row.RespondedAt, Is.EqualTo(expected).Within(TimeSpan.FromSeconds(2)),
                "responded_at should equal midnight-today + offset");

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
        var offset = TimeSpan.FromHours(1);

        await GoldenDataset.Mutate(ctx)
            .ShiftDetectionResultTimestamps(new[] { new TimestampShift(2952, offset) })
            .ShiftWelcomeResponseTimestamps(new[] { new TimestampShift(73, offset) })
            .ApplyAsync();

        var expected = MidnightTodayUtc + offset;
        var drAfter = await ctx.DetectionResults.Where(d => d.Id == 2952).Select(d => d.DetectedAt).SingleAsync();
        var wrAfter = await ctx.WelcomeResponses.Where(w => w.Id == 73).Select(w => w.RespondedAt).SingleAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(drAfter, Is.EqualTo(expected).Within(TimeSpan.FromSeconds(2)));
            Assert.That(wrAfter, Is.EqualTo(expected).Within(TimeSpan.FromSeconds(2)));
        }
    }

    [Test]
    public async Task ShiftMessageTimestamps_AnchorsToMidnightTodayPlusOffset()
    {
        // Canonical msg 212340 in MainChat (-100026957614982) — bare orphan with attached edit.
        const long ChatId = -100026957614982L;
        const int MsgId = 212340;
        var offset = TimeSpan.FromDays(-45);  // 45 days before midnight today

        await using var ctx = _helper!.GetDbContext();
        var before = await ctx.Messages.Where(m => m.MessageId == MsgId && m.ChatId == ChatId)
            .Select(m => m.Timestamp).SingleAsync();

        await GoldenDataset.Mutate(ctx)
            .ShiftMessageTimestamps(ChatId, new[] { new TimestampShift(MsgId, offset) })
            .ApplyAsync();

        var after = await ctx.Messages.Where(m => m.MessageId == MsgId && m.ChatId == ChatId)
            .Select(m => m.Timestamp).SingleAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Is.Not.EqualTo(before), "mutator must change the timestamp");
            var expected = MidnightTodayUtc + offset;
            Assert.That(after, Is.EqualTo(expected).Within(TimeSpan.FromSeconds(2)),
                "shifted timestamp must equal midnight-today + offset within clock-drift tolerance");
        }
    }

    [Test]
    public async Task LockWebUser_SetsLockedUntilInTheFutureAndTheFailedAttemptCount()
    {
        var lockFor = TimeSpan.FromMinutes(30);

        await using var ctx = _helper!.GetDbContext();
        var before = await ctx.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.NoTotpAdminId)
            .Select(u => new { u.LockedUntil, u.FailedLoginAttempts }).SingleAsync();
        Assert.That(before.LockedUntil, Is.Null, "canonical anchor must start unlocked");

        var appliedAt = DateTimeOffset.UtcNow;
        await GoldenDataset.Mutate(ctx)
            .LockWebUser(GoldenDatasetConstants.WebUsers.NoTotpAdminId, lockFor)
            .ApplyAsync();

        var after = await ctx.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.NoTotpAdminId)
            .Select(u => new { u.LockedUntil, u.FailedLoginAttempts }).SingleAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.LockedUntil, Is.Not.Null);
            Assert.That(after.LockedUntil!.Value, Is.EqualTo(appliedAt + lockFor).Within(TimeSpan.FromSeconds(5)),
                "locked_until must be NOW() + lockFor");
            Assert.That(after.FailedLoginAttempts, Is.EqualTo(GoldenMutatePlanBuilder.LockedFailedLoginAttempts));
        }
    }

    [Test]
    public async Task LockWebUser_DoesNotTouchOtherUsers()
    {
        await using var ctx = _helper!.GetDbContext();

        await GoldenDataset.Mutate(ctx)
            .LockWebUser(GoldenDatasetConstants.WebUsers.NoTotpAdminId, TimeSpan.FromMinutes(30))
            .ApplyAsync();

        var lockedOthers = await ctx.Users.AsNoTracking()
            .CountAsync(u => u.Id != GoldenDatasetConstants.WebUsers.NoTotpAdminId
                             && (u.LockedUntil != null || u.FailedLoginAttempts != 0));
        Assert.That(lockedOthers, Is.Zero);
    }

    [Test]
    public void LockWebUser_RejectsANonPositiveDuration()
    {
        using var ctx = _helper!.GetDbContext();
        Assert.That(() => GoldenDataset.Mutate(ctx).LockWebUser(GoldenDatasetConstants.WebUsers.NoTotpAdminId, TimeSpan.Zero),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public async Task LockWebUser_FailsLoudlyForAnUnknownUser()
    {
        await using var ctx = _helper!.GetDbContext();
        var plan = GoldenDataset.Mutate(ctx).LockWebUser("00000000-0000-0000-0000-000000000000", TimeSpan.FromMinutes(1));

        Assert.That(async () => await plan.ApplyAsync(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ShiftMessageTimestamps_DoesNotTouchUnshiftedRows()
    {
        const long ChatId = -100026957614982L;
        const int ShiftedMsgId = 212340;
        const int UntouchedMsgId = 218579;

        await using var ctx = _helper!.GetDbContext();
        var untouchedBefore = await ctx.Messages.Where(m => m.MessageId == UntouchedMsgId && m.ChatId == ChatId)
            .Select(m => m.Timestamp).SingleAsync();

        await GoldenDataset.Mutate(ctx)
            .ShiftMessageTimestamps(ChatId, new[] { new TimestampShift(ShiftedMsgId, TimeSpan.FromDays(-45)) })
            .ApplyAsync();

        var untouchedAfter = await ctx.Messages.Where(m => m.MessageId == UntouchedMsgId && m.ChatId == ChatId)
            .Select(m => m.Timestamp).SingleAsync();
        Assert.That(untouchedAfter, Is.EqualTo(untouchedBefore));
    }
}
