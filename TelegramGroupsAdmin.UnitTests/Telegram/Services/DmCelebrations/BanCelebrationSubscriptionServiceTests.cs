using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.DmCelebrations;

[TestFixture]
public class BanCelebrationSubscriptionServiceTests
{
    private const long ChatId = -100059667856554L;
    private const long UserId = 42L;
    private static readonly ChatIdentity Chat = new(ChatId, "Workshop Alumni");
    private static readonly UserIdentity User = new(UserId, "Kim", null, "kim");

    private IBanCelebrationSubscriberRepository _repository = null!;
    private ITelegramUserRepository _telegramUsers = null!;
    private IManagedChatsRepository _managedChats = null!;
    private IBotMessageService _messages = null!;
    private IBotUserService _botUser = null!;
    private IJobScheduler _jobs = null!;
    private BanCelebrationSubscriptionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IBanCelebrationSubscriberRepository>();
        _telegramUsers = Substitute.For<ITelegramUserRepository>();
        _managedChats = Substitute.For<IManagedChatsRepository>();
        _messages = Substitute.For<IBotMessageService>();
        _botUser = Substitute.For<IBotUserService>();
        _jobs = Substitute.For<IJobScheduler>();

        _botUser.GetMeAsync(Arg.Any<CancellationToken>()).Returns(new User { Id = 1, IsBot = true, FirstName = "Bot", Username = "tga_bot" });
        _messages.SendAndSaveMessageAsync(Arg.Any<long>(), Arg.Any<TelegramMessage>(), Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 777, Chat = new Chat { Id = ChatId } });
        _jobs.ScheduleJobAsync(Arg.Any<string>(), Arg.Any<DeleteMessagePayload>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("job-777");
        _jobs.CancelJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        _sut = new BanCelebrationSubscriptionService(
            _repository, _telegramUsers, _managedChats, _messages, _botUser, _jobs,
            new PipelineMetrics(), NullLogger<BanCelebrationSubscriptionService>.Instance);
    }

    private void DmEnabled(bool enabled, bool isBanned = false) =>
        _telegramUsers.GetOrCreateAsync(Arg.Is<UserIdentity>(u => u!.Id == UserId), false, Arg.Any<CancellationToken>())
            .Returns(new TelegramUser(
                TelegramUserId: UserId, Username: "kim", FirstName: "Kim", LastName: null,
                UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
                IsBot: false, IsTrusted: false, IsBanned: isBanned, KickCount: 0, BotDmEnabled: enabled,
                FirstSeenAt: DateTimeOffset.UtcNow, LastSeenAt: DateTimeOffset.UtcNow,
                CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow));

    private static ManagedChatRecord ManagedChat(long chatId, string name) => new(
        Identity: new ChatIdentity(chatId, name), ChatType: ManagedChatType.Supergroup,
        BotStatus: BotChatStatus.Administrator, IsAdmin: true, AddedAt: DateTimeOffset.UtcNow,
        IsActive: true, IsDeleted: false, LastSeenAt: null, SettingsJson: null, ChatIconPath: null);

    private static BanCelebrationSubscriber Row(int? promptId = null, string? jobId = null) =>
        new(UserId, ChatId, DateTimeOffset.UtcNow, promptId, jobId);

    [Test]
    public async Task SubscribeAsync_DmEnabled_UpsertsAndReturnsSubscribedWithoutPrompt()
    {
        DmEnabled(true);

        var result = await _sut.SubscribeAsync(Chat, User);

        Assert.That(result, Is.EqualTo(DmCelebrationSubscribeResult.Subscribed));
        await _repository.Received(1).UpsertAsync(UserId, ChatId, Arg.Any<CancellationToken>());
        await _messages.DidNotReceiveWithAnyArgs().SendAndSaveMessageAsync(default, default(TelegramMessage)!);
    }

    [Test]
    public async Task SubscribeAsync_BannedUser_ReturnsNotAllowedWithoutUpsertOrPrompt()
    {
        DmEnabled(false, isBanned: true);

        var result = await _sut.SubscribeAsync(Chat, User);

        Assert.That(result, Is.EqualTo(DmCelebrationSubscribeResult.NotAllowed));
        await _repository.DidNotReceiveWithAnyArgs().UpsertAsync(default, default);
        await _messages.DidNotReceiveWithAnyArgs().SendAndSaveMessageAsync(default, default(TelegramMessage)!);
        await _jobs.DidNotReceiveWithAnyArgs().ScheduleJobAsync(default!, default(DeleteMessagePayload)!, default);
    }

    [Test]
    public async Task SubscribeAsync_EnsuresTelegramUserExistsBeforeUpsert()
    {
        DmEnabled(true);

        await _sut.SubscribeAsync(Chat, User);

        Received.InOrder(() =>
        {
            _telegramUsers.GetOrCreateAsync(Arg.Is<UserIdentity>(u => u!.Id == UserId), false, Arg.Any<CancellationToken>());
            _repository.UpsertAsync(UserId, ChatId, Arg.Any<CancellationToken>());
        });
        await _telegramUsers.DidNotReceiveWithAnyArgs().GetByTelegramIdAsync(default);
    }

    [Test]
    public async Task SubscribeAsync_DmDisabled_PostsDeepLinkPromptAndSchedulesSixtySecondDelete()
    {
        DmEnabled(false);
        InlineKeyboardMarkup? keyboard = null;
        TelegramMessage? prompt = null;
        _messages.SendAndSaveMessageAsync(ChatId, Arg.Do<TelegramMessage>(m => prompt = m), Arg.Any<ReplyParameters?>(),
                Arg.Do<InlineKeyboardMarkup?>(k => keyboard = k), Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 777, Chat = new Chat { Id = ChatId } });

        var result = await _sut.SubscribeAsync(Chat, User);

        Assert.That(result, Is.EqualTo(DmCelebrationSubscribeResult.AwaitingStart));
        var button = keyboard!.InlineKeyboard.Single().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(button.Url, Is.EqualTo("https://t.me/tga_bot?start=dmcel_-100059667856554"));
            Assert.That(prompt!.Entities.Any(e => e.Type == MessageEntityType.TextMention && e.User!.Id == UserId), Is.True);
            Assert.That(prompt.Text, Does.Contain("Workshop Alumni"));
        }
        await _jobs.Received(1).ScheduleJobAsync(
            "DeleteMessage",
            Arg.Is<DeleteMessagePayload>(p => p!.ChatId == ChatId && p.MessageId == 777),
            60, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).SetPromptAsync(UserId, ChatId, 777, "job-777", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SubscribeAsync_DmDisabledWithOpenPrompt_CleansOldPromptBeforePostingNew()
    {
        DmEnabled(false);
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));

        await _sut.SubscribeAsync(Chat, User);

        Received.InOrder(() =>
        {
            _jobs.CancelJobAsync("job-500", Arg.Any<CancellationToken>());
            _messages.DeleteAndMarkMessageAsync(ChatId, 500, Arg.Any<string>(), Arg.Any<CancellationToken>());
            _repository.ClearPromptAsync(UserId, ChatId, Arg.Any<CancellationToken>());
            _messages.SendAndSaveMessageAsync(ChatId, Arg.Any<TelegramMessage>(), Arg.Any<ReplyParameters?>(),
                Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task UnsubscribeAsync_ExistingRowWithPrompt_CleansPromptAndDeletes()
    {
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));

        var removed = await _sut.UnsubscribeAsync(Chat, User);

        Assert.That(removed, Is.True);
        await _jobs.Received(1).CancelJobAsync("job-500", Arg.Any<CancellationToken>());
        await _messages.Received(1).DeleteAndMarkMessageAsync(ChatId, 500, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnsubscribeAsync_NoRow_ReturnsFalseAndDeletesNothing()
    {
        var removed = await _sut.UnsubscribeAsync(Chat, User);

        Assert.That(removed, Is.False);
        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Test]
    public async Task ConfirmFromStartAsync_NoRow_ReturnsNullAndTouchesNothing()
    {
        var chat = await _sut.ConfirmFromStartAsync(ChatId, User);

        Assert.That(chat, Is.Null);
        await _jobs.DidNotReceiveWithAnyArgs().CancelJobAsync(default!);
        await _messages.DidNotReceiveWithAnyArgs().DeleteAndMarkMessageAsync(default, default);
    }

    [Test]
    public async Task ConfirmFromStartAsync_RowWithPrompt_CancelsJobDeletesPromptClearsAndReturnsChat()
    {
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));
        _managedChats.GetByChatIdAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(ManagedChat(ChatId, "Workshop Alumni"));

        var chat = await _sut.ConfirmFromStartAsync(ChatId, User);

        Assert.That(chat!.ChatName, Is.EqualTo("Workshop Alumni"));
        await _jobs.Received(1).CancelJobAsync("job-500", Arg.Any<CancellationToken>());
        await _messages.Received(1).DeleteAndMarkMessageAsync(ChatId, 500, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).ClearPromptAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnsubscribeAsync_PromptAlreadyDeleted_StillClearsAndDeletes()
    {
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));
        _messages.DeleteAndMarkMessageAsync(ChatId, 500, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Bad Request: message to delete not found", 400));

        bool removed = false;
        Assert.DoesNotThrowAsync(async () => removed = await _sut.UnsubscribeAsync(Chat, User));

        Assert.That(removed, Is.True);
        await _repository.Received(1).ClearPromptAsync(UserId, ChatId, Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SubscribeAsync_StalePromptWhoseJobAlreadyRan_SkipsDeleteAndPostsNewPrompt()
    {
        DmEnabled(false);
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));
        _jobs.CancelJobAsync("job-500", Arg.Any<CancellationToken>()).Returns(false);

        await _sut.SubscribeAsync(Chat, User);

        await _messages.DidNotReceiveWithAnyArgs().DeleteAndMarkMessageAsync(default, default);
        await _repository.Received(1).ClearPromptAsync(UserId, ChatId, Arg.Any<CancellationToken>());
        await _messages.Received(1).SendAndSaveMessageAsync(ChatId, Arg.Any<TelegramMessage>(), Arg.Any<ReplyParameters?>(),
            Arg.Any<InlineKeyboardMarkup?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConfirmFromStartAsync_PromptAlreadyDeleted_ReturnsChatAndClears()
    {
        _repository.GetAsync(UserId, ChatId, Arg.Any<CancellationToken>()).Returns(Row(500, "job-500"));
        _managedChats.GetByChatIdAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(ManagedChat(ChatId, "Workshop Alumni"));
        _messages.DeleteAndMarkMessageAsync(ChatId, 500, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Bad Request: message to delete not found", 400));

        var chat = await _sut.ConfirmFromStartAsync(ChatId, User);

        Assert.That(chat!.ChatName, Is.EqualTo("Workshop Alumni"));
        await _repository.Received(1).ClearPromptAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    private static ChatMemberUpdated MemberUpdate(ChatType chatType, ChatMember oldMember, ChatMember newMember) => new()
    {
        Chat = new Chat { Id = chatType == ChatType.Private ? UserId : ChatId, Type = chatType, Title = "Workshop Alumni" },
        From = new User { Id = UserId, FirstName = "Kim" },
        Date = DateTime.UtcNow,
        OldChatMember = oldMember,
        NewChatMember = newMember
    };

    private static User TgUser => new() { Id = UserId, FirstName = "Kim" };

    [Test]
    public async Task HandleChatMemberUpdateAsync_MemberLeaves_RemovesThatChat()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberMember { User = TgUser }, new ChatMemberLeft { User = TgUser }));

        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdateAsync_MemberKicked_RemovesThatChat()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberMember { User = TgUser }, new ChatMemberBanned { User = TgUser }));

        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdateAsync_RestrictedMemberLeaves_RemovesThatChat()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberRestricted { User = TgUser, IsMember = true },
            new ChatMemberRestricted { User = TgUser, IsMember = false }));

        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdateAsync_MemberRestrictedButStillInChat_DoesNothing()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberMember { User = TgUser },
            new ChatMemberRestricted { User = TgUser, IsMember = true }));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Test]
    public async Task HandleChatMemberUpdateAsync_AdministratorLeaves_RemovesThatChat()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberAdministrator { User = TgUser }, new ChatMemberLeft { User = TgUser }));

        await _repository.Received(1).DeleteAsync(UserId, ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleChatMemberUpdateAsync_Join_DoesNothing()
    {
        await _sut.HandleChatMemberUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberLeft { User = TgUser }, new ChatMemberMember { User = TgUser }));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_PrivateChatBlocked_DisablesDmAndRemovesAll()
    {
        _repository.DeleteAllForUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(2);

        await _sut.HandleBotMembershipUpdateAsync(MemberUpdate(ChatType.Private,
            new ChatMemberMember { User = TgUser }, new ChatMemberBanned { User = TgUser }));

        await _telegramUsers.Received(1).DisableBotDmAsync(UserId, Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteAllForUserAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_PrivateChatUnblocked_DoesNothing()
    {
        await _sut.HandleBotMembershipUpdateAsync(MemberUpdate(ChatType.Private,
            new ChatMemberBanned { User = TgUser }, new ChatMemberMember { User = TgUser }));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAllForUserAsync(default);
        await _telegramUsers.DidNotReceiveWithAnyArgs().DisableBotDmAsync(default);
    }

    [Test]
    public async Task HandleBotMembershipUpdateAsync_GroupChat_DoesNothing()
    {
        await _sut.HandleBotMembershipUpdateAsync(MemberUpdate(ChatType.Supergroup,
            new ChatMemberMember { User = TgUser }, new ChatMemberBanned { User = TgUser }));

        await _repository.DidNotReceiveWithAnyArgs().DeleteAllForUserAsync(default);
    }

    [Test]
    public async Task RemoveAllForUserAsync_DeletesEveryRowForTheUser()
    {
        await _sut.RemoveAllForUserAsync(User, SubscriptionRemovalReason.Banned);

        await _repository.Received(1).DeleteAllForUserAsync(UserId, Arg.Any<CancellationToken>());
    }
}
