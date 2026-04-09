# Database Maintenance

TGA can automatically optimize your PostgreSQL database to keep it running efficiently. This is managed through the **Database Maintenance** background job.

## What It Does

Two optional maintenance operations:

**VACUUM** — Reclaims disk space from deleted rows. Over time, as TGA processes messages and cleans up old data, PostgreSQL accumulates dead rows. VACUUM reclaims that space and keeps your database compact.

**ANALYZE** — Updates PostgreSQL's query planner statistics. This helps the database choose efficient execution plans for queries, which can improve performance as your data grows.

## Setting It Up

1. Go to **Settings** -> **System** -> **Background Jobs**
2. Find **Database Maintenance** in the jobs list
3. Click **Configure** to choose which operations to run:
   - Check **Run VACUUM** to reclaim storage
   - Check **Run ANALYZE** to update query statistics
4. Enable the job with the toggle
5. Set a schedule (default: weekly on Sunday at 4 AM)

## Do You Need This?

For most installations, PostgreSQL handles maintenance reasonably well on its own through its built-in autovacuum. Enabling this job is useful if:

- Your TGA instance processes a high volume of messages
- You notice database storage growing over time
- You want explicit control over when maintenance runs

If you're unsure, it's fine to leave this disabled. You can always enable it later or run it manually with the **Run Now** button to see if it makes a difference.
