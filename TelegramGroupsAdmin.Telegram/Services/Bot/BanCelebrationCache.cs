namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Singleton cache for ban celebration shuffle-bag state.
/// Uses Fisher-Yates shuffle to randomize order and ensures all items
/// are shown before any repeats.
/// Thread-safe via semaphore locks.
/// </summary>
public class BanCelebrationCache : IBanCelebrationCache
{
    private readonly Queue<int> _gifBag = new();
    private readonly Queue<int> _captionBag = new();
    private readonly SemaphoreSlim _gifLock = new(1, 1);
    private readonly SemaphoreSlim _captionLock = new(1, 1);

    public bool IsGifBagEmpty
    {
        get
        {
            _gifLock.Wait();
            try
            {
                return _gifBag.Count == 0;
            }
            finally
            {
                _gifLock.Release();
            }
        }
    }

    public bool IsCaptionBagEmpty
    {
        get
        {
            _captionLock.Wait();
            try
            {
                return _captionBag.Count == 0;
            }
            finally
            {
                _captionLock.Release();
            }
        }
    }

    public int? GetNextGifId()
    {
        _gifLock.Wait();
        try
        {
            return _gifBag.Count > 0 ? _gifBag.Dequeue() : null;
        }
        finally
        {
            _gifLock.Release();
        }
    }

    public void RepopulateGifBag(List<int> ids)
    {
        _gifLock.Wait();
        try
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
        finally
        {
            _gifLock.Release();
        }
    }

    public int? GetNextCaptionId()
    {
        _captionLock.Wait();
        try
        {
            return _captionBag.Count > 0 ? _captionBag.Dequeue() : null;
        }
        finally
        {
            _captionLock.Release();
        }
    }

    public void RepopulateCaptionBag(List<int> ids)
    {
        _captionLock.Wait();
        try
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
        finally
        {
            _captionLock.Release();
        }
    }
}
