# Backup & Restore

TGA can create encrypted backups of your entire database and restore from them if needed. Navigate to **Settings** -> **System** -> **Backup Configuration** to get started.

## Setting Up Backups

Before creating your first backup, you need to set an encryption passphrase:

1. Go to **Settings** -> **System** -> **Backup Configuration**
2. Set your encryption passphrase — **save this somewhere safe**, you'll need it to restore
3. Configure your backup directory path
4. Click **Save Configuration**

All backups are encrypted with AES-256-GCM. Without the passphrase, backup files cannot be read.

## What's Included

A backup contains everything TGA needs to fully restore your instance:

- All users and their profiles
- Message history across all groups
- Spam detection configuration and thresholds
- Bans, warnings, and moderation history
- Telegram chat mappings
- Reports and review queue items
- Audit logs
- Training data (spam/ham samples, stop words)

## Creating a Backup

**Manual backup:** Go to **Settings** -> **System** -> **Backup Configuration** and click **Backup Now**.

**Automatic backups:** Enable the Scheduled Backup job in **Settings** -> **System** -> **Background Jobs**. By default it runs daily at 2 AM.

## Retention Strategy

TGA uses a Grandfather-Father-Son rotation to keep backups manageable:

| Tier | Default | What It Keeps |
|------|---------|---------------|
| Hourly | Up to 168 (1 week) | Recent backups for quick recovery |
| Daily | Up to 31 | One backup per day for the last month |
| Weekly | Up to 52 | One backup per week for the last year |
| Monthly | Up to 60 | One backup per month for 5 years |
| Yearly | Up to 20 | Long-term archive |

Older backups are automatically pruned when new ones are created. Adjust these numbers in the Backup Configuration settings to match your storage capacity.

## Restoring from a Backup

**Warning:** Restoring a backup **permanently deletes all current data** and replaces it with the backup contents. You will be logged out after the restore completes.

To restore:

1. Go to **Settings** -> **System** -> **Backup Configuration**
2. Either upload a `.tar.gz` backup file or select one from the backup browser
3. Review the backup details (creation date, version, table count)
4. Check the confirmation box: "I understand this will permanently delete all current data"
5. Click **Wipe & Restore**
6. Wait for the restore to complete, then log back in

## Rotating the Encryption Passphrase

If you need to change your encryption passphrase:

1. Go to **Settings** -> **System** -> **Backup Configuration**
2. Click **Rotate Passphrase**
3. Enter your new passphrase
4. TGA will re-encrypt all existing backups with the new passphrase

This is an atomic operation — either all backups get re-encrypted or none do.
