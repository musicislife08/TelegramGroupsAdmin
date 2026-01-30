using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

/// <summary>
/// Unit tests for ContentDetectionConfigMappings extension methods.
/// Validates TimeSpan ↔ double (seconds) conversions between Data and Business layers.
/// These conversions are critical - they fix the Npgsql interval parsing issue with EF Core ToJson().
/// </summary>
[TestFixture]
public class ContentDetectionConfigMappingsTests
{
    #region CasConfig Mappings

    [Test]
    public void CasConfigData_ToModel_ConvertsSecondsToTimeSpan()
    {
        // Arrange
        var data = new CasConfigData
        {
            UseGlobal = true,
            Enabled = true,
            ApiUrl = "https://api.cas.chat",
            TimeoutSeconds = 15.0,
            UserAgent = "TestAgent",
            AlwaysRun = true
        };

        // Act
        var model = data.ToModel();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(model.Timeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(model.UseGlobal, Is.True);
            Assert.That(model.Enabled, Is.True);
            Assert.That(model.ApiUrl, Is.EqualTo("https://api.cas.chat"));
            Assert.That(model.UserAgent, Is.EqualTo("TestAgent"));
            Assert.That(model.AlwaysRun, Is.True);
        });
    }

    [Test]
    public void CasConfig_ToData_ConvertsTimeSpanToSeconds()
    {
        // Arrange
        var model = new CasConfig
        {
            UseGlobal = false,
            Enabled = true,
            ApiUrl = "https://custom.api",
            Timeout = TimeSpan.FromSeconds(10),
            UserAgent = "CustomAgent",
            AlwaysRun = false
        };

        // Act
        var data = model.ToData();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(data.TimeoutSeconds, Is.EqualTo(10.0));
            Assert.That(data.UseGlobal, Is.False);
            Assert.That(data.Enabled, Is.True);
            Assert.That(data.ApiUrl, Is.EqualTo("https://custom.api"));
            Assert.That(data.UserAgent, Is.EqualTo("CustomAgent"));
            Assert.That(data.AlwaysRun, Is.False);
        });
    }

    [Test]
    public void CasConfig_RoundTrip_PreservesTimeSpan()
    {
        // Arrange
        var original = new CasConfig
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        // Act
        var data = original.ToData();
        var roundTrip = data.ToModel();

        // Assert
        Assert.That(roundTrip.Timeout, Is.EqualTo(original.Timeout));
    }

    #endregion

    #region UrlBlocklistConfig Mappings

    [Test]
    public void UrlBlocklistConfigData_ToModel_ConvertsSecondsToTimeSpan()
    {
        // Arrange
        var data = new UrlBlocklistConfigData
        {
            UseGlobal = true,
            Enabled = true,
            CacheDurationSeconds = 3600.0, // 1 hour
            AlwaysRun = true
        };

        // Act
        var model = data.ToModel();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(model.CacheDuration, Is.EqualTo(TimeSpan.FromSeconds(3600)));
            Assert.That(model.CacheDuration, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(model.AlwaysRun, Is.True);
        });
    }

    [Test]
    public void UrlBlocklistConfig_ToData_ConvertsTimeSpanToSeconds()
    {
        // Arrange
        var model = new UrlBlocklistConfig
        {
            CacheDuration = TimeSpan.FromHours(24)
        };

        // Act
        var data = model.ToData();

        // Assert
        Assert.That(data.CacheDurationSeconds, Is.EqualTo(86400.0));
    }

    [Test]
    public void UrlBlocklistConfig_RoundTrip_PreservesCacheDuration()
    {
        // Arrange
        var original = new UrlBlocklistConfig
        {
            CacheDuration = TimeSpan.FromHours(12)
        };

        // Act
        var data = original.ToData();
        var roundTrip = data.ToModel();

        // Assert
        Assert.That(roundTrip.CacheDuration, Is.EqualTo(original.CacheDuration));
    }

    #endregion

    #region ThreatIntelConfig Mappings

    [Test]
    public void ThreatIntelConfigData_ToModel_ConvertsSecondsToTimeSpan()
    {
        // Arrange
        var data = new ThreatIntelConfigData
        {
            UseGlobal = true,
            Enabled = true,
            UseVirusTotal = true,
            TimeoutSeconds = 30.0,
            AlwaysRun = false
        };

        // Act
        var model = data.ToModel();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(model.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(model.UseVirusTotal, Is.True);
        });
    }

    [Test]
    public void ThreatIntelConfig_ToData_ConvertsTimeSpanToSeconds()
    {
        // Arrange
        var model = new ThreatIntelConfig
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        // Act
        var data = model.ToData();

        // Assert
        Assert.That(data.TimeoutSeconds, Is.EqualTo(45.0));
    }

    #endregion

    #region SeoScrapingConfig Mappings

    [Test]
    public void SeoScrapingConfigData_ToModel_ConvertsSecondsToTimeSpan()
    {
        // Arrange
        var data = new SeoScrapingConfigData
        {
            TimeoutSeconds = 10.0
        };

        // Act
        var model = data.ToModel();

        // Assert
        Assert.That(model.Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void SeoScrapingConfig_ToData_ConvertsTimeSpanToSeconds()
    {
        // Arrange
        var model = new SeoScrapingConfig
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // Act
        var data = model.ToData();

        // Assert
        Assert.That(data.TimeoutSeconds, Is.EqualTo(15.0));
    }

    #endregion

    #region ImageContentConfig Mappings

    [Test]
    public void ImageContentConfigData_ToModel_ConvertsSecondsToTimeSpan()
    {
        // Arrange
        var data = new ImageContentConfigData
        {
            TimeoutSeconds = 30.0,
            UseOpenAIVision = true,
            UseOCR = true
        };

        // Act
        var model = data.ToModel();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(model.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(model.UseOpenAIVision, Is.True);
            Assert.That(model.UseOCR, Is.True);
        });
    }

    [Test]
    public void ImageContentConfig_ToData_ConvertsTimeSpanToSeconds()
    {
        // Arrange
        var model = new ImageContentConfig
        {
            Timeout = TimeSpan.FromMinutes(1)
        };

        // Act
        var data = model.ToData();

        // Assert
        Assert.That(data.TimeoutSeconds, Is.EqualTo(60.0));
    }

    #endregion

    #region VideoContentConfig Mappings

    [Test]
    public void VideoContentConfigData_ToModel_ConvertsSecondsToTimeSpan()
    {
        // Arrange
        var data = new VideoContentConfigData
        {
            TimeoutSeconds = 60.0,
            UseOpenAIVision = true
        };

        // Act
        var model = data.ToModel();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(model.Timeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(model.Timeout, Is.EqualTo(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public void VideoContentConfig_ToData_ConvertsTimeSpanToSeconds()
    {
        // Arrange
        var model = new VideoContentConfig
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        // Act
        var data = model.ToData();

        // Assert
        Assert.That(data.TimeoutSeconds, Is.EqualTo(120.0));
    }

    #endregion

    #region Edge Cases - Fractional Seconds

    [TestCase(0.5)]
    [TestCase(1.5)]
    [TestCase(2.75)]
    [TestCase(0.001)] // 1 millisecond
    public void TimeoutConversion_FractionalSeconds_PreservesPrecision(double seconds)
    {
        // Arrange
        var model = new CasConfig { Timeout = TimeSpan.FromSeconds(seconds) };

        // Act
        var data = model.ToData();
        var roundTrip = data.ToModel();

        // Assert
        Assert.That(roundTrip.Timeout.TotalSeconds, Is.EqualTo(seconds).Within(0.000001));
    }

    [Test]
    public void TimeoutConversion_ZeroTimeout_PreservesZero()
    {
        // Arrange
        var model = new CasConfig { Timeout = TimeSpan.Zero };

        // Act
        var data = model.ToData();
        var roundTrip = data.ToModel();

        // Assert
        Assert.That(roundTrip.Timeout, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TimeoutConversion_LargeValue_PreservesValue()
    {
        // Arrange - 24 hours in seconds
        var model = new UrlBlocklistConfig { CacheDuration = TimeSpan.FromDays(1) };

        // Act
        var data = model.ToData();
        var roundTrip = data.ToModel();

        // Assert
        Assert.That(roundTrip.CacheDuration, Is.EqualTo(TimeSpan.FromDays(1)));
        Assert.That(data.CacheDurationSeconds, Is.EqualTo(86400.0));
    }

    #endregion

    #region Full ContentDetectionConfig Round-Trip

    [Test]
    public void ContentDetectionConfig_RoundTrip_PreservesAllTimeSpanValues()
    {
        // Arrange
        var originalData = new ContentDetectionConfigData
        {
            ThreatIntel = new ThreatIntelConfigData { TimeoutSeconds = 30.0 },
            UrlBlocklist = new UrlBlocklistConfigData { CacheDurationSeconds = 3600.0 },
            SeoScraping = new SeoScrapingConfigData { TimeoutSeconds = 10.0 },
            ImageSpam = new ImageContentConfigData { TimeoutSeconds = 30.0 },
            VideoSpam = new VideoContentConfigData { TimeoutSeconds = 60.0 }
        };

        // Act
        var model = originalData.ToModel();
        var roundTripData = model.ToData();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(roundTripData.ThreatIntel.TimeoutSeconds, Is.EqualTo(30.0));
            Assert.That(roundTripData.UrlBlocklist.CacheDurationSeconds, Is.EqualTo(3600.0));
            Assert.That(roundTripData.SeoScraping.TimeoutSeconds, Is.EqualTo(10.0));
            Assert.That(roundTripData.ImageSpam.TimeoutSeconds, Is.EqualTo(30.0));
            Assert.That(roundTripData.VideoSpam.TimeoutSeconds, Is.EqualTo(60.0));
        });
    }

    #endregion
}
