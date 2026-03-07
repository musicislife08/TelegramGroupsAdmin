using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

/// <summary>
/// Unit tests for BitwiseUtilities.
/// Tests Hamming distance calculation and population count operations
/// used in perceptual image hashing for duplicate detection.
/// </summary>
[TestFixture]
public class BitwiseUtilitiesTests
{
    #region HammingDistance Tests

    [Test]
    public void HammingDistance_IdenticalBytes_ReturnsZero()
    {
        var bytes = new byte[] { 0x12, 0x34, 0x56, 0x78 };

        var result = BitwiseUtilities.HammingDistance(bytes, bytes);

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void HammingDistance_IdenticalArraysCopy_ReturnsZero()
    {
        var hash1 = new byte[] { 0xAB, 0xCD, 0xEF };
        var hash2 = new byte[] { 0xAB, 0xCD, 0xEF };

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void HammingDistance_SingleBitDifference_ReturnsOne()
    {
        // 0x00 = 00000000
        // 0x01 = 00000001 (1 bit different)
        var hash1 = new byte[] { 0x00 };
        var hash2 = new byte[] { 0x01 };

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void HammingDistance_AllBitsDifferent_SingleByte_Returns8()
    {
        // 0xFF = 11111111
        // 0x00 = 00000000 (all 8 bits different)
        var hash1 = new byte[] { 0xFF };
        var hash2 = new byte[] { 0x00 };

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        Assert.That(result, Is.EqualTo(8));
    }

    [Test]
    public void HammingDistance_AllBitsDifferent_MultipleBytes_Returns16()
    {
        var hash1 = new byte[] { 0xFF, 0x00 };
        var hash2 = new byte[] { 0x00, 0xFF };

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        Assert.That(result, Is.EqualTo(16));
    }

    [Test]
    public void HammingDistance_EmptyArrays_ReturnsZero()
    {
        var hash1 = Array.Empty<byte>();
        var hash2 = Array.Empty<byte>();

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        Assert.That(result, Is.EqualTo(0));
    }

    [TestCase(new byte[] { 0xF0 }, new byte[] { 0x0F }, ExpectedResult = 8)]
    [TestCase(new byte[] { 0x55 }, new byte[] { 0xAA }, ExpectedResult = 8)]
    [TestCase(new byte[] { 0xFF, 0xFF }, new byte[] { 0x00, 0x00 }, ExpectedResult = 16)]
    [TestCase(new byte[] { 0x0F, 0xF0 }, new byte[] { 0x00, 0x00 }, ExpectedResult = 8)]
    public int HammingDistance_KnownPatterns_ReturnsExpected(byte[] hash1, byte[] hash2)
    {
        return BitwiseUtilities.HammingDistance(hash1, hash2);
    }

    [Test]
    public void HammingDistance_HighNibbleDifference_Returns4()
    {
        // 0xF0 = 11110000
        // 0x00 = 00000000 (upper 4 bits different)
        var hash1 = new byte[] { 0xF0 };
        var hash2 = new byte[] { 0x00 };

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        Assert.That(result, Is.EqualTo(4));
    }

    [Test]
    public void HammingDistance_LowNibbleDifference_Returns4()
    {
        // 0x0F = 00001111
        // 0x00 = 00000000 (lower 4 bits different)
        var hash1 = new byte[] { 0x0F };
        var hash2 = new byte[] { 0x00 };

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        Assert.That(result, Is.EqualTo(4));
    }

    [Test]
    public void HammingDistance_DifferentLengths_ThrowsArgumentException()
    {
        var hash1 = new byte[] { 0x00 };
        var hash2 = new byte[] { 0x00, 0x00 };

        Assert.Throws<ArgumentException>(() =>
            BitwiseUtilities.HammingDistance(hash1, hash2));
    }

    [Test]
    public void HammingDistance_DifferentLengths_ExceptionHasCorrectParameterName()
    {
        var hash1 = new byte[] { 0x00 };
        var hash2 = new byte[] { 0x00, 0x00 };

        var exception = Assert.Throws<ArgumentException>(() =>
            BitwiseUtilities.HammingDistance(hash1, hash2));

        Assert.That(exception!.ParamName, Is.EqualTo("hash2"));
    }

    [Test]
    public void HammingDistance_FirstArrayNull_ThrowsArgumentNullException()
    {
        byte[]? hash1 = null;
        var hash2 = new byte[] { 0x00 };

        Assert.Throws<ArgumentNullException>(() =>
            BitwiseUtilities.HammingDistance(hash1!, hash2));
    }

    [Test]
    public void HammingDistance_FirstArrayNull_ExceptionHasCorrectParameterName()
    {
        byte[]? hash1 = null;
        var hash2 = new byte[] { 0x00 };

        var exception = Assert.Throws<ArgumentNullException>(() =>
            BitwiseUtilities.HammingDistance(hash1!, hash2));

        Assert.That(exception!.ParamName, Is.EqualTo("hash1"));
    }

    [Test]
    public void HammingDistance_SecondArrayNull_ThrowsArgumentNullException()
    {
        var hash1 = new byte[] { 0x00 };
        byte[]? hash2 = null;

        Assert.Throws<ArgumentNullException>(() =>
            BitwiseUtilities.HammingDistance(hash1, hash2!));
    }

    [Test]
    public void HammingDistance_SecondArrayNull_ExceptionHasCorrectParameterName()
    {
        var hash1 = new byte[] { 0x00 };
        byte[]? hash2 = null;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            BitwiseUtilities.HammingDistance(hash1, hash2!));

        Assert.That(exception!.ParamName, Is.EqualTo("hash2"));
    }

    [Test]
    public void HammingDistance_BothArraysNull_ThrowsArgumentNullException()
    {
        byte[]? hash1 = null;
        byte[]? hash2 = null;

        // Should throw for first null parameter encountered
        Assert.Throws<ArgumentNullException>(() =>
            BitwiseUtilities.HammingDistance(hash1!, hash2!));
    }

    [Test]
    public void HammingDistance_LargeArrays_CalculatesCorrectly()
    {
        // Simulate 8-byte perceptual hash (64 bits)
        var hash1 = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        var hash2 = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        Assert.That(result, Is.EqualTo(64)); // 8 bytes * 8 bits
    }

    [Test]
    public void HammingDistance_RealisticImageHash_ReturnsSimilarityDistance()
    {
        // Two similar perceptual hashes (8 bytes each, typical for pHash)
        // Only 3 bits different - these would be considered similar images
        var hash1 = new byte[] { 0xA5, 0xB6, 0xC7, 0xD8, 0xE9, 0xFA, 0x0B, 0x1C };
        var hash2 = new byte[] { 0xA5, 0xB6, 0xC7, 0xD9, 0xE9, 0xFA, 0x0B, 0x1C }; // 0xD8 vs 0xD9 = 2 bits

        var result = BitwiseUtilities.HammingDistance(hash1, hash2);

        // 0xD8 = 11011000, 0xD9 = 11011001 - only 1 bit different
        Assert.That(result, Is.EqualTo(1));
    }

    #endregion

    #region PopCount Tests

    [Test]
    public void PopCount_Zero_ReturnsZero()
    {
        var result = BitwiseUtilities.PopCount(0);

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void PopCount_AllOnes_Byte_Returns8()
    {
        // 0xFF = 11111111 (8 bits set)
        var result = BitwiseUtilities.PopCount(0xFF);

        Assert.That(result, Is.EqualTo(8));
    }

    [Test]
    public void PopCount_SingleBitSet_ReturnsOne()
    {
        var result = BitwiseUtilities.PopCount(1);

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void PopCount_PowerOfTwo_ReturnsOne()
    {
        using (Assert.EnterMultipleScope())
        {
            // Powers of 2 have exactly one bit set
            Assert.That(BitwiseUtilities.PopCount(2), Is.EqualTo(1));
            Assert.That(BitwiseUtilities.PopCount(4), Is.EqualTo(1));
            Assert.That(BitwiseUtilities.PopCount(8), Is.EqualTo(1));
            Assert.That(BitwiseUtilities.PopCount(16), Is.EqualTo(1));
            Assert.That(BitwiseUtilities.PopCount(32), Is.EqualTo(1));
            Assert.That(BitwiseUtilities.PopCount(64), Is.EqualTo(1));
            Assert.That(BitwiseUtilities.PopCount(128), Is.EqualTo(1));
        }
    }

    [TestCase(0x00, ExpectedResult = 0)]
    [TestCase(0x01, ExpectedResult = 1)]
    [TestCase(0x03, ExpectedResult = 2)]
    [TestCase(0x07, ExpectedResult = 3)]
    [TestCase(0x0F, ExpectedResult = 4)]
    [TestCase(0x1F, ExpectedResult = 5)]
    [TestCase(0x3F, ExpectedResult = 6)]
    [TestCase(0x7F, ExpectedResult = 7)]
    [TestCase(0xFF, ExpectedResult = 8)]
    public int PopCount_ConsecutiveBits_ReturnsExpected(int value)
    {
        return BitwiseUtilities.PopCount(value);
    }

    [TestCase(0xF0, ExpectedResult = 4)] // 11110000
    [TestCase(0x0F, ExpectedResult = 4)] // 00001111
    [TestCase(0x55, ExpectedResult = 4)] // 01010101
    [TestCase(0xAA, ExpectedResult = 4)] // 10101010
    [TestCase(0xCC, ExpectedResult = 4)] // 11001100
    [TestCase(0x33, ExpectedResult = 4)] // 00110011
    public int PopCount_KnownPatterns_ReturnsFour(int value)
    {
        return BitwiseUtilities.PopCount(value);
    }

    [Test]
    public void PopCount_MaxInt_Returns31()
    {
        // int.MaxValue = 0x7FFFFFFF = 31 bits set (sign bit is 0)
        var result = BitwiseUtilities.PopCount(int.MaxValue);

        Assert.That(result, Is.EqualTo(31));
    }

    [Test]
    public void PopCount_NegativeOne_Returns32()
    {
        // -1 in two's complement = all bits set = 32 bits
        var result = BitwiseUtilities.PopCount(-1);

        Assert.That(result, Is.EqualTo(32));
    }

    [Test]
    public void PopCount_MinInt_ReturnsOne()
    {
        // int.MinValue = 0x80000000 = only sign bit set
        var result = BitwiseUtilities.PopCount(int.MinValue);

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void PopCount_AlternatingBitsPattern_Returns16()
    {
        // 0x55555555 = 01010101... (16 bits set out of 32)
        var result = BitwiseUtilities.PopCount(0x55555555);

        Assert.That(result, Is.EqualTo(16));
    }

    #endregion

    #region Integration Tests - Hamming with PopCount

    [Test]
    public void HammingDistance_UsesPopCountInternally_ProducesCorrectResult()
    {
        // Verify the integration between HammingDistance and PopCount
        // XOR of 0xFF and 0x00 = 0xFF, which has 8 bits set
        var hash1 = new byte[] { 0xFF };
        var hash2 = new byte[] { 0x00 };

        var hammingResult = BitwiseUtilities.HammingDistance(hash1, hash2);
        var expectedPopCount = BitwiseUtilities.PopCount(0xFF ^ 0x00);

        Assert.That(hammingResult, Is.EqualTo(expectedPopCount));
    }

    [Test]
    public void HammingDistance_IsSymmetric()
    {
        var hash1 = new byte[] { 0xAB, 0xCD };
        var hash2 = new byte[] { 0x12, 0x34 };

        var result1 = BitwiseUtilities.HammingDistance(hash1, hash2);
        var result2 = BitwiseUtilities.HammingDistance(hash2, hash1);

        Assert.That(result1, Is.EqualTo(result2));
    }

    #endregion
}
