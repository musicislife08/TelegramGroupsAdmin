using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Checks;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

/// <summary>
/// Unit tests for BayesContentCheckV2 with V2 scoring model (0.0-5.0 points).
///
/// Test Strategy:
/// - All dependencies are mocked with NSubstitute (no real DB, no real classifier)
/// - ITokenizerService.RemoveEmojis is configured to return the message unchanged by default
/// - IBayesClassifierService.Classify return value drives the main branching logic
///
/// Test Coverage:
/// - ShouldExecute: empty/whitespace, trusted/admin user, valid untrusted user
/// - CheckAsync: message too short, classifier not trained (null), low probability (&lt;40%),
///   uncertain (40-60%), high probability thresholds (61-70, 71-80, 81-95, 96-99, 99+),
///   exception handling
/// </summary>
[TestFixture]
public class BayesContentCheckV2Tests
{
    private ILogger<BayesContentCheckV2> _mockLogger = null!;
    private IBayesClassifierService _mockBayesClassifier = null!;
    private ITokenizerService _mockTokenizerService = null!;
    private BayesContentCheckV2 _check = null!;

    private const long TestUserId = 12345;
    private const long TestChatId = 67890;
    private const int DefaultMinMessageLength = 10;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = Substitute.For<ILogger<BayesContentCheckV2>>();
        _mockBayesClassifier = Substitute.For<IBayesClassifierService>();
        _mockTokenizerService = Substitute.For<ITokenizerService>();

        // Default: RemoveEmojis returns the message unchanged
        _mockTokenizerService.RemoveEmojis(Arg.Any<string>()).Returns(info => info.Arg<string>());

