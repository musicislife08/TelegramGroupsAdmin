using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

[TestFixture]
public class HelpCommandTests
{
    private HelpCommand _command = null!;
    private Message _message = null!;

    /// <summary>
    /// Minimal stub implementing IBotCommand with configurable properties.
    /// </summary>
    private sealed class StubCommand : IBotCommand
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required PermissionLevel MinPermissionLevel { get; init; }
        public string Usage => $"/{Name}";
        public bool RequiresReply => false;
        public bool DeleteCommandMessage => false;
        public int? DeleteResponseAfterSeconds => null;

        public Task<CommandResult> ExecuteAsync(
            Message message,
            string[] args,
            PermissionLevel userPermission,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult(TelegramMessage.Plain("stub"), false));
    }

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();

        // Public (Member-tier) commands
        foreach (var name in new[] { "help", "start", "mystatus", "report", "link", "invite" })
        {
            services.AddKeyedScoped<IBotCommand>(name, (_, _) => new StubCommand
            {
                Name = name,
                Description = $"{name} description",
                MinPermissionLevel = PermissionLevel.Member
            });
        }

        // Admin-tier (moderation) commands
        foreach (var name in new[] { "ban", "delete", "spam", "mute", "tempban", "trust", "unban", "warn" })
        {
            services.AddKeyedScoped<IBotCommand>(name, (_, _) => new StubCommand
            {
                Name = name,
                Description = $"{name} description",
                MinPermissionLevel = PermissionLevel.Admin
            });
        }

        var provider = services.BuildServiceProvider();
        _command = new HelpCommand(provider);
        _message = new Message { Text = "/help" };
    }

    [Test]
    public async Task Help_Footer_ShowsAdmin_ForAdminTier()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Admin);
        Assert.That(result.Message.Text, Does.Contain("Permission: Admin"));
        Assert.That(result.Message.Text, Does.Not.Contain("Permission: GlobalAdmin"));
    }

    [Test]
    public async Task Help_Footer_ShowsMember_ForMemberTier()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Member);
        Assert.That(result.Message.Text, Does.Contain("Permission: Member"));
    }

    [Test]
    public async Task Help_HidesAdminCommandsSection_ForMember()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Member);
        Assert.That(result.Message.Text, Does.Not.Contain("Admin Commands:"));
    }

    [Test]
    public async Task Help_ShowsAdminCommandsSection_ForAdmin()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Admin);
        Assert.That(result.Message.Text, Does.Contain("Admin Commands:"));
    }

    [Test]
    public async Task Help_Member_ListsPublicCommands_IncludingMyStatus()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Member);
        Assert.That(result.Message.Text, Does.Contain("/mystatus"));
        Assert.That(result.Message.Text, Does.Contain("/report"));
        Assert.That(result.Message.Text, Does.Not.Contain("Admin Commands:"));
    }

    [Test]
    public async Task Help_Admin_ListsModeration_IncludingMute()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Admin);
        Assert.That(result.Message.Text, Does.Contain("Admin Commands:"));
        Assert.That(result.Message.Text, Does.Contain("/mute"));
        Assert.That(result.Message.Text, Does.Contain("/ban"));
    }

    [Test]
    public async Task Help_Admin_ExcludesStart()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Admin);
        Assert.That(result.Message.Text, Does.Not.Contain("/start"));
    }

    [Test]
    public async Task Help_Member_ExcludesStart()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Member);
        Assert.That(result.Message.Text, Does.Not.Contain("/start"));
    }

    [Test]
    public async Task Help_GlobalAdmin_SeesAdminSection()
    {
        var result = await _command.ExecuteAsync(_message, [], PermissionLevel.GlobalAdmin);
        Assert.That(result.Message.Text, Does.Contain("Admin Commands:"));
        Assert.That(result.Message.Text, Does.Contain("Permission: GlobalAdmin"));
    }
}
