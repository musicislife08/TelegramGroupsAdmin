# Content Tester - Test Before You Deploy

The **Content Tester** is your sandbox for testing spam detection rules before they affect real messages. Test text, images, files, and URLs to see exactly how the system will respond—without risking false positives or banning legitimate users.

**Think of it as**: A safe playground for experimenting with spam detection.

## Why Use Content Tester?

### Common Use Cases

**Before deploying new rules**:
- Added new stop words? Test them first
- Enabled a new algorithm? See how it performs
- Changed thresholds? Verify impact

**Investigating false positives**:
- User says they were wrongly flagged? Test their message
- See which algorithm triggered the false positive
- Adjust configuration to prevent it

**Training yourself**:
- Learn how algorithms work
- Understand confidence scoring
- Build intuition for what gets flagged

**Testing edge cases**:
- Borderline promotional content
- Legitimate messages with spam keywords
- URLs from gray-area domains

---

## Page Layout

The Content Tester page has a simple, focused layout:

### Input Area (Top)

- **Text input** - Large text area for message content
- **Image upload** - Drag & drop or paste images (Ctrl+V)
- **Video upload** - Test video spam detection
- **File upload** - File attachment support (note: file scanning integration not yet complete)
- **Chat selector** - Test with chat-specific or global config
- **Test button** - Run detection

### Results Area (Bottom)

- **Overall confidence** - Final spam score (0-100%)
- **Action** - What would happen (Auto-Ban, Review, Pass)
- **Per-algorithm breakdown** - Each algorithm's verdict
- **Detailed reasoning** - Why each algorithm flagged/passed

[Screenshot: Content Tester page layout]

---

## Testing Text Messages

### Basic Text Testing

1. Navigate to **Tools** → **Content Tester**
2. Type or paste message text in the text area
3. Click **Test Content**
4. Review results

**Example test**:
```
Join my VIP crypto signals group! Guaranteed 500% profits!
Click here: https://bit.ly/scam123
```

**Expected results**:
- Overall: 85-95% confidence
- Stop Words: 100% (matched "VIP signals", "guaranteed profits")
- URL Content: 100% (suspicious shortened URL)
- Naive Bayes: 80-90% (if trained)
- Action: AUTO-BAN

[Screenshot: Text test results with breakdown]

---

## Testing Images

The Content Tester supports image testing for vision-based spam detection (if OpenAI Vision is enabled).

### How to Test Images

**Method 1 - Paste (Recommended)**:
1. Copy image to clipboard (Ctrl/Cmd+C)
2. Click in text area
3. Paste (Ctrl/Cmd+V)
4. Image appears as preview
5. Click **Test Content**

**Method 2 - Drag & Drop**:
1. Drag image file from your computer
2. Drop into the designated area
3. Image preview appears
4. Click **Test Content**

**Method 3 - File Upload**:
1. Click **Upload Image** button
2. Select image file from dialog
3. Image preview appears
4. Click **Test Content**

### What Gets Analyzed

**If OpenAI Vision is enabled**:
- Image content analyzed by GPT-4 Vision
- Text in images extracted (OCR-like)
- Visual spam indicators detected
- Context-aware analysis

**Confidence score based on**:
- Promotional imagery (ads, logos)
- Text overlays with spam keywords
- Visual spam patterns (urgent CTAs, fake urgency)

**Example**:
```
Image: Screenshot of "GET RICH QUICK!" banner with crypto logos
Expected: 85-95% confidence (OpenAI Vision flags promotional spam)
```

[Screenshot: Image test with OpenAI Vision analysis]

---

## Testing Files

Test uploaded files for malware without actually sending them to your Telegram group.

### How to Test Files

1. Click **Upload File** button
2. Select file (any type: PDF, ZIP, EXE, etc.)
3. File name and size display
4. Click **Test Content**
5. Wait for scan results (may take 10-30 seconds)

### What Gets Scanned

**ClamAV (Local)**:
- Fast antivirus scan (~2 seconds)
- Detects known malware signatures
- Always runs if ClamAV is configured

**VirusTotal (Cloud)**:
- Multi-engine scan (60+ antivirus engines)
- Takes longer (~10-30 seconds)
- Only runs if VirusTotal API key configured

### Scan Results

**Clean file**:
```
File: document.pdf
ClamAV: Clean
VirusTotal: 0/60 engines flagged
Confidence: 0% (safe)
```

**Infected file**:
```
File: malware.exe
ClamAV: FOUND - Win32.Trojan.Generic
VirusTotal: 45/60 engines flagged
Confidence: 100% (malware detected)
Action: AUTO-BAN
```

[Screenshot: File test results with virus scan details]

---

## Testing URLs

Test links against URL filtering rules before adding them to blocklists or whitelist.

