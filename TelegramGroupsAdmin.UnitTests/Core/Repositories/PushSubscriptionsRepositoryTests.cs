using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Http;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.UnitTests.Core.Repositories;

/// <summary>
/// A rejected push endpoint never reaches the database, and the rejection (with the address, which
/// the user-facing message omits) is logged at warning so it can be seen in Seq.
/// </summary>
[TestFixture]
public class PushSubscriptionsRepositoryTests
{
    [TestCase("http://push.example.com/plain", "https")]
    [TestCase("https://127.0.0.1/push", "loopback")]
    [TestCase("https://localhost/push", "localhost")]
    public async Task UpsertAsync_RejectedEndpoint_LogsAWarningAndNeverOpensTheDatabase(string endpoint, string reasonFragment)
    {
        var contextFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        var logs = new CapturingLogger<PushSubscriptionsRepository>();
        var sut = new PushSubscriptionsRepository(contextFactory, logs);

        var ex = Assert.ThrowsAsync<PushEndpointRejectedException>(() => sut.UpsertAsync(
            new PushSubscription { UserId = "u1", Endpoint = endpoint, P256dh = "k", Auth = "a" }));

        Assert.That(ex!.Message, Is.EqualTo(PushEndpointPolicy.RejectedMessage));
        await contextFactory.DidNotReceiveWithAnyArgs().CreateDbContextAsync(default);
        var warning = logs.Entries.SingleOrDefault(e => e.Level == LogLevel.Warning);
        Assert.That(warning.Message, Is.Not.Null.And.Contains(reasonFragment).IgnoreCase.And.Contains("u1"));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
