# Getting Started

Welcome to TelegramGroupsAdmin! This guide will help you set up and configure your spam detection system.

## What is TelegramGroupsAdmin?

TelegramGroupsAdmin is a comprehensive spam detection and moderation tool for Telegram groups. It uses multiple detection methods working together to identify spam while minimizing false positives.

### Key Capabilities

- **Multi-Algorithm Spam Detection** - 9 different detection methods analyze each message
- **File Scanning** - Automatic malware detection for uploaded files
- **URL Filtering** - Block malicious domains with 540,000+ built-in blocklists
- **Self-Learning** - System improves over time from your feedback
- **Automated Actions** - Auto-ban, review queue, or manual moderation

### Performance

- Handles 500-5,000 messages per day easily
- Average detection time: 255ms per message
- Designed for groups with 10-1,000 members

## First-Time Setup

### 1. Access the Web Interface

After installation, visit the web interface (typically `http://localhost:5000` or your configured domain).

### 2. Create Your Admin Account

The first user to register becomes the **Owner** with full access to all features.

**To register**:
1. Click "Register" on the login page
2. Enter your email and password
3. You'll receive a verification email (check spam folder)
4. Click the verification link
5. Log in with your credentials

### 3. Set Up Two-Factor Authentication (Recommended)

1. After first login, you'll be prompted to set up 2FA
2. Scan the QR code with your authenticator app (Google Authenticator, Authy, etc.)
3. Enter the 6-digit code to confirm
4. Save your backup codes in a secure location

## Initial Configuration (First 24 Hours)

### Step 1: Enable Training Mode

**Why**: Collect spam examples without accidentally banning legitimate users

1. Navigate to **Settings** (left sidebar)
2. Click **Content Detection** → **Detection Algorithms**
3. Toggle **Training Mode** to ON
4. Click **Save All Changes**

**Result**: All spam detections will go to a review queue instead of auto-banning.

### Step 2: Enable Basic Detection

Start with simple, reliable detection methods:

1. In **Detection Algorithms** page, enable these algorithms:
   - ✅ **Stop Words Detection** - Keyword matching
   - ✅ **CAS Database** - Known spammer lookup
   - ✅ **Invisible Character Detection** - Unicode abuse detection
   - ✅ **URL/File Content** - Malicious domain blocking

2. Set conservative thresholds:
   - **Auto-Ban Threshold**: 95 (very high confidence required)
   - **Review Queue Threshold**: 70

3. Click **Save All Changes**

### Step 3: Configure URL Filtering

1. Navigate to **Settings** → **Content Detection** → **URL Filtering**
2. In the **Hard Block** panel (left), enable:
   - ✅ Block List Project - Phishing
   - ✅ Block List Project - Scam
3. In the **Whitelist** panel (center), add trusted domains your group uses frequently
4. Click **Save URL Filters**

### Step 4: Review Your First Detections

1. Navigate to **Reports** in the sidebar
2. View spam detections in the review queue
3. For each detection:
   - Click **View** to see the full message
   - Click **Confirm Spam** if it's actually spam
   - Click **Mark as Ham** if it's a false positive

**Important**: Your feedback trains the ML algorithms!

## After 100+ Training Samples

### Enable Machine Learning Algorithms

Once you have enough training data, enable smarter detection:

1. Go to **Settings** → **Content Detection** → **Detection Algorithms**
2. Enable:
   - ✅ **Similarity Detection (TF-IDF)** - Compares to known spam patterns
   - ✅ **Naive Bayes Classifier** - Statistical spam detection
3. Keep thresholds at default (75) for now
4. **Save All Changes**

### Use AI-Powered Threshold Tuning

Let the system recommend optimal settings:

1. Navigate to **Settings** → **Content Detection** → **Algorithm Tuning**
2. Select **Analysis Period**: Last 30 days
3. Click **Generate Recommendations**
4. Review each recommendation:
   - Shows current vs. recommended threshold
   - Displays expected false positive reduction
   - Shows model confidence (prefer ≥85%)
5. Click **Apply Threshold** on high-confidence recommendations

### Disable Training Mode

1. Go to **Settings** → **Content Detection** → **Detection Algorithms**
2. Toggle **Training Mode** to OFF
3. Reduce **Auto-Ban Threshold** from 95 to 85
4. **Save All Changes**

