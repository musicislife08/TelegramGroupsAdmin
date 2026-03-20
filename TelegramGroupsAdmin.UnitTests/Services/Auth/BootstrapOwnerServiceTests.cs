using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.UnitTests.Services.Auth;

/// <summary>
/// Unit tests for BootstrapOwnerService covering BOOT-01 through BOOT-06.
/// </summary>
[TestFixture]
public class BootstrapOwnerServiceTests
{
    private IUserRepository _userRepository = null!;
    private IPasswordHasher _passwordHasher = null!;
    private IAuditService _auditService = null!;
    private ILogger _logger = null!;
    private string? _tempFilePath;

    private const string ValidEmail = "owner@example.com";
    private const string ValidPassword = "SecureP@ssw0rd!";
    private const string HashedPassword = "hashed_password_value";

    [SetUp]
    public void SetUp()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _passwordHasher = Substitute.For<IPasswordHasher>();
        _auditService = Substitute.For<IAuditService>();
        _logger = Substitute.For<ILogger>();

        // Default: no users exist
        _userRepository.AnyUsersExistAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(false);

        // Default: password hasher returns a known hash
        _passwordHasher.HashPassword(Arg.Any<string>()).Returns(HashedPassword);

        // Default: CreateAsync returns a user ID
        _userRepository.CreateAsync(Arg.Any<UserRecord>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<UserRecord>(0).WebUser.Id);

