using System.Text.Json;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Utilities;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

/// <summary>
/// Tests for CheckResultsSerializer — the static JSON roundtrip utility that persists
/// V2 check results to the detection_results.check_results_json JSONB column.
/// </summary>
[TestFixture]
public class CheckResultsSerializerTests
{
    #region Deserialize — Null / Empty / Whitespace Guard

    [Test]
    public void Deserialize_NullInput_ReturnsEmptyList()
    {
        // Arrange / Act
        var result = CheckResultsSerializer.Deserialize(null!);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Deserialize_EmptyString_ReturnsEmptyList()
    {
        // Arrange / Act
        var result = CheckResultsSerializer.Deserialize(string.Empty);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Deserialize_WhitespaceString_ReturnsEmptyList()
    {
        // Arrange / Act
        var result = CheckResultsSerializer.Deserialize("   \t\n  ");

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region Deserialize — Malformed JSON

    [Test]
    public void Deserialize_MalformedJson_ReturnsEmptyList()
    {
        // Arrange / Act
        var result = CheckResultsSerializer.Deserialize("{ this is not valid json !!!");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Deserialize_ValidJsonButWrongShape_ReturnsEmptyList()
    {
        // Arrange — valid JSON but not the expected CheckResults wrapper shape
        var result = CheckResultsSerializer.Deserialize("[1, 2, 3]");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Deserialize_EmptyChecksArray_ReturnsEmptyList()
    {
        // Arrange — valid wrapper shape but no entries
        const string json = """{"Checks":[]}""";

        // Act
        var result = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region Roundtrip — Core Field Preservation

    [Test]
    public void Roundtrip_SingleResult_PreservesAllFields()
    {
        // Arrange
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.Bayes,
                Score = 3.5,
                Abstained = false,
                Details = "Bayes probability 70%",
                ProcessingTimeMs = 42.3
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));
        var result = results[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CheckName, Is.EqualTo(CheckName.Bayes));
            Assert.That(result.Score, Is.EqualTo(3.5).Within(0.0001));
            Assert.That(result.Abstained, Is.False);
            Assert.That(result.Details, Is.EqualTo("Bayes probability 70%"));
            Assert.That(result.ProcessingTimeMs, Is.EqualTo(42.3).Within(0.0001));
        }
    }

    [Test]
    public void Roundtrip_MultipleResults_PreservesAllEntries()
    {
        // Arrange
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.StopWords,
                Score = 1.0,
                Abstained = false,
                Details = "Matched stop word 'casino'",
                ProcessingTimeMs = 5.1
            },
            new()
            {
                CheckName = CheckName.CAS,
                Score = 5.0,
                Abstained = false,
                Details = "CAS ban confirmed",
                ProcessingTimeMs = 120.7
            },
            new()
            {
                CheckName = CheckName.OpenAI,
                Score = 0.0,
                Abstained = true,
                Details = "No spam flags present",
                ProcessingTimeMs = 0.2
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results, Has.Count.EqualTo(3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].CheckName, Is.EqualTo(CheckName.StopWords));
            Assert.That(results[0].Score, Is.EqualTo(1.0).Within(0.0001));

            Assert.That(results[1].CheckName, Is.EqualTo(CheckName.CAS));
            Assert.That(results[1].Score, Is.EqualTo(5.0).Within(0.0001));

            Assert.That(results[2].CheckName, Is.EqualTo(CheckName.OpenAI));
            Assert.That(results[2].Abstained, Is.True);
        }
    }

    [Test]
    public void Roundtrip_EmptyList_SerializesAndDeserializesClean()
    {
        // Arrange
        var input = new List<ContentCheckResponseV2>();

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results, Is.Empty);
    }

    #endregion

    #region Roundtrip — Abstained Flag

