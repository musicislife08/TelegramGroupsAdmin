using Bunit;
using TelegramGroupsAdmin.Components.Shared.ChatManagement;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components.ChatManagement;

[TestFixture]
public class ChatStatusChipTests : MudBlazorTestContext
{
    // ─── Bot Status Tests ────────────────────────────────────────────────────────

    [Test]
    public void BotStatus_Administrator_ShowsSuccessColor()
    {
        var cut = Render<ChatStatusChip>(p => p.Add(x => x.BotStatus, BotChatStatus.Administrator));

        Assert.That(cut.Markup, Does.Contain("Administrator"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-success"));
    }

    [Test]
    public void BotStatus_Member_ShowsInfoColor()
    {
        var cut = Render<ChatStatusChip>(p => p.Add(x => x.BotStatus, BotChatStatus.Member));

        Assert.That(cut.Markup, Does.Contain("Member"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-info"));
    }

    [Test]
    public void BotStatus_Left_ShowsDefaultColor()
    {
        var cut = Render<ChatStatusChip>(p => p.Add(x => x.BotStatus, BotChatStatus.Left));

        Assert.That(cut.Markup, Does.Contain("Left"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-default"));
    }

    [Test]
    public void BotStatus_Kicked_ShowsErrorColor()
    {
        var cut = Render<ChatStatusChip>(p => p.Add(x => x.BotStatus, BotChatStatus.Kicked));

        Assert.That(cut.Markup, Does.Contain("Kicked"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-error"));
    }

    // ─── Health Status Tests ─────────────────────────────────────────────────────

    [Test]
    public void HealthStatus_Healthy_ShowsSuccessColor()
    {
        var cut = Render<ChatStatusChip>(p => p.Add(x => x.HealthStatus, ChatHealthStatusType.Healthy));

        Assert.That(cut.Markup, Does.Contain("Healthy"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-success"));
    }

    [Test]
    public void HealthStatus_Warning_ShowsWarningColor()
    {
        var cut = Render<ChatStatusChip>(p => p.Add(x => x.HealthStatus, ChatHealthStatusType.Warning));

        Assert.That(cut.Markup, Does.Contain("Warning"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-warning"));
    }

    [Test]
    public void HealthStatus_Error_ShowsErrorColor()
    {
        var cut = Render<ChatStatusChip>(p => p.Add(x => x.HealthStatus, ChatHealthStatusType.Error));

        Assert.That(cut.Markup, Does.Contain("Error"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-error"));
    }

    [Test]
    public void HealthStatus_Unknown_ShowsDefaultColor()
    {
        var cut = Render<ChatStatusChip>(p => p.Add(x => x.HealthStatus, ChatHealthStatusType.Unknown));

        Assert.That(cut.Markup, Does.Contain("Unknown"));
        Assert.That(cut.Markup, Does.Contain("mud-chip-color-default"));
    }

    // ─── Warnings Tests ──────────────────────────────────────────────────────────

    [Test]
    public void HealthStatus_WithWarnings_ShowsWarningIcon()
    {
        List<string> warnings = ["Missing permission", "Bot not admin"];

        var cut = Render<ChatStatusChip>(p => p
            .Add(x => x.HealthStatus, ChatHealthStatusType.Warning)
            .Add(x => x.Warnings, warnings));

        // Should show the warning icon
        Assert.That(cut.Markup, Does.Contain("mud-icon-root"));
        Assert.That(cut.Markup, Does.Contain("Warning")); // Material icon name
    }

    [Test]
    public void HealthStatus_WithoutWarnings_NoWarningIcon()
    {
        var cut = Render<ChatStatusChip>(p => p
            .Add(x => x.HealthStatus, ChatHealthStatusType.Healthy)
            .Add(x => x.Warnings, (IReadOnlyList<string>?)null));

        // Should not have the warning icon (only the chip)
        var iconCount = cut.FindAll("svg").Count;
        Assert.That(iconCount, Is.EqualTo(0), "Should not render warning icon when no warnings");
    }

    [Test]
    public void HealthStatus_WithEmptyWarnings_NoWarningIcon()
    {
        var cut = Render<ChatStatusChip>(p => p
            .Add(x => x.HealthStatus, ChatHealthStatusType.Healthy)
            .Add(x => x.Warnings, (List<string>)[]));

        // Should not have the warning icon (only the chip)
        var iconCount = cut.FindAll("svg").Count;
        Assert.That(iconCount, Is.EqualTo(0), "Should not render warning icon when warnings list is empty");
    }

    // ─── Edge Cases ──────────────────────────────────────────────────────────────

    [Test]
    public void NoParameters_RendersNothing()
    {
        var cut = Render<ChatStatusChip>();

        // Should render empty when no status is provided
        Assert.That(cut.Markup.Trim(), Is.Empty);
    }

    [Test]
    public void BotStatus_TakesPrecedence_WhenBothSet()
    {
        // When both are set, BotStatus should be rendered (it's checked first in the template)
        var cut = Render<ChatStatusChip>(p => p
            .Add(x => x.BotStatus, BotChatStatus.Administrator)
            .Add(x => x.HealthStatus, ChatHealthStatusType.Error));

        Assert.That(cut.Markup, Does.Contain("Administrator"));
        Assert.That(cut.Markup, Does.Not.Contain("Error"));
    }
}
