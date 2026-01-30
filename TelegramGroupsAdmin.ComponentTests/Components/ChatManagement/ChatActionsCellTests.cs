using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.ChatManagement;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components.ChatManagement;

/// <summary>
/// Custom test context for ChatActionsCell with mocked dependencies.
/// </summary>
public abstract class ChatActionsCellTestContext : BunitContext
{
    protected ITelegramBotClientFactory BotClientFactory { get; }
    protected ITelegramOperations TelegramOperations { get; }
    protected IManagedChatsRepository ManagedChatsRepository { get; }
    protected IJobScheduler JobScheduler { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected ChatActionsCellTestContext()
    {
        // Create mocks FIRST (before AddMudServices locks the container)
        BotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        TelegramOperations = Substitute.For<ITelegramOperations>();
        ManagedChatsRepository = Substitute.For<IManagedChatsRepository>();
        JobScheduler = Substitute.For<IJobScheduler>();

        // Configure BotClientFactory to return TelegramOperations
        BotClientFactory.GetOperationsAsync()
            .Returns(TelegramOperations);

        // Register mocks
        Services.AddSingleton(BotClientFactory);
        Services.AddSingleton(ManagedChatsRepository);
        Services.AddSingleton(JobScheduler);
        Services.AddSingleton(Substitute.For<ILogger<ChatActionsCell>>());

        // THEN add MudBlazor services (includes ISnackbar)
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Set up JSInterop
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }

    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }

    protected static ManagedChatInfo CreateChatInfo(
        long chatId = -1001234567890,
        string chatName = "Test Chat",
        bool isActive = true,
        bool isDeleted = false)
    {
        return new ManagedChatInfo
        {
            Chat = new ManagedChatRecord(
                ChatId: chatId,
                ChatName: chatName,
                ChatType: ManagedChatType.Supergroup,
                BotStatus: BotChatStatus.Administrator,
                IsAdmin: true,
                AddedAt: DateTimeOffset.UtcNow,
                IsActive: isActive,
                IsDeleted: isDeleted,
                LastSeenAt: null,
                SettingsJson: null,
                ChatIconPath: null
            ),
            HealthStatus = new ChatHealthStatus
            {
                ChatId = chatId,
                Status = ChatHealthStatusType.Healthy,
                IsReachable = true
            },
            HasCustomSpamConfig = false,
            HasCustomWelcomeConfig = false
        };
    }
}

[TestFixture]
public class ChatActionsCellTests : ChatActionsCellTestContext
{
    [SetUp]
    public void Setup()
    {
        BotClientFactory.ClearReceivedCalls();
        TelegramOperations.ClearReceivedCalls();
        ManagedChatsRepository.ClearReceivedCalls();
        JobScheduler.ClearReceivedCalls();
    }

    // ─── Structure Tests ─────────────────────────────────────────────────────────

    [Test]
    public void RendersRefreshButton()
    {
        var chatInfo = CreateChatInfo();
        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // Should have a refresh button with the refresh icon
        Assert.That(cut.Markup, Does.Contain("Refresh"));
    }

    [Test]
    public void RendersConfigureButton()
    {
        var chatInfo = CreateChatInfo();
        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        Assert.That(cut.Markup, Does.Contain("Configure"));
    }

    [Test]
    public void RendersLeaveButton()
    {
        var chatInfo = CreateChatInfo();
        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // Should have leave button (ExitToApp icon)
        Assert.That(cut.Markup, Does.Contain("Leave chat"));
    }

    [Test]
    public void LeaveButton_HasErrorColor()
    {
        var chatInfo = CreateChatInfo();
        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // The leave button should have error color (MudBlazor uses mud-error-text for text-based icon buttons)
        Assert.That(cut.Markup, Does.Contain("mud-error-text"));
    }

    // ─── Refresh Health Tests ────────────────────────────────────────────────────

