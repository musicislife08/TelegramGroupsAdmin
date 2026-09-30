using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

[TestFixture]
public class AIPromptBuilderTests
{
    // #521: history labels come from the message's current verdict. A message allowed below the
    // review threshold must read as "ok", or it biases every later AI veto toward spam.
    [TestCase(false, "ok")]
    [TestCase(true, "spam")]
    public void CreatePrompts_LabelsHistoryMessageByVerdict(bool wasSpam, string expectedStatus)
    {
        HistoryMessage[] history = [new() { UserName = "someone", Message = "earlier message", WasSpam = wasSpam }];

        var prompts = AIPromptBuilder.CreatePrompts(CreateRequest(), history);

        Assert.That(prompts.UserPrompt, Does.Contain($"<historical_message status=\"{expectedStatus}\">"));
    }

    [Test]
    public void CreatePrompts_NoHistory_OmitsHistoryBlock()
    {
        var prompts = AIPromptBuilder.CreatePrompts(CreateRequest(), []);

        Assert.That(prompts.UserPrompt, Does.Not.Contain("<message_history>"));
    }

    private static AIVetoCheckRequest CreateRequest() => new()
    {
        Message = "I am new here and looking for a good friend",
        User = UserIdentity.FromId(123),
        Chat = ChatIdentity.FromId(456),
        SystemPrompt = null,
        HasSpamFlags = true,
        MinMessageLength = 10,
        CheckShortMessages = false,
        MessageHistoryCount = 3,
        Model = "gpt-4",
        MaxTokens = 500,
        CancellationToken = CancellationToken.None
    };
}
