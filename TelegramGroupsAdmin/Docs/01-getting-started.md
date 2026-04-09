# Getting Started

Welcome to TelegramGroupsAdmin! This guide will help you set up and configure your content detection and moderation system.

## What is TelegramGroupsAdmin?

TelegramGroupsAdmin is a comprehensive content detection and moderation tool for Telegram groups. It uses multiple detection methods working together to identify spam while minimizing false positives.

### Key Capabilities

- **Multi-Check Content Detection** - 14 content detection checks analyze each message using additive scoring
- **File Scanning** - Automatic malware detection for uploaded files (ClamAV + VirusTotal)
- **URL Filtering** - Block malicious domains with 540,000+ built-in blocklists
- **Self-Learning** - System improves over time from your feedback
- **Automated Actions** - Auto-ban, review queue, or manual moderation
- **Kick Escalation** - Exponential backoff cooldowns for repeatedly kicked users, auto-escalating to permanent bans
- **Profile Scanning** - AI-powered join security that inspects user profiles on join using the WTelegram User API
- **Ban Celebration** - Posts celebratory GIFs with witty captions when spammers get banned
- **Send As Admin** - Bot/Me toggle lets you send messages as your personal Telegram account via the WTelegram User API
- **Per-User Messages** - Cross-chat message history for any user across all monitored groups

### Performance

- Handles thousands of messages per day easily
- Average detection time: 255ms per message
- Designed for groups with 10-1,000 members

## First-Time Setup

### 1. Access the Web Interface

After installation, visit the web interface (typically `http://localhost:8080` or your configured domain).

### 2. Create Your Admin Account

The first user to register becomes the **Owner** with full access to all features.

**To register**:
1. Click "Register" on the login page
2. Enter your email and password
3. You'll receive a verification email (check spam folder)
4. Click the verification link
5. Log in with your credentials

### 3. Configure Your Bot Token

