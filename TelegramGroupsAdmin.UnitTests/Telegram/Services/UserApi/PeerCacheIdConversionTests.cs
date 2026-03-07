namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Unit tests for bot API chat ID to WTelegram channel ID conversion.
///
/// Testing strategy:
/// - Tests the ID conversion math used by IWTelegramApiClient.GetInputPeerForChat()
/// - Verifies conversion logic for all chat ID formats:
///   * Supergroups: -100{channelId} format (large negative numbers)
///   * Regular groups: -{chatId} format (small negative numbers)
///   * User chats: positive IDs (pass-through)
/// - No mocking required - pure math test
/// - Encapsulates conversion logic to test independently of WTelegram dependency
/// </summary>
[TestFixture]
public class PeerCacheIdConversionTests
{
    /// <summary>
    /// Replicates the bot API ID to channel ID conversion logic.
    /// This mirrors the math in WTelegramApiClient.GetInputPeerForChat().
    /// </summary>
    private static long ConvertBotApiIdToChannelId(long botApiChatId)
    {
        // Supergroup/channel IDs: -100{channelId} format
        // e.g., -1001322973935 → channel ID 1322973935
        if (botApiChatId < -1000000000000)
            return -(botApiChatId + 1000000000000);

        // Regular group chats: -{chatId} format
        // e.g., -123456 → channel ID 123456
        if (botApiChatId < 0)
            return -botApiChatId;

        // User chats: positive IDs pass through unchanged
        return botApiChatId;
    }

    #region Supergroup/Channel Conversion (-100{channelId} format)

    [TestCase(-1001322973935L, 1322973935L, "Supergroup")]
    [TestCase(-1001000000001L, 1000000001L, "Supergroup (near boundary)")]
    [TestCase(-1002000000000L, 2000000000L, "Channel")]
    public void ConvertBotApiIdToChannelId_Supergroup_ReturnsCorrectChannelId(
        long botApiChatId,
        long expectedChannelId,
        string description)
    {
        // Act
        var result = ConvertBotApiIdToChannelId(botApiChatId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedChannelId), description);
    }

    #endregion

    #region Regular Group Conversion (-{chatId} format)

    [TestCase(-123456L, 123456L, "Regular group")]
    [TestCase(-1L, 1L, "Smallest regular group")]
    [TestCase(-999999999L, 999999999L, "Large regular group")]
    public void ConvertBotApiIdToChannelId_RegularGroup_ReturnsAbsoluteValue(
        long botApiChatId,
        long expectedChannelId,
        string description)
    {
        // Act
        var result = ConvertBotApiIdToChannelId(botApiChatId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedChannelId), description);
    }

    #endregion

    #region User Chat Conversion (positive IDs)

    [TestCase(12345L, 12345L, "User chat (positive)")]
    [TestCase(1L, 1L, "Smallest user ID")]
    [TestCase(9876543210L, 9876543210L, "Large user ID")]
    public void ConvertBotApiIdToChannelId_UserChat_ReturnsSameId(
        long botApiChatId,
        long expectedChannelId,
        string description)
    {
        // Act
        var result = ConvertBotApiIdToChannelId(botApiChatId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedChannelId), description);
    }

    #endregion

    #region Boundary and Edge Cases

    [TestCase(0L, 0L, "Zero (edge case)")]
    public void ConvertBotApiIdToChannelId_EdgeCase_Zero_ReturnsSameId(
        long botApiChatId,
        long expectedChannelId,
        string description)
    {
        // Act
        var result = ConvertBotApiIdToChannelId(botApiChatId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedChannelId), description);
    }

    [Test]
    public void ConvertBotApiIdToChannelId_BoundaryRegularVsSupergroup_AtExactBoundary()
    {
        // The boundary is -1000000000000
        // Just before (-999999999999) should be treated as regular group
        // Just after (-1000000000001) should be treated as supergroup

        long justBefore = -999999999999L;  // Regular group
        long justAfter = -1000000000001L;  // Supergroup

        var beforeResult = ConvertBotApiIdToChannelId(justBefore);
        var afterResult = ConvertBotApiIdToChannelId(justAfter);

        // justBefore (-999999999999) → regular group → 999999999999
        Assert.That(beforeResult, Is.EqualTo(999999999999L));

        // justAfter (-1000000000001) → supergroup → 1 (because -(-1000000000001 + 1000000000000) = 1)
        Assert.That(afterResult, Is.EqualTo(1L));
    }

    [Test]
    public void ConvertBotApiIdToChannelId_ExactBoundary_MinusOneBillion()
    {
        // -1000000000000 is NOT a supergroup (strict < check), so it falls into the
        // regular group branch: -(-1000000000000) = 1000000000000
        long atBoundary = -1000000000000L;

        var result = ConvertBotApiIdToChannelId(atBoundary);

        Assert.That(result, Is.EqualTo(1000000000000L));
    }

    #endregion

    #region Symmetric Conversion Verification

    [Test]
    public void ConvertBotApiIdToChannelId_VerifySymmetry_RegularGroup()
    {
        // For regular groups: botApiId = -channelId, conversion = -botApiId = channelId
        long channelId = 123456L;
        long botApiId = -channelId;

        var result = ConvertBotApiIdToChannelId(botApiId);

        Assert.That(result, Is.EqualTo(channelId));
    }

    [Test]
    public void ConvertBotApiIdToChannelId_VerifySymmetry_Supergroup()
    {
        // For supergroups: botApiId = -100000000000 - channelId
        // conversion = -(botApiId + 1000000000000) = channelId
        long channelId = 1322973935L;
        long botApiId = -1000000000000L - channelId;

        var result = ConvertBotApiIdToChannelId(botApiId);

        Assert.That(result, Is.EqualTo(channelId));
    }

    #endregion
}
