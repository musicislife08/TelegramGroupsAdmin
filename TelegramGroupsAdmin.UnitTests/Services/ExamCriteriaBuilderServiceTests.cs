using TelegramGroupsAdmin.Services.ExamCriteriaBuilder;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for ExamCriteriaBuilderService prompt building methods.
/// Tests the internal prompt generation logic that produces AI input.
/// </summary>
[TestFixture]
public class ExamCriteriaBuilderServiceTests
{
    #region BuildSystemPrompt Tests

    [Test]
    public void BuildSystemPrompt_ContainsKeyInstructions()
    {
        // Act
        var prompt = ExamCriteriaBuilderService.BuildSystemPrompt();

        // Assert - verify key elements are present
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("evaluation criteria"));
            Assert.That(prompt, Does.Contain("ONE-SHOT evaluation"));
            Assert.That(prompt, Does.Contain("PASS"));
            Assert.That(prompt, Does.Contain("FAIL"));
            Assert.That(prompt, Does.Contain("human review queue"));
        }
    }

    [Test]
    public void BuildSystemPrompt_ExplainsNoFollowUp()
    {
        // Act
        var prompt = ExamCriteriaBuilderService.BuildSystemPrompt();

        // Assert - critical context that there's no conversation
        Assert.That(prompt, Does.Contain("NO opportunity for follow-up"));
    }

    [Test]
    public void BuildSystemPrompt_ProhibitsMarkdownCodeBlocks()
    {
        // Act
        var prompt = ExamCriteriaBuilderService.BuildSystemPrompt();

        // Assert - AI should not wrap output in code blocks
        Assert.That(prompt, Does.Contain("no markdown code blocks"));
    }

    #endregion

    #region BuildUserPrompt - Basic Tests

    [Test]
    public void BuildUserPrompt_IncludesQuestionAndTopic()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Why do you want to join our community?",
            GroupTopic = "Software Development"
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("Why do you want to join our community?"));
            Assert.That(prompt, Does.Contain("Software Development"));
        }
    }

    [Test]
    public void BuildUserPrompt_UsesXmlTags()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Test question",
            GroupTopic = "Test topic"
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert - verify XML structure for AI parsing
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("<group_context>"));
            Assert.That(prompt, Does.Contain("<exam_question>"));
            Assert.That(prompt, Does.Contain("<strictness_level>"));
        }
    }

    #endregion

    #region BuildUserPrompt - Strictness Level Tests

    [Test]
    public void BuildUserPrompt_LenientStrictness_IncludesLenientGuidance()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic",
            Strictness = ExamStrictnessLevel.Lenient
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        Assert.That(prompt, Does.Contain("Be lenient"));
        Assert.That(prompt, Does.Contain("any genuine effort"));
    }

    [Test]
    public void BuildUserPrompt_BalancedStrictness_IncludesBalancedGuidance()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic",
            Strictness = ExamStrictnessLevel.Balanced
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        Assert.That(prompt, Does.Contain("balanced judgment"));
        Assert.That(prompt, Does.Contain("good-faith attempts"));
    }

    [Test]
    public void BuildUserPrompt_StrictStrictness_IncludesStrictGuidance()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic",
            Strictness = ExamStrictnessLevel.Strict
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        Assert.That(prompt, Does.Contain("Be strict"));
        Assert.That(prompt, Does.Contain("detailed, thoughtful"));
    }

    #endregion

    #region BuildUserPrompt - Optional Hints Tests

    [Test]
    public void BuildUserPrompt_WithGoodAnswerHints_IncludesHints()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic",
            GoodAnswerHints = "Should mention specific technologies they use"
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("<admin_hints_for_good_answers>"));
            Assert.That(prompt, Does.Contain("Should mention specific technologies they use"));
        }
    }

    [Test]
    public void BuildUserPrompt_WithoutGoodAnswerHints_OmitsSection()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic",
            GoodAnswerHints = null
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        Assert.That(prompt, Does.Not.Contain("<admin_hints_for_good_answers>"));
    }

    [Test]
    public void BuildUserPrompt_WithEmptyGoodAnswerHints_OmitsSection()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic",
            GoodAnswerHints = "   "
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        Assert.That(prompt, Does.Not.Contain("<admin_hints_for_good_answers>"));
    }

    [Test]
    public void BuildUserPrompt_WithFailureIndicators_IncludesIndicators()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic",
            FailureIndicators = "Generic answers like 'just interested'"
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("<admin_hints_for_failures>"));
            Assert.That(prompt, Does.Contain("Generic answers like 'just interested'"));
        }
    }

    [Test]
    public void BuildUserPrompt_WithoutFailureIndicators_OmitsSection()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic",
            FailureIndicators = null
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        Assert.That(prompt, Does.Not.Contain("<admin_hints_for_failures>"));
    }

    [Test]
    public void BuildUserPrompt_WithBothHints_IncludesBoth()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Why do you want to join?",
            GroupTopic = "Photography",
            GoodAnswerHints = "Mentions specific camera or genre",
            FailureIndicators = "One word answers"
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("<admin_hints_for_good_answers>"));
            Assert.That(prompt, Does.Contain("Mentions specific camera or genre"));
            Assert.That(prompt, Does.Contain("<admin_hints_for_failures>"));
            Assert.That(prompt, Does.Contain("One word answers"));
        }
    }

    #endregion

    #region BuildUserPrompt - Output Format Tests

    [Test]
    public void BuildUserPrompt_RequestsPassFailStructure()
    {
        // Arrange
        var request = new ExamCriteriaBuilderRequest
        {
            Question = "Question",
            GroupTopic = "Topic"
        };

        // Act
        var prompt = ExamCriteriaBuilderService.BuildUserPrompt(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("PASS if answer:"));
            Assert.That(prompt, Does.Contain("FAIL if answer:"));
            Assert.That(prompt, Does.Contain("ONLY the criteria in plain text"));
        }
    }

    #endregion

    #region BuildImprovementPrompt Tests

    [Test]
    public void BuildImprovementPrompt_IncludesCurrentCriteria()
    {
        // Arrange
        var currentCriteria = """
            PASS if answer:
            - Shows genuine interest

            FAIL if answer:
            - Is too short
            """;

        // Act
        var prompt = ExamCriteriaBuilderService.BuildImprovementPrompt(
            currentCriteria, "Make it stricter");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("<current_criteria>"));
            Assert.That(prompt, Does.Contain("Shows genuine interest"));
            Assert.That(prompt, Does.Contain("Is too short"));
        }
    }

    [Test]
    public void BuildImprovementPrompt_IncludesFeedback()
    {
        // Act
        var prompt = ExamCriteriaBuilderService.BuildImprovementPrompt(
            "Current criteria", "Require minimum 50 words");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("<improvement_request>"));
            Assert.That(prompt, Does.Contain("Require minimum 50 words"));
        }
    }

    [Test]
    public void BuildImprovementPrompt_InstructsToMaintainStructure()
    {
        // Act
        var prompt = ExamCriteriaBuilderService.BuildImprovementPrompt(
            "Criteria", "Feedback");

        // Assert
        Assert.That(prompt, Does.Contain("PASS/FAIL structure"));
        Assert.That(prompt, Does.Contain("Keep what works"));
    }

    [Test]
    public void BuildImprovementPrompt_RequestsPlainTextOutput()
    {
        // Act
        var prompt = ExamCriteriaBuilderService.BuildImprovementPrompt(
            "Criteria", "Feedback");

        // Assert
        Assert.That(prompt, Does.Contain("plain text"));
        Assert.That(prompt, Does.Contain("No preamble"));
    }

    #endregion
}