1. Navigate to **Settings** (left sidebar)
2. Click **Telegram** -> **Bot Configuration** -> **General**
3. Enter your Telegram Bot Token (from [@BotFather](https://t.me/BotFather))
4. Click **Save**

### 4. Set Up Two-Factor Authentication (Required)

1. After first login, you are required to set up 2FA (Owner accounts can override this for other admins)
2. Scan the QR code with your authenticator app (Google Authenticator, Authy, etc.)
3. Enter the 6-digit code to confirm
4. Save your backup codes in a secure location

## Understanding the Dashboard

### Main Dashboard (Home)

- **Recent Activity** - Live feed of messages and detections
- **Spam Statistics** - Detection rates and trends
- **Quick Actions** - Common moderation tasks

### Analytics Page

- **Detection Trends** - Graphs showing spam over time
- **Algorithm Performance** - Which checks catch the most spam
- **False Positive Rate** - Track accuracy improvements

### Messages Page

- **All Messages** - Browse all cached messages from your groups
- **Filters** - Search by user, date, spam status, deleted messages
- **Infinite Scroll** - Loads 50 messages at a time as you scroll
- **Send As Admin** - Bot/Me toggle to send messages as your personal account

### Reports Page

Use the **Type** dropdown to filter reports:

- **Moderation Reports** - Spam detections needing review (borderline scores, auto-deleted confirmations)
- **Impersonation Alerts** - Users flagged for impersonating admins or other members
- **Exam Reviews** - Failed welcome exam attempts requiring admin review
- **Profile Scan Alerts** - Suspicious profiles detected on join (see [Profile Scanning](features/08-profile-scanning.md))

Use the **Status** filter to show only pending items or all history.

## Initial Configuration (First 24 Hours)

### Step 1: Enable Training Mode

**Why**: Collect spam examples without accidentally banning legitimate users

1. Navigate to **Settings** (left sidebar)
2. Click **Content Detection** -> **Detection Algorithms**
3. Toggle **Training Mode** to ON
4. Click **Save All Changes**

**Result**: All spam detections will go to a review queue instead of auto-banning.

### Step 2: Enable Basic Detection

Start with simple, reliable detection methods:

1. In **Detection Algorithms** page, enable these checks:
   - **Stop Words Detection** - Keyword matching
   - **CAS Database** - Known spammer lookup
   - **Invisible Character Detection** - Unicode abuse detection
   - **URL Blocklist** - Malicious domain blocking

2. The default thresholds (Auto-Ban: 4.0, Review Queue: 2.5) are well-calibrated — no changes needed

3. Click **Save All Changes**

### Step 3: Configure URL Filtering

1. Navigate to **Settings** -> **Content Detection** -> **URL Filtering**
2. In the **Hard Block** panel (left), enable:
   - Block List Project - Phishing
   - Block List Project - Scam
3. In the **Whitelist** panel (center), add trusted domains your group uses frequently
4. Click **Save URL Filters**

### Step 4: Connect an AI Provider (Optional)

Connecting an AI provider unlocks several powerful features: AI Veto (reduces false positives by 80-90%), Image Spam Detection, Video Spam Detection, and the AI Prompt Builder.

1. Navigate to **Settings** -> **System** -> **AI Providers**
2. Enter your API key (OpenAI or compatible provider)
3. Click **Save**

You can enable AI-powered checks later in **Settings** -> **Content Detection** -> **AI Integration**.

### Step 5: Review Your First Detections

1. Navigate to **Reports** in the sidebar
2. View spam detections in the review queue
3. For each detection:
   - Click **View** to see the full message
   - Click **Delete as Spam** if it's actually spam
   - Click **Dismiss** if it's a false positive

**Important**: Your feedback trains the ML algorithms!

## After 100+ Training Samples

### Enable Machine Learning Algorithms

Once you have enough training data, enable smarter detection:

1. Go to **Settings** -> **Content Detection** -> **Detection Algorithms**
2. Enable:
   - **Similarity Detection (TF-IDF)** - Compares to known spam patterns
   - **Naive Bayes Classifier** - Statistical spam detection
3. Keep thresholds at default for now
4. **Save All Changes**

### Disable Training Mode

1. Go to **Settings** -> **Content Detection** -> **Detection Algorithms**
2. Toggle **Training Mode** to OFF
3. **Save All Changes**

**Result**: System now auto-bans high-confidence spam.

## Production Configuration

### Recommended Settings for Active Moderation

Navigate to **Settings** -> **Content Detection** -> **Detection Algorithms**:

```
Training Mode: OFF
Auto-Ban Threshold: 4.0 points
Review Queue Threshold: 2.5 points
First Message Only: ON (check first 3 messages from new users)
Min Message Length: 10
```

**Enabled Checks**:
- Stop Words Detection
- CAS Database
- Similarity Detection
- Naive Bayes Classifier
- Spacing Analysis
- Invisible Character Detection
- URL Blocklist
- SEO Scraping Detection
- Threat Intelligence
- Channel Reply Detection
- OpenAI Veto (if using OpenAI API)
- Image Spam Detection (if using OpenAI API)
- Video Spam Detection (if using OpenAI API)
- File Scanning (if ClamAV/VirusTotal configured)

### Optional: Enable OpenAI Veto

**What it does**: AI reviews borderline spam to reduce false positives by 80-90%

**Cost**: ~$0.002 per message reviewed (only runs on borderline cases)

**To enable**:
1. Navigate to **Settings** -> **System** -> **AI Providers**
2. Enter your OpenAI API key
3. Go to **Settings** -> **Content Detection** -> **AI Integration**
4. Scroll to **OpenAI Integration**
5. Toggle **Veto Mode** to ON
6. Optionally customize the **System Prompt** for your group's context
7. Click **Save**

## Common Workflows

### Reviewing Spam Detections

1. Click **Reports** in sidebar
2. Use the **Type** dropdown to filter by report type
3. For each message:
   - Read the full message and context
   - Check which checks flagged it
   - Review point scores
   - Click **Delete as Spam** or **Dismiss**

### Manually Banning a User

1. Navigate to **Users** page
2. Search for the user by name or Telegram ID
3. Click the user to view their profile
4. Click **Ban User** button
5. Choose ban type:
   - **Permanent Ban** - User cannot rejoin
   - **Temporary Ban** - Specify duration (hours, days, weeks)
   - **Delete All Messages** - Remove all their messages (optional)

### Viewing a User's Cross-Chat History

1. Navigate to **Users** page and click on a user
2. In the User Detail Dialog, click **View Messages**
3. Browse all messages from that user across every monitored group

## Tips for Best Results

### Start Conservative

- Use high thresholds (5.0 points) during training
- Enable only basic checks at first
- Review all detections manually
- Build confidence before automating

### Collect Quality Training Data

- Mark spam/ham consistently
- Review borderline cases carefully
- Aim for 100+ spam and 100+ ham samples
- Quality > quantity

### Monitor Performance

- Check Analytics page weekly
- Look for false positive trends
- Adjust thresholds if needed
- Review check performance

### Use Custom Prompts (OpenAI)

Tailor AI spam detection to your group's context using the [AI Prompt Builder](features/06-ai-prompt-builder.md). Navigate to **Settings** -> **Content Detection** -> **AI Integration** to customize the system prompt:

**Example for crypto trading group**:
```
This is a cryptocurrency trading community. Allow:
- Technical analysis discussions
- Price predictions with reasoning
- News about Bitcoin/Ethereum/altcoins
- Trading strategy questions

Block:
- "Join my VIP signal group" promotions
- Guaranteed profit claims
- Pump and dump coordination
- Phishing links
```

## Next Steps

- **[Spam Detection Guide](02-spam-detection.md)** - Learn about each content detection check
- **[URL Filtering Guide](features/04-url-filtering.md)** - Master domain blocking
- **[Best Practices](04-best-practices.md)** - Recommended workflows and tips
- **[Kick Escalation](features/07-kick-escalation.md)** - Configure graduated kick penalties
- **[Profile Scanning](features/08-profile-scanning.md)** - AI-powered join security
- **[Ban Celebration](features/09-ban-celebration.md)** - Celebratory GIFs on bans
- **[Send As Admin](features/10-send-as-admin.md)** - Send messages as your personal account
- **[Per-User Messages](features/11-per-user-messages.md)** - Cross-chat user message history
- **[Analytics Dashboard](features/12-analytics.md)** - Understand your spam detection performance
- **[Backup & Restore](features/14-backup-restore.md)** - Protect your data with encrypted backups
- **[Audit Log](features/17-audit-log.md)** - Track every action taken in your system
- **[Settings Reference](admin/04-settings-reference.md)** - Find any setting quickly
- **[External Integrations](admin/05-integrations.md)** - Understand the services TGA connects to

## Need Help?

- **Auto-ban not working?** Check that Training Mode is OFF and your thresholds are set
- **Too many false positives?** Enable OpenAI Veto or increase thresholds
- **Spam getting through?** Enable more checks and lower thresholds
- **Bot not responding?** Verify bot token is configured in Settings -> Telegram -> Bot Configuration and bot is admin in your Telegram group with proper permissions
