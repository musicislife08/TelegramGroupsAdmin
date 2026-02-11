using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.UnitTests.Services.Auth;

/// <summary>
/// Unit tests for PendingRecoveryCodesService.
/// Tests the in-memory storage of recovery codes during 2FA setup flow.
/// </summary>
[TestFixture]
public class PendingRecoveryCodesServiceTests
{
    private ILogger<PendingRecoveryCodesService> _mockLogger = null!;
    private PendingRecoveryCodesService _service = null!;

    private const string TestToken = "test-intermediate-token-abc123";
    private const string TestUserId = "test-user-id-123";

    [SetUp]
    public void SetUp()
    {
        _mockLogger = Substitute.For<ILogger<PendingRecoveryCodesService>>();
        _service = new PendingRecoveryCodesService(_mockLogger);
    }

    #region StoreRecoveryCodes Tests

    [Test]
    public void StoreRecoveryCodes_ValidInput_StoresSuccessfully()
    {
        // Arrange
        var codes = new List<string> { "code1", "code2", "code3" };

        // Act
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Assert
        Assert.That(_service.HasRecoveryCodes(TestToken, TestUserId), Is.True);
    }

    [Test]
    public void StoreRecoveryCodes_OverwritesPreviousEntry()
    {
        // Arrange
        var firstCodes = new List<string> { "first1", "first2" };
        var secondCodes = new List<string> { "second1", "second2", "second3" };

        // Act
        _service.StoreRecoveryCodes(TestToken, TestUserId, firstCodes);
        _service.StoreRecoveryCodes(TestToken, TestUserId, secondCodes);

        // Assert - should have the second set of codes
        var retrieved = _service.RetrieveRecoveryCodes(TestToken, TestUserId);
        Assert.That(retrieved, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved!.Count, Is.EqualTo(3));
            Assert.That(retrieved[0], Is.EqualTo("second1"));
        }
    }

    #endregion

    #region RetrieveRecoveryCodes Tests

    [Test]
    public void RetrieveRecoveryCodes_ValidTokenAndUser_ReturnsCodes()
    {
        // Arrange
        var codes = new List<string> { "abc123", "def456", "ghi789" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var retrieved = _service.RetrieveRecoveryCodes(TestToken, TestUserId);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved!.Count, Is.EqualTo(3));
            Assert.That(retrieved[0], Is.EqualTo("abc123"));
            Assert.That(retrieved[1], Is.EqualTo("def456"));
            Assert.That(retrieved[2], Is.EqualTo("ghi789"));
        }
    }

    [Test]
    public void RetrieveRecoveryCodes_CodesConsumedAfterFirstAccess()
    {
        // Arrange
        var codes = new List<string> { "code1", "code2" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act - first access should succeed
        var firstAccess = _service.RetrieveRecoveryCodes(TestToken, TestUserId);

        // Second access should fail (codes consumed)
        var secondAccess = _service.RetrieveRecoveryCodes(TestToken, TestUserId);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(firstAccess, Is.Not.Null);
            Assert.That(secondAccess, Is.Null);
        }
    }

    [Test]
    public void RetrieveRecoveryCodes_NullToken_ReturnsNull()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.RetrieveRecoveryCodes(null!, TestUserId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RetrieveRecoveryCodes_EmptyToken_ReturnsNull()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.RetrieveRecoveryCodes("", TestUserId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RetrieveRecoveryCodes_NullUserId_ReturnsNull()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.RetrieveRecoveryCodes(TestToken, null!);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RetrieveRecoveryCodes_EmptyUserId_ReturnsNull()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.RetrieveRecoveryCodes(TestToken, "");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RetrieveRecoveryCodes_WrongToken_ReturnsNull()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.RetrieveRecoveryCodes("wrong-token", TestUserId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RetrieveRecoveryCodes_WrongUserId_ReturnsNull()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.RetrieveRecoveryCodes(TestToken, "wrong-user-id");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RetrieveRecoveryCodes_NonExistentToken_ReturnsNull()
    {
        // Act - try to retrieve without storing first
        var result = _service.RetrieveRecoveryCodes(TestToken, TestUserId);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region HasRecoveryCodes Tests

    [Test]
    public void HasRecoveryCodes_AfterStore_ReturnsTrue()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.HasRecoveryCodes(TestToken, TestUserId);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void HasRecoveryCodes_AfterCodesConsumed_ReturnsFalse()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Consume the codes
        _service.RetrieveRecoveryCodes(TestToken, TestUserId);

        // Act
        var result = _service.HasRecoveryCodes(TestToken, TestUserId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasRecoveryCodes_NullToken_ReturnsFalse()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.HasRecoveryCodes(null!, TestUserId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasRecoveryCodes_EmptyToken_ReturnsFalse()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.HasRecoveryCodes("", TestUserId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasRecoveryCodes_NullUserId_ReturnsFalse()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.HasRecoveryCodes(TestToken, null!);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasRecoveryCodes_WrongUserId_ReturnsFalse()
    {
        // Arrange
        var codes = new List<string> { "code1" };
        _service.StoreRecoveryCodes(TestToken, TestUserId, codes);

        // Act
        var result = _service.HasRecoveryCodes(TestToken, "wrong-user-id");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasRecoveryCodes_NonExistent_ReturnsFalse()
    {
        // Act
        var result = _service.HasRecoveryCodes(TestToken, TestUserId);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Multiple Entries Tests

    [Test]
    public void MultipleEntries_DifferentTokens_IndependentStorage()
    {
        // Arrange
        var codes1 = new List<string> { "user1-code1" };
        var codes2 = new List<string> { "user2-code1", "user2-code2" };

        _service.StoreRecoveryCodes("token1", "user1", codes1);
        _service.StoreRecoveryCodes("token2", "user2", codes2);

        using (Assert.EnterMultipleScope())
        {
            // Act & Assert - both should exist independently
            Assert.That(_service.HasRecoveryCodes("token1", "user1"), Is.True);
            Assert.That(_service.HasRecoveryCodes("token2", "user2"), Is.True);
        }

        // Accessing one shouldn't affect the other
        var retrieved1 = _service.RetrieveRecoveryCodes("token1", "user1");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved1!.Count, Is.EqualTo(1));
            Assert.That(_service.HasRecoveryCodes("token2", "user2"), Is.True);
        }
    }

    [Test]
    public void MultipleEntries_SameUserDifferentTokens_BothAccessible()
    {
        // Arrange - same user, different tokens (e.g., multiple browser sessions)
        var codes1 = new List<string> { "session1-code" };
        var codes2 = new List<string> { "session2-code" };

        _service.StoreRecoveryCodes("token-session1", TestUserId, codes1);
        _service.StoreRecoveryCodes("token-session2", TestUserId, codes2);

        using (Assert.EnterMultipleScope())
        {
            // Act & Assert
            Assert.That(_service.HasRecoveryCodes("token-session1", TestUserId), Is.True);
            Assert.That(_service.HasRecoveryCodes("token-session2", TestUserId), Is.True);
        }

        var retrieved1 = _service.RetrieveRecoveryCodes("token-session1", TestUserId);
        var retrieved2 = _service.RetrieveRecoveryCodes("token-session2", TestUserId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved1![0], Is.EqualTo("session1-code"));
            Assert.That(retrieved2![0], Is.EqualTo("session2-code"));
        }
    }

    #endregion
}
