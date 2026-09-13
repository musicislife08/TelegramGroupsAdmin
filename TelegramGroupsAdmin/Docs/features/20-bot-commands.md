# Bot Commands

The bot responds to slash commands typed in any group it manages, and a few work in a private chat with the bot. Type `/help` in a group to see the commands available to **you** — the list is filtered by your permission tier in that chat.

## Who Can Run What

Commands are gated by the same tiers the web app uses — see [How Telegram-Side Permissions Are Resolved](../admin/01-web-user-management.md#how-telegram-side-permissions-are-resolved). In short:

- **Everyone** can run the public commands below
- **Admin commands** need you to be a Telegram admin or creator of that chat, or to have a Telegram account linked to a GlobalAdmin/Owner web account

## Public Commands

| Command | What it does | Notes |
|---------|--------------|-------|
| `/help` | Lists the commands you can use here | Response is removed after 30 seconds |
| `/report` (reply to a message) | Sends the message to the [Reports](02-reports.md) queue for admin review | One pending report per message; the command stays visible as confirmation |
| `/invite` | Posts the chat's invite link | Groups only. The command and its response are removed after 30 seconds. Can be disabled globally. |
| `/mystatus` | Shows your trust status, active and recent warnings, account created / last active dates, and whether bot DMs are enabled | Private by design: if you run it in a group, the bot deletes your command and DMs you the answer. If your DMs to the bot are closed, it tells you so in the chat. |
| `/link <token>` | Links your Telegram account to your web account | Get the token from your [Profile](../user/01-profile-security.md#telegram-account-linking) page; the command message is deleted |
| `/start` | Starts a private conversation with the bot | DM only (ignored in groups). Enables DM notifications, delivers anything queued for you, and handles the "Read Rules" / entrance-exam deep links from the welcome system. |

## Admin Commands

| Command | What it does | Notes |
|---------|--------------|-------|
| `/ban` (reply) · `/ban @username` · `/ban <user_id>` · `/ban <name>` | Bans the user from **all** managed chats | Triggers [cross-chat cleanup](15-cross-chat-bans.md) and, if enabled, a [ban celebration](09-ban-celebration.md). The command message is deleted. See [What the banned user sees](#what-the-banned-user-sees). |
| `/unban` (reply) | Removes the ban | Command stays visible as confirmation |
| `/tempban` (reply) `<duration> [reason]` | Bans the user and automatically lifts the restriction when the duration ends | Default duration **1h** if omitted or unparseable. Command message is deleted. |
| `/mute` (reply) `<duration> [reason]` | Mutes the user and automatically unmutes when the duration ends | Default duration **5m** if omitted or unparseable. Cannot mute chat admins. Command message is deleted. |
| `/warn` (reply) `[reason]` | Records a warning against the user | By default the **third** warning triggers an automatic ban. The reply shows the running total. Command stays visible. |
| `/spam` (reply) | Marks the message as spam, deletes it, and bans the sender | Also feeds the message into spam training. Works even on trusted users (trust only bypasses *automatic* detection). Refuses to act on messages from chat admins. Command message is deleted. |
| `/delete` (reply) | Deletes the replied-to message | Both messages are removed |
| `/trust` (reply) | Toggles the user's trusted status | Trusted users bypass automatic spam detection in every chat and skip Security on Join checks; with **Auto-admit Trusted Users** enabled they also skip the welcome flow. Run it again to remove trust. The `/trust <username>` form is advertised by the bot but not yet supported — reply to one of the user's messages instead. Command stays visible as confirmation. |

### Duration Format

`/mute` and `/tempban` accept a number followed by a unit:

| Suffix | Meaning | Example |
|--------|---------|---------|
| `m` | minutes | `30m` |
| `h` | hours | `24h` |
| `d` | days | `7d` |
| `w` | weeks | `2w` |
| `M` | months (30 days) | `1M` |
| `y` | years (365 days) | `1y` |

Note that `m` is minutes and `M` is months. Anything the bot can't parse falls back to the command's default.

## What the Banned User Sees

`/ban` and the ban buttons on admin notifications send the banned user a **direct message** with the chat name, the reason, and how many chats were affected, plus a note that they can contact the chat admins to appeal. Nothing is posted in the group — the user has already been removed and couldn't read a chat mention anyway. If the user has never started a DM with the bot, the notice cannot be delivered; it is logged, but the ban still applies. Automatic spam bans don't send this notice.

## Housekeeping

Moderation commands (`/ban`, `/tempban`, `/mute`, `/spam`, `/delete`) delete the command message itself to keep the chat tidy. `/report`, `/warn`, `/unban`, and `/trust` leave the command visible so other members and admins can see what happened.

## Related Documentation

- **[Web User Management](../admin/01-web-user-management.md)** — Permission tiers and how they map to Telegram
- **[Reports](02-reports.md)** — Where `/report` submissions land
- **[Spam Detection](03-spam-detection.md)** — How trust and auto-trust interact with detection
- **[Cross-Chat Bans](15-cross-chat-bans.md)** — What happens after a ban
- **[Kick Escalation](07-kick-escalation.md)** — Welcome-timeout kicks and their auto-ban threshold
