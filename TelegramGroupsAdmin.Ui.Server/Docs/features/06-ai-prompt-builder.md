# AI Prompt Builder - Customize GPT-4 Detection

The **AI Prompt Builder** is a meta-AI feature that uses GPT-4 to generate and improve custom spam detection prompts tailored to your group's specific context, rules, and culture.

**What it does**: Creates custom prompts that tell GPT-4 exactly what spam looks like in YOUR group, dramatically improving detection accuracy.

## Why Custom Prompts Matter

### The Problem with Generic Detection

Default spam detection uses generic rules:
- "VIP signals" = spam
- "Guaranteed profits" = spam
- Promotional links = spam

But context matters:
- Crypto trading group: Discussing signals is legitimate
- Investment group: Sharing opportunities is expected
- Tech community: Promotional content from sponsors is allowed

### The Solution: Context-Aware Prompts

Custom prompts tell GPT-4:
- What your group discusses
- What's allowed vs. not allowed
- Your community's culture and norms
- Specific spam patterns you've seen

**Result**: 80-90% reduction in false positives while maintaining or improving spam detection.

---

## How to Access

1. Navigate to **Settings** → **Content Detection** → **External Services Config**
2. Scroll to **OpenAI Integration** section
3. Find **Custom Prompt Configuration**
4. Click **Open Prompt Builder** button

[Screenshot: OpenAI Integration section with Prompt Builder button]

---

## Two Ways to Create Prompts

### Method 1: Generate from Scratch

Use the form-based builder to create a new prompt from your requirements.

**Best for**:
- First-time setup
- Creating prompts for new groups
- Starting fresh with AI guidance

### Method 2: Improve Existing Prompt

Let GPT-4 analyze your current prompt and suggest improvements based on feedback.

**Best for**:
- Refining existing prompts
- Addressing specific false positive patterns
- Iterative improvement over time

---

## Method 1: Generate from Scratch

### Step-by-Step Walkthrough

#### 1. Open Prompt Generator

Click **Generate New Prompt** in the Prompt Builder dialog.

#### 2. Fill Out the Form

**Group Topic** (Required):
```
What is your group about?
Example: "Cryptocurrency trading and DeFi discussion"
```

**Group Description** (Required):
```
Provide more details about your community.
Example: "A community for experienced crypto traders to discuss
market analysis, trading strategies, and DeFi protocols. We allow
sharing trading signals if accompanied by reasoning."
```

**Community Rules** (Optional but recommended):
```
What are your group's rules?
Example:
- No pump-and-dump schemes
- No unsolicited DMs requests
- Trading signals must include analysis
- Referral codes allowed only from verified members
```

**Spam Patterns** (Optional):
```
What spam have you seen?
Example:
- "Join my VIP signals group" invitations
- Guaranteed profit claims without analysis
- External Telegram group promotions
- Phishing links disguised as "airdrop" claims
```

**Legitimate Content Examples** (Optional):
```
What legitimate messages get wrongly flagged?
Example:
- "Bitcoin will moon if it breaks $50k resistance"
- Technical analysis with multiple indicators
- Questions about which exchange to use
- Sharing news articles about regulations
```

**Strictness Level** (Required):
- **Lenient**: Fewer false positives, might miss some spam
- **Moderate**: Balanced (recommended)
- **Strict**: Catches more spam, more false positives

[Screenshot: Prompt generation form filled out]

#### 3. Generate Prompt

Click **Generate Prompt** button.

**What happens**:
- Form data sent to GPT-4
- GPT-4 analyzes your group's context
- Generates custom spam detection prompt
- Prompt appears in text area (usually 300-500 words)

**Time**: 10-30 seconds

#### 4. Review Generated Prompt

GPT-4 returns a structured prompt like:

```
You are analyzing messages for a cryptocurrency trading community.

COMMUNITY CONTEXT:
This group discusses market analysis, trading strategies, and DeFi
protocols. Members share trading signals when accompanied by reasoning.

ALLOWED CONTENT:
- Technical analysis with charts and indicators
- Market predictions with supporting data
- Questions about exchanges, wallets, protocols
- Educational content about crypto concepts
- Sharing news articles from reputable sources
- Trading signals with detailed reasoning

NOT ALLOWED (SPAM):
- Unsolicited "VIP signals group" invitations
- Guaranteed profit claims without analysis
- External Telegram group promotions
- "DM me for more info" requests
- Phishing links disguised as airdrops
- Pump-and-dump coordination

LEGITIMATE PATTERNS THAT ARE NOT SPAM:
- "Bitcoin will moon" - Common expression in crypto communities
- "Which exchange is best?" - Genuine questions
- Technical jargon and abbreviations - Normal in this context

Analyze the message and determine if it's spam. Consider context,
tone, and intent. If unsure, err on the side of caution (not spam).
```

