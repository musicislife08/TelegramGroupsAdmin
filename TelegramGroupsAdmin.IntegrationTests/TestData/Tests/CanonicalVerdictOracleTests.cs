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
    private static readonly VerdictSource[] ChatZeroSources =
        [VerdictSource.TrainingDataPage, VerdictSource.TrainingExclude, VerdictSource.Import];

    private static readonly VerdictClassification[] ChatZeroClassifications =
    [
        VerdictClassification.ExplicitSpam, VerdictClassification.ExplicitHam,
        VerdictClassification.UntrainedSpam, VerdictClassification.UntrainedHam
    ];

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
            .Select(s => $"{s.ChatId}/{s.MessageId}: slice {s.Slice}, view {verdicts.GetValueOrDefault((s.ChatId, s.MessageId), VerdictClassification.Unscanned)}")
            .ToList();

        Assert.That(mismatches, Is.Empty);
    }

    /// <summary>
    /// Chat-0 training samples (Training Data page, tg-spam import) are explicit decisions in the new
    /// model, so the old-slice oracle excludes them; they are checked here instead.
    /// </summary>
    [Test]
    public async Task EveryChatZeroSample_ResolvesToATrainingPageOrImportVerdict()
    {
        await using var ctx = _helper.GetDbContext();
        var chatZeroMessageIds = await ctx.DetectionResults.AsNoTracking()
            .Where(d => d.ChatId == 0)
            .Select(d => d.MessageId)
            .Distinct()
            .ToListAsync();

        var verdicts = await ctx.MessageVerdicts.AsNoTracking()
            .Where(v => v.ChatId == 0 && chatZeroMessageIds.Contains(v.MessageId))
            .ToListAsync();

        var wrong = verdicts
            .Where(v => v.Source is not { } source
                        || !ChatZeroSources.Contains((VerdictSource)source)
                        || !ChatZeroClassifications.Contains((VerdictClassification)v.Classification))
            .Select(v => $"0/{v.MessageId}: source {v.Source}, classification {(VerdictClassification)v.Classification}")
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chatZeroMessageIds, Is.Not.Empty, "canonical should carry chat-0 training samples");
            Assert.That(verdicts, Has.Count.EqualTo(chatZeroMessageIds.Count));
            Assert.That(wrong, Is.Empty);
        }
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
