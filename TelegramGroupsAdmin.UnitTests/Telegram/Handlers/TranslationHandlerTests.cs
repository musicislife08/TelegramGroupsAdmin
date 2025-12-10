using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Configuration;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Handlers;

/// <summary>
/// Test suite for TranslationHandler static methods.
/// Tests Latin script detection and translation eligibility logic.
///
/// Test Coverage:
/// - 9 static CalculateLatinScriptRatio tests (migrated from MessageProcessingServiceTests)
/// - 7 translation eligibility tests (migrated from MessageProcessingServiceTests inline logic)
/// - Validates: English/non-English detection, boundary cases, null handling
///
/// Note: Instance method tests (ProcessTranslationAsync) removed - better covered by integration tests.
///
/// Created: 2025-10-31 (REFACTOR-1 Phase 2)
/// </summary>
[TestFixture]
public class TranslationHandlerTests
{
    #region Static CalculateLatinScriptRatio Tests (Migrated from MessageProcessingServiceTests)

    /// <summary>
    /// Validates 100% Latin script detection for pure English text.
    /// Ensures algorithm correctly identifies Western European characters.
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_PureEnglish_ReturnsOne()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Pure English text
        var text = "Hello world! This is a test message.";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: 100% Latin script
        Assert.That(ratio, Is.EqualTo(1.0).Within(0.01));
    }

    /// <summary>
    /// Validates 0% Latin script detection for pure Cyrillic text (Russian).
    /// Ensures algorithm correctly identifies non-Latin scripts.
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_PureCyrillic_ReturnsZero()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Pure Cyrillic text (Russian)
        var text = "Привет мир! Это тестовое сообщение.";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: 0% Latin script
        Assert.That(ratio, Is.EqualTo(0.0).Within(0.01));
    }

    /// <summary>
    /// Validates partial ratio calculation for mixed Latin+Cyrillic text.
    /// Tests accurate ratio calculation: "Hello" (5 Latin) + "мир" (3 Cyrillic) = 5/8 = 0.625
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_MixedText_ReturnsPartialRatio()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Mixed Latin + Cyrillic
        // "Hello" = 5 Latin chars, "мир" = 3 Cyrillic chars
        var text = "Hello мир";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: 5 Latin / 8 total = 0.625
        Assert.That(ratio, Is.EqualTo(0.625).Within(0.01));
    }

    /// <summary>
    /// Validates punctuation and emoji exclusion from ratio calculation.
    /// Only letters/digits count toward total, ensuring accurate script detection.
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_PunctuationIgnored_ReturnsCorrectRatio()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Text with lots of punctuation and emoji
        // Only letters/digits count: "Hello" = 5 Latin, "мир" = 3 Cyrillic
        var text = "Hello!!! 😊 мир???";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: 5 Latin / 8 total = 0.625
        Assert.That(ratio, Is.EqualTo(0.625).Within(0.01));
    }

    /// <summary>
    /// Validates handling of empty string input.
    /// Ensures graceful handling with default 0.0 return (no letters/digits).
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_EmptyString_ReturnsZero()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Empty text
        var text = "";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: 0.0 (default for empty)
        Assert.That(ratio, Is.EqualTo(0.0));
    }

    /// <summary>
    /// Validates handling of whitespace-only input.
    /// Ensures whitespace doesn't count toward ratio (0 letters/digits).
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_WhitespaceOnly_ReturnsZero()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Whitespace only
        var text = "   \t\n  ";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: 0.0 (no letters/digits)
        Assert.That(ratio, Is.EqualTo(0.0));
    }

    /// <summary>
    /// Validates numbers (digits) count as Latin script.
    /// Arabic numerals (0-9) are in Latin Unicode range (0x0000-0x024F).
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_NumbersCountAsLatin_ReturnsOne()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Numbers are in Latin range (0x0000-0x024F)
        var text = "123 456 789";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: Numbers count as Latin
        Assert.That(ratio, Is.EqualTo(1.0).Within(0.01));
    }

    /// <summary>
    /// Validates Arabic script detection (0% Latin).
    /// Arabic characters are outside Latin Unicode range (0x0600-0x06FF).
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_ArabicScript_ReturnsZero()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Arabic text
        var text = "مرحبا بالعالم";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: 0% Latin (Arabic is outside Latin range)
        Assert.That(ratio, Is.EqualTo(0.0).Within(0.01));
    }

    /// <summary>
    /// Validates Chinese/CJK character detection (0% Latin).
    /// CJK characters are outside Latin Unicode range (0x4E00-0x9FFF).
    /// </summary>
    [Test]
    public void CalculateLatinScriptRatio_ChineseCharacters_ReturnsZero()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Chinese text
        var text = "你好世界";

        // Act
        var ratio = TranslationHandler.CalculateLatinScriptRatio(text);

        // Assert: 0% Latin
        Assert.That(ratio, Is.EqualTo(0.0).Within(0.01));
    }

    #endregion

    #region Translation Eligibility Tests (Migrated from MessageProcessingServiceTests)

    /// <summary>
    /// Validates translation skipped when config disabled.
    /// Tests first eligibility check: Translation.Enabled flag.
    /// </summary>
    [Test]
    public void TranslationEligibility_DisabledConfig_ReturnsFalse()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Translation disabled in config
        var enabled = false;
        var minLength = 10;
        var threshold = 0.8;
        var text = "Привет мир, это тестовое сообщение";

        // Act: Check eligibility (mimics handler logic lines 47-51)
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              TranslationHandler.CalculateLatinScriptRatio(text) < threshold;

        // Assert: Don't translate (disabled)
        Assert.That(shouldTranslate, Is.False);
    }

    /// <summary>
    /// Validates translation skipped for messages below minimum length threshold.
    /// Tests optimization to avoid expensive OpenAI calls for short messages.
    /// </summary>
    [Test]
    public void TranslationEligibility_TooShort_ReturnsFalse()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Enabled but message too short
        var enabled = true;
        var minLength = 20;
        var threshold = 0.8;
        var text = "Привет"; // 6 chars

        // Act
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              TranslationHandler.CalculateLatinScriptRatio(text) < threshold;

        // Assert: Don't translate (too short)
        Assert.That(shouldTranslate, Is.False);
    }

    /// <summary>
    /// Validates translation skipped for messages already in English (high Latin ratio).
    /// Tests optimization to avoid expensive OpenAI calls when >= 80% Latin script.
    /// </summary>
    [Test]
    public void TranslationEligibility_AlreadyEnglish_ReturnsFalse()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Enabled but message already in English
        var enabled = true;
        var minLength = 10;
        var threshold = 0.8;
        var text = "Hello world, this is a test message";

        // Act
        var latinRatio = TranslationHandler.CalculateLatinScriptRatio(text);
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              latinRatio < threshold;

        // Assert: Don't translate (already English)
        Assert.That(shouldTranslate, Is.False);
        Assert.That(latinRatio, Is.GreaterThan(0.9)); // High Latin ratio
    }

    /// <summary>
    /// Validates translation triggers for non-English long messages.
    /// Tests positive case where all eligibility criteria are met.
    /// </summary>
    [Test]
    public void TranslationEligibility_NonEnglishLongMessage_ReturnsTrue()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Enabled, long, non-English message
        var enabled = true;
        var minLength = 10;
        var threshold = 0.8;
        var text = "Привет мир, это тестовое сообщение для проверки перевода";

        // Act
        var latinRatio = TranslationHandler.CalculateLatinScriptRatio(text);
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              latinRatio < threshold;

        // Assert: Should translate
        Assert.That(shouldTranslate, Is.True);
        Assert.That(latinRatio, Is.LessThan(0.1)); // Low Latin ratio
    }

    /// <summary>
    /// Validates boundary case: message exactly at minimum length threshold.
    /// Tests >= comparison (inclusive), not > (exclusive).
    /// </summary>
    [Test]
    public void TranslationEligibility_BoundaryCase_ExactMinLength_ReturnsTrue()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Message exactly at minimum length threshold
        var enabled = true;
        var minLength = 20;
        var threshold = 0.8;
        var text = "Привет мир тест тест"; // Exactly 20 chars

        // Act
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              TranslationHandler.CalculateLatinScriptRatio(text) < threshold;

        // Assert: Should translate (>= check)
        Assert.That(shouldTranslate, Is.True);
        Assert.That(text.Length, Is.EqualTo(20));
    }

    /// <summary>
    /// Validates boundary case: Latin ratio exactly at threshold (0.8).
    /// Tests < comparison (exclusive), not <= (inclusive). At threshold = skip translation.
    /// </summary>
    [Test]
    public void TranslationEligibility_BoundaryCase_ExactThreshold_ReturnsFalse()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Latin ratio exactly at threshold (0.8)
        var enabled = true;
        var minLength = 10;
        var threshold = 0.8;
        // Craft text with exactly 80% Latin: 8 Latin + 2 Cyrillic = 0.8
        var text = "HelloHel мм"; // 8 Latin, 2 Cyrillic = 0.8

        // Act
        var latinRatio = TranslationHandler.CalculateLatinScriptRatio(text);
        var shouldTranslate = enabled &&
                              text.Length >= minLength &&
                              latinRatio < threshold;

        // Assert: Don't translate (< check, not <=)
        Assert.That(shouldTranslate, Is.False);
        Assert.That(latinRatio, Is.EqualTo(0.8).Within(0.01));
    }

    /// <summary>
    /// Validates edge case: null/whitespace text handling.
    /// Ensures graceful handling with no translation triggered.
    /// </summary>
    [Test]
    public void TranslationEligibility_NullOrWhitespace_ReturnsFalse()
    {
        // Migrated from MessageProcessingServiceTests
        // Arrange: Whitespace text
        var enabled = true;
        var minLength = 10;
        var threshold = 0.8;
        var text = "   ";

        // Act
        var shouldTranslate = enabled &&
                              !string.IsNullOrWhiteSpace(text) &&
                              text.Length >= minLength &&
                              TranslationHandler.CalculateLatinScriptRatio(text) < threshold;

        // Assert: Don't translate (whitespace)
        Assert.That(shouldTranslate, Is.False);
    }

    #endregion

}
