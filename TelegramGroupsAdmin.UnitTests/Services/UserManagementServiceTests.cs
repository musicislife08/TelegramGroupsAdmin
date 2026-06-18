using NSubstitute;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin.UnitTests.Services;

[TestFixture]
public class UserManagementServiceTests
{
    private IUserRepository _userRepository = null!;
    private IAuditService _auditService = null!;
    private UserManagementService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _auditService = Substitute.For<IAuditService>();
        _service = new UserManagementService(_userRepository, _auditService);
    }

    [Test]
    public async Task UpdatePermissionLevelAsync_RotatesSecurityStamp()
    {
        // Act: modifier is Owner (level 2) downgrading a user to Admin (level 0)
        await _service.UpdatePermissionLevelAsync("target-user", permissionLevel: 0, modifiedBy: "owner-user", modifierPermissionLevel: 2);

        // Assert
        await _userRepository.Received(1).UpdatePermissionLevelAsync("target-user", 0, "owner-user", Arg.Any<CancellationToken>());
        await _userRepository.Received(1).UpdateSecurityStampAsync("target-user", Arg.Any<CancellationToken>());
    }
}