        _tempFilePath = null;
    }

    [TearDown]
    public void TearDown()
    {
        if (_tempFilePath != null && File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    private string CreateValidBootstrapFile(string email = ValidEmail, string password = ValidPassword)
    {
        var path = Path.GetTempFileName();
        _tempFilePath = path;
        var json = JsonSerializer.Serialize(new { email, password });
        File.WriteAllText(path, json);
        return path;
    }

    #region BOOT-01: Happy path - creates Owner account

    [Test]
    public async Task BOOT01_ValidFile_NoExistingUsers_CreatesOwnerAccount()
    {
        // Arrange
        var filePath = CreateValidBootstrapFile();

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: filePath,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.True);
        await _userRepository.Received(1).CreateAsync(
            Arg.Is<UserRecord>(r =>
                r.WebUser.PermissionLevel == PermissionLevel.Owner &&
                r.WebUser.Email == ValidEmail &&
                r.PasswordHash == HashedPassword &&
                r.EmailVerified == true &&
                r.TotpEnabled == true &&
                r.TotpSecret == null),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region BOOT-02: Idempotent skip when users already exist

    [Test]
    public async Task BOOT02_UsersAlreadyExist_SkipsCreation_ReturnsSuccess()
    {
        // Arrange
        _userRepository.AnyUsersExistAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(true);

        // No file path needed - should not even try to read the file
        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: null,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("already").IgnoreCase);
        await _userRepository.DidNotReceive().CreateAsync(
            Arg.Any<UserRecord>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region BOOT-03: Validation failures

    [Test]
    public async Task BOOT03a_NullFilePath_ReturnsFailed()
    {
        // Arrange
        _userRepository.AnyUsersExistAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: null,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.False);
        await _userRepository.DidNotReceive().CreateAsync(
            Arg.Any<UserRecord>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BOOT03b_FileNotFound_ReturnsFailed()
    {
        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: "/nonexistent/path/bootstrap.json",
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.False);
        await _userRepository.DidNotReceive().CreateAsync(
            Arg.Any<UserRecord>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BOOT03c_EmptyFile_ReturnsFailed()
    {
        // Arrange
        var path = Path.GetTempFileName();
        _tempFilePath = path;
        File.WriteAllText(path, "   "); // whitespace only

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: path,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.False);
        await _userRepository.DidNotReceive().CreateAsync(
            Arg.Any<UserRecord>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BOOT03d_InvalidJson_ReturnsFailed()
    {
        // Arrange
        var path = Path.GetTempFileName();
        _tempFilePath = path;
        File.WriteAllText(path, "{ this is not json }");

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: path,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.False);
        await _userRepository.DidNotReceive().CreateAsync(
            Arg.Any<UserRecord>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BOOT03e_MissingEmail_ReturnsFailed()
    {
        // Arrange
        var path = Path.GetTempFileName();
        _tempFilePath = path;
        File.WriteAllText(path, JsonSerializer.Serialize(new { password = "SomePassword" }));

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: path,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.False);
        await _userRepository.DidNotReceive().CreateAsync(
            Arg.Any<UserRecord>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BOOT03e_EmailMissingAtSign_ReturnsFailed()
    {
        // Arrange
        var path = Path.GetTempFileName();
        _tempFilePath = path;
        File.WriteAllText(path, JsonSerializer.Serialize(new { email = "notanemail", password = "SomePassword" }));

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: path,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.False);
        await _userRepository.DidNotReceive().CreateAsync(
            Arg.Any<UserRecord>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BOOT03f_MissingPassword_ReturnsFailed()
    {
        // Arrange
        var path = Path.GetTempFileName();
        _tempFilePath = path;
        File.WriteAllText(path, JsonSerializer.Serialize(new { email = ValidEmail }));

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: path,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.False);
        await _userRepository.DidNotReceive().CreateAsync(
            Arg.Any<UserRecord>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region BOOT-04: EmailVerified=true

    [Test]
    public async Task BOOT04_CreatedUser_HasEmailVerifiedTrue()
    {
        // Arrange
        var filePath = CreateValidBootstrapFile();
        UserRecord? capturedRecord = null;
        await _userRepository.CreateAsync(
            Arg.Do<UserRecord>(r => capturedRecord = r),
            cancellationToken: Arg.Any<CancellationToken>());

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: filePath,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(capturedRecord, Is.Not.Null);
        Assert.That(capturedRecord!.EmailVerified, Is.True);
    }

    #endregion

    #region BOOT-05: TotpEnabled=true, TotpSecret=null

    [Test]
    public async Task BOOT05_CreatedUser_HasTotpEnabledTrueAndSecretNull()
    {
        // Arrange
        var filePath = CreateValidBootstrapFile();
        UserRecord? capturedRecord = null;
        await _userRepository.CreateAsync(
            Arg.Do<UserRecord>(r => capturedRecord = r),
            cancellationToken: Arg.Any<CancellationToken>());

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: filePath,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(capturedRecord, Is.Not.Null);
        Assert.That(capturedRecord!.TotpEnabled, Is.True);
        Assert.That(capturedRecord!.TotpSecret, Is.Null);
    }

    #endregion

    #region BOOT-06: Audit log

    [Test]
    public async Task BOOT06a_AuditLog_WrittenWithBootstrapActor()
    {
        // Arrange
        var filePath = CreateValidBootstrapFile();

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: filePath,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert
        Assert.That(result.Success, Is.True);
        await _auditService.Received(1).LogEventAsync(
            AuditEventType.UserRegistered,
            Arg.Is<Actor>(a => a.SystemIdentifier == "bootstrap"),
            Arg.Any<Actor?>(),
            Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BOOT06b_AuditLogFailure_IsNonFatal_ReturnsSuccess()
    {
        // Arrange
        var filePath = CreateValidBootstrapFile();
        _auditService.LogEventAsync(
                Arg.Any<AuditEventType>(),
                Arg.Any<Actor>(),
                Arg.Any<Actor?>(),
                Arg.Any<string?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Audit DB connection failed"));

        // Act
        var result = await BootstrapOwnerService.ExecuteAsync(
            filePath: filePath,
            userRepository: _userRepository,
            passwordHasher: _passwordHasher,
            auditService: _auditService,
            logger: _logger);

        // Assert: overall success despite audit failure
        Assert.That(result.Success, Is.True);
    }

    #endregion

    #region Exception propagation

    [Test]
    public void ExecuteAsync_WhenCreateAsyncThrows_ExceptionPropagates()
    {
        // Arrange
        var filePath = CreateValidBootstrapFile();

        _userRepository.AnyUsersExistAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(false);

        _userRepository.CreateAsync(Arg.Any<UserRecord>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("Duplicate email"));

        // Act & Assert
        Assert.ThrowsAsync<DbUpdateException>(async () =>
            await BootstrapOwnerService.ExecuteAsync(
                filePath: filePath,
                userRepository: _userRepository,
                passwordHasher: _passwordHasher,
                auditService: _auditService,
                logger: _logger));
    }

    [Test]
    public void ExecuteAsync_WhenAnyUsersExistAsyncThrows_ExceptionPropagates()
    {
        // Arrange
        _userRepository.AnyUsersExistAsync(cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await BootstrapOwnerService.ExecuteAsync(
                filePath: null,
                userRepository: _userRepository,
                passwordHasher: _passwordHasher,
                auditService: _auditService,
                logger: _logger));
    }

    #endregion
}
