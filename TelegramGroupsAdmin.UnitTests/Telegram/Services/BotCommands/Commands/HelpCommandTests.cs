using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands.Commands;

[TestFixture]
public class HelpCommandTests
{
    private HelpCommand _command = null!;
    private Message _message = null!;

    [SetUp]
    public void SetUp()
    {
        _command = new HelpCommand();
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
}
