# Background Jobs

TGA runs several automated tasks in the background to keep your system healthy and up to date. You can view and manage these from **Settings** -> **System** -> **Background Jobs**.

## What Runs Automatically

| Job | Default Schedule | What It Does |
|-----|-----------------|--------------|
| **Chat Health Check** | Every 30 minutes | Monitors bot connectivity and permissions in each group |
| **Classifier Retraining** | Every 8 hours | Retrains spam detection models using your latest training data |
| **User Photo Refresh** | Daily at 3 AM | Updates cached profile photos for active users |
| **Blocklist Sync** | Weekly (Sunday 3 AM) | Downloads the latest URL blocklist data |
| **Scheduled Backup** | Daily at 2 AM | Creates an encrypted database backup (disabled by default — see [Backup & Restore](14-backup-restore.md)) |
| **Data Cleanup** | Daily | Removes expired messages and reports based on retention settings (disabled by default) |
| **Database Maintenance** | Weekly (Sunday 4 AM) | Optimizes database performance (disabled by default — see [Database Maintenance](19-database-maintenance.md)) |
| **Profile Rescan** | Every 6 hours | Re-scans user profiles for changes (disabled by default) |

## Managing Jobs

For each job you can:

- **Enable or disable** it with the toggle
- **Change the schedule** using natural language (e.g., "every 30 minutes", "every day at 2pm", "every week on sunday at 3am")
- **Run it immediately** with the "Run Now" button
- **Configure job-specific settings** where applicable (e.g., retention periods for Data Cleanup, VACUUM/ANALYZE options for Database Maintenance)

The jobs table shows the last run time and next scheduled run for each job, so you can confirm everything is running on schedule.

## You Don't Need to Touch Most of These

The defaults are designed to work well out of the box. The three jobs disabled by default (Scheduled Backup, Data Cleanup, Database Maintenance) are optional and depend on your preferences — enable them when you're ready.
