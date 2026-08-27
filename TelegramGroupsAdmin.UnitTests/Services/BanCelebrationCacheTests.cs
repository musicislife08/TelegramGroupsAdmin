using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for BanCelebrationCache shuffle-bag behavior, focused on
/// AddGifId/AddCaptionId splicing new library items into a live bag.
/// </summary>
[TestFixture]
public class BanCelebrationCacheTests
{
    private BanCelebrationCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _cache = new BanCelebrationCache();
    }

    /// <summary>Drains a bag into a list, so a whole cycle can be asserted on.</summary>
    private List<int> DrainGifBag()
    {
        var drained = new List<int>();
        while (_cache.GetNextGifId() is { } id)
        {
            drained.Add(id);
        }

        return drained;
    }

    private List<int> DrainCaptionBag()
    {
        var drained = new List<int>();
        while (_cache.GetNextCaptionId() is { } id)
        {
            drained.Add(id);
        }

        return drained;
    }

    [Test]
    public void AddGifId_BagHasPendingItems_NewIdJoinsCurrentCycleExactlyOnce()
    {
        _cache.RepopulateGifBag([1, 2, 3, 4]);
        _cache.GetNextGifId(); // dispense one, leaving three pending

        _cache.AddGifId(99);

        var remaining = DrainGifBag();
        Assert.Multiple(() =>
        {
            Assert.That(remaining, Has.Count.EqualTo(4), "three pending items plus the new one");
            Assert.That(remaining.Count(id => id == 99), Is.EqualTo(1), "new ID appears exactly once");
            Assert.That(remaining, Is.Unique, "no item is duplicated by the splice");
        });
    }

    [Test]
    public void AddGifId_BagHasPendingItems_AlreadyDispensedItemDoesNotReturn()
    {
        _cache.RepopulateGifBag([1, 2, 3, 4]);
        var dispensed = _cache.GetNextGifId()!.Value;

        _cache.AddGifId(99);

        Assert.That(DrainGifBag(), Does.Not.Contain(dispensed),
            "splicing must not restore an item that was already shown this cycle");
    }

    [Test]
    public void AddGifId_EmptyBag_IsNoOpSoNextPullReloadsFromDatabase()
    {
        _cache.AddGifId(99);

        Assert.Multiple(() =>
        {
            Assert.That(_cache.IsGifBagEmpty, Is.True);
            Assert.That(_cache.GetNextGifId(), Is.Null);
        });
    }

    [Test]
    public void AddCaptionId_BagHasPendingItems_NewIdJoinsCurrentCycleExactlyOnce()
    {
        _cache.RepopulateCaptionBag([1, 2, 3, 4]);
        _cache.GetNextCaptionId();

        _cache.AddCaptionId(99);

        var remaining = DrainCaptionBag();
        Assert.Multiple(() =>
        {
            Assert.That(remaining, Has.Count.EqualTo(4));
            Assert.That(remaining.Count(id => id == 99), Is.EqualTo(1));
            Assert.That(remaining, Is.Unique);
        });
    }

    [Test]
    public void AddCaptionId_EmptyBag_IsNoOpSoNextPullReloadsFromDatabase()
    {
        _cache.AddCaptionId(99);

        Assert.Multiple(() =>
        {
            Assert.That(_cache.IsCaptionBagEmpty, Is.True);
            Assert.That(_cache.GetNextCaptionId(), Is.Null);
        });
    }

    [Test]
    public void AddGifId_DoesNotTouchCaptionBag()
    {
        _cache.RepopulateCaptionBag([1, 2]);

        _cache.AddGifId(99);

        Assert.That(DrainCaptionBag(), Is.EquivalentTo(new[] { 1, 2 }));
    }
}
