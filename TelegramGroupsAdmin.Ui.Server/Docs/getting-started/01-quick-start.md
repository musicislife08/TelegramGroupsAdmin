# Quick Start Guide

Welcome to TelegramGroupsAdmin! This guide will help you get up and running in just 5 minutes.

## What is TelegramGroupsAdmin?

TelegramGroupsAdmin is a comprehensive spam detection and moderation tool for Telegram groups. It uses **11 different detection algorithms** working together to identify spam while minimizing false positives, and provides a powerful web interface for managing your Telegram communities.

### Key Features at a Glance

- **Multi-Algorithm Spam Detection** - 11 detection methods analyze each message
- **AI-Powered Review** - Optional GPT-4 veto reduces false positives by 80-90%
- **File Scanning** - Automatic malware detection for uploaded files
- **URL Filtering** - Block malicious domains with 540,000+ built-in blocklists
- **Self-Learning** - System improves over time from your feedback
- **Web Dashboard** - Browse messages, review detections, manage users
- **Automated Actions** - Auto-ban, review queue, or manual moderation

## Step 1: Create Your Account

### Registration

1. Navigate to your TelegramGroupsAdmin web interface (typically `http://localhost:5000` or your configured domain)
2. Click **Register** on the login page
3. Enter your email address and choose a strong password
4. Click **Create Account**

**Important**: The first user to register automatically becomes the **Owner** with full administrative access.

### Email Verification

1. Check your email inbox for a verification email (check spam folder if needed)
2. Click the verification link in the email
3. You'll be redirected back to the login page

**Note**: You must verify your email before you can log in (except for the first Owner account).

[Screenshot: Registration page]

---

## Step 2: First Login & Two-Factor Authentication

### Logging In

1. Enter your email and password
2. Click **Sign In**

### Setting Up 2FA (Required)

After your first login, you must set up two-factor authentication. This adds an extra layer of security to your account.

**To set up 2FA**:

1. Open your authenticator app (Google Authenticator, Authy, Microsoft Authenticator, etc.)
2. Scan the QR code displayed on screen
3. Enter the 6-digit code from your authenticator app
4. Click **Verify and Enable**

**Save Your Backup Codes**:
- You'll be shown a list of backup codes
- **Copy these and store them securely** (password manager, encrypted file, etc.)
- These codes can be used if you lose access to your authenticator app
- You won't be able to see them again!

[Screenshot: 2FA setup screen with QR code]

---

## Step 3: Link Your Telegram Account

Linking your Telegram account allows you to:
- Receive direct message notifications from the bot
- Verify your identity as an admin
- Enable personalized features

### How to Link

1. Navigate to **Profile** in the left sidebar
2. Scroll to the **Telegram Account Linking** section
3. Click **Generate Link Token**
4. A 6-character code will appear (valid for 15 minutes)
5. Open Telegram and start a chat with your TelegramGroupsAdmin bot
6. Send this command: `/link ABC123` (replace with your actual code)
7. The bot will confirm your account is linked
8. Refresh the Profile page to see your linked Telegram account

**Troubleshooting**:
- If the code expires, click **Generate Link Token** again to get a new one
- Make sure you're messaging the correct bot (check the bot username)
- The `/link` command only works in direct messages with the bot, not in groups

[Screenshot: Profile page showing Telegram linking section]

---

## Step 4: Verify Bot Status

Before configuring spam detection, let's make sure your bot is properly connected to your Telegram group.

### Dashboard Health Check

1. Click **Home** in the sidebar to view the dashboard
2. Look for the **Chat Health** section
3. You should see your Telegram group(s) listed with health status indicators:
   - **Green check mark** ✓ - Bot is healthy and functioning
   - **Yellow warning** ⚠️ - Bot has warnings (may need attention)
   - **Red X** ✗ - Bot is not functioning properly

### What Healthy Means

A healthy bot status indicates:
- Bot is connected and polling for messages
- Bot has admin permissions in the group
- Bot can send messages
- Bot can delete messages
- Bot can ban users
- Invite link is valid

### If Bot is Unhealthy

Common issues and solutions:
- **Bot not admin**: Make the bot an admin in your Telegram group
- **Missing permissions**: Grant the bot these permissions: Delete Messages, Ban Users, Invite Users via Link
- **Wrong chat ID**: Verify the `TELEGRAM__CHATID` configuration matches your group
- **Invalid token**: Check that `TELEGRAM__BOTTOKEN` is correct

[Screenshot: Dashboard showing Chat Health section with healthy status]

---

## Step 5: Understand the Dashboard

Let's quickly tour the main navigation:

### Main Navigation (All Users)

