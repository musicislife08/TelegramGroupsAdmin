using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// ChatAdminsRepository.IsAdminOfAnyChatAsync on canonical chat_admins rows (read-only):
/// any active row counts; inactive (demoted) rows and no rows do not.
/// Anchors: UsersPage.ChatAdminMemberId (4 active rows), ChatAdmins.DemotedAdminUserId
/// (2 inactive rows), UsersPage.UntrustedActiveMemberId (no row).
/// </summary>
[TestFixture]
public class ChatAdminsRepositoryAnyChatTests
{
    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private IChatAdminsRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging();
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();
        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.CreateScope().ServiceProvider.GetRequiredService<IChatAdminsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task<(int Active, int Inactive)> ReadRowsAsync(long userId)
    {
        await using var ctx = _testHelper!.GetDbContext();
        var rows = await ctx.ChatAdmins.AsNoTracking().Where(a => a.TelegramId == userId).ToListAsync();
        return (rows.Count(r => r.IsActive), rows.Count(r => !r.IsActive));
    }

    [Test]
    public async Task ActiveAdminRow_IsAdminOfAnyChat()
    {
        var userId = GoldenDatasetConstants.UsersPage.ChatAdminMemberId;
        var (active, _) = await ReadRowsAsync(userId);
        Assert.That(active, Is.GreaterThan(0), "anchor must have an active chat_admins row");

        Assert.That(await _repository!.IsAdminOfAnyChatAsync(userId), Is.True);
    }

    [Test]
    public async Task OnlyInactiveAdminRows_IsNotAdminOfAnyChat()
    {
        var userId = GoldenDatasetConstants.ChatAdmins.DemotedAdminUserId;
        Assert.That(await ReadRowsAsync(userId), Is.EqualTo((0, 2)), "anchor must have only inactive chat_admins rows");

        Assert.That(await _repository!.IsAdminOfAnyChatAsync(userId), Is.False);
    }

    [Test]
    public async Task NoAdminRow_IsNotAdminOfAnyChat()
    {
        var userId = GoldenDatasetConstants.UsersPage.UntrustedActiveMemberId;
        Assert.That(await ReadRowsAsync(userId), Is.EqualTo((0, 0)), "anchor must have no chat_admins row");

        Assert.That(await _repository!.IsAdminOfAnyChatAsync(userId), Is.False);
    }
}
