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
