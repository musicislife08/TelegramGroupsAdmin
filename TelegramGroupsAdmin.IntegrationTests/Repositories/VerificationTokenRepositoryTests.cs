using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for VerificationTokenRepository's domain-model boundary: CreateAsync takes the
/// domain VerificationToken and returns the database-generated id, and GetValidTokenAsync takes the
/// domain TokenType.
///
/// verification_tokens is an operational table that canonical does not export, so these tests create
/// their own token through the repository: the create is also the write under test. The user_id FK is
/// satisfied by the canonical Owner fixture (GoldenDatasetConstants.WebUsers.OwnerId).
/// </summary>
[TestFixture]
public class VerificationTokenRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IVerificationTokenRepository? _repository;

    private const string UserId = GoldenDatasetConstants.WebUsers.OwnerId;

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
        services.AddScoped<IVerificationTokenRepository, VerificationTokenRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IVerificationTokenRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    private static VerificationToken NewToken(TokenType type, DateTimeOffset? expiresAt = null, DateTimeOffset? usedAt = null, string? value = null) => new(
        Id: 0,
        UserId: UserId,
        TokenType: type,
        Token: Guid.NewGuid().ToString("N"),
        Value: value,
        ExpiresAt: expiresAt ?? DateTimeOffset.UtcNow.AddHours(24),
        CreatedAt: DateTimeOffset.UtcNow,
        UsedAt: usedAt);

    [Test]
    public async Task CreateAsync_DomainToken_ReturnsGeneratedIdAndIsReadableByType()
    {
        var token = NewToken(TokenType.PasswordReset);

        var id = await _repository!.CreateAsync(token);

        Assert.That(id, Is.GreaterThan(0));
        var stored = await _repository.GetValidTokenAsync(token.Token, TokenType.PasswordReset);
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Id, Is.EqualTo(id));
            Assert.That(stored.UserId, Is.EqualTo(UserId));
            Assert.That(stored.TokenType, Is.EqualTo(TokenType.PasswordReset));
        }
    }

    [Test]
    public async Task GetValidTokenAsync_OtherType_ReturnsNull()
    {
        var token = NewToken(TokenType.EmailVerification);
        await _repository!.CreateAsync(token);

        var stored = await _repository.GetValidTokenAsync(token.Token, TokenType.PasswordReset);

        Assert.That(stored, Is.Null);
    }

    [Test]
    public async Task GetValidTokenAsync_ExpiredToken_ReturnsNull()
    {
        var token = NewToken(TokenType.EmailVerification, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        await _repository!.CreateAsync(token);

        var stored = await _repository.GetValidTokenAsync(token.Token, TokenType.EmailVerification);

        Assert.That(stored, Is.Null);
    }

    [Test]
    public async Task GetValidTokenAsync_UsedToken_ReturnsNull()
    {
        var token = NewToken(TokenType.PasswordReset, usedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        await _repository!.CreateAsync(token);

        var stored = await _repository.GetValidTokenAsync(token.Token, TokenType.PasswordReset);

        Assert.That(stored, Is.Null);
    }

    [Test]
    public async Task MarkAsUsedAsync_SetsUsedAtAndInvalidatesToken()
    {
        var token = NewToken(TokenType.PasswordReset);
        await _repository!.CreateAsync(token);
        var before = DateTimeOffset.UtcNow;

        await _repository.MarkAsUsedAsync(token.Token);

        var stored = await _repository.GetByTokenAsync(token.Token);
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.UsedAt, Is.Not.Null.And.GreaterThanOrEqualTo(before.AddSeconds(-1)));
            Assert.That(await _repository.GetValidTokenAsync(token.Token, TokenType.PasswordReset), Is.Null);
        }
    }

    [Test]
    public async Task CreateAsync_EmailChangeToken_RoundTripsTypeAndValue()
    {
        var token = NewToken(TokenType.EmailChange, value: "new-address@example.com");

        var id = await _repository!.CreateAsync(token);

        var stored = await _repository.GetValidTokenAsync(token.Token, TokenType.EmailChange);
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Id, Is.EqualTo(id));
            Assert.That(stored.TokenType, Is.EqualTo(TokenType.EmailChange));
            Assert.That(stored.Value, Is.EqualTo("new-address@example.com"));
        }
    }
}
