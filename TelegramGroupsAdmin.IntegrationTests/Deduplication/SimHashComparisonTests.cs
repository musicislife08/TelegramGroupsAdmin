using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Deduplication;

/// <summary>
/// Integration tests for SimHash-based training data deduplication.
/// Tests PostgreSQL bit_count() queries and near-duplicate detection accuracy.
/// </summary>
[TestFixture]
public class SimHashIntegrationTests
{
    private MigrationTestHelper _testHelper = null!;
    private SimHashService _simHashService = null!;

    [SetUp]
    public Task Setup()
    {
        _testHelper = new MigrationTestHelper();
        _simHashService = new SimHashService();
        return Task.CompletedTask;
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper.Dispose();
    }

    #region PostgreSQL Query Tests

    [Test]
    public async Task PostgreSQL_BitCount_HammingDistance_Query()
    {
        // Canonical anchor: message 212355 (banned-user investment-platform spam:
        // "Hello I have a good platform that you can earn from daily..."). The
        // test query is a one-word variant ("good" → "great"), giving a verified
        // SimHash Hamming distance of 8 — well within the ≤15 near-duplicate threshold.
        const int AnchorMessageId = 212355;

        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
        await using var context = _testHelper.GetDbContext();

        // Near-duplicate query: one-word variant of msg 212355's text.
        var queryText = "Hello I have a great platform that you can earn from daily they offer 2.0% hourly you can withdraw your profit daily you can also withdraw your capital at the end of 5days if you are interested inbox me privately for the platform link";
        var queryHash = _simHashService.ComputeHash(queryText);

        // Act: Query using PostgreSQL bit_count() for Hamming distance
        var rawResults = await context.Database
            .SqlQuery<HammingResult>($"""
                SELECT
                    message_id as MessageId,
                    message_text as MessageText,
                    bit_count((similarity_hash # {queryHash})::bit(64))::int as HammingDistance
                FROM messages
                WHERE similarity_hash IS NOT NULL
                ORDER BY bit_count((similarity_hash # {queryHash})::bit(64))
                LIMIT 5
                """)
            .ToListAsync();

        // Assert
        TestContext.Out.WriteLine($"Top 5 similar messages to: \"{queryText[..60]}...\"");
        foreach (var r in rawResults)
        {
            TestContext.Out.WriteLine($"  [{r.HammingDistance} bits] Msg {r.MessageId}: {r.MessageText?[..Math.Min(60, r.MessageText.Length)]}...");
        }

        Assert.That(rawResults, Has.Count.GreaterThan(0), "Should find similar messages");
        Assert.That(rawResults[0].MessageId, Is.EqualTo(AnchorMessageId),
            "Closest match should be the anchor (one-word variant of the query)");
        Assert.That(rawResults[0].HammingDistance, Is.LessThanOrEqualTo(15),
            "Closest match should be a near-duplicate spam message");
    }

