# Reports Queue - Moderation Central

The **Reports** page is your central hub for reviewing and managing spam detections that need human judgment. This is where borderline spam (scores between 2.5-3.9 points), suspected impersonators, exam failures, and profile scan alerts land for your review.

**Think of it as**: Your moderation inbox - messages that aren't clearly spam or ham need your decision.

## Page Overview

The Reports page uses a unified queue with type filters:

1. **Moderation Reports** - Spam detections needing manual review
2. **Impersonation Alerts** - Suspected impersonators (duplicate photos, similar usernames)
3. **Exam Reviews** - Users who failed the welcome exam and need manual approval
4. **Profile Scan Alerts** - Suspicious user profiles flagged by automated scanning

All types follow the same workflow: **Review → Decide → Act → Train**

[Screenshot: Reports page with both tabs]

---

## Moderation Reports Tab

This is where spam detections scoring between **2.5-3.9 points** appear. These are "borderline" cases where the system isn't confident enough to auto-ban but suspicious enough to flag.

### What Triggers a Moderation Report?

A message lands in Moderation Reports if:
- **Score is 2.5-3.9 points** (above review threshold but below auto-ban)
- **Training Mode is ON** (all detections go here regardless of score)
- **AI veto overrode** an auto-ban (AI said "not spam")

### Report Card Layout

Each report displays:
- **Message preview** - First few lines of the message
- **User info** - Username, user ID, profile photo
- **Timestamp** - When message was sent
- **Spam score** - Overall spam score (e.g., "3.2 points")
- **Check breakdown** - Which checks flagged it
- **Status** - Pending, Resolved, Dismissed
- **Action buttons** - Delete as Spam, Ban User, Warn, Dismiss

[Screenshot: Moderation report card with all elements labeled]

---

## Understanding Spam Scores

The system uses **additive scoring** where each detection check contributes 0.0-5.0 points. Individual check scores are summed to produce a total spam score.

### Score Ranges

- **4.0+ points**: High confidence spam → Auto-ban (if Training Mode OFF)
- **2.5-3.9 points**: Moderate confidence → Review Queue (manual review needed)
- **Below 2.5 points**: Low confidence → Pass (message allowed)

### Why Borderline Cases Matter

**2.5-3.9 points** means:
- Multiple checks flagged it, but the total didn't reach auto-ban threshold
- OR one check strongly flagged it, others abstained
- OR patterns similar to spam but not enough combined signal

**Your decision trains the ML algorithms!**
- Mark as spam → ML learns this pattern is spam
- Mark as ham → ML learns this pattern is legitimate

---

## Reviewing a Report - Step by Step

### Step 1: Read the Full Message

1. Click anywhere on the report card to expand details
2. Read the complete message text
3. Look for spam indicators:
   - Unsolicited promotions
   - Suspicious links
   - "Get rich quick" schemes
   - Aggressive calls to action
   - Off-topic content
   - Copy-paste spam

### Step 2: Check Algorithm Breakdown

Review which checks contributed points to the total score:

**Strong indicators (likely spam)**:
- **Similarity** (3.0-5.0 pts) - High similarity to known spam samples
- **Naive Bayes** (3.0-5.0 pts) - ML classifier strongly indicates spam
- **Stop Words** (1.5-2.0 pts) - Multiple spam keywords matched

**Moderate indicators (investigate further)**:
- **Naive Bayes** (1.0-2.9 pts) - ML leans toward spam but not certain
- **Similarity** (1.0-2.9 pts) - Some similarity to known spam patterns
- **Spacing Detection** (1.0-2.0 pts) - Unusual character patterns detected

**Weak indicators (possibly false positive)**:
- **Stop Words** (0.5-1.0 pt) - Matched one common keyword
- **Invisible Characters** (0.5-1.0 pt) - Few suspicious characters
- **Translation** (0.5-1.0 pt) - Inconsistent across languages

### Step 3: Consider Context

Ask yourself:
- **Is this user established?** - Check their message history
- **Is this on-topic?** - Related to group's purpose?
- **Is it solicited?** - Was user asked for this info?
- **Is the tone spammy?** - Urgent, pushy, promotional?
- **Are there links?** - Do they go to legitimate sites?

### Step 4: Make a Decision

You have four options:

#### Option 1: Delete as Spam ✓

**Choose this if**: Message is definitely spam

**What happens**:
- Message is deleted from Telegram
- Added to spam training samples (trains ML)
- Report marked as "Resolved"
- Audit log entry created

