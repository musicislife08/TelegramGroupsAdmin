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
        await Expect(_accounts.InviteLinkInput).ToHaveValueAsync(InviteLinks.Pattern);
        await Expect(_accounts.SnackbarWithText("Invite created successfully")).ToBeVisibleAsync();

        var token = InviteLinks.TokenFrom(await InviteLinks.ReadAsync(_accounts));
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
        await Expect(_accounts.InviteLinkInput).ToHaveValueAsync(InviteLinks.Pattern);
        var inviteLink = await InviteLinks.ReadAsync(_accounts);
        var token = InviteLinks.TokenFrom(inviteLink);
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
}

/// <summary>
/// The verification-ON sibling of <see cref="InviteGoldenTests.RegisterWithInvite_CreatesAnActiveAccountThatLogsIn"/>:
/// the same canonical, plus a dummy SendGrid key (<c>EnableSendGridApiKey</c>) so the strict email gate reads
/// email as configured. The key is never sent anywhere — the factory swaps <c>IEmailService</c> for a capturing
/// stub, and the verification email the app hands that stub is an assertion subject here. Its own fixture
/// because <see cref="GoldenE2ETestBase.ArrangeDataAsync"/> runs once, before the app starts.
/// </summary>
[TestFixture]
public class InviteEmailVerificationGoldenTests : GoldenE2ETestBase
{
    /// <summary>A value, not a key: it only has to be present for the gate to read email as configured.</summary>
    private const string DummySendGridApiKey = "SG.e2e-dummy";

