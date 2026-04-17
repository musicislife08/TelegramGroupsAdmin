# Cross-Chat Bans

When you ban a user in one of your Telegram groups, TGA automatically cleans up their messages across **all** your managed groups — not just the one where the ban happened.

## How It Works

1. A user gets banned (either by auto-ban, manual action, or through the Reports page)
2. TGA queues a background job to find and delete that user's messages in every group you manage
3. Messages are deleted within Telegram's 48-hour deletion window (Telegram API limitation)

You don't need to configure anything — this happens automatically for every ban.

## What Gets Cleaned Up

- All messages from the banned user across all your groups
- Only messages within the last 48 hours (Telegram's API limit for message deletion)
- Older messages remain visible but the user is still banned from all groups

## Where to See It

The [Audit Log](17-audit-log.md) records every ban action with details about which groups were affected and who issued the ban (system auto-ban, admin action, or Telegram-side moderation).
