using TelegramGroupsAdmin.Telegram.Helpers;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Helpers;

/// <summary>
/// Unit tests for ProfileChangeReason.Build: the human-readable ProfileChange audit reason
/// describing username, first name and last name changes, including null (none) cases.
/// </summary>
[TestFixture]
public class ProfileChangeReasonTests
{
    [Test]
    public void Build_UsernameChanged_IncludesUsernameInReason()
    {
        var result = ProfileChangeReason.Build(Previous(username: "old_user"), Current(username: "new_user"));

        Assert.That(result, Is.EqualTo("Username: @old_user → @new_user"));
    }

    [Test]
    public void Build_FirstNameChanged_IncludesFirstNameInReason()
    {
        var result = ProfileChangeReason.Build(Previous(firstName: "OldFirst"), Current(firstName: "NewFirst"));

        Assert.That(result, Is.EqualTo("First name: OldFirst → NewFirst"));
    }

    [Test]
    public void Build_LastNameChanged_IncludesLastNameInReason()
    {
        var result = ProfileChangeReason.Build(Previous(lastName: "OldLast"), Current(lastName: "NewLast"));

        Assert.That(result, Is.EqualTo("Last name: OldLast → NewLast"));
    }

    [Test]
    public void Build_MultipleFieldsChanged_IncludesAllInOrder()
    {
        var result = ProfileChangeReason.Build(
            Previous(username: "old_user", firstName: "OldFirst", lastName: "OldLast"),
            Current(username: "new_user", firstName: "NewFirst", lastName: "NewLast"));

        Assert.That(result, Is.EqualTo(
            "Username: @old_user → @new_user, First name: OldFirst → NewFirst, Last name: OldLast → NewLast"));
    }

    [Test]
    public void Build_NullToValue_ShowsNone()
    {
        var result = ProfileChangeReason.Build(Previous(username: null), Current(username: "new_user"));

        Assert.That(result, Is.EqualTo("Username: @(none) → @new_user"));
    }

    [Test]
    public void Build_ValueToNull_ShowsNone()
    {
        var result = ProfileChangeReason.Build(Previous(username: "old_user"), Current(username: null));

        Assert.That(result, Is.EqualTo("Username: @old_user → @(none)"));
    }

    [Test]
    public void Build_FirstNameToNull_ShowsNone()
    {
        var result = ProfileChangeReason.Build(Previous(firstName: "OldFirst"), Current(firstName: null));

        Assert.That(result, Is.EqualTo("First name: OldFirst → (none)"));
    }

    private static PreviousNames Previous(
        string? username = "testuser", string? firstName = "Test", string? lastName = "User") =>
        new(firstName, lastName, username);

    private static ObservedUser Current(
        string? username = "testuser", string? firstName = "Test", string? lastName = "User") =>
        new(12345, firstName, lastName, username, IsBot: false, ObservationSource.BotUpdate, DateTimeOffset.UtcNow);
}
