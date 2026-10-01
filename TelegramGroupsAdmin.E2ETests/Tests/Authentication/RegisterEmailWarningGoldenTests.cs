using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// The register page's "email verification disabled" note on canonical data as it is: canonical has users
/// (so the page is in its invite-code mode, not first-run) and an enabled <c>sendgrid_config</c> without a
/// stored SendGrid key, which the strict email-configuration gate reads as Disabled. Read-only.
/// </summary>
[TestFixture]
public class RegisterEmailWarningGoldenTests : GoldenE2ETestBase
{
    [Test]
    public async Task Register_ShowsTheEmailVerificationDisabledNote_WhenNoSendGridKeyIsStored()
    {
        var register = new RegisterPage(Page);
        await register.NavigateAsync();

        await Expect(register.InviteCodeInput).ToBeVisibleAsync();
        await Expect(register.EmailVerificationDisabledNote).ToBeVisibleAsync();
        await Expect(register.EmailVerificationDisabledNote).ToContainTextAsync("log in immediately after registration");
    }
}

/// <summary>
/// The same register page with email read as configured (<c>EnableSendGridApiKey</c> adds the dummy key the
/// gate looks for): the note is gone. Read-only.
/// </summary>
[TestFixture]
public class RegisterEmailWarningEmailConfiguredGoldenTests : GoldenE2ETestBase
{
    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        await GoldenDataset.Mutate(context)
            .EnableSendGridApiKey(SharedKeyRing(), "SG.e2e-dummy")
            .ApplyAsync();
    }

    [Test]
    public async Task Register_HidesTheEmailVerificationDisabledNote_WhenEmailIsConfigured()
    {
        var register = new RegisterPage(Page);
        await register.NavigateAsync();

        // The invite field renders in the same initialisation that decides the note, so the page is settled.
        await Expect(register.InviteCodeInput).ToBeVisibleAsync();
        await Expect(register.EmailVerificationDisabledNote).ToHaveCountAsync(0);
    }
}
