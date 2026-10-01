using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.E2ETests.PageObjects.Settings;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Testing.Golden;
using InviteStatus = TelegramGroupsAdmin.Data.Models.InviteStatus;
using UserStatus = TelegramGroupsAdmin.Core.Models.UserStatus;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Invite creation on the Web Admin Accounts page and registration through the generated link, on
/// canonical as it is: the invite the Owner creates and the account the invitee registers ARE the
/// assertion subjects, and both are read back from this test's clone. Canonical has users, so the
/// register page is in its invite-code (non-first-run) mode; canonical's global <c>sendgrid_config</c>
/// is enabled but <c>04_configs.api_keys.json</c> carries no SendGrid key, so email verification is
/// off and the invitee can log in straight away (the register page says as much).
/// </summary>
[TestFixture]
public class InviteGoldenTests : GoldenE2ETestBase
{
    private const string PermissionAdmin = "Admin - Chat-scoped moderation";
    private const string PermissionGlobalAdmin = "GlobalAdmin - Global moderation";
    private const string PermissionOwner = "Owner - Full system access";

    private static readonly Regex InviteLinkPattern = new(@"^https?://[^/]+/register\?invite=[0-9a-f-]{36}$");

    private WebAdminAccountsPage _accounts = null!;
    private int _canonicalInviteCount;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        // The dialog offers permission options up to the current user's own level: pin the two anchors'
        // levels so a canonical change fails here, not as a confusing dropdown mismatch.
        var levels = await context.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.OwnerId || u.Id == GoldenDatasetConstants.WebUsers.GlobalAdminId)
            .ToDictionaryAsync(u => u.Id, u => u.PermissionLevel);
        Assert.That(levels[GoldenDatasetConstants.WebUsers.OwnerId], Is.EqualTo(2), "PermissionLevel.Owner");
        Assert.That(levels[GoldenDatasetConstants.WebUsers.GlobalAdminId], Is.EqualTo(1), "PermissionLevel.GlobalAdmin");

        _canonicalInviteCount = await context.Invites.CountAsync();
    }

    [SetUp]
    public void CreatePageObjects()
    {
        _accounts = new WebAdminAccountsPage(Page);
    }

    [Test]
    public async Task Owner_CreatesInvite_ShowsTheLinkAndPersistsIt()
    {
        await OpenAccountsPageAsync(LoginAsOwnerAsync);
        await _accounts.ClickCreateUserAsync();
        await Expect(_accounts.DialogTitle).ToHaveTextAsync("Create User");

        // The Owner is offered every level; pick a non-default one so the read-back proves the choice travelled.
        await _accounts.OpenPermissionOptionsAsync();
        await Expect(_accounts.PermissionOptions).ToHaveTextAsync([PermissionAdmin, PermissionGlobalAdmin, PermissionOwner]);
        await _accounts.SelectPermissionOptionAsync("GlobalAdmin");

        await _accounts.FillValidDaysAsync(3);
        await Expect(_accounts.ValidDaysInput).ToHaveValueAsync("3");

        var generatedAt = DateTimeOffset.UtcNow;
        await _accounts.GenerateInviteAsync();

        await Expect(_accounts.DialogTitle).ToHaveTextAsync("Invite Link Created");
        await Expect(_accounts.InviteLinkInput).ToHaveValueAsync(InviteLinkPattern);
        await Expect(_accounts.SnackbarWithText("Invite created successfully")).ToBeVisibleAsync();

        var token = TokenFromLink(await ReadInviteLinkAsync());
        await _accounts.ClickDoneAsync();

        await using var ctx = CreateDbContext();
        var invite = await ctx.Invites.AsNoTracking().Where(i => i.Token == token)
            .Select(i => new { i.CreatedBy, i.PermissionLevel, i.Status, i.UsedBy, i.ExpiresAt }).SingleAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(invite.CreatedBy, Is.EqualTo(GoldenDatasetConstants.WebUsers.OwnerId));
            Assert.That(invite.PermissionLevel, Is.EqualTo(1), "PermissionLevel.GlobalAdmin as picked in the dialog");
            Assert.That(invite.Status, Is.EqualTo(InviteStatus.Pending));
            Assert.That(invite.UsedBy, Is.Null);
            Assert.That(invite.ExpiresAt, Is.EqualTo(generatedAt.AddDays(3)).Within(TimeSpan.FromMinutes(2)), "expires_at = now + the days entered");
            Assert.That(await ctx.Invites.CountAsync(), Is.EqualTo(_canonicalInviteCount + 1), "exactly one invite row was added to canonical");
        }
    }

    [Test]
    public async Task GlobalAdmin_CreateInvite_OffersOnlyAdminAndGlobalAdmin()
    {
        await OpenAccountsPageAsync(LoginAsGlobalAdminAsync);
        await _accounts.ClickCreateUserAsync();
        await Expect(_accounts.DialogTitle).ToHaveTextAsync("Create User");

        await _accounts.OpenPermissionOptionsAsync();

        // The exact option list is the presence check; the Owner absence check runs against that same render.
        await Expect(_accounts.PermissionOptions).ToHaveTextAsync([PermissionAdmin, PermissionGlobalAdmin]);
        await Expect(_accounts.PermissionOptions.Filter(new() { HasText = "Owner" })).ToHaveCountAsync(0);

        await _accounts.ClosePermissionOptionsAsync();
        await _accounts.CloseDialogAsync();
    }

    [Test]
    public async Task RegisterWithInvite_CreatesAnActiveAccountThatLogsIn()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var features = scope.ServiceProvider.GetRequiredService<IFeatureAvailabilityService>();
            Assert.That(await features.IsEmailVerificationEnabledAsync(), Is.False,
                "canonical: sendgrid_config is enabled but 04_configs.api_keys.json carries no SendGrid key — this test covers the verification-off path");
        }

        // The Owner creates an invite with the dialog defaults (Admin, 7 days).
        await OpenAccountsPageAsync(LoginAsOwnerAsync);
        await _accounts.ClickCreateUserAsync();
        await _accounts.GenerateInviteAsync();
        await Expect(_accounts.InviteLinkInput).ToHaveValueAsync(InviteLinkPattern);
        var inviteLink = await ReadInviteLinkAsync();
        var token = TokenFromLink(inviteLink);
        await _accounts.ClickDoneAsync();

        // Continue as the invitee: anonymous, following the link the Owner would send.
        await Context.ClearCookiesAsync();
        var email = TestCredentials.GenerateEmail("invited");
        var password = TestCredentials.GeneratePassword();
        var register = new RegisterPage(Page);
        await register.NavigateToInviteLinkAsync(inviteLink);

        await Expect(register.InviteCodeInput).ToHaveValueAsync(token);
        await Expect(register.EmailVerificationDisabledNote).ToBeVisibleAsync();

        await register.FillEmailAsync(email);
        await register.FillPasswordAsync(password);
        await register.FillConfirmPasswordAsync(password);
        await register.SubmitAsync();

        await Expect(register.SuccessAlert).ToContainTextAsync("Account created successfully");
        await Page.WaitForURLAsync("**/login", new() { Timeout = 10000 });

        // The note promised an immediate login: a new account lands on TOTP setup (TotpEnabled by default).
        var login = new LoginPage(Page);
        await login.LoginAsync(email, password);
        await Expect(Page).ToHaveURLAsync(new Regex("/login/setup-2fa"), new() { Timeout = 10000 });

        await using var ctx = CreateDbContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await ctx.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => new { u.Id, u.PermissionLevel, u.InvitedBy, u.EmailVerified, u.TotpEnabled, u.Status, u.IsActive }).SingleAsync();
        var invite = await ctx.Invites.AsNoTracking().Where(i => i.Token == token)
            .Select(i => new { i.Status, i.UsedBy, i.ModifiedAt }).SingleAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.PermissionLevel, Is.EqualTo(0), "PermissionLevel.Admin from the invite");
            Assert.That(user.InvitedBy, Is.EqualTo(GoldenDatasetConstants.WebUsers.OwnerId));
            Assert.That(user.EmailVerified, Is.True, "verification is off, so the account starts verified");
            Assert.That(user.TotpEnabled, Is.True, "every new account must set up 2FA");
            Assert.That((UserStatus)(int)user.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(user.IsActive, Is.True);
            Assert.That(invite.Status, Is.EqualTo(InviteStatus.Used));
            Assert.That(invite.UsedBy, Is.EqualTo(user.Id));
            Assert.That(invite.ModifiedAt, Is.Not.Null);
            Assert.That(EmailService.GetEmailsByTemplate(EmailTemplate.EmailVerification), Is.Empty, "no verification email without an email service");
        }
    }

    private async Task OpenAccountsPageAsync(Func<Task> loginAsync)
    {
        await loginAsync();
        await _accounts.NavigateAsync();
        await _accounts.WaitForLoadAsync();
    }

    /// <summary>
    /// The generated link, read once its value has been asserted with Expect: the test needs the value
    /// itself (to follow the link and to find the invite row), which no Expect can hand back.
    /// </summary>
    private async Task<string> ReadInviteLinkAsync()
    {
#pragma warning disable RS0030 // Legitimate value read after the ToHaveValueAsync sync on the same field
        return await _accounts.InviteLinkInput.InputValueAsync();
#pragma warning restore RS0030
    }

    /// <summary>The <c>invite</c> query value of a generated link (<c>{BaseUri}register?invite={token}</c>).</summary>
    private static string TokenFromLink(string inviteLink)
    {
        var query = new Uri(inviteLink).Query.TrimStart('?');
        var invite = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Single(kv => kv[0] == "invite");
        return Uri.UnescapeDataString(invite[1]);
    }
}
