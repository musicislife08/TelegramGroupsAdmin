using System.Text.Json;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.UnitTests.Data;

/// <summary>
/// Pins the persisted media_features JSON contract. The Core model may later become a C# 15
/// union; this DTO contract must not change when it does.
/// </summary>
[TestFixture]
public class MediaFeaturesJsonTests
{
    private static readonly byte[] Hash = [1, 2, 3, 4, 5, 6, 7, 8];

    [Test]
    public void Photo_RoundTrips_WithTypeDiscriminator()
    {
        var json = JsonSerializer.Serialize<MediaFeaturesDto>(new PhotoFeaturesDto(Hash), MediaFeaturesJson.Options);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.StartWith("""{"type":"photo","""));
            Assert.That(JsonSerializer.Deserialize<MediaFeaturesDto>(json, MediaFeaturesJson.Options),
                Is.TypeOf<PhotoFeaturesDto>().With.Property(nameof(PhotoFeaturesDto.Hash)).EqualTo(Hash));
        }
    }

    [Test]
    public void Video_RoundTrips()
    {
        MediaFeaturesDto video = new VideoFeaturesDto([new KeyframeFeatureDto(0.1, Hash), new KeyframeFeatureDto(0.5, Hash)]);
        var back = JsonSerializer.Deserialize<MediaFeaturesDto>(JsonSerializer.Serialize(video, MediaFeaturesJson.Options), MediaFeaturesJson.Options);
        Assert.That(((VideoFeaturesDto)back!).Keyframes, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// PostgreSQL jsonb stores keys shortest-first then bytewise, so a stored photo reads back as
    /// {"hash":…,"type":"photo"}: the discriminator is not the first property.
    /// </summary>
    [Test]
    public void Photo_WithDiscriminatorAfterHash_Deserializes()
        => Assert.That(JsonSerializer.Deserialize<MediaFeaturesDto>("""{"hash":"AQIDBAUGBwg=","type":"photo"}""", MediaFeaturesJson.Options),
            Is.TypeOf<PhotoFeaturesDto>().With.Property(nameof(PhotoFeaturesDto.Hash)).EqualTo(Hash));

    [Test]
    public void Photo_WithoutHash_IsRejected()
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MediaFeaturesDto>("""{"type":"photo"}""", MediaFeaturesJson.Options));

    [Test]
    public void UnknownType_IsRejected()
        => Assert.Catch(() => JsonSerializer.Deserialize<MediaFeaturesDto>("""{"type":"audio","hash":"AQ=="}""", MediaFeaturesJson.Options));
}