- **Home** - Dashboard with chat health, recent activity, quick stats
- **Messages** - Browse all messages from your Telegram groups (Telegram-style interface)
- **Tools** - Content Tester for testing spam detection
- **Documentation** - This documentation system
- **Help** - Quick help articles
- **Profile** - Your account settings, password, 2FA, Telegram linking

### Admin Navigation (GlobalAdmin/Owner Only)

- **Analytics** - Charts and graphs showing spam trends, detection rates, performance
- **Chat Management** - Manage monitored chats, configure per-chat settings
- **Users** - Telegram user management, ban users, add warnings
- **Reports** - Review queue for borderline spam detections
- **Audit Log** - Track all administrative actions
- **Settings** - System configuration (algorithms, URL filters, API keys, etc.)

[Screenshot: Full sidebar navigation]

---

## Step 6: Quick Tour of Key Features

### Messages Page - Your Message Browser

The **Messages** page is like a Telegram client inside your browser:
- **Left panel**: List of chats
- **Right panel**: Messages from selected chat
- **Infinite scroll**: Automatically loads more messages as you scroll
- **Filters**: Search by spam status, deleted messages, date range, user
- **Actions**: Click any message to view details, ban user, delete message, see edit history

**Tip**: This is where you'll spend most of your time browsing group activity.

### Reports Page - The Review Queue

The **Reports** page shows:
- **Moderation Reports**: Borderline spam (confidence 70-84) needing manual review
- **Impersonation Alerts**: Suspected impersonators (duplicate photos, similar usernames)

**Workflow**: Review each report and click **Confirm Spam** or **Mark as Ham**. Your feedback trains the machine learning algorithms!

### Settings Page - Configuration Hub

The **Settings** page has nested navigation with 4 groups:
- **System**: General, security, admin accounts, external services, logging, backups
- **Telegram**: Bot configuration, notifications
- **Content Detection**: Algorithms, tuning, OpenAI, URL filtering, file scanning
- **Training Data**: Stop words, training samples

**Don't worry**: You don't need to configure everything now. We'll walk through the essentials in the next guide.

---

## Next Steps - Your First Configuration

Now that you're logged in and familiar with the interface, it's time to configure your spam detection system!

**Recommended next steps**:

1. **[First Configuration Guide](02-first-configuration.md)** - Set up your first spam detection rules (5-10 minutes)
2. **[Messages Documentation](../features/01-messages.md)** - Learn how to use the message browser effectively
3. **[Reports Documentation](../features/02-reports.md)** - Master the review queue workflow

---

## Common First-Time Questions

### Do I need an OpenAI API key?

**No**, it's optional. The system has 11 detection algorithms, and most work without OpenAI. However, the OpenAI-powered features (GPT-4 verification, translation, vision-based spam detection) significantly improve accuracy and are worth enabling once you're comfortable with the basics.

**Cost**: ~$0.002 per message reviewed (OpenAI only processes borderline cases, not every message).

### How many groups can I monitor?

There's no hard limit, but performance is optimized for 1-10 groups. The system easily handles 5,000+ messages per day.

### Can I have multiple web admins?

Yes! Owners can invite additional admins with different permission levels:
- **Admin**: Chat-scoped moderation
- **GlobalAdmin**: Global moderation across all chats
- **Owner**: Full system access

See **[Web User Management](../admin/01-web-user-management.md)** for details.

### What if I accidentally ban a legitimate user?

You can unban users from the **Users** page. Additionally, enabling **Training Mode** (covered in the next guide) prevents automatic bans while you're learning the system.

### Is my data private?

Yes. All data is stored in your own PostgreSQL database. API calls to external services (OpenAI, VirusTotal, CAS) only send necessary information (message text, file hashes, user IDs) and don't include personally identifiable information beyond what's inherent in the message content.

---

## Troubleshooting

### Can't log in after registration

- Check your email for the verification link (check spam folder)
- Ensure you're entering the correct email and password
- Try resetting your password with the "Forgot Password" link

### 2FA code not working

- Ensure your device clock is synchronized (TOTP relies on accurate time)
- Try using a backup code if you have one
- Contact an existing Owner to reset your 2FA

### Bot not responding in Telegram

- Verify the bot is an admin in your group
- Check the bot has required permissions (Delete Messages, Ban Users)
- Ensure Privacy Mode is OFF for the bot (via BotFather)
- Check the Dashboard → Chat Health section for specific errors

### Dashboard shows no data

- Ensure the bot is active and polling for messages
- Send a test message in your Telegram group
- Check Settings → Telegram → Bot Configuration to verify bot token and chat ID are correct

---

## Get Help

- **In-App Documentation**: Click **Documentation** in the sidebar
- **Help Articles**: Click **Help** in the sidebar for quick reference
- **Audit Log**: If something unexpected happens, check the Audit Log to see what actions were taken

**Ready to configure spam detection?** Continue to **[First Configuration Guide](02-first-configuration.md)**!
