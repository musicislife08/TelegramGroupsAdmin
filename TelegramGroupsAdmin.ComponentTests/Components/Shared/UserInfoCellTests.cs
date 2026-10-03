using Bunit;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components.Shared;

[TestFixture]
public class UserInfoCellTests : MudBlazorTestContext
{
    private sealed record StubUser(
        bool IsTrusted = false,
        bool IsAdmin = false,
        bool IsTagged = false) : IUserDisplayInfo
    {
        public long TelegramUserId => 42;
        public string? Username => "someone";
        public string? FirstName => "Some";
        public string? LastName => "One";
        public string? UserPhotoPath => null;
        public string DisplayName => "Some One";
    }

    [Test]
    public void TrustedUser_ShowsLabelledTrustedBadge()
    {
        var cut = Render<UserInfoCell>(p => p.Add(x => x.User, new StubUser(IsTrusted: true)));

        Assert.That(cut.FindAll("[aria-label='Trusted user']"), Has.Count.EqualTo(1));
    }

    [Test]
    public void AdminUser_ShowsLabelledAdminBadge()
    {
        var cut = Render<UserInfoCell>(p => p.Add(x => x.User, new StubUser(IsAdmin: true)));

        Assert.That(cut.FindAll("[aria-label='Chat admin']"), Has.Count.EqualTo(1));
    }

    /// <summary>
    /// An accessible name only reaches assistive technology when neither the labelled element nor
    /// any ancestor is aria-hidden; MudIcon hard-codes aria-hidden="true" on its svg.
    /// </summary>
    [TestCase("Trusted user", true, false)]
    [TestCase("Chat admin", false, true)]
    public void Badge_IsExposedToTheAccessibilityTree(string name, bool trusted, bool admin)
    {
        var cut = Render<UserInfoCell>(p => p.Add(x => x.User, new StubUser(IsTrusted: trusted, IsAdmin: admin)));

        var badge = cut.Find($"[aria-label='{name}']");
        Assert.That(badge.GetAttribute("role"), Is.EqualTo("img"), "the badge is an image with a name");
        for (var element = badge; element is not null; element = element.ParentElement)
        {
            Assert.That(element.GetAttribute("aria-hidden"), Is.Not.EqualTo("true"),
                $"<{element.LocalName}> hides the '{name}' badge from assistive technology");
        }
    }

    [Test]
    public void PlainUser_ShowsNoBadges()
    {
        var cut = Render<UserInfoCell>(p => p.Add(x => x.User, new StubUser()));

        Assert.That(cut.FindAll("[aria-label='Trusted user']"), Is.Empty);
        Assert.That(cut.FindAll("[aria-label='Chat admin']"), Is.Empty);
    }

    [Test]
    public void ShowBadgesFalse_HidesBadges()
    {
        var cut = Render<UserInfoCell>(p => p
            .Add(x => x.User, new StubUser(IsTrusted: true, IsAdmin: true))
            .Add(x => x.ShowBadges, false));

        Assert.That(cut.FindAll("[aria-label]"), Is.Empty);
    }
}
