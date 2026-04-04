using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for MessageProcessingService.BuildProfileChangeReason.
/// Verifies that the human-readable audit reason string correctly describes
/// username, first name, and last name changes, including null (none) cases.
/// </summary>
[TestFixture]
public class MessageProcessingServiceProfileDiffTests
{
    [Test]
    public void BuildProfileChangeReason_UsernameChanged_IncludesUsernameInReason()
    {
        var old = CreateUser(username: "old_user");
        var current = CreateSdkUser(username: "new_user");

        var result = MessageProcessingService.BuildProfileChangeReason(old, current);

        Assert.That(result, Does.Contain("Username: @old_user → @new_user"));
    }

    [Test]
    public void BuildProfileChangeReason_FirstNameChanged_IncludesFirstNameInReason()
    {
        var old = CreateUser(firstName: "OldFirst");
        var current = CreateSdkUser(firstName: "NewFirst");

        var result = MessageProcessingService.BuildProfileChangeReason(old, current);

        Assert.That(result, Does.Contain("First name: OldFirst → NewFirst"));
    }

    [Test]
    public void BuildProfileChangeReason_LastNameChanged_IncludesLastNameInReason()
    {
        var old = CreateUser(lastName: "OldLast");
        var current = CreateSdkUser(lastName: "NewLast");

        var result = MessageProcessingService.BuildProfileChangeReason(old, current);

        Assert.That(result, Does.Contain("Last name: OldLast → NewLast"));
    }

    [Test]
    public void BuildProfileChangeReason_MultipleFieldsChanged_IncludesAll()
    {
        var old = CreateUser(username: "old_user", firstName: "OldFirst", lastName: "OldLast");
        var current = CreateSdkUser(username: "new_user", firstName: "NewFirst", lastName: "NewLast");

        var result = MessageProcessingService.BuildProfileChangeReason(old, current);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Username: @old_user → @new_user"));
            Assert.That(result, Does.Contain("First name: OldFirst → NewFirst"));
            Assert.That(result, Does.Contain("Last name: OldLast → NewLast"));
        });
    }

    [Test]
    public void BuildProfileChangeReason_NullToValue_ShowsNone()
    {
        var old = CreateUser(username: null);
        var current = CreateSdkUser(username: "new_user");

        var result = MessageProcessingService.BuildProfileChangeReason(old, current);

        Assert.That(result, Does.Contain("Username: @(none) → @new_user"));
    }

    [Test]
    public void BuildProfileChangeReason_ValueToNull_ShowsNone()
    {
        var old = CreateUser(username: "old_user");
        var current = CreateSdkUser(username: null);

        var result = MessageProcessingService.BuildProfileChangeReason(old, current);

        Assert.That(result, Does.Contain("Username: @old_user → @(none)"));
    }

    private static TelegramUser CreateUser(
        string? username = "testuser", string? firstName = "Test", string? lastName = "User")
    {
        var now = DateTimeOffset.UtcNow;
        return new TelegramUser(
            TelegramUserId: 12345,
            Username: username, FirstName: firstName, LastName: lastName,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: false, IsBanned: false,
            KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: now, LastSeenAt: now, CreatedAt: now, UpdatedAt: now);
    }

    private static global::Telegram.Bot.Types.User CreateSdkUser(
        string? username = "testuser", string? firstName = "Test", string? lastName = "User")
    {
        return new global::Telegram.Bot.Types.User
        {
            Id = 12345,
            IsBot = false,
            FirstName = firstName ?? "Test",
            LastName = lastName,
            Username = username,
        };
    }
}
