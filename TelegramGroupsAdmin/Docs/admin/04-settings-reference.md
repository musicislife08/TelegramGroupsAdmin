# Settings Reference

This page helps you find the right settings section for what you want to configure. Navigate to **Settings** in the sidebar to access the settings page.

Settings marked with **per-chat** can be customized for individual groups via the Chat Config Modal on the Chat Management page. All others are global.

## System

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **General** | App display name, timezone, general system behavior | — |
| **Security** | Two-factor authentication enforcement, session timeout | [Getting Started](../01-getting-started.md) |
| **Admin Accounts** | Manage web admin users, roles, invitations | [Web User Management](01-web-user-management.md) |
| **AI Providers** | OpenAI API key, model selection, provider configuration | [AI Prompt Builder](../features/06-ai-prompt-builder.md) |
| **Email** | SendGrid API key, sender address for verification emails | — |
| **ClamAV** | Local antivirus scanner connection settings | [Integrations](05-integrations.md) |
| **VirusTotal** | API key for cloud-based file and URL scanning | [Integrations](05-integrations.md) |
| **Logging** | Log levels, Seq integration for structured logging | — |
| **Background Jobs** | Enable/disable and schedule automated tasks | [Background Jobs](../features/13-background-jobs.md) |
| **Backup Configuration** | Encryption, retention strategy, backup directory | [Backup & Restore](../features/14-backup-restore.md) |

## Telegram

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Bot Configuration** | Bot token, bot behavior settings | [Getting Started](../01-getting-started.md) |
| **User API** | WTelegram credentials for profile scanning and send-as-admin | [Integrations](05-integrations.md) |
| **Service Messages** | Control which Telegram service messages the bot handles | — |

## Moderation

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Ban Celebration** | Celebratory GIFs and captions when spammers are banned | [Ban Celebration](../features/09-ban-celebration.md) |

## Notifications

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Web Push** | Browser push notification preferences | [Notifications](../features/18-dm-notifications.md) |

## Content Detection

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Detection Algorithms** | Enable/disable individual checks, Training Mode, thresholds **per-chat** | [Spam Detection](../features/03-spam-detection.md) |
| **AI Integration** | OpenAI Veto, image/video analysis, custom system prompt **per-chat** | [AI Prompt Builder](../features/06-ai-prompt-builder.md) |
| **URL Filtering** | Blocklists, whitelists, manual domains **per-chat** | [URL Filtering](../features/04-url-filtering.md) |
| **File Scanning** | ClamAV and VirusTotal file scanning toggles | [Spam Detection](../features/03-spam-detection.md) |

## Training Data

| Subsection | What's In There | Learn More |
|------------|----------------|------------|
| **Stop Words Library** | Manage stop words, generate recommendations | [Stop Word Recommendations](../features/16-stop-word-recommendations.md) |
| **Training Samples** | View and manage spam/ham training examples | [Spam Detection](../features/03-spam-detection.md) |
