using TL;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Thin wrapper around WTelegram.Client to enable unit testing.
/// All methods delegate directly to the underlying client — no business logic.
///
/// Why this exists: WTelegram.Client uses non-virtual instance methods for API calls
/// which NSubstitute cannot mock. This interface provides mockable virtual methods.
/// Same pattern as ITelegramApiClient for Telegram.Bot.
/// </summary>
public interface IWTelegramApiClient : IAsyncDisposable
{
    // ═══════════════════════════════════════════════════════════════════════════
    // CLIENT STATE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>ID of the current logged-in user or 0.</summary>
    long UserId { get; }

    /// <summary>Info about the current logged-in user (filled after successful login).</summary>
    User? User { get; }

    /// <summary>Has this client been disconnected?</summary>
    bool Disconnected { get; }

    // ═══════════════════════════════════════════════════════════════════════════
    // AUTH / LOGIN
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Login as a user (resumes existing session if possible).</summary>
    Task<User> LoginUserIfNeeded(CodeSettings? settings = null, bool reloginOnFailedResume = true);

    /// <summary>Provide login info (phone, verification code, 2FA password).
    /// Returns the next config key needed, or null when login is complete.</summary>
    Task<string?> Login(string loginInfo);

    // ═══════════════════════════════════════════════════════════════════════════
    // USER OPERATIONS (Part 2: profile scan)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Get full user info including bio, about, profile photo details.</summary>
    Task<Users_UserFull> Users_GetFullUser(InputUserBase user);

    /// <summary>Get a user's profile photos.</summary>
    Task<Photos_Photos> Photos_GetUserPhotos(InputUserBase user, int offset = 0, long maxId = 0, int limit = 100);

    /// <summary>Resolve a public username to a peer (user or channel).</summary>
    Task<Contacts_ResolvedPeer> Contacts_ResolveUsername(string username);

    /// <summary>Search for users/chats by name substring. Returns up to <paramref name="limit"/> results.</summary>
    Task<Contacts_Found> Contacts_Search(string query, int limit = 20);

    /// <summary>Get full channel info including about/description text.</summary>
    Task<Messages_ChatFull> Channels_GetFullChannel(InputChannelBase channel);

    /// <summary>Get pinned stories for a peer. Returns stories permanently displayed on their profile.</summary>
    Task<Stories_Stories> Stories_GetPinnedStories(InputPeer peer, int offset_id = 0, int limit = 20);

    /// <summary>Get full story data by IDs. Use to resolve min stories that have omitted captions/media.</summary>
    Task<Stories_Stories> Stories_GetStoriesByID(InputPeer peer, params int[] id);

    /// <summary>Download a photo to a stream. Returns the file type (jpeg, png, etc.).</summary>
    Task<Storage_FileType> DownloadFileAsync(Photo photo, Stream outputStream);

    /// <summary>Download a document (or its thumbnail) to a stream. Returns MIME type string.</summary>
    Task<string?> DownloadFileAsync(Document document, Stream outputStream, PhotoSizeBase? thumbSize = null);

    /// <summary>Download a peer's profile/channel photo. Works with User, Chat, Channel (all implement IPeerInfo).</summary>
    Task<Storage_FileType> DownloadProfilePhotoAsync(IPeerInfo peer, Stream outputStream, bool big = false);

    // ═══════════════════════════════════════════════════════════════════════════
    // DIALOG / CHAT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Get all dialogs (chats/groups/channels the account is in).</summary>
    Task<Messages_Dialogs> Messages_GetAllDialogs();

    /// <summary>Get all chats, channels and supergroups the account is in.
    /// Returns a dictionary of ChatBase keyed by channel/chat ID.</summary>
    Task<Messages_Chats> Messages_GetAllChats();

    // ═══════════════════════════════════════════════════════════════════════════
    // MESSAGING (Part 4: send as admin)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Send a text message as the logged-in user.
    /// The bot will see this as a normal incoming message and populate the UI automatically.</summary>
    /// <param name="peer">Destination chat (use <see cref="GetInputPeerForChat"/> to resolve)</param>
    /// <param name="text">Message text (max 4096 characters)</param>
    /// <param name="replyToMsgId">Optional message ID to reply to</param>
    Task<Message> SendMessageAsync(InputPeer peer, string text, int replyToMsgId = 0);

    /// <summary>Edit a message previously sent by the logged-in user.</summary>
    /// <param name="peer">Chat where the message was sent</param>
    /// <param name="messageId">ID of the message to edit</param>
    /// <param name="text">New message text</param>
    Task<UpdatesBase> Messages_EditMessage(InputPeer peer, int messageId, string text);

    // ═══════════════════════════════════════════════════════════════════════════
    // PEER CACHE (resolves bot API chat IDs to InputPeer with access hash)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Warm the peer cache by fetching all chats/channels the user is in.
    /// Must be called after login before sending messages.</summary>
    Task WarmPeerCacheAsync();

    /// <summary>Resolve a bot API chat ID (e.g. -1001322973935) to an InputPeer.
    /// Returns null if the user is not a member of that chat.</summary>
    InputPeer? GetInputPeerForChat(long botApiChatId);

    // ═══════════════════════════════════════════════════════════════════════════
    // GENERIC INVOKE (escape hatch for one-off calls not yet in the interface)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Call any TL method directly. Use named methods above when available.</summary>
    Task<T> Invoke<T>(IMethod<T> query);
}
