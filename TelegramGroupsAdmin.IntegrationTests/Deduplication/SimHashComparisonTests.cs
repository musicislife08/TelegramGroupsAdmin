using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
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
    public async Task Setup()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();
        _simHashService = new SimHashService();
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
        // Arrange: Seed dataset and compute hashes
        await using var context = _testHelper.GetDbContext();
        await GoldenDataset.SeedAsync(context);

        // Compute and store hashes for test messages
        var messages = await context.Messages
            .Where(m => m.MessageText != null && m.MessageId >= 90001 && m.MessageId <= 90040)
            .ToListAsync();

        foreach (var msg in messages)
        {
            msg.SimilarityHash = _simHashService.ComputeHash(msg.MessageText);
        }
        await context.SaveChangesAsync();

        // New message to check against existing training data
        // Original msg 90001: "Earn passive income with Bitcoin! Message me for details on this amazing opportunity."
        // Near-duplicate with single word change for reliable detection
        var newSpam = "Earn passive income with Bitcoin! Message me for details on this incredible opportunity.";
        var newHash = _simHashService.ComputeHash(newSpam);

        // Act: Query using PostgreSQL bit_count() for Hamming distance
        // PostgreSQL bit_count works on bit types, so cast bigint XOR result to bit(64)
        var rawResults = await context.Database
            .SqlQuery<HammingResult>($"""
                SELECT
                    message_id as MessageId,
                    message_text as MessageText,
                    bit_count((similarity_hash # {newHash})::bit(64))::int as HammingDistance
                FROM messages
                WHERE similarity_hash IS NOT NULL
                  AND message_id >= 90001 AND message_id <= 90040
                ORDER BY bit_count((similarity_hash # {newHash})::bit(64))
                LIMIT 5
                """)
            .ToListAsync();

        // Assert
        TestContext.WriteLine($"Top 5 similar messages to: \"{newSpam}\"");
        foreach (var r in rawResults)
        {
            TestContext.WriteLine($"  [{r.HammingDistance} bits] Msg {r.MessageId}: {r.MessageText?[..Math.Min(60, r.MessageText.Length)]}...");
        }

        Assert.That(rawResults, Has.Count.GreaterThan(0), "Should find similar messages");
        Assert.That(rawResults[0].HammingDistance, Is.LessThanOrEqualTo(15),
            "Closest match should be a near-duplicate spam message");
    }

    [Test]
    public async Task SimHash_Deterministic_AcrossContexts()
    {
        // Arrange: Compute hash, save to DB, read back
        await using var context = _testHelper.GetDbContext();
        await GoldenDataset.SeedAsync(context);

        var testText = "Join our telegram channel for exclusive crypto signals";
        var computedHash = _simHashService.ComputeHash(testText);

        // Save to a message
        var message = await context.Messages.FirstAsync(m => m.MessageId == 90001);
        message.SimilarityHash = computedHash;
        await context.SaveChangesAsync();

        // Act: Read back in new context
        await using var context2 = _testHelper.GetDbContext();
        var savedHash = await context2.Messages
            .Where(m => m.MessageId == 90001)
            .Select(m => m.SimilarityHash)
            .FirstAsync();

        var recomputedHash = _simHashService.ComputeHash(testText);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(savedHash, Is.EqualTo(computedHash), "Hash should survive DB round-trip");
            Assert.That(recomputedHash, Is.EqualTo(computedHash), "Hash should be deterministic");
        });
    }

    #endregion

    #region Near-Duplicate Detection Tests

    [Test]
    public async Task SimHash_DetectsNearDuplicateGroups_InDedupTestData()
    {
        // Arrange: Seed base data + deduplication test data with intentional near-duplicates
        await using var context = _testHelper.GetDbContext();
        await GoldenDataset.SeedWithoutTrainingDataAsync(context);
        await GoldenDataset.SeedDeduplicationTestDataAsync(context);

        // Load all dedup test messages and compute hashes
        var messages = await context.Messages
            .Where(m => m.MessageText != null && m.MessageId >= 95001 && m.MessageId <= 95022)
            .Select(m => new { m.MessageId, m.MessageText })
            .ToListAsync();

        Assert.That(messages, Has.Count.EqualTo(22), "Should have 22 dedup test messages");

        // Compute hashes for all messages
        var hashDict = messages.ToDictionary(
            m => m.MessageId,
            m => _simHashService.ComputeHash(m.MessageText));

        // Define expected near-duplicate groups
        var group1 = new[] { 95001L, 95002L, 95003L, 95004L }; // Crypto signals variants
        var group2 = new[] { 95005L, 95006L, 95007L };         // Investment scam variants
        var group3 = new[] { 95008L, 95009L, 95010L };         // Giveaway scam variants
        var group6 = new[] { 95017L, 95018L, 95019L };         // Ham ML project variants
        var group7 = new[] { 95020L, 95021L, 95022L };         // Money fast variants

        // Act & Assert: Messages within groups should be similar (low Hamming distance)
        // Note: We check that FIRST message in each group is similar to ALL others
        // (this is how dedup works in practice - compare new message against existing ones)
        var groupResults = new List<(string Name, int MinDistance, int MaxDistance, bool AllSimilarToFirst)>();

        foreach (var (name, group) in new[] {
            ("Group1-Crypto", group1), ("Group2-Investment", group2),
            ("Group3-Giveaway", group3), ("Group6-HamML", group6), ("Group7-MoneyFast", group7) })
        {
            int minDistanceToFirst = int.MaxValue;
            int maxDistanceToFirst = 0;
            bool allSimilarToFirst = true;

            var firstHash = hashDict[group[0]];
            for (int i = 1; i < group.Length; i++)
            {
                var dist = _simHashService.HammingDistance(firstHash, hashDict[group[i]]);
                minDistanceToFirst = Math.Min(minDistanceToFirst, dist);
                maxDistanceToFirst = Math.Max(maxDistanceToFirst, dist);
                if (dist > 15) allSimilarToFirst = false;
            }

            groupResults.Add((name, minDistanceToFirst, maxDistanceToFirst, allSimilarToFirst));
            TestContext.WriteLine($"{name}: Distance to first = {minDistanceToFirst}-{maxDistanceToFirst}, All similar to first: {allSimilarToFirst}");
        }

        // Assert that ALL groups have messages similar to their first message
        // (this tests the actual deduplication use case - finding existing similar samples)
        var similarGroups = groupResults.Count(r => r.AllSimilarToFirst);
        Assert.That(similarGroups, Is.EqualTo(5),
            $"All 5 near-duplicate groups should have messages similar to first (got {similarGroups})");
    }

    [Test]
    public async Task SimHash_DistinguishesDifferentGroups_InDedupTestData()
    {
        // Arrange: Seed dedup test data
        await using var context = _testHelper.GetDbContext();
        await GoldenDataset.SeedWithoutTrainingDataAsync(context);
        await GoldenDataset.SeedDeduplicationTestDataAsync(context);

        // Get representative messages from different groups
        var cryptoSpam = await context.Messages.Where(m => m.MessageId == 95001).Select(m => m.MessageText).FirstAsync();
        var giveawaySpam = await context.Messages.Where(m => m.MessageId == 95008).Select(m => m.MessageText).FirstAsync();
        var datingSpam = await context.Messages.Where(m => m.MessageId == 95011).Select(m => m.MessageText).FirstAsync();
        var hamML = await context.Messages.Where(m => m.MessageId == 95017).Select(m => m.MessageText).FirstAsync();

        // Act: Compute cross-group distances
        var cryptoHash = _simHashService.ComputeHash(cryptoSpam);
        var giveawayHash = _simHashService.ComputeHash(giveawaySpam);
        var datingHash = _simHashService.ComputeHash(datingSpam);
        var hamHash = _simHashService.ComputeHash(hamML);

        var cryptoVsGiveaway = _simHashService.HammingDistance(cryptoHash, giveawayHash);
        var cryptoVsDating = _simHashService.HammingDistance(cryptoHash, datingHash);
        var cryptoVsHam = _simHashService.HammingDistance(cryptoHash, hamHash);
        var giveawayVsHam = _simHashService.HammingDistance(giveawayHash, hamHash);

        // Assert: Different groups should have high Hamming distance
        TestContext.WriteLine($"Crypto vs Giveaway: {cryptoVsGiveaway}");
        TestContext.WriteLine($"Crypto vs Dating: {cryptoVsDating}");
        TestContext.WriteLine($"Crypto vs Ham: {cryptoVsHam}");
        TestContext.WriteLine($"Giveaway vs Ham: {giveawayVsHam}");

        Assert.Multiple(() =>
        {
            Assert.That(cryptoVsGiveaway, Is.GreaterThan(15), "Different spam types should be distinguishable");
            Assert.That(cryptoVsHam, Is.GreaterThan(20), "Spam and ham should be clearly different");
            Assert.That(giveawayVsHam, Is.GreaterThan(20), "Different topics should have high distance");
        });
    }

    #endregion

    /// <summary>
    /// DTO for raw SQL Hamming distance query results
    /// </summary>
    private record HammingResult(long MessageId, string? MessageText, int HammingDistance);
}
