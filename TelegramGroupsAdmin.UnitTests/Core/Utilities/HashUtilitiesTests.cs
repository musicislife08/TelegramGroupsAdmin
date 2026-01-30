using System.Text;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

/// <summary>
/// Unit tests for HashUtilities.
/// Tests SHA256 hashing for file deduplication, content correlation, and message duplicate detection.
/// </summary>
[TestFixture]
public class HashUtilitiesTests
{
    #region ComputeSHA256 (String) - Basic Tests

    [Test]
    public void ComputeSHA256_KnownInput_ReturnsExpectedHash()
    {
        // SHA256 of "hello" is well-known
        var result = HashUtilities.ComputeSHA256("hello");

        Assert.That(result, Is.EqualTo("2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824"));
    }

    [Test]
    public void ComputeSHA256_EmptyString_ReturnsEmptyStringHash()
    {
        // SHA256 of empty string is well-known
        var result = HashUtilities.ComputeSHA256("");

        Assert.That(result, Is.EqualTo("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"));
    }

    [Test]
    public void ComputeSHA256_SameInput_ReturnsSameHash()
    {
        var result1 = HashUtilities.ComputeSHA256("test content");
        var result2 = HashUtilities.ComputeSHA256("test content");

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void ComputeSHA256_DifferentInput_ReturnsDifferentHash()
    {
        var result1 = HashUtilities.ComputeSHA256("content a");
        var result2 = HashUtilities.ComputeSHA256("content b");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void ComputeSHA256_ReturnsUppercaseHex()
    {
        var result = HashUtilities.ComputeSHA256("test");

        // Should be uppercase hex with no lowercase letters
        Assert.That(result, Does.Match("^[A-F0-9]+$"));
    }

    [Test]
    public void ComputeSHA256_Returns64Characters()
    {
        var result = HashUtilities.ComputeSHA256("any content");

        // SHA256 is 256 bits = 64 hex characters
        Assert.That(result.Length, Is.EqualTo(64));
    }

    #endregion

    #region ComputeSHA256 (String) - Edge Cases

    [Test]
    public void ComputeSHA256_Whitespace_HashesCorrectly()
    {
        var result = HashUtilities.ComputeSHA256("   ");

        Assert.That(result.Length, Is.EqualTo(64));
        Assert.That(result, Is.Not.EqualTo(HashUtilities.ComputeSHA256("")));
    }

    [Test]
    public void ComputeSHA256_Unicode_HashesCorrectly()
    {
        var result = HashUtilities.ComputeSHA256("Hello 世界 🌍");

        Assert.That(result.Length, Is.EqualTo(64));
    }

    [Test]
    public void ComputeSHA256_LongString_HashesCorrectly()
    {
        var longString = new string('x', 10000);
        var result = HashUtilities.ComputeSHA256(longString);

        Assert.That(result.Length, Is.EqualTo(64));
    }

    [Test]
    public void ComputeSHA256_CaseSensitive()
    {
        var resultLower = HashUtilities.ComputeSHA256("hello");
        var resultUpper = HashUtilities.ComputeSHA256("HELLO");

        Assert.That(resultLower, Is.Not.EqualTo(resultUpper));
    }

    #endregion

    #region ComputeSHA256Async (Stream) - Basic Tests

    [Test]
    public async Task ComputeSHA256Async_KnownInput_ReturnsExpectedHash()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var result = await HashUtilities.ComputeSHA256Async(stream);

        // Same content as string version, but lowercase
        Assert.That(result, Is.EqualTo("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"));
    }

    [Test]
    public async Task ComputeSHA256Async_EmptyStream_ReturnsEmptyHash()
    {
        using var stream = new MemoryStream();

        var result = await HashUtilities.ComputeSHA256Async(stream);

        // Same as empty string hash, but lowercase
        Assert.That(result, Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
    }

    [Test]
    public async Task ComputeSHA256Async_SameContent_ReturnsSameHash()
    {
        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("test content"));

        var result1 = await HashUtilities.ComputeSHA256Async(stream1);
        var result2 = await HashUtilities.ComputeSHA256Async(stream2);

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public async Task ComputeSHA256Async_DifferentContent_ReturnsDifferentHash()
    {
        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("content a"));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("content b"));

        var result1 = await HashUtilities.ComputeSHA256Async(stream1);
        var result2 = await HashUtilities.ComputeSHA256Async(stream2);

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public async Task ComputeSHA256Async_ReturnsLowercaseHex()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        var result = await HashUtilities.ComputeSHA256Async(stream);

        // Should be lowercase hex with no uppercase letters
        Assert.That(result, Does.Match("^[a-f0-9]+$"));
    }