### How to Test URLs

1. Type message containing URL in text area:
```
Check out this site: https://example.com
```
2. Click **Test Content**
3. Review URL Content algorithm results

### URL Test Results

**Blocked domain**:
```
URL: https://phishing-site.com
Blocklist: Block List Project - Phishing
Category: Financial Phishing
Confidence: 100%
Action: AUTO-BAN
```

**Whitelisted domain**:
```
URL: https://github.com/yourproject
Whitelist: Matched
Confidence: 0% (whitelisted)
Action: PASS
```

**Clean domain**:
```
URL: https://wikipedia.org
Blocklist: No match
Whitelist: No match
Confidence: 0% (clean)
Action: PASS
```

**Shortened URL** (with VirusTotal):
```
URL: https://bit.ly/abc123
Resolves to: https://evil-scam-site.com
Blocklist: Block List Project - Scam
Confidence: 100%
Action: AUTO-BAN
```

---

## Chat-Specific vs. Global Testing

You can test using chat-specific configuration or global defaults.

### Chat Selector

At the top of the Content Tester:
- **Dropdown menu** - Select chat or "Global Configuration"
- **Global** - Uses default system settings
- **Specific chat** - Uses per-chat overrides (if configured)

### When to Use Each

**Global Configuration**:
- Testing general rules
- Before applying config to all chats
- Default behavior testing

**Specific Chat**:
- Chat has custom settings
- Different threshold per chat
- Chat-specific whitelist/blocklist

**Example**:
```
Chat A: Crypto trading (allows crypto URLs)
Chat B: Tech support (blocks all crypto links)

Test same message in both configs to see different results
```

[Screenshot: Chat selector dropdown]

---

## Interpreting Results

### Overall Confidence Score

The final confidence (0-100) determines the action:

- **85-100**: AUTO-BAN → User banned, message deleted
- **70-84**: REVIEW QUEUE → Manual review required
- **0-69**: PASS → Message allowed

**Color coding**:
- 🔴 Red (85+): Auto-ban
- 🟡 Yellow (70-84): Review
- 🟢 Green (<70): Pass

### Per-Algorithm Breakdown

Each algorithm shows:
- **Name** (e.g., Stop Words, Naive Bayes)
- **Confidence** (0-100%)
- **Status** (Flagged ✓ or Passed ○)
- **Reason** - Why it flagged or passed

**Example breakdown**:
```
✓ Stop Words (100%) - Matched: "guaranteed profits", "VIP signals"
✓ URL Content (100%) - Blocked domain: bit.ly/scam123
✓ Naive Bayes (82%) - Classified as spam (trained on 250 samples)
○ CAS Database (0%) - User not in global spammer database
○ Invisible Chars (0%) - No suspicious characters detected
○ Similarity (45%) - Low similarity to known spam patterns

Overall: 87% → AUTO-BAN
```

### Understanding "Why"

For each algorithm that flagged the message, you'll see:

**Stop Words**:
```
Matched keywords: "VIP", "guaranteed", "profits", "click here"
```

**URL Content**:
```
Domain: bit.ly/scam123
Blocklist: Block List Project - Redirect + Scam (after resolution)
```

**Naive Bayes**:
```
Spam probability: 0.82
Based on 250 training samples
```

**OpenAI Verification**:
```
GPT-4 Analysis: "This message is unsolicited promotion with
urgency tactics and suspicious links. Clear spam pattern."
Confidence: 92%
```

---

## Common Testing Scenarios

### Scenario 1: Testing New Stop Words

**Goal**: You want to add "moon" and "lambo" as stop words for your crypto group.

**Test**:
```
When will Bitcoin moon? 🚀 Time to buy a lambo!
```

**Before adding stop words**:
- Stop Words: 0%
- Overall: 20-30% (probably passes)

**After adding**:
- Stop Words: 80-90%
- Overall: 60-70% (might hit review queue)

**Conclusion**: These words cause false positives in legitimate crypto discussion. Don't add them as stop words. Use context-aware OpenAI instead.

---

### Scenario 2: Testing Threshold Changes

**Goal**: You changed auto-ban threshold from 85 to 80.

**Test message** (borderline spam):
```
Check out this new DeFi project: https://newproject.com
```

**At 85 threshold**:
- Overall: 82%
- Action: REVIEW QUEUE

**At 80 threshold**:
- Overall: 82%
- Action: AUTO-BAN

**Conclusion**: Lowering threshold from 85 to 80 causes more auto-bans. Monitor for false positives.

---

### Scenario 3: Testing Whitelist

**Goal**: Verify your company domain is whitelisted.

**Test**:
```
More details at https://yourcompany.com/promo
```

