using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// <see cref="UserActionsRepository.GetPagedActionsAsync"/>'s issued-by filter on canonical as it is:
/// the Audit Log's Issued By column renders a system actor's display name, a web user's email or a
/// Telegram admin's name, and the filter must find rows by what the column shows. Expected row sets
/// are computed from the raw actor columns, never through the filter under test.
/// </summary>
[TestFixture]
public class UserActionsRepositoryIssuedByFilterTests
{
    private const long TelegramAdminId = GoldenDatasetConstants.ModerationLog.TelegramAdminIssuerId;
    private const string OwnerId = GoldenDatasetConstants.WebUsers.OwnerId;
    private const string OwnerEmail = GoldenDatasetConstants.WebUsers.OwnerEmail;

    /// <summary>Larger than canonical's user_actions count, so one page holds every match.</summary>
    private const int AllRows = 10_000;

    private MigrationTestHelper _testHelper = null!;
    private ServiceProvider _serviceProvider = null!;
    private IUserActionsRepository _repository = null!;
    private IDbContextFactory<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();
        services.AddSingleton(new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString).Build());
        services.AddDbContextFactory<AppDbContext>((_, options) => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.GetRequiredService<IUserActionsRepository>();
        _contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        _testHelper.Dispose();
    }

    [Test]
    public async Task SystemActorDisplayName_ReturnsEveryRowOfThatActor()
    {
        // "Exam Flow" is only the display name: "%Exam Flow%" never matches the stored "exam_flow".
        var expected = await IdsWhereAsync(a => a.SystemIdentifier == SystemActorIds.ExamFlow);
        Assert.That(expected, Is.Not.Empty, "canonical must carry exam_flow actions");

        var (actions, total) = await _repository.GetPagedActionsAsync(0, AllRows, issuedByFilter: "Exam Flow");

        Assert.That(actions.Select(a => a.Id), Is.EquivalentTo(expected));
        Assert.That(total, Is.EqualTo(expected.Count));
        Assert.That(actions.Select(a => a.IssuedBy.DisplayName).Distinct(), Is.EqualTo(["Exam Flow"]));
    }

    [Test]
    public async Task SystemActorIdentifier_StillReturnsEveryRowOfThatActor()
    {
        var expected = await IdsWhereAsync(a => a.SystemIdentifier == SystemActorIds.ExamFlow);

        var (actions, total) = await _repository.GetPagedActionsAsync(0, AllRows, issuedByFilter: SystemActorIds.ExamFlow);

        Assert.That(actions.Select(a => a.Id), Is.EquivalentTo(expected));
        Assert.That(total, Is.EqualTo(expected.Count));
    }

    [Test]
    public async Task PartialSystemActorDisplayName_ReturnsEveryActorWhoseNameContainsIt()
    {
        // "auto-" (with the hyphen) is in the display names "Auto-Detection" and "Auto-Trust" only; the
        // stored identifiers use an underscore, so neither matches it as raw text.
        var expected = await IdsWhereAsync(a =>
            a.SystemIdentifier == SystemActorIds.AutoDetection || a.SystemIdentifier == SystemActorIds.AutoTrust);
        var autoDetection = await IdsWhereAsync(a => a.SystemIdentifier == SystemActorIds.AutoDetection);
        var autoTrust = await IdsWhereAsync(a => a.SystemIdentifier == SystemActorIds.AutoTrust);
        Assert.That(autoDetection, Is.Not.Empty);
        Assert.That(autoTrust, Is.Not.Empty);

        var (actions, total) = await _repository.GetPagedActionsAsync(0, AllRows, issuedByFilter: "auto-");

        Assert.That(actions.Select(a => a.Id), Is.EquivalentTo(expected));
        Assert.That(total, Is.EqualTo(expected.Count));
    }

    [Test]
    public async Task PartialWebUserEmail_ReturnsThatWebUsersRows()
    {
        var expected = await IdsWhereAsync(a => a.WebUserId == OwnerId);
        Assert.That(expected, Is.Not.Empty, "canonical must carry Owner-issued actions");

        // The local part alone: a substring of the email, and of no other canonical web user's email.
        var term = OwnerEmail[..(OwnerEmail.IndexOf('@') + 1)];
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            var matchingEmails = await context.Users.AsNoTracking()
                .Where(u => EF.Functions.ILike(u.Email, $"%{term}%"))
                .Select(u => u.Id)
                .ToListAsync();
            Assert.That(matchingEmails, Is.EqualTo([OwnerId]), "the search term must identify the Owner alone");
        }

        var (actions, total) = await _repository.GetPagedActionsAsync(0, AllRows, issuedByFilter: term.ToUpperInvariant());

        Assert.That(actions.Select(a => a.Id), Is.EquivalentTo(expected));
        Assert.That(total, Is.EqualTo(expected.Count));
        Assert.That(actions.Select(a => a.IssuedBy.DisplayName).Distinct(), Is.EqualTo([OwnerEmail]),
            "a web user's rows carry the email the column renders");
    }

    [Test]
    public async Task TelegramAdminName_ReturnsThatAdminsRowsNamedAsTheColumnRendersThem()
    {
        var expected = await IdsWhereAsync(a => a.TelegramUserId == TelegramAdminId);
        Assert.That(expected, Is.Not.Empty, "canonical must carry actions issued by the Telegram admin anchor");

        string term;
        string displayName;
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            var admin = await context.TelegramUsers.AsNoTracking()
                .Where(u => u.TelegramUserId == TelegramAdminId)
                .Select(u => new { u.Username, u.FirstName, u.LastName })
                .SingleAsync();
            Assert.That(admin.FirstName, Is.Not.Null.And.Not.Empty);
            Assert.That(admin.LastName, Is.Not.Null.And.Not.Empty);
            displayName = TelegramDisplayName.Format(admin.FirstName, admin.LastName, admin.Username, TelegramAdminId);

            // Search across the first/last name boundary, as the column renders it ("First Last").
            term = displayName.Substring(admin.FirstName!.Length - 1, 3);
            Assert.That(term, Does.Contain(' '));

            // The term must single out this admin among every Telegram user that issued an action.
            var issuerIds = await context.UserActions.AsNoTracking()
                .Where(a => a.TelegramUserId != null)
                .Select(a => a.TelegramUserId!.Value)
                .Distinct()
                .ToListAsync();
            var issuers = await context.TelegramUsers.AsNoTracking()
                .Where(u => issuerIds.Contains(u.TelegramUserId))
                .Select(u => new { u.TelegramUserId, u.Username, u.FirstName, u.LastName })
                .ToListAsync();
            var matchingIssuers = issuers
                .Where(u => TelegramDisplayName.Format(u.FirstName, u.LastName, u.Username, u.TelegramUserId)
                                .Contains(term, StringComparison.OrdinalIgnoreCase)
                            || (u.Username?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(u => u.TelegramUserId)
                .ToList();
            Assert.That(matchingIssuers, Is.EqualTo([TelegramAdminId]), "the search term must identify the anchor admin alone");
        }

        var (actions, total) = await _repository.GetPagedActionsAsync(0, AllRows, issuedByFilter: term.ToUpperInvariant());

        Assert.That(actions.Select(a => a.Id), Is.EquivalentTo(expected));
        Assert.That(total, Is.EqualTo(expected.Count));
        Assert.That(actions.Select(a => a.IssuedBy.DisplayName).Distinct(), Is.EqualTo([displayName]),
            "a Telegram admin's rows carry the name the column renders");
    }

    [Test]
    public async Task TermMatchingNoIssuer_ReturnsNothing()
    {
        var (actions, total) = await _repository.GetPagedActionsAsync(0, AllRows, issuedByFilter: "no such issuer anywhere");

        Assert.That(actions, Is.Empty);
        Assert.That(total, Is.Zero);
    }

    private async Task<List<long>> IdsWhereAsync(System.Linq.Expressions.Expression<Func<Data.Models.UserActionRecordDto, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.UserActions.AsNoTracking().Where(predicate).Select(a => a.Id).ToListAsync();
    }
}