    [Test]
    public async Task ComputeSHA256Async_Returns64Characters()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("any content"));

        var result = await HashUtilities.ComputeSHA256Async(stream);

        Assert.That(result.Length, Is.EqualTo(64));
    }

    #endregion

    #region ComputeSHA256Async (Stream) - Edge Cases

    [Test]
    public async Task ComputeSHA256Async_BinaryData_HashesCorrectly()
    {
        var binaryData = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD };
        using var stream = new MemoryStream(binaryData);

        var result = await HashUtilities.ComputeSHA256Async(stream);

        Assert.That(result.Length, Is.EqualTo(64));
    }

    [Test]
    public async Task ComputeSHA256Async_LargeStream_HashesCorrectly()
    {
        var largeData = new byte[100000];
        Array.Fill<byte>(largeData, 0xAB);
        using var stream = new MemoryStream(largeData);

        var result = await HashUtilities.ComputeSHA256Async(stream);

        Assert.That(result.Length, Is.EqualTo(64));
    }

    [Test]
    public async Task ComputeSHA256Async_WithCancellation_CanBeCancelled()
    {
        var largeData = new byte[1000000];
        using var stream = new MemoryStream(largeData);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // TaskCanceledException inherits from OperationCanceledException
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await HashUtilities.ComputeSHA256Async(stream, cts.Token));
    }

    #endregion

    #region ComputeContentHash - Basic Tests

    [Test]
    public void ComputeContentHash_BasicContent_ReturnsHash()
    {
        var result = HashUtilities.ComputeContentHash("Hello world", "https://example.com");

        Assert.That(result.Length, Is.EqualTo(64));
    }

    [Test]
    public void ComputeContentHash_SameContent_ReturnsSameHash()
    {
        var result1 = HashUtilities.ComputeContentHash("message", "url");
        var result2 = HashUtilities.ComputeContentHash("message", "url");

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void ComputeContentHash_DifferentContent_ReturnsDifferentHash()
    {
        var result1 = HashUtilities.ComputeContentHash("message a", "url");
        var result2 = HashUtilities.ComputeContentHash("message b", "url");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void ComputeContentHash_DifferentUrls_ReturnsDifferentHash()
    {
        var result1 = HashUtilities.ComputeContentHash("message", "url a");
        var result2 = HashUtilities.ComputeContentHash("message", "url b");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    #endregion

    #region ComputeContentHash - Normalization

    [Test]
    public void ComputeContentHash_NormalizesToLowerCase()
    {
        var resultLower = HashUtilities.ComputeContentHash("hello", "url");
        var resultUpper = HashUtilities.ComputeContentHash("HELLO", "URL");

        Assert.That(resultLower, Is.EqualTo(resultUpper));
    }

    [Test]
    public void ComputeContentHash_TrimsWhitespace()
    {
        var resultTrimmed = HashUtilities.ComputeContentHash("hello", "url");
        var resultWithSpaces = HashUtilities.ComputeContentHash("  hello  ", "  url  ");

        Assert.That(resultTrimmed, Is.EqualTo(resultWithSpaces));
    }

    [Test]
    public void ComputeContentHash_NormalizesAndTrims()
    {
        var result1 = HashUtilities.ComputeContentHash("Hello World", "https://example.com");
        var result2 = HashUtilities.ComputeContentHash("  HELLO WORLD  ", "  HTTPS://EXAMPLE.COM  ");

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void ComputeContentHash_ConcatenatesTextAndUrls()
    {
        // The hash should be of the concatenated normalized string
        var result = HashUtilities.ComputeContentHash("text", "url");

        // This should equal ComputeSHA256("texturl")
        var expected = HashUtilities.ComputeSHA256("texturl");
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region ComputeContentHash - Message Deduplication Scenarios

    [Test]
    public void ComputeContentHash_SpamDeduplication_DetectsDuplicates()
    {
        // Same spam message with different formatting should hash the same
        var spam1 = HashUtilities.ComputeContentHash(
            "CHECK OUT THIS AMAZING OFFER!!!",
            "[\"https://scam-site.com\"]");
        var spam2 = HashUtilities.ComputeContentHash(
            "check out this amazing offer!!!",
            "[\"HTTPS://SCAM-SITE.COM\"]");

        Assert.That(spam1, Is.EqualTo(spam2));
    }

    [Test]
    public void ComputeContentHash_EmptyMessageText_StillHashesUrls()
    {
        var result = HashUtilities.ComputeContentHash("", "https://example.com");

        Assert.That(result.Length, Is.EqualTo(64));
    }

    [Test]
    public void ComputeContentHash_EmptyUrls_StillHashesText()
    {
        var result = HashUtilities.ComputeContentHash("Hello world", "");

        Assert.That(result.Length, Is.EqualTo(64));
    }

    [Test]
    public void ComputeContentHash_BothEmpty_ReturnsEmptyHash()
    {
        var result = HashUtilities.ComputeContentHash("", "");

        Assert.That(result, Is.EqualTo(HashUtilities.ComputeSHA256("")));
    }

    #endregion

    #region Consistency Tests

    [Test]
    public async Task ComputeSHA256_StringAndStream_ProduceDifferentCaseHashes()
    {
        // String version returns uppercase, stream version returns lowercase
        var content = "hello";
        var stringResult = HashUtilities.ComputeSHA256(content);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var streamResult = await HashUtilities.ComputeSHA256Async(stream);

        // Should be same hash value, just different case
        Assert.That(stringResult.ToLowerInvariant(), Is.EqualTo(streamResult));
    }

    #endregion
}