**Result**: System now auto-bans high-confidence spam.

## Production Configuration

### Recommended Settings for Active Moderation

Navigate to **Settings** → **Content Detection** → **Detection Algorithms**:

```
Training Mode: OFF
Auto-Ban Threshold: 85
Review Queue Threshold: 70
First Message Only: ON (check first 3 messages from new users)
Min Message Length: 10
```

**Enabled Algorithms**:
- Stop Words Detection
- CAS Database
- Similarity Detection (threshold: 0.75)
- Naive Bayes Classifier (threshold: 0.75)
- Invisible Character Detection
- URL/File Content Detection
- OpenAI Veto (if using OpenAI API)

### Optional: Enable OpenAI Veto

**What it does**: GPT-4 reviews borderline spam to reduce false positives by 80-90%

**Cost**: ~$0.002 per message reviewed (only runs on borderline cases)

**To enable**:
1. Navigate to **Settings** → **Content Detection** → **External Services Config**
2. Scroll to **OpenAI Integration**
3. Toggle **Veto Mode** to ON
4. Set **Veto Threshold** to 100
5. Optionally customize the **System Prompt** for your group's context
6. Click **Save**

## Understanding the Dashboard

### Main Dashboard (Home)

- **Recent Activity** - Live feed of messages and detections
- **Spam Statistics** - Detection rates and trends
- **Quick Actions** - Common moderation tasks

### Analytics Page

- **Detection Trends** - Graphs showing spam over time
- **Algorithm Performance** - Which algorithms catch the most spam
- **False Positive Rate** - Track accuracy improvements

### Messages Page

- **All Messages** - Browse all cached messages from your groups
- **Filters** - Search by user, date, spam status, deleted messages
- **Infinite Scroll** - Loads 50 messages at a time as you scroll

### Reports Page

- **Review Queue** - Borderline spam needing manual review
- **Spam Reports** - Confirmed spam detections
- **User Reports** - Flagged users and ban history

## Common Workflows

### Reviewing Spam Detections

1. Click **Reports** in sidebar
2. Select **Review Queue** tab
3. For each message:
   - Read the full message and context
   - Check which algorithms flagged it
   - Review confidence scores
   - Click **Confirm Spam** or **Mark as Ham**

### Manually Banning a User

1. Navigate to **Users** page
2. Search for the user by name or Telegram ID
3. Click the user to view their profile
4. Click **Ban User** button
5. Choose ban type:
   - **Permanent Ban** - User cannot rejoin
   - **Temporary Ban** - Specify duration (hours, days, weeks)
   - **Delete All Messages** - Remove all their messages (optional)

### Adding Custom Blocked Domains

1. Go to **Settings** → **Content Detection** → **URL Filtering**
2. In the **Hard Block** panel, scroll to **Manual Domains**
3. Enter domains (one per line):
   ```
   evil-site.com
   scam-domain.net
   *.suspicious-pattern.org
   ```
4. Supports wildcards: `*.example.com` blocks all subdomains
5. Click **Save URL Filters**

### Whitelisting Trusted Domains

1. Go to **Settings** → **Content Detection** → **URL Filtering**
2. In the **Whitelist** panel (center), add trusted domains:
   ```
   yourcompany.com
   github.com
   wikipedia.org
   ```
3. Click **Save URL Filters**

**Result**: These domains will never be flagged, even if they appear on blocklists.

## Tips for Best Results

### Start Conservative

- Use high thresholds (95) during training
- Enable only basic algorithms at first
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
- Review algorithm performance

### Use Custom Prompts (OpenAI)

Tailor spam detection to your group's context:

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

- **[Spam Detection Guide](02-spam-detection.md)** - Learn about each detection algorithm
- **[File Scanning Guide](03-file-scanning.md)** - Configure malware detection
- **[URL Filtering Guide](04-url-filtering.md)** - Master domain blocking
- **[Settings Reference](05-configuration.md)** - Complete settings documentation

## Need Help?

- **Review Queue not working?** Check that Training Mode is OFF
- **Too many false positives?** Enable OpenAI Veto or increase thresholds
- **Spam getting through?** Enable more algorithms and lower thresholds
- **Bot not responding?** Verify bot is admin in your Telegram group with proper permissions
