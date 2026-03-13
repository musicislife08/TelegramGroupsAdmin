# Unified Content Tester - Test Before You Deploy

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
- Understand additive scoring
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
- **File upload** - File attachment support for malware scanning (ClamAV + VirusTotal)
- **Chat selector** - Test with chat-specific or global config
- **Test button** - Run detection

### Results Area (Bottom)

- **Total score** - Final spam score (additive, 0.0-5.0+ points)
- **Action** - What would happen (Auto-Ban, Review, Pass)
- **Per-algorithm breakdown** - Each algorithm's verdict and point contribution
- **Detailed reasoning** - Why each algorithm flagged/passed

[Screenshot: Content Tester page layout]

---

## Testing Text Messages

### Basic Text Testing

1. Navigate to **Tools** → **Content Tester**
2. Type or paste message text in the text area
3. Click **Run Content Check**
4. Review results

**Example test**:
```
Join my VIP crypto signals group! Guaranteed 500% profits!
Click here: https://bit.ly/scam123
```

**Expected results**:
- Total Score: 4.5-5.0+ pts
- Stop Words: +2.0 pts (matched "VIP signals", "guaranteed profits")
- URL Content: +1.5 pts (suspicious shortened URL)
- Naive Bayes: +1.0 pts (if trained)
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
5. Click **Run Content Check**

**Method 2 - Drag & Drop**:
1. Drag image file from your computer
2. Drop into the designated area
3. Image preview appears
4. Click **Run Content Check**

**Method 3 - File Upload**:
1. Click **Upload Image** button
2. Select image file from dialog
3. Image preview appears
4. Click **Run Content Check**

### What Gets Analyzed

**If OpenAI Vision is enabled**:
- Image content analyzed by AI Vision
- Text in images extracted (OCR-like)
- Visual spam indicators detected
- Context-aware analysis

**Score based on**:
- Promotional imagery (ads, logos)
- Text overlays with spam keywords
- Visual spam patterns (urgent CTAs, fake urgency)

**Example**:
```
Image: Screenshot of "GET RICH QUICK!" banner with crypto logos
Expected: +1.5-2.0 pts from image detection (OpenAI Vision flags promotional spam)
```

[Screenshot: Image test with OpenAI Vision analysis]

---

## Testing Files

Test uploaded files for malware without actually sending them to your Telegram group.

### How to Test Files

1. Click **Upload File** button
2. Select file (any type: PDF, ZIP, EXE, etc.)
3. File name and size display
4. Click **Run Content Check**
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
Score: 0.00 pts (safe)
```

**Infected file**:
```
File: malware.exe
ClamAV: FOUND - Win32.Trojan.Generic
VirusTotal: 45/60 engines flagged
Score: 5.00 pts (malware detected)
Action: Delete + DM notification
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
2. Click **Run Content Check**
3. Review URL Content algorithm results

### URL Test Results

**Blocked domain**:
```
URL: https://phishing-site.com
Blocklist: Block List Project - Phishing
Category: Financial Phishing
Score: +2.0 pts
Action: AUTO-BAN (if total >= 4.0)
```

**Whitelisted domain**:
```
URL: https://github.com/yourproject
Whitelist: Matched
Score: 0.00 pts (whitelisted)
Action: PASS
```

**Clean domain**:
```
URL: https://wikipedia.org
Blocklist: No match
Whitelist: No match
Score: 0.00 pts (clean)
Action: PASS
```