    [Test]
    public async Task RefreshButton_SchedulesHealthCheckJob()
    {
        var chatInfo = CreateChatInfo(chatId: -100555);

        JobScheduler.ScheduleJobAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns("job-id-123");

        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // Find and click the refresh button
        var refreshButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Refresh") == true);
        await cut.InvokeAsync(() => refreshButton.Click());

        // Verify job was scheduled
        await JobScheduler.Received(1).ScheduleJobAsync(
            BackgroundJobNames.ChatHealthCheck,
            Arg.Any<object>(),
            0, // delaySeconds
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshButton_InvokesCallback()
    {
        var chatInfo = CreateChatInfo();
        var callbackInvoked = false;

        JobScheduler.ScheduleJobAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns("job-id-123");

        var cut = Render<ChatActionsCell>(p => p
            .Add(x => x.ChatInfo, chatInfo)
            .Add(x => x.OnHealthRefreshQueued, () => { callbackInvoked = true; return Task.CompletedTask; }));

        var refreshButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Refresh") == true);
        await cut.InvokeAsync(() => refreshButton.Click());

        Assert.That(callbackInvoked, Is.True);
    }

    // ─── Leave Chat Tests ────────────────────────────────────────────────────────

    [Test]
    public void LeaveButton_OpensConfirmationDialog()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo(chatName: "My Test Group");

        // Render the component inside the dialog provider context
        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // Click the leave button
        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        leaveButton.Click();

        // Dialog should appear with confirmation text
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Are you sure you want the bot to leave"));
            Assert.That(provider.Markup, Does.Contain("My Test Group"));
        });
    }

    [Test]
    public void LeaveDialog_ShowsWarningBullets()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo();

        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        leaveButton.Click();

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Remove the bot from the Telegram group"));
            Assert.That(provider.Markup, Does.Contain("Delete all chat configuration and history"));
            Assert.That(provider.Markup, Does.Contain("This action cannot be undone"));
        });
    }

    [Test]
    public async Task LeaveConfirmed_CallsLeaveChatApi()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo(chatId: -100777);

        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // Click leave button
        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        await cut.InvokeAsync(() => leaveButton.Click());

        // Wait for dialog
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Leave Chat"));
        });

        // Click confirm button in dialog
        var confirmButton = provider.FindAll("button")
            .First(b => b.TextContent.Contains("Leave Chat"));
        await cut.InvokeAsync(() => confirmButton.Click());

        // Verify API was called
        await TelegramOperations.Received(1).LeaveChatAsync(-100777, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LeaveConfirmed_DeletesFromDatabase()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo(chatId: -100888);

        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // Click leave button
        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        await cut.InvokeAsync(() => leaveButton.Click());

        // Wait for dialog
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Leave Chat"));
        });

        // Click confirm button
        var confirmButton = provider.FindAll("button")
            .First(b => b.TextContent.Contains("Leave Chat"));
        await cut.InvokeAsync(() => confirmButton.Click());

        // Verify database delete was called
        await ManagedChatsRepository.Received(1).DeleteAsync(-100888);
    }

    [Test]
    public async Task LeaveConfirmed_InvokesCallback()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo();
        var callbackInvoked = false;

        var cut = Render<ChatActionsCell>(p => p
            .Add(x => x.ChatInfo, chatInfo)
            .Add(x => x.OnChatLeft, () => { callbackInvoked = true; return Task.CompletedTask; }));

        // Click leave button
        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        await cut.InvokeAsync(() => leaveButton.Click());

        // Wait for dialog
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Leave Chat"));
        });

        // Click confirm button
        var confirmButton = provider.FindAll("button")
            .First(b => b.TextContent.Contains("Leave Chat"));
        await cut.InvokeAsync(() => confirmButton.Click());

        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public async Task LeaveCancelled_DoesNotCallApi()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo();

        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // Click leave button
        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        await cut.InvokeAsync(() => leaveButton.Click());

        // Wait for dialog
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });

        // Click cancel button
        var cancelButton = provider.FindAll("button")
            .First(b => b.TextContent.Contains("Cancel"));
        await cut.InvokeAsync(() => cancelButton.Click());

        // Verify API was NOT called
        await TelegramOperations.DidNotReceive().LeaveChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await ManagedChatsRepository.DidNotReceive().DeleteAsync(Arg.Any<long>());
    }

    [Test]
    public async Task LeaveCancelled_DoesNotInvokeCallback()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo();
        var callbackInvoked = false;

        var cut = Render<ChatActionsCell>(p => p
            .Add(x => x.ChatInfo, chatInfo)
            .Add(x => x.OnChatLeft, () => { callbackInvoked = true; return Task.CompletedTask; }));

        // Click leave button
        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        await cut.InvokeAsync(() => leaveButton.Click());

        // Wait for dialog
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });

        // Click cancel button
        var cancelButton = provider.FindAll("button")
            .First(b => b.TextContent.Contains("Cancel"));
        await cut.InvokeAsync(() => cancelButton.Click());

        Assert.That(callbackInvoked, Is.False);
    }

    // ─── Error Handling Tests ────────────────────────────────────────────────────

    [Test]
    public async Task RefreshButton_WhenSchedulingFails_DoesNotInvokeCallback()
    {
        var chatInfo = CreateChatInfo();
        var callbackInvoked = false;

        JobScheduler.ScheduleJobAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new Exception("Scheduling failed")));

        var cut = Render<ChatActionsCell>(p => p
            .Add(x => x.ChatInfo, chatInfo)
            .Add(x => x.OnHealthRefreshQueued, () => { callbackInvoked = true; return Task.CompletedTask; }));

        var refreshButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Refresh") == true);
        await cut.InvokeAsync(() => refreshButton.Click());

        Assert.That(callbackInvoked, Is.False);
    }

    [Test]
    public async Task LeaveConfirmed_WhenTelegramApiFails_DoesNotDeleteFromDb()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo();

        TelegramOperations.LeaveChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("Telegram API error")));

        var cut = Render<ChatActionsCell>(p => p.Add(x => x.ChatInfo, chatInfo));

        // Open and confirm dialog
        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        await cut.InvokeAsync(() => leaveButton.Click());

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Leave Chat"));
        });

        var confirmButton = provider.FindAll("button")
            .First(b => b.TextContent.Contains("Leave Chat"));
        await cut.InvokeAsync(() => confirmButton.Click());

        // Database delete should NOT be called when Telegram API fails
        await ManagedChatsRepository.DidNotReceive().DeleteAsync(Arg.Any<long>());
    }

    [Test]
    public async Task LeaveConfirmed_WhenTelegramApiFails_DoesNotInvokeCallback()
    {
        var provider = RenderDialogProvider();
        var chatInfo = CreateChatInfo();
        var callbackInvoked = false;

        TelegramOperations.LeaveChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("Telegram API error")));

        var cut = Render<ChatActionsCell>(p => p
            .Add(x => x.ChatInfo, chatInfo)
            .Add(x => x.OnChatLeft, () => { callbackInvoked = true; return Task.CompletedTask; }));

        // Open and confirm dialog
        var leaveButton = cut.FindAll("button").First(b =>
            b.Attributes["title"]?.Value?.Contains("Leave") == true);
        await cut.InvokeAsync(() => leaveButton.Click());

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Leave Chat"));
        });

        var confirmButton = provider.FindAll("button")
            .First(b => b.TextContent.Contains("Leave Chat"));
        await cut.InvokeAsync(() => confirmButton.Click());

        Assert.That(callbackInvoked, Is.False);
    }
}