#### 5. Test the Prompt

Before saving, test it:
1. Copy the generated prompt
2. Navigate to **Tools** → **Content Tester**
3. Test borderline messages
4. Verify false positives are reduced

#### 6. Save Prompt

Click **Save Prompt** in the dialog.

**What happens**:
- Prompt saved as new version (version 1, 2, 3, etc.)
- Timestamp and creator recorded
- Immediately active for spam detection
- Previous version archived

[Screenshot: Generated prompt with Save button]

---

## Method 2: Improve Existing Prompt

### When to Use Improvement

**Scenarios**:
- False positives persist after using generated prompt
- New spam patterns emerged
- Group culture changed
- Want to refine specific areas

### How to Improve

#### 1. Open Prompt Improver

In the Prompt Builder, click **Improve Existing Prompt**.

#### 2. Provide Feedback

**Current Issues** (Required):
```
What problems are you experiencing?
Example:
"Too many false positives on legitimate price predictions.
Members say 'Bitcoin will moon' and it gets flagged as spam.
Also, we now allow referral codes from verified members but
they're still being caught."
```

**Examples of False Positives** (Optional):
```
Paste actual messages that were wrongly flagged:
1. "BTC will moon if it breaks $50k resistance - my analysis attached"
2. "Use my Binance referral code for 10% discount: ABC123"
3. "Check out this new DeFi protocol: [legitimate project link]"
```

**Examples of Missed Spam** (Optional):
```
Spam that got through:
1. "Exclusive access to my private signals! DM me now!"
2. [Actual spam messages that passed detection]
```

**Additional Context** (Optional):
```
Any other information GPT-4 should know:
"We recently allowed referral codes from users with 100+ messages.
New members cannot share referrals."
```

[Screenshot: Improvement form with feedback]

#### 3. Generate Improved Prompt

Click **Generate Improvements**.

**What happens**:
- Current prompt sent to GPT-4 with your feedback
- GPT-4 analyzes issues and examples
- Generates improved version addressing your concerns
- Shows side-by-side comparison of old vs. new

**Time**: 15-45 seconds

#### 4. Review Improvements

GPT-4 shows:
- **What changed**: Specific sections modified
- **Why**: Reasoning for each change
- **Expected impact**: How this should improve detection

**Example**:
```
CHANGES MADE:

1. Added clarification for "moon" terminology:
   OLD: [no mention]
   NEW: "Moon" is common crypto slang for price increase. Not spam.

2. Updated referral code policy:
   OLD: "No referral codes"
   NEW: "Referral codes allowed from users with 100+ messages"

3. Refined legitimate project sharing:
   OLD: "No external project promotions"
   NEW: "Sharing new DeFi projects is OK if user provides analysis"

EXPECTED IMPACT:
- Reduce false positives on price predictions by ~80%
- Allow referral codes from established members
- Maintain spam blocking for unsolicited VIP group invites
```

#### 5. Accept or Reject

- **Accept**: Saves as new prompt version
- **Reject**: Keeps current version, you can try again with different feedback

[Screenshot: Improvement comparison with Accept/Reject buttons]

---

## Version Management

Every prompt change creates a new version.

### Version History

**View all versions**:
1. Settings → Content Detection → External Services → OpenAI Integration
2. Scroll to **Prompt Version History**
3. See list of all versions with metadata

**For each version**:
- **Version number** (e.g., v3)
- **Created date** (e.g., "March 15, 2025 at 2:30 PM")
- **Created by** (username)
- **How created** (Generated or Improved)
- **Actions**: View, Restore, Improve

[Screenshot: Version history list]

### Viewing Old Versions

Click **View** on any version:
- Shows full prompt text
- Shows generation parameters (if generated)
- Shows improvement feedback (if improved)
- Auto-growing textarea for reading

### Restoring Old Versions

If new prompt isn't working:
1. Find previous version in history
2. Click **Restore**
3. Confirm restoration
4. Previous version becomes active

**Note**: Creates a NEW version (copy of old one), doesn't delete current.

### Improving from History

Click **Improve** on any version to use it as the base for improvements.

---

## Best Practices

### Writing Good Descriptions

**DO**:
- Be specific: "Crypto trading group for DeFi discussion"
- Include culture: "We're technical, allow jargon"
- List what's OK: "Price predictions are fine"
- List what's not: "No VIP group invites"