    /// <summary><c>verification_tokens.token_type</c> for an email-verification token (<c>TokenType.EmailVerification</c>).</summary>
    private const string EmailVerifyTokenType = "email_verify";

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        await GoldenDataset.Mutate(context)
            .EnableSendGridApiKey(SharedKeyRing(), DummySendGridApiKey)
            .ApplyAsync();
    }

    [Test]
    public async Task RegisterWithInvite_WithEmailConfigured_SendsVerificationAndRefusesLoginUntilVerified()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var features = scope.ServiceProvider.GetRequiredService<IFeatureAvailabilityService>();
            Assert.That(await features.IsEmailVerificationEnabledAsync(), Is.True,
                "the dummy SendGrid key in api_keys must make the strict email gate read Enabled — this test covers the verification-on path");
        }

        // The Owner creates an invite with the dialog defaults (Admin, 7 days).
        var accounts = new WebAdminAccountsPage(Page);
        await LoginAsOwnerAsync();
        await accounts.NavigateAsync();
        await accounts.WaitForLoadAsync();
        await accounts.ClickCreateUserAsync();
        await accounts.GenerateInviteAsync();
        await Expect(accounts.InviteLinkInput).ToHaveValueAsync(InviteLinks.Pattern);
        var inviteLink = await InviteLinks.ReadAsync(accounts);
        var token = InviteLinks.TokenFrom(inviteLink);
        await accounts.ClickDoneAsync();

        // Continue as the invitee: anonymous, following the link the Owner would send.
        await Context.ClearCookiesAsync();
        var email = TestCredentials.GenerateEmail("invited-verify");
        var password = TestCredentials.GeneratePassword();
        var register = new RegisterPage(Page);
        await register.NavigateToInviteLinkAsync(inviteLink);

        // The invite code only populates once the page has resolved its first-run and email state, so the
        // note's absence is checked against that settled render.
        await Expect(register.InviteCodeInput).ToHaveValueAsync(token);
        await Expect(register.EmailVerificationDisabledNote).ToHaveCountAsync(0);

        await register.FillEmailAsync(email);
        await register.FillPasswordAsync(password);
        await register.FillConfirmPasswordAsync(password);
        await register.SubmitAsync();

        // Verification-on: the page tells the user to verify and stays put (no auto-redirect to /login), offering a
        // link instead. The URL check runs against the render that already shows the link, so it is not a pre-redirect race.
        await Expect(register.SuccessAlert).ToContainTextAsync("We've sent a verification link to your email");
        await Expect(register.GoToLoginLink).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/register"));
        await register.GoToLoginLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));

        // Unverified, so the login is refused before any TOTP step: the page re-renders with the error and never
        // redirects to /login/setup-2fa (the URL check runs against the render that carries the error).
        var login = new LoginPage(Page);
        await login.LoginAsync(email, password);
        await Expect(login.ErrorAlert).ToContainTextAsync("Please verify your email before logging in");
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));

        await using var ctx = CreateDbContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await ctx.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => new { u.Id, u.PermissionLevel, u.InvitedBy, u.EmailVerified, u.TotpEnabled, u.Status, u.IsActive }).SingleAsync();
        var invite = await ctx.Invites.AsNoTracking().Where(i => i.Token == token)
            .Select(i => new { i.Status, i.UsedBy }).SingleAsync();
        var storedTokens = await ctx.VerificationTokens.AsNoTracking()
            .Where(t => t.UserId == user.Id && t.TokenTypeString == EmailVerifyTokenType)
            .Select(t => new { t.Token, t.UsedAt, t.ExpiresAt }).ToListAsync();
        var verificationEmails = EmailService.GetEmailsByTemplate(EmailTemplate.EmailVerification).ToList();

        // The two "exactly one" checks gate the indexing below, so they run first and fail on their own message.
        Assert.That(verificationEmails, Has.Count.EqualTo(1), "exactly one verification email was handed to the email service");
        Assert.That(storedTokens, Has.Count.EqualTo(1), "registration stores exactly one email-verification token");
        var verificationEmail = verificationEmails[0];
        var storedToken = storedTokens[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.PermissionLevel, Is.EqualTo(0), "PermissionLevel.Admin from the invite");
            Assert.That(user.InvitedBy, Is.EqualTo(GoldenDatasetConstants.WebUsers.OwnerId));
            Assert.That(user.EmailVerified, Is.False, "verification is on, so the account starts unverified");
            Assert.That(user.TotpEnabled, Is.True, "every new account must set up 2FA (once verified)");
            Assert.That((UserStatus)(int)user.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(user.IsActive, Is.True);
            Assert.That(invite.Status, Is.EqualTo(InviteStatus.Used));
            Assert.That(invite.UsedBy, Is.EqualTo(user.Id));

            Assert.That(storedToken.UsedAt, Is.Null, "the token is unused until the link is followed");
            Assert.That(storedToken.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow));

            Assert.That(verificationEmail.To, Is.EqualTo(new[] { email }), "sent to the invitee's address only");
            Assert.That(verificationEmail.Parameters, Is.Not.Null);
            Assert.That(verificationEmail.Parameters!["VerificationToken"], Is.EqualTo(storedToken.Token),
                "the token in the email is the one stored for the user");
            Assert.That(verificationEmail.Parameters!["BaseUrl"], Is.EqualTo(BaseUrl), "the link points at this app instance");
            Assert.That(EmailService.GetEmailsTo(email).Count(), Is.EqualTo(1), "no other email reached the invitee");
        }
    }
}

/// <summary>The generated invite link, shared by the invite fixtures in this file.</summary>
file static class InviteLinks
{
    /// <summary>What the "Invite Link Created" dialog shows: <c>{BaseUri}register?invite={guid}</c>.</summary>
    public static readonly Regex Pattern = new(@"^https?://[^/]+/register\?invite=[0-9a-f-]{36}$");

    /// <summary>
    /// The generated link, read once its value has been asserted with Expect: the test needs the value
    /// itself (to follow the link and to find the invite row), which no Expect can hand back.
    /// </summary>
    public static async Task<string> ReadAsync(WebAdminAccountsPage accounts)
    {
#pragma warning disable RS0030 // Legitimate value read after the ToHaveValueAsync sync on the same field
        return await accounts.InviteLinkInput.InputValueAsync();
#pragma warning restore RS0030
    }

    /// <summary>The <c>invite</c> query value of a generated link.</summary>
    public static string TokenFrom(string inviteLink)
    {
        var query = new Uri(inviteLink).Query.TrimStart('?');
        var invite = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Single(kv => kv[0] == "invite");
        return Uri.UnescapeDataString(invite[1]);
    }
}