**Shortened URL** (with VirusTotal):
```
URL: https://bit.ly/abc123
Resolves to: https://evil-scam-site.com
Blocklist: Block List Project - Scam
Score: +2.0 pts
Action: AUTO-BAN (if total >= 4.0)
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

### Total Score

The total score is additive: each algorithm contributes 0.0-5.0 points, and the scores are summed. The total determines the action:

- **4.0+**: AUTO-BAN → User banned, message deleted (default threshold)
- **2.5-3.9**: REVIEW QUEUE → Manual review required (default threshold)
- **Below 2.5**: PASS → Message allowed

These thresholds are configurable in Settings → Content Detection (AutoBanThreshold and ReviewQueueThreshold).

**Color coding**:
- Red (>= 4.0): Auto-ban
- Yellow (2.5-3.9): Review
- Green (< 2.5): Pass

### Per-Algorithm Breakdown

Each algorithm shows:
- **Check Name** (e.g., Stop Words, Naive Bayes)
- **Result** (SPAM, CLEAN, or ABSTAINED)
- **Score** - Points contributed (0.00-5.00)
- **Details** - Why it flagged or passed

**Example breakdown**:
```
SPAM  Stop Words:     +2.00 pts - Matched: "guaranteed profits", "VIP signals"
SPAM  URL Content:    +1.50 pts - Blocked domain: bit.ly/scam123
SPAM  Naive Bayes:    +1.00 pts - Classified as spam (trained on 250 samples)
CLEAN CAS Database:    0.00 pts - User not in global spammer database
CLEAN Invisible Chars: 0.00 pts - No suspicious characters detected
CLEAN Similarity:      0.00 pts - Low similarity to known spam patterns

Total: 4.50 pts → AUTO-BAN
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
Score: +1.00 pts
Based on 250 training samples
```

**OpenAI Verification**:
```
AI Analysis: "This message is unsolicited promotion with
urgency tactics and suspicious links. Clear spam pattern."
Score: +1.50 pts
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
- Stop Words: 0.00 pts
- Total: 0.5-1.0 pts (probably passes)

**After adding**:
- Stop Words: +2.00 pts
- Total: 2.5-3.0 pts (might hit review queue)

**Conclusion**: These words cause false positives in legitimate crypto discussion. Don't add them as stop words. Use context-aware OpenAI instead.

---

### Scenario 2: Testing Threshold Changes

**Goal**: You changed auto-ban threshold from 4.0 to 3.5.

**Test message** (borderline spam):
```
Check out this new DeFi project: https://newproject.com
```

**At 4.0 threshold**:
- Total: 3.7 pts
- Action: REVIEW QUEUE

**At 3.5 threshold**:
- Total: 3.7 pts
- Action: AUTO-BAN

**Conclusion**: Lowering threshold from 4.0 to 3.5 causes more auto-bans. Monitor for false positives.

---

### Scenario 3: Testing Whitelist

**Goal**: Verify your company domain is whitelisted.

**Test**:
```
More details at https://yourcompany.com/promo
```

**Without whitelist**:
- URL Content: +1.0 pts (word "promo" in URL suspicious)
- Total: 1.5-2.0 pts

**With whitelist**:
- URL Content: 0.00 pts (whitelisted domain)
- Total: 0.5-1.0 pts

**Conclusion**: Whitelist working correctly.

---

### Scenario 4: Testing Image Spam

**Goal**: See if promotional banner gets flagged.

**Test**: Upload image of "LIMITED TIME OFFER! 50% OFF" banner

**With OpenAI Vision**:
- OpenAI Verification: +1.5 pts
- Reason: "Promotional imagery with urgency tactics"
- Total: 2.5-3.5 pts

**Without OpenAI Vision**:
- No image analysis
- Total: 0.00 pts (text-only algorithms see nothing)

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
2. **Note total score**
3. **Change configuration** (e.g., enable algorithm)
4. **Test again**
5. **Compare scores**

**Example workflow**:
```
Test 1 (without OpenAI): 3.2 pts total
Change: Enable OpenAI Verification (AI veto)
Test 2 (with OpenAI): 1.8 pts total (AI vetoed — not spam)

Conclusion: OpenAI veto reduces false positive
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
- Image uploaded but OpenAI Verification shows 0.00 pts
- No AI analysis in results

**Solutions**:
- Verify OpenAI API key is configured
- Ensure OpenAI Verification algorithm is enabled
- Check image format (JPG, PNG supported)
- Check API quota (OpenAI rate limits)

### File scanning not working

**Symptoms**:
- File uploaded but no scan results appear
- Scan takes too long or times out

**Solutions**:
- Verify ClamAV is running and reachable
- Check VirusTotal API key is configured (if using VirusTotal)
- Ensure file is under 100MB (Content Tester limit) and under 20MB for VirusTotal
- Use the EICAR test file (eicar.com) to verify scanning works end-to-end
- Check browser console for errors if the upload itself fails

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
Result: 3.5 pts total (false positive — hit review queue!)
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