    [Test]
    public void Roundtrip_AbstainedTrue_PreservesAbstainedFlag()
    {
        // Arrange
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.ThreatIntel,
                Score = 0.0,
                Abstained = true,
                Details = "No URLs in message",
                ProcessingTimeMs = 1.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results[0].Abstained, Is.True);
    }

    [Test]
    public void Roundtrip_AbstainedFalse_PreservesAbstainedFlag()
    {
        // Arrange
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.Similarity,
                Score = 2.5,
                Abstained = false,
                Details = "Similarity 50%",
                ProcessingTimeMs = 8.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results[0].Abstained, Is.False);
    }

    #endregion

    #region Roundtrip — ProcessingTimeMs

    [Test]
    public void Roundtrip_ProcessingTimeMs_IsPreserved()
    {
        // Arrange
        const double expectedMs = 237.89;
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.ImageSpam,
                Score = 4.0,
                Abstained = false,
                Details = "Vision API flagged crypto scam pattern",
                ProcessingTimeMs = expectedMs
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results[0].ProcessingTimeMs, Is.EqualTo(expectedMs).Within(0.0001));
    }

    [Test]
    public void Roundtrip_ProcessingTimeMsZero_IsPreserved()
    {
        // Arrange — zero processing time edge case
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.InvisibleChars,
                Score = 0.0,
                Abstained = true,
                Details = "No invisible characters found",
                ProcessingTimeMs = 0.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results[0].ProcessingTimeMs, Is.EqualTo(0.0).Within(0.0001));
    }

    #endregion

    #region IsSpam Computed Property — Boundary Conditions

    [Test]
    public void IsSpam_ScoreZeroAbstainedFalse_ReturnsFalse()
    {
        // Arrange — score 0 and not abstained: IsSpam should be false (score not > 0)
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.Spacing,
                Score = 0.0,
                Abstained = false,
                Details = "No suspicious spacing",
                ProcessingTimeMs = 3.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results[0].IsSpam, Is.False);
    }

    [Test]
    public void IsSpam_ScoreZeroAbstainedTrue_ReturnsFalse()
    {
        // Arrange — abstained with score 0: IsSpam should be false
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.UrlBlocklist,
                Score = 0.0,
                Abstained = true,
                Details = "No URLs to check",
                ProcessingTimeMs = 0.5
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results[0].IsSpam, Is.False);
    }

    [Test]
    public void IsSpam_PositiveScoreAbstainedFalse_ReturnsTrue()
    {
        // Arrange — smallest positive score and not abstained: IsSpam should be true
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.SeoScraping,
                Score = 0.1,
                Abstained = false,
                Details = "Faint SEO signal",
                ProcessingTimeMs = 10.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results[0].IsSpam, Is.True);
    }

    [Test]
    public void IsSpam_PositiveScoreAbstainedTrue_ReturnsFalse()
    {
        // Arrange — abstained overrides any non-zero score
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.FileScanning,
                Score = 2.0,
                Abstained = true,
                Details = "Abstained despite score (should not happen in practice)",
                ProcessingTimeMs = 15.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results[0].IsSpam, Is.False,
            "Abstained flag must take precedence over a positive score");
    }

    #endregion

    #region CheckName Enum — Integer Serialization Format

    [Test]
    public void Serialize_CheckNameBayes_SerializesAsInteger()
    {
        // Arrange — verify CheckName is stored as its numeric ordinal, not as a string.
        // Bayes is the 4th entry (0-based index 3) in the enum declaration.
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.Bayes,
                Score = 1.0,
                Abstained = false,
                Details = "Bayes check",
                ProcessingTimeMs = 5.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);

        // Assert — the raw JSON must contain the integer value, not the string name.
        // CheckName.Bayes = 3 (StopWords=0, CAS=1, Similarity=2, Bayes=3)
        var expectedBayesOrdinal = (int)CheckName.Bayes;
        Assert.That(json, Does.Contain($"\"CheckName\":{expectedBayesOrdinal}"),
            $"CheckName should serialize as integer {expectedBayesOrdinal}, not as the string \"Bayes\"");
    }

    [Test]
    public void Roundtrip_AllCheckNameValues_SurviveRoundtrip()
    {
        // Arrange — every CheckName value must roundtrip correctly.
        var allCheckNames = Enum.GetValues<CheckName>();
        var input = allCheckNames.Select(cn => new ContentCheckResponseV2
        {
            CheckName = cn,
            Score = 1.0,
            Abstained = false,
            Details = $"Test for {cn}",
            ProcessingTimeMs = 1.0
        }).ToList();

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results, Has.Count.EqualTo(allCheckNames.Length));
        for (var i = 0; i < allCheckNames.Length; i++)
        {
            Assert.That(results[i].CheckName, Is.EqualTo(allCheckNames[i]),
                $"CheckName {allCheckNames[i]} did not survive roundtrip at index {i}");
        }
    }

    #endregion

    #region Serialize — Output Format Contracts

    [Test]
    public void Serialize_ProducesCompactJson_NoIndentation()
    {
        // Arrange — serializer is configured with WriteIndented=false
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.CAS,
                Score = 5.0,
                Abstained = false,
                Details = "CAS ban",
                ProcessingTimeMs = 100.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);

        // Assert — compact JSON must not contain newlines introduced by indentation
        Assert.That(json, Does.Not.Contain('\n'),
            "Serialized JSON must be compact (WriteIndented=false) for JSONB column storage");
    }

    [Test]
    public void Serialize_ProducesChecksWrapper_MatchesDatabaseFormat()
    {
        // Arrange — database format requires top-level "Checks" array wrapper
        var input = new List<ContentCheckResponseV2>
        {
            new()
            {
                CheckName = CheckName.StopWords,
                Score = 2.0,
                Abstained = false,
                Details = "Matched word",
                ProcessingTimeMs = 2.0
            }
        };

        // Act
        var json = CheckResultsSerializer.Serialize(input);
        var doc = JsonDocument.Parse(json);

        // Assert — root must be an object with a "Checks" array property
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(doc.RootElement.TryGetProperty("Checks", out var checksElement), Is.True,
            "Root JSON object must have a 'Checks' array property");
        Assert.That(checksElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    #endregion

    #region Deserialize — Case-Insensitive Property Matching

    [Test]
    public void Deserialize_LowercasePropertyNames_MatchesCaseInsensitively()
    {
        // Arrange — DeserializeOptions uses PropertyNameCaseInsensitive=true;
        // verify that lowercase keys from external tooling still deserialize correctly.
        const string json = """{"checks":[{"checkName":3,"score":2.5,"abstained":false,"details":"bayes result","processingTimeMs":7.5}]}""";

        // Act
        var results = CheckResultsSerializer.Deserialize(json);

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].CheckName, Is.EqualTo(CheckName.Bayes));
            Assert.That(results[0].Score, Is.EqualTo(2.5).Within(0.0001));
            Assert.That(results[0].Abstained, Is.False);
            Assert.That(results[0].Details, Is.EqualTo("bayes result"));
            Assert.That(results[0].ProcessingTimeMs, Is.EqualTo(7.5).Within(0.0001));
        }
    }

    #endregion
}
