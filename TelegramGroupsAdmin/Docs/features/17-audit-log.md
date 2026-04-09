# Audit Log

The Audit Log records every significant action taken in TGA — whether by an admin through the web interface, by the bot automatically, or by a Telegram user. Navigate to **Audit** in the sidebar to access it.

This feature requires GlobalAdmin or Owner permissions.

## Two Logs, Two Perspectives

### Web Admin Log

Tracks everything that happens in the TGA web interface:

| Event Types | Examples |
|-------------|----------|
| **Login / Password Changed** | Admin authentication activity |
| **System Config Changed** | Any settings modification |
| **Permission Changed** | Role or access changes for admin accounts |
| **User Registration / Deletion** | Admin account lifecycle |
| **Invite Created / Revoked** | Admin invitation management |
| **TOTP events** | Two-factor authentication setup and changes |
| **Data Export** | When data is exported from the system |

Each entry shows:
- **Timestamp** — When it happened
- **Event Type** — Color-coded chip for quick scanning
- **Actor (Who)** — Which admin or SYSTEM performed the action
- **Target** — What or who was affected
- **Details** — Additional context (configuration values, reason, etc.)

### Telegram Moderation Log

Tracks all moderation actions across your Telegram groups:

| Action Type | Color | What It Means |
|-------------|-------|---------------|
| **Ban** | Red | User was permanently or temporarily banned |
| **Warn** | Yellow | User received a warning |
| **Mute** | Yellow | User was muted |
| **Trust** | Green | User was marked as trusted |
| **Unban** | Blue | User's ban was lifted |

Each entry shows:
- **Telegram User** — Avatar, display name, and Telegram ID
- **Issued By** — Who triggered the action: "Bot Protection" (automatic), "Auto-system" (rule-based), a web admin email, or a Telegram user
- **Reason** — Why the action was taken (if provided)
- **Expires At** — When the action expires, or "Permanent"

## Filtering

Both logs support filtering to help you find what you're looking for:

- **Web Admin Log** — Filter by event type, actor, or target user
- **Telegram Moderation Log** — Filter by action type, Telegram user ID, or issuer

Pagination options: 25, 50, or 100 entries per page.

## Why It Matters

The audit log gives you accountability and transparency. If something unexpected happens — a setting was changed, a user was banned incorrectly, or an admin account was compromised — you can trace exactly what happened, when, and who did it.