    [Test]
    public async Task SimHash_Deterministic_AcrossContexts()
    {
        // Canonical anchor: message_id 4575 (sample spam-labeled message in
        // -100048429560480). Test cares only about hash round-trip persistence,
        // not the message's content — any canonical msg_id works.
        const int AnchorMessageId = 4575;

        // Arrange: Compute hash, save to DB, read back
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
        await using var context = _testHelper.GetDbContext();

        var testText = "Join our telegram channel for exclusive crypto signals";
        var computedHash = _simHashService.ComputeHash(testText);

        // Save to a message
        var message = await context.Messages.FirstAsync(m => m.MessageId == AnchorMessageId);
        message.SimilarityHash = computedHash;
        await context.SaveChangesAsync();

        // Act: Read back in new context
        await using var context2 = _testHelper.GetDbContext();
        var savedHash = await context2.Messages
            .Where(m => m.MessageId == AnchorMessageId)
            .Select(m => m.SimilarityHash)
            .FirstAsync();

        var recomputedHash = _simHashService.ComputeHash(testText);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedHash, Is.EqualTo(computedHash), "Hash should survive DB round-trip");
            Assert.That(recomputedHash, Is.EqualTo(computedHash), "Hash should be deterministic");
        }
    }

    #endregion

    #region Near-Duplicate Detection Tests

    [Test]
    public async Task SimHash_DetectsNearDuplicates_InRecruitmentSpamCluster()
    {
        // Canonical anchor cluster: 4 banned-user recruitment-spam variants from a
        // single spam campaign (MainChat -100026957614982), preserved verbatim from
        // dev DB. All pairwise SimHash Hamming distances ≤ 10 — a real near-duplicate
        // cluster from production spam, not synthetic test fixtures.
        int[] cluster = { 220848, 221429, 221904, 222949 };

        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
        await using var context = _testHelper.GetDbContext();

        var messages = await context.Messages
            .Where(m => cluster.Contains(m.MessageId))
            .Select(m => new { m.MessageId, m.MessageText })
            .ToListAsync();

        Assert.That(messages, Has.Count.EqualTo(cluster.Length), "All cluster anchors should be in canonical");

        // Compute SimHashes from message text (test verifies the algorithm itself —
        // do NOT rely on canonical's pre-baked similarity_hash column).
        var hashes = messages.ToDictionary(
            m => m.MessageId,
            m => _simHashService.ComputeHash(m.MessageText));

        // Assert all pairwise intra-cluster distances are within near-duplicate threshold
        var maxDistance = 0;
        for (int i = 0; i < cluster.Length; i++)
        {
            for (int j = i + 1; j < cluster.Length; j++)
            {
                var d = _simHashService.HammingDistance(hashes[cluster[i]], hashes[cluster[j]]);
                TestContext.Out.WriteLine($"  msg {cluster[i]} <-> msg {cluster[j]}: {d} bits");
                maxDistance = Math.Max(maxDistance, d);
            }
        }

        Assert.That(maxDistance, Is.LessThanOrEqualTo(15),
            "All cluster members should be SimHash near-duplicates of each other (max distance must be ≤ 15)");
    }

    [Test]
    public async Task SimHash_DistinguishesDifferentGroups_InDedupTestData()
    {
        // Canonical anchors: 4 banned-user spam messages from 4 distinct topical
        // categories. All in MainChat (-100026957614982). Pairwise Hamming
        // distances verified ≥ 30 (probed via dev-DB bit_count, see commit notes).
        const int PharmaSpamId = 20849;     // Ivermectin/Hydroxychloroquine
        const int ShopifySpamId = 4666;     // Shopify & Payment Gateway services
        const int WormGptSpamId = 14538;    // WORMGPT hacker-tool spam
        const int InvestmentSpamId = 221139; // Connectedpip Trading Platform

        // Arrange: clone canonical template
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
        await using var context = _testHelper.GetDbContext();

        // Get representative messages from different topics
        var pharmaSpam = await context.Messages.Where(m => m.MessageId == PharmaSpamId).Select(m => m.MessageText).FirstAsync();
        var shopifySpam = await context.Messages.Where(m => m.MessageId == ShopifySpamId).Select(m => m.MessageText).FirstAsync();
        var wormGptSpam = await context.Messages.Where(m => m.MessageId == WormGptSpamId).Select(m => m.MessageText).FirstAsync();
        var investmentSpam = await context.Messages.Where(m => m.MessageId == InvestmentSpamId).Select(m => m.MessageText).FirstAsync();

        // Act: Compute cross-topic distances
        var pharmaHash = _simHashService.ComputeHash(pharmaSpam);
        var shopifyHash = _simHashService.ComputeHash(shopifySpam);
        var wormGptHash = _simHashService.ComputeHash(wormGptSpam);
        var investmentHash = _simHashService.ComputeHash(investmentSpam);

        var pharmaVsShopify = _simHashService.HammingDistance(pharmaHash, shopifyHash);
        var pharmaVsWormGpt = _simHashService.HammingDistance(pharmaHash, wormGptHash);
        var pharmaVsInvestment = _simHashService.HammingDistance(pharmaHash, investmentHash);
        var shopifyVsInvestment = _simHashService.HammingDistance(shopifyHash, investmentHash);

        // Assert: Different topics should have high Hamming distance
        TestContext.Out.WriteLine($"Pharma vs Shopify:    {pharmaVsShopify}");
        TestContext.Out.WriteLine($"Pharma vs WormGpt:    {pharmaVsWormGpt}");
        TestContext.Out.WriteLine($"Pharma vs Investment: {pharmaVsInvestment}");
        TestContext.Out.WriteLine($"Shopify vs Investment: {shopifyVsInvestment}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pharmaVsShopify, Is.GreaterThan(15), "Different spam topics should be distinguishable");
            Assert.That(pharmaVsInvestment, Is.GreaterThan(20), "Distinct spam topics should have high distance");
            Assert.That(shopifyVsInvestment, Is.GreaterThan(20), "Distinct spam topics should have high distance");
        }
    }

    #endregion

    /// <summary>
    /// DTO for raw SQL Hamming distance query results
    /// </summary>
    private record HammingResult(int MessageId, string? MessageText, int HammingDistance);
}
