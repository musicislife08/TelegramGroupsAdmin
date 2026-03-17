using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for InviteRepository filter behavior.
///
/// Regression coverage for #397: InviteFilter.All must return all invites
/// regardless of status, not silently apply a bogus status filter.
/// </summary>
[TestFixture]
public class InviteRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IInviteRepository? _repository;

    private const string TestUserId = "test-user-id";

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddScoped<IInviteRepository, InviteRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.CreateScope()
            .ServiceProvider.GetRequiredService<IInviteRepository>();

        // Seed a user to satisfy FK constraint on invites.created_by
        await SeedTestUserAsync();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task SeedTestUserAsync()
    {
        await _testHelper!.ExecuteSqlAsync($"""
            INSERT INTO users (id, email, normalized_email, password_hash, security_stamp,
                               permission_level, is_active, totp_enabled, created_at, status,
                               email_verified, failed_login_attempts)
            VALUES ('{TestUserId}', 'test@test.com', 'TEST@TEST.COM', 'hash', 'stamp',
                    0, true, false, NOW(), 1, true, 0)
            """);
    }

    private async Task<InviteRecord> CreateInviteWithStatusAsync(Data.Models.InviteStatus status)
    {
        // Create as pending first
        var token = await _repository!.CreateAsync(TestUserId, validDays: 7, permissionLevel: 0);

        if (status != Data.Models.InviteStatus.Pending)
        {
            if (status == Data.Models.InviteStatus.Used)
            {
                await _repository.MarkAsUsedAsync(token, TestUserId);
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

        // Assert — regression for #397: All must return all 3, not zero
        Assert.That(results, Has.Count.EqualTo(3));
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

        // Assert — regression for #397
        Assert.That(results, Has.Count.EqualTo(3));
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

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Status, Is.EqualTo(Core.Models.InviteStatus.Pending));
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

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Status, Is.EqualTo(Core.Models.InviteStatus.Used));
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

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Status, Is.EqualTo(Core.Models.InviteStatus.Revoked));
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
