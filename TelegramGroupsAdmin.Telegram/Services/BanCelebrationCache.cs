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
            using var _ = _gifLock.EnterScope();
            
            using (_gifLock.EnterScope())
            {
                
            }
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
}
