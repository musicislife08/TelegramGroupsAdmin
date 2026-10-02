using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.Fixtures;
using TelegramGroupsAdmin.Data.Constants;
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
    public async Task ShiftDetectionResultTimestamps_FailsLoudlyForAnUnknownId()
    {
        await using var ctx = _helper!.GetDbContext();
        var plan = GoldenDataset.Mutate(ctx)
            .ShiftDetectionResultTimestamps([new TimestampShift(long.MaxValue, TimeSpan.FromHours(1))]);

        Assert.That(async () => await plan.ApplyAsync(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ShiftWelcomeResponseTimestamps_FailsLoudlyForAnUnknownId()
    {
        await using var ctx = _helper!.GetDbContext();
        var plan = GoldenDataset.Mutate(ctx)
            .ShiftWelcomeResponseTimestamps([new TimestampShift(long.MaxValue, TimeSpan.FromHours(1))]);

        Assert.That(async () => await plan.ApplyAsync(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ShiftMessageTimestamps_FailsLoudlyForAnUnknownMessage()
    {
        await using var ctx = _helper!.GetDbContext();
        var plan = GoldenDataset.Mutate(ctx)
            .ShiftMessageTimestamps(long.MaxValue, [new TimestampShift(long.MaxValue, TimeSpan.FromHours(1))]);

        Assert.That(async () => await plan.ApplyAsync(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ExtendTelegramUserWarnings_MovesEveryWarningExpiryToNowPlusDuration()
    {
        const long UserId = GoldenDatasetConstants.UsersPage.WarnedTrustedMemberId;
        var expiresIn = TimeSpan.FromDays(30);

        await using var ctx = _helper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == UserId).Select(u => u.Warnings).SingleAsync();
        Assert.That(before, Is.Not.Null.And.Not.Empty, "canonical anchor must carry at least one warning");
        Assert.That(before!.All(w => w.ExpiresAt < DateTimeOffset.UtcNow), "canonical warnings are expired");

        var appliedAt = DateTimeOffset.UtcNow;
        await GoldenDataset.Mutate(ctx)
            .ExtendTelegramUserWarnings(UserId, expiresIn)
            .ApplyAsync();

        var after = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == UserId).Select(u => u.Warnings).SingleAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Has.Count.EqualTo(before.Count), "no warning is added or dropped");
            foreach (var (original, extended) in before.Zip(after!))
            {
                Assert.That(extended.ExpiresAt, Is.Not.Null);
                Assert.That(extended.ExpiresAt!.Value, Is.EqualTo(appliedAt + expiresIn).Within(TimeSpan.FromSeconds(5)),
                    "ExpiresAt must be NOW() + expiresIn");
                Assert.That(extended.IssuedAt, Is.EqualTo(original.IssuedAt), "IssuedAt is left alone");
                Assert.That(extended.Reason, Is.EqualTo(original.Reason), "Reason is left alone");
                Assert.That(extended.ActorType, Is.EqualTo(original.ActorType));
                Assert.That(extended.ActorId, Is.EqualTo(original.ActorId));
                Assert.That(extended.ChatId, Is.EqualTo(original.ChatId));
                Assert.That(extended.MessageId, Is.EqualTo(original.MessageId));
            }
        }
    }

    [Test]
    public async Task ExtendTelegramUserWarnings_DoesNotTouchOtherUsers()
    {
        const long Extended = GoldenDatasetConstants.UsersPage.WarnedTrustedMemberId;
        const long Untouched = GoldenDatasetConstants.UsersPage.ExpiredWarningTrustedMemberId;

        await using var ctx = _helper!.GetDbContext();
        var untouchedBefore = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == Untouched).Select(u => u.Warnings).SingleAsync();

        await GoldenDataset.Mutate(ctx)
            .ExtendTelegramUserWarnings(Extended, TimeSpan.FromDays(30))
            .ApplyAsync();

        var untouchedAfter = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == Untouched).Select(u => u.Warnings).SingleAsync();
        Assert.That(untouchedAfter!.Select(w => w.ExpiresAt), Is.EqualTo(untouchedBefore!.Select(w => w.ExpiresAt)));
    }

    [Test]
    public void ExtendTelegramUserWarnings_RejectsANonPositiveDuration()
    {
        using var ctx = _helper!.GetDbContext();
        Assert.That(
            () => GoldenDataset.Mutate(ctx).ExtendTelegramUserWarnings(GoldenDatasetConstants.UsersPage.WarnedTrustedMemberId, TimeSpan.Zero),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public async Task ExtendTelegramUserWarnings_FailsLoudlyForAUserWithoutWarnings()
    {
        await using var ctx = _helper!.GetDbContext();
        var hasWarnings = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == GoldenDatasetConstants.UsersPage.KickedJoinerId)
            .Select(u => u.Warnings != null && u.Warnings.Any()).SingleAsync();
        Assert.That(hasWarnings, Is.False, "the kicked joiner must carry no warnings for this test to mean anything");

        var plan = GoldenDataset.Mutate(ctx)
            .ExtendTelegramUserWarnings(GoldenDatasetConstants.UsersPage.KickedJoinerId, TimeSpan.FromDays(1));

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

    private static async Task<JsonObject> ReadApiKeysAsync(Data.AppDbContext ctx)
    {
        var cipher = await ctx.Configs.AsNoTracking().Where(c => c.ChatId == 0).Select(c => c.ApiKeys).SingleAsync();
        Assert.That(cipher, Is.Not.Null);
        var plaintext = PostgresFixture.SharedDataProtectionProvider
            .CreateProtector(DataProtectionPurposes.ApiKeys).Unprotect(cipher!);
        return JsonNode.Parse(plaintext)!.AsObject();
    }

    [Test]
    public async Task EnableSendGridApiKey_StoresTheKeyUnderTheApiKeysPurposeAndKeepsTheAiConnectionKeys()
    {
        const string ApiKey = "SG.canonical-mutate-test";

        await using var ctx = _helper!.GetDbContext();
        var before = await ReadApiKeysAsync(ctx);
        Assert.That(before["sendGrid"], Is.Null, "canonical carries no SendGrid key");

        await GoldenDataset.Mutate(ctx)
            .EnableSendGridApiKey(PostgresFixture.SharedDataProtectionProvider, ApiKey)
            .ApplyAsync();

        var after = await ReadApiKeysAsync(ctx);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((string?)after["sendGrid"], Is.EqualTo(ApiKey));
            Assert.That(after["aiConnectionKeys"]!.ToJsonString(), Is.EqualTo(before["aiConnectionKeys"]!.ToJsonString()),
                "the canonical AI connection keys are kept");
        }
    }

    [Test]
    public async Task EnableSendGridApiKey_DoesNotTouchTheSendGridConfigOrOtherConfigRows()
    {
        await using var ctx = _helper!.GetDbContext();
        var before = await ctx.Configs.AsNoTracking().OrderBy(c => c.ChatId)
            .Select(c => new { c.ChatId, c.SendGridConfig, c.WelcomeConfig, c.ApiKeys }).ToListAsync();

        await GoldenDataset.Mutate(ctx)
            .EnableSendGridApiKey(PostgresFixture.SharedDataProtectionProvider, "SG.canonical-mutate-test")
            .ApplyAsync();

        var after = await ctx.Configs.AsNoTracking().OrderBy(c => c.ChatId)
            .Select(c => new { c.ChatId, c.SendGridConfig, c.WelcomeConfig, c.ApiKeys }).ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.Select(c => c.SendGridConfig), Is.EqualTo(before.Select(c => c.SendGridConfig)));
            Assert.That(after.Select(c => c.WelcomeConfig), Is.EqualTo(before.Select(c => c.WelcomeConfig)));
            Assert.That(after.Where(c => c.ChatId != 0).Select(c => c.ApiKeys), Is.EqualTo(before.Where(c => c.ChatId != 0).Select(c => c.ApiKeys)));
        }
    }

    [Test]
    public void EnableSendGridApiKey_RejectsABlankKey()
    {
        using var ctx = _helper!.GetDbContext();
        Assert.That(
            () => GoldenDataset.Mutate(ctx).EnableSendGridApiKey(PostgresFixture.SharedDataProtectionProvider, " "),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task EnableSendGridApiKey_FailsLoudlyWithoutAStoredApiKeysRow()
    {
        await using var ctx = _helper!.GetDbContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE configs SET api_keys = NULL WHERE chat_id = 0");

        var plan = GoldenDataset.Mutate(ctx).EnableSendGridApiKey(PostgresFixture.SharedDataProtectionProvider, "SG.x");

        Assert.That(async () => await plan.ApplyAsync(), Throws.TypeOf<InvalidOperationException>());
    }
}
