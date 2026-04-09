# External Integrations

TGA connects to several external services to provide its detection and moderation capabilities. This page explains what each service does for you, whether you need to set it up, and what happens if it's unavailable.

## OpenAI (or Compatible AI Provider)

**What it does for you:** Powers the AI Veto system (reduces false positives by 80-90%), image spam detection, video spam detection, and the AI Prompt Builder for custom detection rules.

**Do you need to configure it?** Optional but recommended. Go to **Settings** -> **System** -> **AI Providers** and enter your API key.

**What happens if it's unavailable:** AI-powered checks are skipped — TGA falls back to its rule-based and ML detection checks. No messages are missed, but you lose the AI layer's accuracy boost.

**Cost:** Approximately $0.002 per message reviewed. The AI only runs on borderline cases, not every message.

## VirusTotal

**What it does for you:** Scans files shared in your groups against 70+ antivirus engines in the cloud. Catches malware, ransomware, and other threats that local scanning might miss.

**Do you need to configure it?** Optional. Get a free API key from [virustotal.com](https://www.virustotal.com) and enter it in **Settings** -> **System** -> **VirusTotal**.

**Free tier limits:** 500 lookups per day, 4 per minute. TGA optimizes usage by checking file hashes first (cached for 24 hours) — if someone shares the same file twice, it only counts as one lookup.

**What happens if it's unavailable:** File scanning falls back to ClamAV only (local scanning). If neither is configured, file scanning is disabled entirely.

## ClamAV

**What it does for you:** Scans files locally on your server for malware. This is the first line of defense — fast and free, with no API limits.

**Do you need to configure it?** Only if you run ClamAV alongside TGA (e.g., in a Docker sidecar). Configure the connection in **Settings** -> **System** -> **ClamAV**.

**How it works with VirusTotal:** ClamAV runs first (Tier 1, local). If ClamAV doesn't flag the file, VirusTotal checks it in the cloud (Tier 2). If either flags it, the file is treated as malicious.

**What happens if it's unavailable:** File scanning relies on VirusTotal only, or is disabled if neither is configured.

## CAS (Combot Anti-Spam)

**What it does for you:** Checks every new user who joins your groups against the [CAS global spam database](https://cas.chat). This catches known spammers the moment they join, before they can send a single message.

**Do you need to configure it?** No — CAS is enabled by default as a join-time check. No API key required.

**What happens if it's unavailable:** The CAS check is skipped for that join event. TGA still evaluates the user through its other detection checks. CAS uses a fail-open design — a temporary outage won't block legitimate users from joining.

## Block List Project

**What it does for you:** Provides a database of 540,000+ known malicious domains across categories: Phishing, Scam, Malware, Ransomware, Fraud, Abuse, Piracy, Ads, Tracking, and Redirect. When someone posts a URL from one of these domains, TGA flags it.

**Do you need to configure it?** Choose which categories to enable in **Settings** -> **Content Detection** -> **URL Filtering**. Recommended categories:
- **Essential** (low false-positive risk): Phishing, Scam, Malware, Ransomware
- **Moderate** (some false positives): Fraud, Abuse
- **Aggressive** (higher false positives): Piracy, Ads, Tracking, Redirect

**Updates:** TGA automatically syncs the latest blocklist data weekly (Sunday at 3 AM by default). You can trigger a manual sync from **Settings** -> **System** -> **Background Jobs**.

**What happens if it's unavailable:** The last downloaded blocklist data continues to work. URL filtering only loses coverage if the blocklist hasn't been updated in a very long time.

## WTelegram User API

**What it does for you:** Connects to Telegram as a real user account (yours), unlocking features that the Bot API cannot provide:
- **[Profile Scanning](../features/08-profile-scanning.md)** — Inspect full user profiles (bio, photos, linked channels) when someone joins
- **[Send As Admin](../features/10-send-as-admin.md)** — Send messages as your personal Telegram account instead of the bot
- **[Per-User Messages](../features/11-per-user-messages.md)** — Resolve full user details for cross-chat history

**Do you need to configure it?** Optional but unlocks significant capabilities. Go to **Settings** -> **Telegram** -> **User API** and enter your Telegram phone number. You'll need to complete SMS verification to establish the session.

**Session management:** Your Telegram session is stored securely in the database and persists across TGA restarts. You only need to authenticate once unless you log out or your session expires.

**What happens if it's unavailable:** Features that require the User API (profile scanning, send-as-admin) are disabled. The bot continues to work normally for all other detection and moderation tasks.