**Example**: Unsolicited crypto signals promotion with suspicious links

#### Option 2: Ban User ⛔

**Choose this if**: User is a spammer who should be permanently removed

**What happens**:
- User is permanently banned from the group
- Report marked as "Resolved"
- Audit log entry created

**Example**: Known spammer account with repeated offenses

#### Option 3: Warn ⚠

**Choose this if**: Message is borderline and user should be notified

**What happens**:
- User receives a warning
- Message stays
- Report marked as "Resolved"

**Example**: Slightly promotional content from a regular member

#### Option 4: Dismiss ⊘

**Choose this if**: You're unsure or the message is legitimate (false positive)

**What happens**:
- No action taken
- User not banned, message stays
- Does NOT train ML (neutral)
- Report marked as "Dismissed"
- Can revisit later

**Example**: Legitimate question about cryptocurrency that matched spam keywords

[Screenshot: Report detail view with action buttons]

```mermaid
flowchart TD
    M[Message Received] --> S[Run Detection Checks]
    S --> T{Total Score?}
    T -->|4.0+ points| AB[Auto-Ban]
    T -->|2.5-3.9 points| RQ[Review Queue]
    T -->|Below 2.5| P[Pass - Allow]

    RQ --> A[Admin Reviews Report]
    A --> B{Decision}
    B -->|Delete as Spam| C[Delete Message]
    B -->|Ban User| C2[Ban + Delete]
    B -->|Warn| D[Warn User]
    B -->|Dismiss| E[No Action]

    C --> F[Train ML: This is Spam]
    C2 --> F
    E --> H[No ML Training]

    F --> I[Future Similar Messages Score Higher]
    G --> J[Future Similar Messages Score Lower]
    H --> K[No Change to ML]

    style AB fill:#ff6b6b
    style C fill:#ff6b6b
    style D fill:#6bcf7f
    style E fill:#ffd93d
    style P fill:#6bcf7f
```

---

## Best Practices for Reviewing

### Daily Review Routine

**Morning routine (5-10 minutes)**:
1. Open Reports → Moderation Reports
2. Filter to "Pending" status
3. Review each report from top to bottom
4. Make decisions on all pending reports
5. Switch to "All" filter to verify yesterday's actions

**Goal**: Empty the pending queue daily to prevent backlog.

### How to Decide Quickly

**Clear spam (instant decision)**:
- Multiple checks contributing high points (3.5+ total)
- CAS Database flagged (known spammer)
- Blocked URL domains
- Obvious promotional content
- **Decision**: Delete as Spam or Ban User

**Clear false positive (instant decision)**:
- Low total score (2.5-2.8 points)
- Only one weak check flagged it
- Established user with good history
- On-topic, legitimate question
- **Decision**: Dismiss

**Borderline cases (investigate)**:
- Mixed algorithm signals
- New user with first message
- Promotional but on-topic
- Check user profile and message history
- **Decision**: Use judgment or Dismiss if unsure

### Patterns to Watch For

**Common spam patterns**:
- "Join my VIP group" - Recruitment spam
- "Guaranteed profits" - Scam promises
- "Click here now" - Urgency tactics
- "DM me" + link - Off-platform solicitation
- "Limited time offer" - Pressure tactics
- Copy-paste messages - Repeated content

**Common false positive patterns**:
- Legitimate crypto discussion with keywords
- Technical questions about trading
- News articles with clickbait titles
- Community event announcements
- Referral codes from known members

---

## Training Mode vs. Production Mode

### Training Mode (Learning Phase)

**When**: First 30-60 days, or after major configuration changes

**Behavior**:
- **ALL detections** → Moderation Reports (even 4.0+ point scores)
- **No auto-bans** - You review everything
- **Goal**: Collect 100+ training samples

**Review workflow**:
1. Review every detection
2. Mark as spam or ham consistently
3. Build confidence in system accuracy
4. After 100+ reviews, transition to Production

### Production Mode (Active Moderation)

**When**: After training phase complete

**Behavior**:
- **4.0+ points** → Auto-ban (no review needed)
- **2.5-3.9 points** → Moderation Reports (manual review)
- **Below 2.5 points** → Pass (message allowed)

**Review workflow**:
1. Review only borderline cases (2.5-3.9 points)
2. Occasional spot checks on auto-bans
3. Monitor for new spam patterns
4. Adjust thresholds as needed

[Screenshot: Training Mode toggle in Settings]

---

