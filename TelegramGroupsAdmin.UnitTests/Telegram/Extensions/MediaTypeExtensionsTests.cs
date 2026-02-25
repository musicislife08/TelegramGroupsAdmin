using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Extensions;

/// <summary>
/// Unit tests for MediaTypeExtensions.ToDisplayName() extension method.
/// Validates display name generation for all MediaType enum values.
/// </summary>
[TestFixture]
public class MediaTypeExtensionsTests
{
    #region Explicit Cases

    [TestCase(MediaType.Animation, "GIF")]
    [TestCase(MediaType.Video, "Video")]
    [TestCase(MediaType.Audio, "Audio")]
    [TestCase(MediaType.Voice, "Voice message")]
    [TestCase(MediaType.Sticker, "Sticker")]
    [TestCase(MediaType.VideoNote, "Video message")]
    [TestCase(MediaType.Document, "Document")]
    public void ToDisplayName_WithExplicitMediaType_ReturnsCorrectDisplayName(MediaType mediaType, string expectedName)
    {
        // Act
        var result = mediaType.ToDisplayName();

        // Assert
        Assert.That(result, Is.EqualTo(expectedName));
    }

    #endregion

    #region Default Cases

    [TestCase(MediaType.None)]
    public void ToDisplayName_WithUnhandledMediaType_ReturnsDefaultMediaName(MediaType mediaType)
    {
        // Act
        var result = mediaType.ToDisplayName();

        // Assert
        Assert.That(result, Is.EqualTo("Media"));
    }

    #endregion

    #region All Enum Values Coverage

    [Test]
    public void ToDisplayName_AllEnumValues_ReturnNonEmptyString()
    {
        // Arrange
        var allMediaTypes = Enum.GetValues(typeof(MediaType)).Cast<MediaType>();

        // Act & Assert
        foreach (var mediaType in allMediaTypes)
        {
            var result = mediaType.ToDisplayName();
            Assert.That(result, Is.Not.Empty, $"ToDisplayName returned empty string for {mediaType}");
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    public void ToDisplayName_None_ReturnsMediaFallback()
    {
        // Act
        var result = MediaType.None.ToDisplayName();

        // Assert
        Assert.That(result, Is.EqualTo("Media"));
    }

    [Test]
    public void ToDisplayName_VoiceAndVideoNoteAreDistinct()
    {
        // Act
        var voiceResult = MediaType.Voice.ToDisplayName();
        var videoNoteResult = MediaType.VideoNote.ToDisplayName();

        // Assert
        Assert.That(voiceResult, Is.EqualTo("Voice message"));
        Assert.That(videoNoteResult, Is.EqualTo("Video message"));
        Assert.That(voiceResult, Is.Not.EqualTo(videoNoteResult));
    }

    [Test]
    public void ToDisplayName_AnimationAndVideoAreDistinct()
    {
        // Act
        var animationResult = MediaType.Animation.ToDisplayName();
        var videoResult = MediaType.Video.ToDisplayName();

        // Assert
        Assert.That(animationResult, Is.EqualTo("GIF"));
        Assert.That(videoResult, Is.EqualTo("Video"));
        Assert.That(animationResult, Is.Not.EqualTo(videoResult));
    }

    #endregion

    #region Return Type Validation

    [Test]
    public void ToDisplayName_AlwaysReturnsString()
    {
        // Arrange
        var testValue = MediaType.Animation;

        // Act
        var result = testValue.ToDisplayName();

        // Assert
        Assert.That(result, Is.TypeOf<string>());
    }

    [Test]
    public void ToDisplayName_NeverReturnsNull()
    {
        // Arrange
        var allMediaTypes = Enum.GetValues(typeof(MediaType)).Cast<MediaType>();

        // Act & Assert
        foreach (var mediaType in allMediaTypes)
        {
            var result = mediaType.ToDisplayName();
            Assert.That(result, Is.Not.Null, $"ToDisplayName returned null for {mediaType}");
        }
    }

    #endregion
}
