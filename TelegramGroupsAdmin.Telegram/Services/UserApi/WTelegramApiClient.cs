using Microsoft.Extensions.Logging;
using TL;
using WTelegram;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Production implementation wrapping a live WTelegram.Client instance.
/// All API methods route through <see cref="CallAsync{T}"/> which enforces a
/// client-level FLOOD_WAIT gate — once any call triggers rate limiting, all
/// subsequent calls are blocked until the wait expires.
/// </summary>
public sealed class WTelegramApiClient(Client client, ILogger<WTelegramApiClient> logger) : IWTelegramApiClient
{
    /// <summary>
    /// Maximum FLOOD_WAIT duration (in seconds) the client will handle transparently.
    /// Waits longer than this throw <see cref="TelegramFloodWaitException"/> immediately
    /// while keeping the gate active to protect Telegram from further calls.
    /// </summary>
    private const int MaxWaitSeconds = 60;

    /// <summary>
    /// When set, all API calls are blocked until this time.
    /// Set by any method that receives a FLOOD_WAIT response.
    /// </summary>
    private DateTimeOffset? _floodWaitUntil;

    // ═══════════════════════════════════════════════════════════════════════════
    // FLOOD GATE
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<T> CallAsync<T>(Func<Task<T>> apiCall)
    {
        // Gate check: a previous call set the flood wait timer
        if (_floodWaitUntil is { } until)
        {
            var remaining = until - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                if (remaining.TotalSeconds > MaxWaitSeconds)
                {
                    // Too long to wait transparently — throw so caller can bail, gate stays set
                    logger.LogWarning("Flood gate active — {Seconds:F0}s remaining, rejecting call",
                        remaining.TotalSeconds);
                    throw new TelegramFloodWaitException((int)remaining.TotalSeconds, until);
                }

                // Short remaining wait — block transparently
                logger.LogDebug("Flood gate active — waiting {Seconds:F0}s before API call",
                    remaining.TotalSeconds);
                await Task.Delay(remaining);
            }

            _floodWaitUntil = null; // Timer expired or short wait completed
        }

        try
        {
            return await apiCall();
        }
        catch (RpcException ex) when (ex.Code == 420)
        {
            _floodWaitUntil = DateTimeOffset.UtcNow.AddSeconds(ex.X);

            if (ex.X > MaxWaitSeconds)
            {
                // Set gate but throw immediately — caller bails, Telegram is protected
                logger.LogWarning("FLOOD_WAIT_{Seconds} — gate set until {Until:u}, rejecting call",
                    ex.X, _floodWaitUntil);
                throw new TelegramFloodWaitException(ex.X, _floodWaitUntil.Value);
            }

            // Short wait — handle transparently with single retry
            logger.LogWarning("FLOOD_WAIT_{Seconds} — waiting before retry", ex.X);
            await Task.Delay(TimeSpan.FromSeconds(ex.X));
            _floodWaitUntil = null;

            try
            {
                return await apiCall();
            }
            catch (RpcException retryEx) when (retryEx.Code == 420)
            {
                // Retry also rate-limited — set gate and surface as our exception type
                _floodWaitUntil = DateTimeOffset.UtcNow.AddSeconds(retryEx.X);
                logger.LogWarning("FLOOD_WAIT_{Seconds} on retry — gate reset until {Until:u}",
                    retryEx.X, _floodWaitUntil);
                throw new TelegramFloodWaitException(retryEx.X, _floodWaitUntil.Value);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CLIENT STATE (direct passthrough — no gate needed)
    // ═══════════════════════════════════════════════════════════════════════════

    public long UserId => client.UserId;
    public User? User => client.User;
    public bool Disconnected => client.Disconnected;

    // ═══════════════════════════════════════════════════════════════════════════
    // AUTH / LOGIN (direct passthrough — auth has its own flow)
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<User> LoginUserIfNeeded(CodeSettings? settings = null, bool reloginOnFailedResume = true)
        => client.LoginUserIfNeeded(settings, reloginOnFailedResume);

    public Task<string?> Login(string loginInfo)
        => client.Login(loginInfo);

    // ═══════════════════════════════════════════════════════════════════════════
    // API METHODS (all gated through CallAsync)
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<Users_UserFull> Users_GetFullUser(InputUserBase user)
        => CallAsync(() => client.Users_GetFullUser(user));

    public Task<Photos_Photos> Photos_GetUserPhotos(InputUserBase user, int offset = 0, long maxId = 0, int limit = 100)
        => CallAsync(() => client.Photos_GetUserPhotos(user, offset, maxId, limit));

    public Task<Contacts_ResolvedPeer> Contacts_ResolveUsername(string username)
        => CallAsync(() => client.Contacts_ResolveUsername(username));

    public Task<Contacts_Found> Contacts_Search(string query, int limit = 20)
        => CallAsync(() => client.Contacts_Search(query, limit));

    public Task<Messages_ChatFull> Channels_GetFullChannel(InputChannelBase channel)
        => CallAsync(() => client.Channels_GetFullChannel(channel));

    public Task<Stories_Stories> Stories_GetPinnedStories(InputPeer peer, int offset_id = 0, int limit = 20)
        => CallAsync(() => client.Stories_GetPinnedStories(peer, offset_id, limit));

    public Task<Stories_Stories> Stories_GetStoriesByID(InputPeer peer, params int[] id)
        => CallAsync(() => client.Stories_GetStoriesByID(peer, id));

    public Task<Storage_FileType> DownloadFileAsync(Photo photo, Stream outputStream)
        => CallAsync(() => client.DownloadFileAsync(photo, outputStream));

    public Task<string?> DownloadFileAsync(Document document, Stream outputStream, PhotoSizeBase? thumbSize = null)
        => CallAsync(() => client.DownloadFileAsync(document, outputStream, thumbSize));

    public Task<Storage_FileType> DownloadProfilePhotoAsync(IPeerInfo peer, Stream outputStream, bool big = false)
        => CallAsync(() => client.DownloadProfilePhotoAsync(peer, outputStream, big));

    public Task<Messages_Dialogs> Messages_GetAllDialogs()
        => CallAsync(() => client.Messages_GetAllDialogs());

    public Task<T> Invoke<T>(IMethod<T> query)
        => CallAsync(() => client.Invoke(query));

    // ═══════════════════════════════════════════════════════════════════════════
    // DISPOSE
    // ═══════════════════════════════════════════════════════════════════════════

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}
