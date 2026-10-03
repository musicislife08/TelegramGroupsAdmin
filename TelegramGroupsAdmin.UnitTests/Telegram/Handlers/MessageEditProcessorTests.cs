using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Handlers;

/// <summary>
/// An edited message reports the editor's current names: the edit processor records them through
/// IUserIdentityService, dated by the edit, and rescans a rename inline like a new message.
/// </summary>
[TestFixture]
public class MessageEditProcessorTests
{
    private const long ChatId = -100123;
    private const long EditorId = 7;

    private ServiceProvider _provider = null!;
    private IUserIdentityService _identities = null!;
    private MessageEditProcessor _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<RenameRescan>(), Arg.Any<CancellationToken>())
            .Returns(ci => UserIdentity.ForTest(ci.Arg<ObservedUser>().Id, "A"));

        var services = new ServiceCollection();
        services.AddSingleton(_identities);
        // An unknown message ends the edit flow right after the observation.
        services.AddSingleton(Substitute.For<IMessageHistoryRepository>());
        services.AddSingleton(Substitute.For<IMessageEditService>());
        services.AddSingleton(Substitute.For<IMessageTranslationService>());
        _provider = services.BuildServiceProvider();

        _sut = new MessageEditProcessor(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MessageEditProcessor>.Instance);
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task EditedMessage_ObservesEditorWithEditDate()
    {
        var edited = EditedTextMessage(from: EditorId, editDate: new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));

        using var scope = _provider.CreateScope();
        await _sut.ProcessEditAsync(edited, scope, CancellationToken.None);

        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == EditorId
                && o.ObservedAt == new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero)
                && o.Source == ObservationSource.BotUpdate),
            Arg.Is<ProfileChangeContext>(c => c!.Chat!.Id == ChatId && c.MessageId == edited.MessageId),
            RenameRescan.Inline,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EditedMessage_WithoutEditDate_ObservesWithMessageDate()
    {
        var edited = EditedTextMessage(from: EditorId, editDate: null);

        using var scope = _provider.CreateScope();
        await _sut.ProcessEditAsync(edited, scope, CancellationToken.None);

        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == EditorId
                && o.ObservedAt == new DateTimeOffset(2026, 10, 3, 11, 0, 0, TimeSpan.Zero)),
            Arg.Any<ProfileChangeContext>(),
            RenameRescan.Inline,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EditedPrivateMessage_IsNotObserved()
    {
        // As for new DMs: a DM rename recorded without a rescan would use the rename up.
        var edited = EditedTextMessage(from: EditorId, editDate: null, chatType: ChatType.Private, chatId: EditorId);

        using var scope = _provider.CreateScope();
        await _sut.ProcessEditAsync(edited, scope, CancellationToken.None);

        await _identities.DidNotReceiveWithAnyArgs().ObserveAsync(default!, default!, default, default);
    }

    private static Message EditedTextMessage(long from, DateTime? editDate, ChatType chatType = ChatType.Supergroup, long chatId = ChatId) => new()
    {
        Id = 42,
        Date = new DateTime(2026, 10, 3, 11, 0, 0, DateTimeKind.Utc),
        EditDate = editDate,
        Chat = new Chat { Id = chatId, Type = chatType, Title = chatType == ChatType.Private ? null : "Group" },
        From = new User { Id = from, FirstName = "A", IsBot = false },
        Text = "edited"
    };
}
