# Chat Health Monitoring

TGA automatically monitors whether your bot is working correctly in each of your Telegram groups. You can see the health status at a glance on the **Chat Management** page.

## What the Colors Mean

Each group shows a color-coded health indicator:

| Color | Status | What It Means |
|-------|--------|---------------|
| Green | Healthy | Everything is working — your bot has all the permissions it needs |
| Yellow | Warning | Something needs attention — the bot is missing a permission or can't perform an action |
| Red | Error | The bot cannot reach this group — it may have been removed or kicked |
| Gray | Unknown | The group hasn't been checked yet (happens briefly after TGA starts up) |

Click **View Details** on any group to see the complete health report, including which specific checks passed or failed and when the last successful check occurred.

## What Gets Checked

TGA runs these checks on each group every 30 minutes:

| Check | Why It Matters |
|-------|---------------|
| **Bot is reachable** | Can the bot connect to the group at all? If the bot was removed or the group was deleted, this fails. |
| **Bot is an admin** | The bot needs admin status to moderate messages and manage users. |
| **Can delete messages** | Required for removing spam and cleaning up after bans. |
| **Can ban users** | Required for auto-ban and manual moderation actions. |
| **Can promote members** | Required for managing user permissions during welcome exam and profile scan gates. |
| **Invite link is valid** | TGA checks that the group's invite link is accessible for user verification features. |

## The 3-Strike Rule

If TGA cannot reach a group three times in a row, it automatically marks that group as **inactive**. This prevents TGA from wasting resources trying to moderate a group it can't access.

**What inactive means:**
- TGA stops trying to moderate messages in that group
- The group appears dimmed on the Chat Management page
- Health checks continue at a reduced frequency

**How to reactivate:**
1. Fix the underlying issue (re-add the bot to the group, restore admin permissions)
2. Go to **Chat Management** and trigger a manual health check
3. Once the bot can reach the group again, TGA automatically reactivates it

## Health-Gated Moderation

TGA will not moderate a group it cannot confirm is healthy. This is a safety feature:

- If health status is Unknown (e.g., right after TGA restarts), moderation pauses for that group until the first health check completes
- This prevents TGA from attempting actions it might not have permission to perform
- Moderation resumes automatically once the health check confirms the bot has the right permissions

## Running a Manual Health Check

You don't need to wait for the automatic 30-minute cycle. To check a group immediately:

1. Navigate to **Chat Management** in the sidebar
2. Find the group you want to check
3. Click the **refresh** button in the actions column
4. The health status updates within a few seconds

This is useful after making permission changes in Telegram to verify TGA has picked them up.

## Related

- **[Chat Management](02-chat-management.md)** — Managing your groups, adding new chats, per-chat configuration
- **[Settings → System → Background Jobs](04-settings-reference.md)** — Adjust the health check schedule
