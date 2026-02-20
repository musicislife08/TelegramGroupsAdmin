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

    // ═══════════════════════════════════════════════════════════════════════════
    // DIALOG / CHAT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Get all dialogs (chats/groups/channels the account is in).</summary>
    Task<Messages_Dialogs> Messages_GetAllDialogs();

    // ═══════════════════════════════════════════════════════════════════════════
    // GENERIC INVOKE (escape hatch for one-off calls not yet in the interface)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Call any TL method directly. Use named methods above when available.</summary>
    Task<T> Invoke<T>(IMethod<T> query);
}