## Impersonation Alerts

The Impersonation Alerts filter shows suspected impersonators - users who may be copying other members' profiles.

### What Triggers an Impersonation Alert?

An alert is created when:
- **Photo hash match** - User uploads same profile photo as existing member
- **Username similarity** - Username very similar to existing member (Levenshtein distance)
- **Combined signals** - New user with similar name AND photo

### Alert Display

Each alert shows:
- **Suspected impersonator** - New user's info
- **Original user** - Member being impersonated
- **Similarity score** - How similar (0-100%)
- **Evidence** - What triggered the alert (photo, username, both)
- **Risk level** - High, Medium, Low

### Reviewing Impersonation Alerts

**Step 1: Compare profiles**
- Look at both profile photos side-by-side
- Compare usernames character-by-character
- Check join dates (impersonator is newer)

**Step 2: Check message history**
- What has the suspected impersonator posted?
- Is it similar to original user's style?
- Are they soliciting DMs or money?

**Step 3: Decide**
- **Ban** - If it's a clear impersonation attempt
- **Dismiss** - If it's coincidental or legitimate (common photos, similar names)
- **Whitelist** - Add to impersonation whitelist (won't alert again)

### Common Impersonation Tactics

**Profile cloning**:
- Exact copy of profile photo
- Similar username (e.g., "@john_doe" → "@john_d0e" with zero)
- Similar display name

**Social engineering**:
- Impersonator DMs members pretending to be admin
- Requests funds, personal info, or external site visits
- Uses authority/trust of impersonated user

**Action**: Ban immediately if detected.

[Screenshot: Impersonation alert comparison view]

### Profile Scan Alerts Integration

Profile scanning also feeds into the Reports queue. When a user's profile is scanned and the outcome is **HeldForReview**, a Profile Scan Alert is created for admin review. These alerts show the user's profile score breakdown (rule-based + AI scoring on a 0.0-5.0 scale), AI reasoning, and detected signals. From the alert, you can **Allow**, **Kick**, or **Ban** the user.

For details on how profile scanning works, see **[Profile Scanning](08-profile-scanning.md)**.

---

## Status Filters

All report types share two status filters:

### Pending

- **Definition**: Reports awaiting your decision
- **Default view**: This is what you see when opening Reports page
- **Action needed**: Review and resolve

### All

- **Definition**: Every report regardless of status (includes resolved and dismissed)
- **Purpose**: Search across all reports, audit trail, verify past decisions
- **Use case**: Find specific report by user or content, review historical actions

[Screenshot: Status filter dropdown]

---

## Advanced Features

### Bulk Actions (Future Feature)

Currently planned but not implemented:
- Select multiple reports
- Bulk confirm spam
- Bulk mark as ham
- Bulk dismiss

**Workaround**: Review reports one-by-one (keyboard shortcuts help)

### Search Reports

Filter reports by:
- **User** - Find all reports for specific user
- **Date range** - Reports from specific timeframe
- **Score range** - Only 3.5+ points, for example
- **Chat** - Reports from specific Telegram group

**How to search**:
1. Use filter controls at top of page
2. Combine filters for precise results
3. Clear filters to reset

### Export Reports

Not currently available, planned for future:
- Export pending reports to CSV
- Export resolved reports for analysis
- Export impersonation alerts

**Workaround**: Use Analytics page for aggregate stats

---

## Integration with Messages Page

Reports and Messages pages are deeply integrated:

### From Reports → Messages

Click **View Message** on any report to:
- See message in full context (conversation thread)
- View user's entire message history
- Check if user has other flagged messages

### From Messages → Reports

Click **View Detection Report** on spam messages to:
- See the original report card
- Review your decision (if resolved)
- Re-evaluate if needed

**Cross-navigation** allows full context for every decision.

---

## Analytics and Metrics

Track your moderation performance in Analytics page:

### Metrics to Monitor

**Review Queue Stats**:
- **Pending count** - How many reports need review
- **Daily resolution rate** - How many you resolve per day
- **Average time to resolve** - How long reports sit pending

**Accuracy Metrics**:
- **False positive rate** - % of reports marked as ham
- **False negative rate** - Spam that slipped through (scored below 2.5 points)
- **ML training samples** - Total spam/ham samples collected

**Action Stats**:
- **Bans per day** - How many users banned
- **Dismissals** - How often you're unsure
- **Reverted decisions** - Changes after initial decision

**Goal**: False positive rate <10%, pending queue cleared daily.

[Screenshot: Analytics showing review queue metrics]

---

## Troubleshooting

### Too many reports (overwhelmed)

**Solutions**:
- **Enable AI Veto** - Reduces reports significantly (AI filters borderline cases)
- **Raise review threshold** - Increase from 2.5 to 3.0 (fewer reports)
- **Lower auto-ban threshold** - Decrease from 4.0 to 3.5 (more auto-bans, fewer reviews)
- **Disable sensitive checks** - Turn off Spacing Detection if too many false positives

### Too few reports (spam getting through)

**Solutions**:
- **Lower review threshold** - Decrease from 2.5 to 2.0 (catch more borderline spam)
- **Enable more checks** - Turn on Translation, Spacing Detection
- **Review auto-bans** - Check Messages page for 4.0+ point auto-bans to verify accuracy

### False positives in every batch

**Solutions**:
- **Review stop words list** - Remove overly broad keywords
- **Whitelist common domains** - Add legitimate sites to URL whitelist
- **Mark as ham consistently** - Train ML to recognize these patterns
- **Adjust check thresholds** - Fine-tune individual check sensitivity in Settings

### Impersonation alerts are all false positives

**Solutions**:
- **Whitelist common photos** - Default avatars, group logos
- **Increase similarity threshold** - Require higher match % for alerts
- **Disable photo matching** - If group uses common imagery
- **Add legitimate name variations** - Whitelist similar usernames

---

## Common Workflows

### Morning Review Routine

**Time**: 5-10 minutes

1. **Open Reports** → Moderation Reports
2. **Check pending count** - Should be <20 from overnight
3. **Review from top to bottom**:
   - Quick decisions on obvious spam/ham
   - Dismiss borderline cases for later
4. **Switch to Impersonation Alerts**
5. **Check for new alerts**
6. **Verify yesterday's actions** using the All status filter

### Weekly Audit

**Time**: 30 minutes

1. **Review last 7 days of resolved reports**:
   - Look for patterns in spam
   - Check if false positive rate increased
   - Identify new spam tactics
2. **Analyze impersonation attempts**:
   - Were they successful?
   - Did you catch them quickly?
3. **Adjust configuration**:
   - Add new stop words
   - Whitelist new domains
   - Update thresholds
4. **Review auto-bans in Messages page**:
   - Spot check 4.0+ point auto-bans
   - Unban any false positives

### Handling Backlog

**If you have 50+ pending reports**:

1. **Filter by score** - Start with 3.5+ points (likely spam)
2. **Bulk decisions** - Confirm obvious spam quickly
3. **Skip borderline** - Dismiss 2.5-2.8 point reports for now
4. **Set aside time** - 30 minutes focused review
5. **Enable AI Veto** - Prevent future backlog

---

## Related Documentation

- **[Messages Tab](01-messages.md)** - View messages in context
- **[Spam Detection Guide](03-spam-detection.md)** - Understand the detection checks and additive scoring
- **[URL Filtering](04-url-filtering.md)** - URL blocklist and threat intelligence
- **[Content Tester](05-content-tester.md)** - Test content detection without live messages
- **[AI Prompt Builder](06-ai-prompt-builder.md)** - Configure AI veto behavior
- **[Kick Escalation](07-kick-escalation.md)** - Automatic kick-to-ban escalation
- **[Profile Scanning](08-profile-scanning.md)** - Automated profile analysis and alerts
- **[Ban Celebration](09-ban-celebration.md)** - Fun ban notification messages
- **[First Configuration](../getting-started/02-first-configuration.md)** - Set up Training Mode

---

## Tips for Success

### Consistency is Key

- **Review daily** - Don't let pending queue grow
- **Be consistent** - Similar messages should get similar decisions
- **Trust the ML** - After 100+ samples, ML becomes reliable
- **Document patterns** - Keep notes on new spam tactics

### Use Your Judgment

- **Context matters** - A promotional message from a regular member may be okay
- **Group culture** - Some groups allow promotions, others don't
- **When in doubt, dismiss** - Better than wrong decision
- **Learn from mistakes** - If you unban someone, learn why they were flagged

### Optimize Your Workflow

- **Set a schedule** - Review at same time daily (habit formation)
- **Use keyboard shortcuts** - Faster than clicking
- **Filter aggressively** - Don't review everything at once
- **Enable AI Veto** - Best investment for reducing workload

---

**Master spam detection next**: Continue to **[Spam Detection Guide](03-spam-detection.md)**!
