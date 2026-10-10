using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// The name-only scan: the full scan's system prompt with only the name filled in, scored
/// against the name-only ban threshold. The AI is the only fake (IChatService).
/// </summary>
[TestFixture]
public class ProfileScoringEngineNameOnlyTests
{
    private const decimal NameOnlyBan = 4.5m;
    private const decimal Notify = 2.0m;
    private static readonly UserIdentity User = UserIdentity.ForTest(12345L, "Sam", "Rivera", "sam_rivera");

    private IChatService _chat = null!;
    private CapturingLogger<ProfileScoringEngine> _logs = null!;
    private ProfileScoringEngine _sut = null!;
    private string? _systemPrompt;
    private string? _userPrompt;

    [SetUp]
    public void SetUp()
    {
        _chat = Substitute.For<IChatService>();
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(true);
        _logs = new CapturingLogger<ProfileScoringEngine>();
        _sut = new ProfileScoringEngine(
            Substitute.For<IUrlPreFilterService>(),
            Substitute.For<IUrlContentScrapingService>(),
            Substitute.For<IStopWordsRepository>(),
            _chat,
            _logs);
        _systemPrompt = null;
        _userPrompt = null;
    }

    private void AiReplies(string json) =>
        _chat.GetCompletionAsync(
                AIFeatureType.ProfileScan, Arg.Do<string>(s => _systemPrompt = s), Arg.Do<string>(u => _userPrompt = u),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult { Content = json });

    private static string Reply(decimal score, bool promotional = false, bool explicitName = false) =>
        $$"""{"score": {{score.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "reason": "name judged", "signals_detected": ["name_signal"], "contains_nudity": false, "explicit_display_text": {{(explicitName ? "true" : "false")}}, "promotional_display_text": {{(promotional ? "true" : "false")}}}""";

