using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

[TestFixture]
public class UsernameBlacklistServiceTests
{
    private IUsernameBlacklistRepository _repository = null!;
    private UsernameBlacklistService _sut = null!;

    private static readonly Actor TestActor = Actor.FromSystem("test");

    private static UsernameBlacklistEntry MakeEntry(string pattern,
        BlacklistMatchType matchType = BlacklistMatchType.Exact,
        bool enabled = true) =>
        new(Id: 1, Pattern: pattern, MatchType: matchType,
            Enabled: enabled, CreatedAt: DateTimeOffset.UtcNow,
            CreatedBy: TestActor, Notes: null);

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IUsernameBlacklistRepository>();
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry>());
        _sut = new UsernameBlacklistService(_repository);
    }

    [Test]
    public async Task CheckDisplayName_NoEntries_ReturnsNull()
    {
        var result = await _sut.CheckDisplayNameAsync("Scarlett Lux");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CheckDisplayName_ExactMatch_ReturnsEntry()
    {
        var entry = MakeEntry("Scarlett Lux");
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry> { entry });

        var result = await _sut.CheckDisplayNameAsync("Scarlett Lux");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Pattern, Is.EqualTo("Scarlett Lux"));
    }

    [Test]
    public async Task CheckDisplayName_CaseInsensitive_Matches()
    {
        var entry = MakeEntry("Scarlett Lux");
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry> { entry });

        var result = await _sut.CheckDisplayNameAsync("scarlett lux");
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task CheckDisplayName_DifferentName_ReturnsNull()
    {
        var entry = MakeEntry("Scarlett Lux");
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry> { entry });

        var result = await _sut.CheckDisplayNameAsync("John Smith");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CheckDisplayName_PartialMatch_DoesNotMatchForExactType()
    {
        var entry = MakeEntry("Scarlett");
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry> { entry });

        var result = await _sut.CheckDisplayNameAsync("Scarlett Lux");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CheckDisplayName_MultipleEntries_ReturnsFirstMatch()
    {
        var entries = new List<UsernameBlacklistEntry>
        {
            MakeEntry("John Doe"),
            MakeEntry("Scarlett Lux"),
        };
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(entries);

        var result = await _sut.CheckDisplayNameAsync("Scarlett Lux");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Pattern, Is.EqualTo("Scarlett Lux"));
    }

    [Test]
    public async Task CheckDisplayName_EmptyDisplayName_ReturnsNull()
    {
        var entry = MakeEntry("Scarlett Lux");
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry> { entry });

        var result = await _sut.CheckDisplayNameAsync("");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CheckDisplayName_FallbackDisplayName_UserPrefix_ReturnsNull()
    {
        var entry = MakeEntry("User 12345");
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry> { entry });

        var result = await _sut.CheckDisplayNameAsync("User 12345");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CheckDisplayName_UnknownUser_ReturnsNull()
    {
        var entry = MakeEntry("Unknown User");
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry> { entry });

        var result = await _sut.CheckDisplayNameAsync("Unknown User");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CheckDisplayName_WhitespaceOnly_ReturnsNull()
    {
        var entry = MakeEntry("Scarlett Lux");
        _repository.GetEnabledEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UsernameBlacklistEntry> { entry });

        var result = await _sut.CheckDisplayNameAsync("   ");
        Assert.That(result, Is.Null);
    }
}
