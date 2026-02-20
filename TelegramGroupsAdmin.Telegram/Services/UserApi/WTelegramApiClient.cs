using TL;
using WTelegram;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Production implementation wrapping a live WTelegram.Client instance.
/// Pure passthrough — no business logic, no error handling, no caching.
/// </summary>
public sealed class WTelegramApiClient(Client client) : IWTelegramApiClient
{
    public long UserId => client.UserId;
    public User? User => client.User;
    public bool Disconnected => client.Disconnected;

    public Task<User> LoginUserIfNeeded(CodeSettings? settings = null, bool reloginOnFailedResume = true)
        => client.LoginUserIfNeeded(settings, reloginOnFailedResume);

    public Task<string?> Login(string loginInfo)
        => client.Login(loginInfo);

    public Task<Users_UserFull> Users_GetFullUser(InputUserBase user)
        => client.Users_GetFullUser(user);

    public Task<Photos_Photos> Photos_GetUserPhotos(InputUserBase user, int offset = 0, long maxId = 0, int limit = 100)
        => client.Photos_GetUserPhotos(user, offset, maxId, limit);

    public Task<Messages_ChatFull> Channels_GetFullChannel(InputChannelBase channel)
        => client.Channels_GetFullChannel(channel);

    public Task<Stories_PeerStories> Stories_GetPeerStories(InputPeer peer)
        => client.Stories_GetPeerStories(peer);

    public Task<Messages_Dialogs> Messages_GetAllDialogs()
        => client.Messages_GetAllDialogs();

    public Task<T> Invoke<T>(IMethod<T> query)
        => client.Invoke(query);

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}
