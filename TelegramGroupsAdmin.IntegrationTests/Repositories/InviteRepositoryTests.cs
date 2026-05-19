using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for InviteRepository filter behavior.
///
/// Regression coverage for #397: InviteFilter.All must return all invites
/// regardless of status, not silently apply a bogus status filter.
///
/// Test Infrastructure:
/// - Unique PostgreSQL database per test (cloned from golden_template)
/// - Canonical dataset provides 19 invites: 1 Pending, 13 Used, 5 Revoked.
/// - created_by FK satisfied via canonical Owner fixture (GoldenDatasetConstants.WebUsers.OwnerId).
///
/// Canonical baseline counts (before each test adds its own rows):
///   All:     19  Pending: 1  Used: 13  Revoked: 5
/// Each test creates 1 Pending + 1 Used + 1 Revoked = 3 more, so totals become:
///   All:     22  Pending: 2  Used:  14  Revoked: 6
/// </summary>
[TestFixture]
public class InviteRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IInviteRepository? _repository;

    // Canonical Owner fixture (owner@example.com) — satisfies invites.created_by FK without raw INSERT.
    private const string CreatedByUserId = GoldenDatasetConstants.WebUsers.OwnerId;

    // Canonical baseline counts (from 29_invites.sql)
    private const int CanonicalTotal = 19;
    private const int CanonicalPending = 1;
    private const int CanonicalUsed = 13;
    private const int CanonicalRevoked = 5;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddScoped<IInviteRepository, InviteRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IInviteRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task<InviteRecord> CreateInviteWithStatusAsync(Data.Models.InviteStatus status)
    {
        // Create as pending first
        var token = await _repository!.CreateAsync(CreatedByUserId, validDays: 7, permissionLevel: 0);

        if (status != Data.Models.InviteStatus.Pending)
        {
            if (status == Data.Models.InviteStatus.Used)
            {
                await _repository.MarkAsUsedAsync(token, CreatedByUserId);
            }
            else if (status == Data.Models.InviteStatus.Revoked)
            {
                await _repository.RevokeAsync(token);
            }
        }

        var invite = await _repository.GetByTokenAsync(token);
        return invite!;
    }

    #region InviteFilter.All Regression Tests (#397)

    [Test]
    public async Task GetAllAsync_FilterAll_ReturnsAllInvitesRegardlessOfStatus()
    {
        // Arrange — create one invite per status
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Pending);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Used);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Revoked);

        // Act
        var results = await _repository!.GetAllAsync(InviteFilter.All);

        // Assert — regression for #397: All must return all rows (canonical 19 + 3 new)
        Assert.That(results, Has.Count.EqualTo(CanonicalTotal + 3));
    }

    [Test]
    public async Task GetAllWithCreatorEmailAsync_FilterAll_ReturnsAllInvitesRegardlessOfStatus()
    {
        // Arrange
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Pending);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Used);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Revoked);

        // Act
        var results = await _repository!.GetAllWithCreatorEmailAsync(InviteFilter.All);

        // Assert — regression for #397 (canonical 19 + 3 new; all have valid created_by FKs)
        Assert.That(results, Has.Count.EqualTo(CanonicalTotal + 3));
    }

    #endregion

    #region InviteFilter Status Tests

    [Test]
    public async Task GetAllAsync_FilterPending_ReturnsOnlyPendingInvites()
    {
        // Arrange
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Pending);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Used);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Revoked);

        // Act
        var results = await _repository!.GetAllAsync(InviteFilter.Pending);

        // Assert — count matches canonical baseline (1) + 1 newly created Pending
        Assert.That(results, Has.Count.EqualTo(CanonicalPending + 1));
        Assert.That(results.All(r => r.Status == Core.Models.InviteStatus.Pending), Is.True,
            "All returned invites must have Pending status");
    }

    [Test]
    public async Task GetAllAsync_FilterUsed_ReturnsOnlyUsedInvites()
    {
        // Arrange
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Pending);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Used);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Revoked);

        // Act
        var results = await _repository!.GetAllAsync(InviteFilter.Used);

        // Assert — count matches canonical baseline (13) + 1 newly created Used
        Assert.That(results, Has.Count.EqualTo(CanonicalUsed + 1));
        Assert.That(results.All(r => r.Status == Core.Models.InviteStatus.Used), Is.True,
            "All returned invites must have Used status");
    }

    [Test]
    public async Task GetAllAsync_FilterRevoked_ReturnsOnlyRevokedInvites()
    {
        // Arrange
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Pending);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Used);
        await CreateInviteWithStatusAsync(Data.Models.InviteStatus.Revoked);

        // Act
        var results = await _repository!.GetAllAsync(InviteFilter.Revoked);

        // Assert — count matches canonical baseline (5) + 1 newly created Revoked
        Assert.That(results, Has.Count.EqualTo(CanonicalRevoked + 1));
        Assert.That(results.All(r => r.Status == Core.Models.InviteStatus.Revoked), Is.True,
            "All returned invites must have Revoked status");
    }

    #endregion

    #region Enum Value Alignment Guard

    [Test]
    public void InviteFilter_StatusValues_MatchInviteStatusForDirectCast()
    {
        // Guard: The repo casts (InviteStatus)(int)filter — these must stay aligned
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)InviteFilter.Pending, Is.EqualTo((int)Core.Models.InviteStatus.Pending), "Pending");
            Assert.That((int)InviteFilter.Used, Is.EqualTo((int)Core.Models.InviteStatus.Used), "Used");
            Assert.That((int)InviteFilter.Revoked, Is.EqualTo((int)Core.Models.InviteStatus.Revoked), "Revoked");
        }
    }

    [Test]
    public void InviteFilter_All_DoesNotCollideWithAnyInviteStatus()
    {
        // Guard: All must not match any InviteStatus value
        var allStatusValues = Enum.GetValues<Core.Models.InviteStatus>()
            .Select(s => (int)s)
            .ToHashSet();

        Assert.That(allStatusValues, Does.Not.Contain((int)InviteFilter.All),
            "InviteFilter.All must not collide with any InviteStatus value");
    }

    #endregion
}
