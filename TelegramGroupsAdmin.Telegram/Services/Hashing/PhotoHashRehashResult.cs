namespace TelegramGroupsAdmin.Telegram.Services.Hashing;

/// <summary>
/// Outcome of a rehash pass.
/// </summary>
/// <param name="Recomputed">Rows whose hash was recomputed from a file on disk.</param>
/// <param name="Unrecoverable">Rows left NULL because the source file no longer exists.</param>
/// <param name="Skipped">Rows deliberately left NULL, such as banned users whose photo was blur-censored in place.</param>
public sealed record PhotoHashRehashResult(int Recomputed, int Unrecoverable, int Skipped)
{
    public static PhotoHashRehashResult Empty { get; } = new(0, 0, 0);

    public PhotoHashRehashResult Add(PhotoHashRehashResult other) =>
        new(Recomputed + other.Recomputed,
            Unrecoverable + other.Unrecoverable,
            Skipped + other.Skipped);
}