**Without whitelist**:
- URL Content: 50% (word "promo" in URL suspicious)
- Overall: 40-50%

**With whitelist**:
- URL Content: 0% (whitelisted domain)
- Overall: 10-20%

**Conclusion**: Whitelist working correctly.

---

### Scenario 4: Testing Image Spam

**Goal**: See if promotional banner gets flagged.

**Test**: Upload image of "LIMITED TIME OFFER! 50% OFF" banner

**With OpenAI Vision**:
- OpenAI Verification: 85%
- Reason: "Promotional imagery with urgency tactics"
- Overall: 75-85%

**Without OpenAI Vision**:
- No image analysis
- Overall: 0% (text-only algorithms see nothing)

**Conclusion**: OpenAI Vision is essential for image spam detection.

---

## Advanced Features

### Testing Multiple Messages

Currently, you must test one message at a time.

**Workaround for batch testing**:
1. Keep Content Tester open in browser tab
2. Test first message
3. Clear text area
4. Paste next message
5. Repeat

**Keyboard shortcuts** (future feature):
- Ctrl/Cmd+Enter: Test content
- Ctrl/Cmd+L: Clear input
- Ctrl/Cmd+R: Reload last test

### Saving Test Results

Test results are not saved automatically.

**Workaround**:
- Take screenshots
- Copy/paste results to notes
- Use browser dev tools to export HTML

**Future feature**: Test history with ability to replay previous tests.

### Comparing Configurations

Test the same message with different configurations:

1. **Baseline test**: Test with current settings
2. **Note overall confidence**
3. **Change configuration** (e.g., enable algorithm)
4. **Test again**
5. **Compare confidence scores**

**Example workflow**:
```
Test 1 (without OpenAI): 72% confidence
Change: Enable OpenAI Verification
Test 2 (with OpenAI): 58% confidence (GPT-4 said not spam)

Conclusion: OpenAI reduces false positive
```

---

## Troubleshooting

### Test results different from actual detection

**Possible causes**:
- Cache: Real detection may use cached results
- Timing: Algorithms may behave differently under load
- Training data: ML models retrain overnight

**Solutions**:
- Clear cache and retest
- Test during low-traffic period
- Compare test results to actual message detection in Reports

### OpenAI not analyzing images

**Symptoms**:
- Image uploaded but OpenAI Verification shows 0%
- No GPT-4 analysis in results

**Solutions**:
- Verify OpenAI API key is configured
- Ensure OpenAI Verification algorithm is enabled
- Check image format (JPG, PNG supported)
- Check API quota (OpenAI rate limits)

### File scanning not working

**Note**: File scanning integration in Content Tester is not yet complete. The UI accepts file uploads, but scanning is not currently processed. File scanning works in production (real Telegram messages), just not in the Content Tester tool yet.

**Workaround**: Test file scanning by sending actual files in your monitored Telegram group.

### Content Tester is slow

**Symptoms**:
- Test takes >5 seconds
- Loading spinner spins indefinitely

**Solutions**:
- Disable slow algorithms temporarily (OpenAI, Translation)
- Reduce image size (<2MB)
- Check network connection (VirusTotal requires internet)
- Clear browser cache

---

## Best Practices

### Test Before Deploying

**Always test**:
- New stop words
- Threshold changes
- Whitelist additions
- Algorithm enable/disable

**Never deploy** configuration changes without testing representative samples first.

### Test Real Examples

Don't just test obvious spam like "BUY VIAGRA NOW!"

**Test realistic borderline cases**:
- Legitimate promotions from members
- Technical discussions with spam keywords
- News articles with clickbait titles
- Referral codes from known users

### Test Both Sides

Test spam examples AND legitimate examples:

**Spam tests** (should be flagged):
```
VIP crypto signals: bit.ly/signals
GET RICH QUICK! Limited time!
DM me for more info
```

**Ham tests** (should pass)**:
```
What's the best crypto exchange?
Check out this GitHub project: github.com/project
Our company is hiring: yourcompany.com/jobs
```

### Document Test Results

Keep a log of tests:
```
Date: 2025-03-15
Change: Added "moon" and "lambo" to stop words
Test: "When Bitcoin moon?"
Result: 85% confidence (false positive!)
Decision: Removed stop words, use OpenAI instead
```

---

## Related Documentation

- **[Spam Detection Guide](03-spam-detection.md)** - Understand algorithms being tested
- **[URL Filtering](04-url-filtering.md)** - Configure URL rules to test
- **[First Configuration](../getting-started/02-first-configuration.md)** - Initial setup before testing
- **[Reports Queue](02-reports.md)** - Compare test results to actual detections

---

**Next: Customize spam detection with AI** → Continue to **[AI Prompt Builder](06-ai-prompt-builder.md)**!