**DON'T**:
- Be vague: "A group"
- Assume GPT-4 knows: "The usual spam stuff"
- Skip examples: "You know what spam is"

### Iterative Improvement

**Don't expect perfection on first try**:
1. Generate initial prompt
2. Test for 1-2 weeks
3. Collect false positive examples
4. Improve with specific feedback
5. Repeat

**Each iteration** should address 1-2 specific issues, not everything at once.

### Testing New Prompts

**Before activating**:
1. Save prompt
2. Test in Content Tester with 10+ examples
3. Compare results to previous prompt
4. Check false positive rate
5. Activate if improved

### Monitoring After Changes

**After activating new prompt**:
1. Watch Reports queue closely for 24-48 hours
2. Look for unexpected false positives
3. Check if known spam still gets caught
4. Revert if worse, improve if slight issues

---

## Common Use Cases

### Use Case 1: Crypto Trading Group

**Generated prompt includes**:
- "Moon", "lambo", "wen" are normal slang, not spam
- Technical analysis with indicators is encouraged
- Sharing signals OK if reasoning included
- "DM me for VIP access" is spam
- External group promotions are spam

**Result**: False positives on price discussions drop from 30% to <5%

---

### Use Case 2: Tech Support Community

**Generated prompt includes**:
- Product recommendations are helpful, not spam
- Sharing GitHub projects is encouraged
- "Check out this tool" is OK with explanation
- "Get certified fast!" and "Buy our course!" is spam
- Referral links allowed if disclosing affiliation

**Result**: Legitimate tool recommendations no longer flagged

---

### Use Case 3: Bilingual Community

**Generated prompt includes**:
- Group uses both English and Spanish
- Cultural context for both languages
- Spam patterns in both languages
- Code-switching is normal, not suspicious

**Result**: Multi-language messages correctly evaluated

---

## Troubleshooting

### Generated prompt too generic

**Symptom**: Prompt doesn't reflect your group's specifics

**Solution**:
- Provide more detailed group description
- Add more community rules
- Include actual spam examples
- Be specific about what's allowed

### Improved prompt made things worse

**Symptom**: More false positives after improvement

**Solution**:
- Revert to previous version immediately
- Review what feedback you gave (was it unclear?)
- Try improving with different, clearer feedback
- Test each improvement in Content Tester first

### Prompt not reducing false positives

**Symptom**: Custom prompt performs similar to default

**Solution**:
- Check that OpenAI Verification is enabled
- Verify OpenAI API key is working
- Ensure prompt is actually active (check version history)
- Provide more specific legitimate content examples

### Prompt too lenient (spam getting through)

**Symptom**: Spam passing that should be caught

**Solution**:
- Use **Improve** with missed spam examples
- Set strictness to "Strict" when regenerating
- Add specific spam patterns to prompt
- Consider combining with other algorithms (Stop Words, URL filters)

---

## Cost Considerations

### OpenAI API Costs

**Prompt generation**: ~$0.01 per generation
**Prompt improvement**: ~$0.02 per improvement
**Spam detection with custom prompt**: ~$0.002 per message reviewed

### Cost Optimization

**Use Veto Mode** (recommended):
- Only runs GPT-4 on borderline cases (70-84 confidence)
- Costs ~10x less than running on every message
- Still gets benefit of custom prompt

**Batch improvements**:
- Collect feedback for 1-2 weeks
- Make one improvement addressing multiple issues
- Better than frequent small improvements

---

## Advanced Tips

### Seasonal Adjustments

**Example**: Crypto bull market
- More legitimate price excitement
- "Moon" usage increases legitimately
- Improve prompt to be more lenient during bull runs

### A/B Testing

**Compare two prompts**:
1. Version A: Current prompt
2. Version B: New experimental prompt
3. Test same messages with both in Content Tester
4. Pick the one with fewer false positives

### Exporting Prompts

**Not yet supported**, but workaround:
- View prompt version
- Copy full text to clipboard
- Save externally (notes, docs)
- Share with other admins or groups

---

## Related Documentation

- **[Spam Detection Guide](03-spam-detection.md)** - How OpenAI Verification works
- **[Content Tester](05-content-tester.md)** - Test custom prompts
- **[Reports Queue](02-reports.md)** - Monitor false positives
- **[First Configuration](../getting-started/02-first-configuration.md)** - When to enable OpenAI

---

**Next: Manage web admin accounts** → Continue to **[Web User Management](../admin/01-web-user-management.md)**!
