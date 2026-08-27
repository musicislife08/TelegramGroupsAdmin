namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Singleton cache for ban celebration shuffle-bag state.
/// Uses Fisher-Yates shuffle to randomize order and ensures all items
/// are shown before any repeats.
/// Thread-safe via .NET 9+ Lock type.
/// </summary>
public class BanCelebrationCache : IBanCelebrationCache
{
    private readonly Queue<int> _gifBag = new();
    private readonly Queue<int> _captionBag = new();
    private readonly Lock _gifLock = new();
    private readonly Lock _captionLock = new();

    public bool IsGifBagEmpty
    {
        get
        {
            lock (_gifLock)
            {
                return _gifBag.Count == 0;
            }
        }
    }

    public bool IsCaptionBagEmpty
    {
        get
        {
            lock (_captionLock)
            {
                return _captionBag.Count == 0;
            }
        }
    }

    public int? GetNextGifId()
    {
        lock (_gifLock)
        {
            return _gifBag.Count > 0 ? _gifBag.Dequeue() : null;
        }
    }

    public void RepopulateGifBag(List<int> ids)
    {
        lock (_gifLock)
        {
            _gifBag.Clear();

            // Fisher-Yates shuffle
            for (var i = ids.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            foreach (var id in ids)
            {
                _gifBag.Enqueue(id);
            }
        }
    }

    public void AddGifId(int id)
    {
        lock (_gifLock)
        {
            // Empty bag means the next pull reloads all IDs from the database,
            // which will include this one. Inserting here would instead make the
            // new item jump the queue and force an immediate reshuffle after it.
            if (_gifBag.Count == 0)
                return;

            SpliceInto(_gifBag, id);
        }
    }

    public int? GetNextCaptionId()
    {
        lock (_captionLock)
        {
            return _captionBag.Count > 0 ? _captionBag.Dequeue() : null;
        }
    }

    public void RepopulateCaptionBag(List<int> ids)
    {
        lock (_captionLock)
        {
            _captionBag.Clear();

            // Fisher-Yates shuffle
            for (var i = ids.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            foreach (var id in ids)
            {
                _captionBag.Enqueue(id);
            }
        }
    }

    public void AddCaptionId(int id)
    {
        lock (_captionLock)
        {
            // Same rationale as AddGifId.
            if (_captionBag.Count == 0)
                return;

            SpliceInto(_captionBag, id);
        }
    }

    /// <summary>
    /// Inserts an ID at a uniformly random position among the queue's pending items.
    /// Queue&lt;int&gt; has no random insert, so the pending items are drained to a list,
    /// the ID is inserted, and the list is re-enqueued in order. Bags hold at most the
    /// library size (tens of items), so the copy is negligible.
    /// Callers must already hold the matching lock.
    /// </summary>
    private static void SpliceInto(Queue<int> bag, int id)
    {
        var pending = new List<int>(bag);
        bag.Clear();

        pending.Insert(Random.Shared.Next(pending.Count + 1), id);

        foreach (var pendingId in pending)
        {
            bag.Enqueue(pendingId);
        }
    }
}
