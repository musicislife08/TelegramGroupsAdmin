# Notifications

TGA can notify you about important events through three channels: **Telegram DM**, **Email**, and **Web Push** (the in-app notification bell). Each channel can be independently configured per event type, so you get exactly the alerts you want, where you want them.

## Setting Up Telegram DM Notifications

Before TGA can send you Telegram DMs, two steps are required:

### Step 1: Link Your Telegram Account

1. In the TGA web app, go to your **Profile** page
2. Find the **Linked Telegram Accounts** section
3. Click **Generate Token** — this creates a one-time security token
4. Copy the token
5. Open a private chat with your TGA bot in Telegram
6. Send `/link <token>` (paste the token after /link)
7. The bot confirms the link and deletes the message for security

This connects your Telegram identity to your web admin account, so TGA knows where to send your DMs.

### Step 2: Start the Bot

In your private chat with the TGA bot, send `/start`. This is a **Telegram API requirement** — bots can only send DMs to users who have initiated a conversation first.

Once both steps are complete, the Telegram DM channel becomes available in your notification preferences.

## What Triggers Notifications

| Event | Who Gets Notified |
|-------|-------------------|
| **Spam Detected** | Chat admins + global admins |
| **Spam Auto-Deleted** | Chat admins + global admins |
| **User Banned** | Chat admins + global admins |
| **Message Reported** | Chat admins + global admins |
| **Malware Detected** | Chat admins + global admins |
| **Exam Failed** | Admins for that chat |
| **Profile Scan Alert** | Chat admins + global admins |
| **Chat Admin Changed** | Owners only |
| **Chat Health Warning** | Owners only |
| **Backup Failed** | Owners only |

Some Telegram DM notifications include **action buttons** — for example, a Profile Scan Alert DM lets you tap Ban, Kick, or Allow directly from Telegram without opening the web app.

## Configuring Your Preferences

Go to your **Profile** -> **Notification Preferences** to control what you receive:

- **Telegram DM** tab — Select which events send you a Telegram message (requires linked account)
- **Email** tab — Select which events send you an email, with optional digest batching
- **Web Push** tab — Select which events appear in the notification bell and trigger browser notifications

Each admin configures their own preferences independently. You can enable spam alerts via Telegram DM but only receive backup failures by email — whatever combination works for you.

## The Pending Queue

If TGA tries to DM you but can't (you haven't run `/start` yet, or you temporarily blocked the bot), the message is **queued automatically**. When you run `/start` again, all pending notifications are delivered immediately.

Queued messages expire after 30 days if undelivered.

## Telegram Admins Without Web Accounts

Telegram group admins who don't have a linked web account still receive DM notifications automatically — as long as they've run `/start` with the bot. They receive all notifications for groups they admin, delivered exclusively via Telegram DM.

## The Notification Bell

The bell icon in the top navigation bar shows your web push notifications with a red unread count badge. Click it to see recent events, mark them as read, or clear them. These update in real-time.