        _check = new BayesContentCheckV2(
            _mockLogger,
            _mockBayesClassifier,
            _mockTokenizerService);
    }

    #region ShouldExecute Tests

    [Test]
    public void ShouldExecute_EmptyMessage_ReturnsFalse()
    {
        // Arrange
        var request = CreateShouldExecuteRequest(message: "", isUserTrusted: false, isUserAdmin: false);

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_WhitespaceMessage_ReturnsFalse()
    {
        // Arrange
        var request = CreateShouldExecuteRequest(message: "   \n\t  ", isUserTrusted: false, isUserAdmin: false);

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_TrustedUser_ReturnsFalse()
    {
        // Arrange
        var request = CreateShouldExecuteRequest(message: "Test message", isUserTrusted: true, isUserAdmin: false);

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_AdminUser_ReturnsFalse()
    {
        // Arrange
        var request = CreateShouldExecuteRequest(message: "Test message", isUserTrusted: false, isUserAdmin: true);

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_ValidMessageUntrustedUser_ReturnsTrue()
    {
        // Arrange
        var request = CreateShouldExecuteRequest(message: "Test message", isUserTrusted: false, isUserAdmin: false);

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region CheckAsync — Short Message Tests

    [Test]
    public async Task CheckAsync_MessageTooShort_Abstains()
    {
        // Arrange
        var request = CreateCheckRequest(message: "Hi", minMessageLength: 10);

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.True);
            Assert.That(response.Score, Is.EqualTo(0.0));
            Assert.That(response.Details, Does.Contain("too short"));
            Assert.That(response.CheckName, Is.EqualTo(CheckName.Bayes));
        }

        // Verify classifier was never consulted for short messages
        _mockBayesClassifier.DidNotReceive().Classify(Arg.Any<string>());
    }

    [Test]
    public async Task CheckAsync_MessageExactlyAtMinLength_DoesNotAbstainForLength()
    {
        // Arrange — Message length exactly equals MinMessageLength
        const string message = "1234567890"; // exactly 10 chars
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.20, "Ham probability: 0.800", 0.60));

        var request = CreateCheckRequest(message: message, minMessageLength: 10);

        // Act
        var response = await _check.CheckAsync(request);

        // Assert — Abstention should NOT be for length reasons
        Assert.That(response.Details, Does.Not.Contain("too short"));
    }

    #endregion

    #region CheckAsync — Classifier Not Trained Tests

    [Test]
    public async Task CheckAsync_ClassifierNotTrained_AbstainsWithCorrectDetails()
    {
        // Arrange — Classify returns null (classifier not trained)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns((BayesClassificationResult?)null);

        var request = CreateCheckRequest(message: "This is a valid length message to check");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.True);
            Assert.That(response.Score, Is.EqualTo(0.0));
            Assert.That(response.Details, Does.Contain("Classifier not trained"));
            Assert.That(response.CheckName, Is.EqualTo(CheckName.Bayes));
        }
    }

    #endregion

    #region CheckAsync — Probability Threshold Tests

    [Test]
    public async Task CheckAsync_LowProbability_BelowUncertaintyLowerBound_AbstainsAsLikelyHam()
    {
        // Arrange — 39% spam probability is below UncertaintyLowerBound (40%)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.39, "Ham probability: 0.610", 0.22));

        var request = CreateCheckRequest(message: "Normal conversational message about everyday topics");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.True);
            Assert.That(response.Score, Is.EqualTo(0.0));
            Assert.That(response.Details, Does.Contain("ham").Or.Contain("Ham"));
        }
    }

    [Test]
    public async Task CheckAsync_UncertainProbability_AtLowerBound_Abstains()
    {
        // Arrange — 40% is the lower edge of the uncertain zone (40-60%)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.40, "Spam probability: 0.400", 0.20));

        var request = CreateCheckRequest(message: "A message that could go either way for classification");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.True);
            Assert.That(response.Score, Is.EqualTo(0.0));
            Assert.That(response.Details, Does.Contain("Uncertain").Or.Contain("ham").Or.Contain("Ham"));
        }
    }

    [Test]
    public async Task CheckAsync_UncertainProbability_AtUpperBound_Abstains()
    {
        // Arrange — 60% is the upper edge of the uncertain zone (40-60%)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.60, "Spam probability: 0.600", 0.20));

        var request = CreateCheckRequest(message: "A borderline message that could be spam or legitimate");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.True);
            Assert.That(response.Score, Is.EqualTo(0.0));
            Assert.That(response.Details, Does.Contain("Uncertain").Or.Contain("ham").Or.Contain("Ham"));
        }
    }

    [Test]
    public async Task CheckAsync_HighProbability_AboveUncertainZone_61Percent_ReturnsWeakSignalScore()
    {
        // Arrange — 61% probability is just above the uncertain zone; falls into weak signal (60-70 range)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.61, "Spam probability: 0.610 (key words: buy)", 0.22));

        var request = CreateCheckRequest(message: "Buy cheap products with guaranteed results for everyone");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert — Should score 0.5 (weak signal for 60-70% range)
            Assert.That(response.Abstained, Is.False);
            Assert.That(response.Score, Is.EqualTo(0.5));
            Assert.That(response.CheckName, Is.EqualTo(CheckName.Bayes));
        }
    }

    [Test]
    public async Task CheckAsync_HighProbability_70Percent_ReturnsBayes70Score()
    {
        // Arrange — 70% probability maps to ScoreBayes70 (1.0)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.70, "Spam probability: 0.700 (key words: click, buy)", 0.40));

        var request = CreateCheckRequest(message: "Click here to buy cheap products with guaranteed results now");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.False);
            Assert.That(response.Score, Is.EqualTo(ScoringConstants.ScoreBayes70));
            Assert.That(response.Details, Does.Contain("certainty:"));
        }
    }

    [Test]
    public async Task CheckAsync_HighProbability_80Percent_ReturnsBayes80Score()
    {
        // Arrange — 80% probability maps to ScoreBayes80 (2.0)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.80, "Spam probability: 0.800 (key words: buy, click, now)", 0.60));

        var request = CreateCheckRequest(message: "Buy crypto now and get guaranteed returns click here");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.False);
            Assert.That(response.Score, Is.EqualTo(ScoringConstants.ScoreBayes80));
        }
    }

    [Test]
    public async Task CheckAsync_HighProbability_95Percent_ReturnsBayes95Score()
    {
        // Arrange — 95% probability maps to ScoreBayes95 (3.5)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.95, "Spam probability: 0.950 (key words: buy, click, crypto)", 0.90));

        var request = CreateCheckRequest(message: "Buy crypto now click here guaranteed profit join channel");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.False);
            Assert.That(response.Score, Is.EqualTo(ScoringConstants.ScoreBayes95));
        }
    }

    [Test]
    public async Task CheckAsync_HighProbability_99Percent_ReturnsBayes99Score()
    {
        // Arrange — 99% probability maps to ScoreBayes99 (5.0)
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.99, "Spam probability: 0.990 (key words: buy, click, crypto, guaranteed)", 0.98));

        var request = CreateCheckRequest(message: "Buy crypto now guaranteed profit click join channel spam");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.False);
            Assert.That(response.Score, Is.EqualTo(ScoringConstants.ScoreBayes99));
        }
    }

    [TestCase(0.61, 0.5, TestName = "61% → weak signal (0.5)")]
    [TestCase(0.65, 0.5, TestName = "65% → weak signal (0.5)")]
    [TestCase(0.70, ScoringConstants.ScoreBayes70, TestName = "70% → ScoreBayes70 (1.0)")]
    [TestCase(0.75, ScoringConstants.ScoreBayes70, TestName = "75% → ScoreBayes70 (1.0)")]
    [TestCase(0.80, ScoringConstants.ScoreBayes80, TestName = "80% → ScoreBayes80 (2.0)")]
    [TestCase(0.90, ScoringConstants.ScoreBayes80, TestName = "90% → ScoreBayes80 (2.0)")]
    [TestCase(0.95, ScoringConstants.ScoreBayes95, TestName = "95% → ScoreBayes95 (3.5)")]
    [TestCase(0.98, ScoringConstants.ScoreBayes95, TestName = "98% → ScoreBayes95 (3.5)")]
    [TestCase(0.99, ScoringConstants.ScoreBayes99, TestName = "99% → ScoreBayes99 (5.0)")]
    [TestCase(1.00, ScoringConstants.ScoreBayes99, TestName = "100% → ScoreBayes99 (5.0)")]
    public async Task CheckAsync_ProbabilityMappedToCorrectScore(double probability, double expectedScore)
    {
        // Arrange
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(probability, $"Spam probability: {probability:F3}", 0.9));

        var request = CreateCheckRequest(message: "This is a message with sufficient length for classification");

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(expectedScore).Within(0.001),
            $"Probability {probability:P0} should map to score {expectedScore}");
        Assert.That(response.Abstained, Is.False);
    }

    #endregion

    #region CheckAsync — Details Format Tests

    [Test]
    public async Task CheckAsync_SpamDetected_DetailsContainsCertainty()
    {
        // Arrange
        const string classifierDetails = "Spam probability: 0.850 (key words: buy, crypto)";
        _mockBayesClassifier.Classify(Arg.Any<string>()).Returns(
            new BayesClassificationResult(0.85, classifierDetails, 0.70));

        var request = CreateCheckRequest(message: "Buy crypto now with guaranteed returns from our channel");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert — Response details should embed certainty and classifier details
            Assert.That(response.Details, Does.Contain("certainty:"));
            Assert.That(response.Details, Does.Contain("0.700"));
        }
    }

    [Test]
    public async Task CheckAsync_TokenizerRemoveEmojisCalledWithProcessedMessage()
    {
        // Arrange — Message with emojis; tokenizer returns a cleaned version
        const string originalMessage = "Buy crypto now 🚀💰 guaranteed returns";
        const string cleanedMessage = "Buy crypto now  guaranteed returns";

        _mockTokenizerService.RemoveEmojis(originalMessage).Returns(cleanedMessage);
        _mockBayesClassifier.Classify(cleanedMessage).Returns(
            new BayesClassificationResult(0.85, "Spam probability: 0.850", 0.70));

        var request = CreateCheckRequest(message: originalMessage);

        // Act
        var response = await _check.CheckAsync(request);

        // Assert — Tokenizer was called with original, classifier was called with cleaned text
        _mockTokenizerService.Received(1).RemoveEmojis(originalMessage);
        _mockBayesClassifier.Received(1).Classify(cleanedMessage);
    }

    #endregion

    #region CheckAsync — Exception Handling Tests

    [Test]
    public async Task CheckAsync_ClassifierThrows_AbstainsWithErrorSet()
    {
        // Arrange — Classifier throws unexpectedly
        var exception = new InvalidOperationException("Internal classifier failure");
        _mockBayesClassifier.When(x => x.Classify(Arg.Any<string>())).Do(_ => throw exception);

        var request = CreateCheckRequest(message: "This is a test message with sufficient length");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.True);
            Assert.That(response.Score, Is.EqualTo(0.0));
            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Details, Does.Contain("Error"));
        }
    }

    [Test]
    public async Task CheckAsync_TokenizerThrows_AbstainsWithErrorSet()
    {
        // Arrange — Tokenizer throws unexpectedly
        _mockTokenizerService.When(x => x.RemoveEmojis(Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("Tokenizer failure"));

        var request = CreateCheckRequest(message: "This is a test message with sufficient length");

        // Act
        var response = await _check.CheckAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(response.Abstained, Is.True);
            Assert.That(response.Score, Is.EqualTo(0.0));
            Assert.That(response.Error, Is.Not.Null);
        }
    }

    #endregion

    #region Helper Methods

    private static ContentCheckRequest CreateShouldExecuteRequest(
        string message,
        bool isUserTrusted,
        bool isUserAdmin)
    {
        return new ContentCheckRequest
        {
            Message = message,
            User = UserIdentity.FromId(TestUserId),
            Chat = ChatIdentity.FromId(TestChatId),
            IsUserTrusted = isUserTrusted,
            IsUserAdmin = isUserAdmin
        };
    }

    private static BayesCheckRequest CreateCheckRequest(
        string message,
        int minMessageLength = DefaultMinMessageLength)
    {
        return new BayesCheckRequest
        {
            Message = message,
            User = UserIdentity.FromId(TestUserId),
            Chat = ChatIdentity.FromId(TestChatId),
            MinMessageLength = minMessageLength,
            CancellationToken = CancellationToken.None
        };
    }

    #endregion
}
