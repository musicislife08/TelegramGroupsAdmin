using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.UnitTests.Services.Auth;

/// <summary>
/// Unit tests for recovery code issuing and use in TotpService. Issuing a set must replace the user's
/// whole stored set in one repository call, never append to it, and a code handed to the user must be
/// accepted by the same hashing the login path uses.
/// </summary>
[TestFixture]
public class TotpServiceRecoveryCodeTests
{
    private static readonly WebUserIdentity User = new("user-id", "user@unit.test", PermissionLevel.Admin);

    private IUserRepository _users = null!;
    private TotpService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<IUserRepository>();
        _service = new TotpService(
            _users,
            Substitute.For<IAuditService>(),
            Substitute.For<IDataProtectionService>(),
            Substitute.For<ILogger<TotpService>>());
    }

    [Test]
    public async Task GenerateRecoveryCodesAsync_ReplacesTheStoredSetInOneCall()
    {
        IReadOnlyCollection<string>? storedHashes = null;
        await _users.ReplaceRecoveryCodesAsync(User.Id, Arg.Do<IReadOnlyCollection<string>>(h => storedHashes = h), Arg.Any<CancellationToken>());

        var codes = await _service.GenerateRecoveryCodesAsync(User);

        await _users.Received(1).ReplaceRecoveryCodesAsync(User.Id, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(codes, Has.Count.EqualTo(AuthenticationConstants.RecoveryCodeCount));
            Assert.That(codes, Is.Unique);
            Assert.That(codes, Has.All.Length.EqualTo(AuthenticationConstants.RecoveryCodeStringLength));
            Assert.That(storedHashes, Has.Count.EqualTo(codes.Count), "one stored hash per issued code");
            Assert.That(storedHashes, Has.None.AnyOf(codes.ToArray()), "only hashes are stored, never the codes themselves");
        }
    }

    [Test]
    public async Task GenerateRecoveryCodesAsync_IssuedCodeIsAcceptedByTheLoginPath_InAnyCase()
    {
        IReadOnlyCollection<string> storedHashes = [];
        await _users.ReplaceRecoveryCodesAsync(User.Id, Arg.Do<IReadOnlyCollection<string>>(h => storedHashes = h), Arg.Any<CancellationToken>());
        _users.UseRecoveryCodeAsync(User.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => storedHashes.Contains(call.ArgAt<string>(1)));

        var codes = await _service.GenerateRecoveryCodesAsync(User);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _service.UseRecoveryCodeAsync(User, codes.First()), Is.True);
            Assert.That(await _service.UseRecoveryCodeAsync(User, codes.Last().ToUpperInvariant()), Is.True);
            Assert.That(await _service.UseRecoveryCodeAsync(User, "0000000000000000"), Is.False);
        }
    }
}
