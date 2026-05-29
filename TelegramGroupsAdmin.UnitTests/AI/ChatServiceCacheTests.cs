using System.Collections;
using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Core.Metrics;

namespace TelegramGroupsAdmin.UnitTests.AI;

[TestFixture]
public class ChatServiceCacheTests
{
    private static IDictionary GetCache()
    {
        var field = typeof(ChatService).GetField("ClientCache",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IDictionary)field.GetValue(null)!;
    }

    private static object NewCachedClient(IChatClient client)
    {
        // CachedClient is a private nested record: CachedClient(IChatClient Client, string ModelId)
        var type = typeof(ChatService).GetNestedType("CachedClient", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, client, "test-model")!;
    }

    private static ChatService CreateService() => new(
        Substitute.For<ISystemConfigRepository>(),
        NullLogger<ChatService>.Instance,
        new ApiMetrics(),
        new CacheMetrics());

    [TearDown]
    public void ClearStaticCache() => GetCache().Clear();

    [Test]
    public void InvalidateCache_DisposesEvictedClient()
    {
        var fakeClient = Substitute.For<IChatClient>();
        var cache = GetCache();
        // Key shape: id|provider|model|azureDeployment|azureEndpoint|localEndpoint (no api key segment)
        var key = "dispose-test|OpenAI|gpt-4o|||";
        cache[key] = NewCachedClient(fakeClient);

        CreateService().InvalidateCache("dispose-test");

        fakeClient.Received(1).Dispose();
        Assert.That(cache.Contains(key), Is.False);
    }
}
