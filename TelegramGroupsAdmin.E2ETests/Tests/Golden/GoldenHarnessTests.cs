using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Golden;

/// <summary>
/// Proves the golden E2E path: per-test clone, canonical login, arrange-before-start,
/// isolation between tests, and the shared key ring.
/// </summary>
[TestFixture]
public class GoldenHarnessTests : GoldenE2ETestBase
{
    [Test]
    public async Task CanonicalOwner_SeesTheProfileWithCanonicalEmail()
    {
        await LoginAsOwnerAsync();

        var profile = new ProfilePage(Page);
        await profile.NavigateAsync();

        // The email is a read-only input on the Account Information card, so assert its value.
        await Expect(profile.AccountInfoSection.GetByLabel("Email"))
            .ToHaveValueAsync(GoldenDatasetConstants.WebUsers.OwnerEmail);
    }

    [Test]
    public async Task CanonicalChats_AreListed()
    {
        // The Chats page lists non-deleted chats ordered by name (GetAllChatsAsync + the UI's
        // deleted filter), 10 per page: the first by name is always on page 1.
        string firstChatTitle;
        await using (var ctx = CreateDbContext())
        {
            firstChatTitle = await ctx.ManagedChats.AsNoTracking()
                .Where(c => !c.IsDeleted).OrderBy(c => c.ChatName).Select(c => c.ChatName!).FirstAsync();
        }
        await LoginAsOwnerAsync();

        var chats = new ChatsPage(Page);
        await chats.NavigateAsync();

        await Expect(chats.ChatName(firstChatTitle)).ToBeVisibleAsync();
    }

    [Test]
    public async Task ArrangeDataAsync_RunsBeforeTheAppStarts()
    {
        // ArrangeDataAsync (below) reduced messages to zero for this test only.
        await LoginAsOwnerAsync();

        var home = new HomePage(Page);
        await home.NavigateAsync();
        await home.WaitForLoadAsync();

        await Expect(home.StatValue("Total Messages")).ToHaveTextAsync("0");
    }

    [Test, Order(1)]
    public async Task Isolation_WriteInOneTest()
    {
        // Deliberate write-as-subject probe: the write itself is what is under test — the next
        // test asserts its clone does not see it. Not a seeding pattern; do not copy it.
        // The pair is only meaningful when both run, in this order (Order(1) then Order(2));
        // Isolation_NextTestSeesCleanCanonical passes trivially when filtered on its own.
        await LoginAsOwnerAsync();
        await using var ctx = CreateDbContext();
        // TEST-DATA RULE EXCEPTION: this raw write is the assertion subject, not setup: it proves
        // per-test clone isolation (the next test must not see it). Do not copy it as a setup pattern.
        var rows = await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE users SET email = 'isolation-probe@e2e.local' WHERE id = {0}",
            GoldenDatasetConstants.WebUsers.NoTotpAdminId);

        Assert.That(rows, Is.EqualTo(1), "the probe must hit the canonical row");
        Assert.Pass("wrote a marker; Isolation_NextTestSeesCleanCanonical asserts it is gone");
    }

    [Test, Order(2)]
    public async Task Isolation_NextTestSeesCleanCanonical()
    {
        await using var ctx = CreateDbContext();
        var email = await ctx.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.NoTotpAdminId).Select(u => u.Email).SingleAsync();

        Assert.That(email, Is.EqualTo(GoldenDatasetConstants.WebUsers.NoTotpAdminEmail));
    }

    [Test]
    public async Task SharedKeyRing_AppDecryptsCanonicalApiKeys()
    {
        await LoginAsOwnerAsync();

        // The app's own read path: a key-ring mismatch makes Unprotect throw (the repository logs
        // it and returns null); a plaintext in the wrong shape deserialises to an empty key set.
        // Reading the key back through ApiKeysConfig therefore pins both the key ring and the format.
        using var scope = Factory.Services.CreateScope();
        var configs = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();
        var apiKeys = await configs.GetApiKeysAsync();

        Assert.That(apiKeys, Is.Not.Null, "the app could not decrypt canonical configs.api_keys");
        Assert.That(apiKeys!.GetAIConnectionKey(GoldenDatasetConstants.SystemConfig.OpenAiConnectionId),
            Is.EqualTo(GoldenDatasetConstants.SystemConfig.OpenAiConnectionKey));
    }

    [Test]
    public async Task CanonicalNoTotpAdmin_LogsInThroughTheUi()
    {
        await LoginViaUiAsync(GoldenDatasetConstants.WebUsers.NoTotpAdminEmail);

        // Authenticated as that user: the profile shows the canonical email.
        var profile = new ProfilePage(Page);
        await profile.NavigateAsync();

        await Expect(profile.AccountInfoSection.GetByLabel("Email"))
            .ToHaveValueAsync(GoldenDatasetConstants.WebUsers.NoTotpAdminEmail);
    }

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        if (TestContext.CurrentContext.Test.MethodName == nameof(ArrangeDataAsync_RunsBeforeTheAppStarts))
        {
            await GoldenDataset.Reduce(context).KeepMessages(0).ApplyAsync();
        }
    }
}

/// <summary>The Empty template has no web users, so the app is in first-run mode.</summary>
[TestFixture]
public class GoldenHarnessEmptyTemplateTests : GoldenE2ETestBase
{
    protected override GoldenTemplate Template => GoldenTemplate.Empty;

    [Test]
    public async Task EmptyTemplate_RedirectsToFirstRunRegistration()
    {
        await Page.GotoAsync("/");

        await Expect(Page).ToHaveURLAsync(new Regex("/register"));
    }
}