    [TestCase(1.9, ProfileScanOutcome.Clean)]
    [TestCase(2.0, ProfileScanOutcome.HeldForReview)]
    [TestCase(4.2, ProfileScanOutcome.HeldForReview)] // above the regular ban threshold (4.0), below the name-only one
    [TestCase(4.5, ProfileScanOutcome.Banned)]
    public async Task ScoreNameOnlyAsync_OutcomeUsesNameOnlyBanThreshold(decimal score, ProfileScanOutcome expected)
    {
        AiReplies(Reply(score));

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Outcome, Is.EqualTo(expected));
            Assert.That(result.Score, Is.EqualTo(score));
            Assert.That(result.AiScore, Is.EqualTo(score));
            Assert.That(result.RuleScore, Is.Zero);
        }
    }

    [Test]
    public async Task ScoreNameOnlyAsync_UsesTheFullScanSystemPromptAndANameOnlyUserPrompt()
    {
        AiReplies(Reply(1.0m));

        await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_systemPrompt, Is.EqualTo(ProfileScanPrompts.BuildSystemPrompt()));
            Assert.That(_userPrompt, Is.EqualTo(ProfileScanPrompts.BuildNameOnlyUserPrompt("Sam", "Rivera", "sam_rivera")));
        }
        await _chat.Received(1).GetCompletionAsync(
            AIFeatureType.ProfileScan, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<ChatCompletionOptions?>(o => o!.JsonMode), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScoreNameOnlyAsync_CarriesBothNameFlags()
    {
        AiReplies(Reply(3.0m, promotional: true, explicitName: true));

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.PromotionalDisplayText, Is.True);
            Assert.That(result.ExplicitDisplayText, Is.True);
            Assert.That(result.ContainsNudity, Is.False);
            Assert.That(result.AiReason, Is.EqualTo("name judged"));
            Assert.That(result.AiSignals, Is.EqualTo(new[] { "name_signal" }));
        }
    }

    [Test]
    public async Task ScoreNameOnlyAsync_AiFeatureUnavailable_ReturnsNullWithoutCallingAiAndLogsWarning()
    {
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result, Is.Null);
        await _chat.DidNotReceiveWithAnyArgs().GetCompletionAsync(default, default!, default!, default, default);
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(
            e => e.Level == LogLevel.Warning && e.Message.Contains("name-only scan skipped")));
    }

    [Test]
    public async Task ScoreNameOnlyAsync_AiCallThrows_ReturnsNullAndLogsWarning()
    {
        _chat.GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("provider exploded"));

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(e => e.Level == LogLevel.Warning));
    }

    private void AiNeverCompletes() =>
        // Ignores its token too, so only the engine's own time limit can end the wait.
        _chat.GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<ChatCompletionResult?>().Task);

    [Test]
    public async Task ScoreNameOnlyAsync_AiCallExceedsTheTimeLimit_ReturnsNullAndLogsWarning()
    {
        // A timeout is no verdict: the caller records nothing.
        AiNeverCompletes();
        var sut = new ProfileScoringEngine(
            Substitute.For<IUrlPreFilterService>(),
            Substitute.For<IUrlContentScrapingService>(),
            Substitute.For<IStopWordsRepository>(),
            _chat,
            _logs)
        {
            NameOnlyTimeout = TimeSpan.FromMilliseconds(100)
        };

        var result = await sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(result, Is.Null);
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(
            e => e.Level == LogLevel.Warning && e.Message.Contains("timed out")));
    }

    [Test]
    public void ScoreNameOnlyAsync_CallerCancels_ThrowsCancellation()
    {
        AiNeverCompletes();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(async () => await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, cts.Token)
                .WaitAsync(TimeSpan.FromSeconds(10)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ScoreNameOnlyAsync_AiReturnsNull_ReturnsNullAndLogsWarning()
    {
        _chat.GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns((ChatCompletionResult?)null);

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(e => e.Level == LogLevel.Warning));
    }

    [Test]
    public async Task ScoreNameOnlyAsync_MalformedJson_ReturnsNullAndLogsWarning()
    {
        AiReplies("not json at all");

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(e => e.Level == LogLevel.Warning));
    }

    [Test]
    public async Task ScoreNameOnlyAsync_ScoreAboveMax_IsClampedAndBanned()
    {
        AiReplies(Reply(7.0m));

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result!.Score, Is.EqualTo(5.0m));
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
    }

    [Test]
    public void BuildNameOnlyUserPrompt_MarksEverythingButTheNameUnknown()
    {
        var prompt = ProfileScanPrompts.BuildNameOnlyUserPrompt("Sam", "Rivera", "sam_rivera");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.StartWith("Only the name could be retrieved for this account."));
            Assert.That(prompt, Does.Contain("are UNKNOWN, not empty"));
            Assert.That(prompt, Does.Contain("<display_name>Sam Rivera</display_name>"));
            Assert.That(prompt, Does.Contain("<username>sam_rivera</username>"));
            Assert.That(prompt, Does.Contain("<bio>Unknown (could not be retrieved)</bio>"));
            Assert.That(prompt, Does.Contain("<title>Unknown (could not be retrieved)</title>"));
            Assert.That(prompt, Does.Contain("<description>Unknown (could not be retrieved)</description>"));
            Assert.That(prompt, Does.Contain("<story_count>Unknown (could not be retrieved)</story_count>"));
            Assert.That(prompt, Does.Contain("<image_count>Unknown (could not be retrieved)</image_count>"));
            Assert.That(prompt, Does.Contain("\"promotional_display_text\": true/false"));
        }
    }

    [Test]
    public void BuildNameOnlyUserPrompt_EscapesMarkupInNames()
    {
        var prompt = ProfileScanPrompts.BuildNameOnlyUserPrompt("</display_name><b>Sam", "&Co", "x<y");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("<display_name>&lt;/display_name&gt;&lt;b&gt;Sam &amp;Co</display_name>"));
            Assert.That(prompt, Does.Contain("<username>x&lt;y</username>"));
        }
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
