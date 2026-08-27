namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Singleton cache for ban celebration shuffle-bag state.
/// Ensures GIFs and captions are shown in a randomized order without repeats
/// until all items have been displayed.
/// </summary>
public interface IBanCelebrationCache
{
    /// <summary>
    /// Gets the next GIF ID from the shuffle bag, or null if the bag is empty.
    /// Caller should repopulate the bag when null is returned.
    /// </summary>
    int? GetNextGifId();

    /// <summary>
    /// Repopulates the GIF shuffle bag with the provided IDs (shuffled).
    /// </summary>
    void RepopulateGifBag(List<int> ids);

    /// <summary>
    /// Splices a newly added GIF ID into the pending items of the current bag at a
    /// random position, so new library content joins the rotation immediately.
    /// No-op when the bag is empty: the next pull reloads every ID from the database,
    /// which already includes the new one.
    /// </summary>
    void AddGifId(int id);

    /// <summary>
    /// Returns true if the GIF bag is empty and needs repopulating.
    /// </summary>
    bool IsGifBagEmpty { get; }

    /// <summary>
    /// Gets the next caption ID from the shuffle bag, or null if the bag is empty.
    /// Caller should repopulate the bag when null is returned.
    /// </summary>
    int? GetNextCaptionId();

    /// <summary>
    /// Repopulates the caption shuffle bag with the provided IDs (shuffled).
    /// </summary>
    void RepopulateCaptionBag(List<int> ids);

    /// <summary>
    /// Splices a newly added caption ID into the pending items of the current bag at a
    /// random position. Same semantics as <see cref="AddGifId"/>.
    /// </summary>
    void AddCaptionId(int id);

    /// <summary>
    /// Returns true if the caption bag is empty and needs repopulating.
    /// </summary>
    bool IsCaptionBagEmpty { get; }
}
