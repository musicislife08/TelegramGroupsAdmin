using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Adds PostgreSQL-specific partial indexes for common filtered queries.
/// Partial indexes are smaller and faster than full indexes for specific WHERE conditions.
/// </summary>
[Migration(202601083)]
public class AddPartialIndexes : Migration
{
    public override void Up()
    {
        // Active users only - login queries don't need inactive/deleted users
        // Much smaller than indexing all users
        Execute.Sql(@"
            CREATE INDEX idx_active_users_email
            ON users(email, normalized_email)
            WHERE status = 1;
        ");

        // Pending invites - cleanup queries only care about unused invites
        Execute.Sql(@"
            CREATE INDEX idx_pending_invites_expires
            ON invites(expires_at)
            WHERE status = 0;
        ");

        // Enabled spam samples - active detection only uses enabled patterns
        Execute.Sql(@"
            CREATE INDEX idx_enabled_spam_samples_text
            ON spam_samples(sample_text)
            WHERE enabled = true;
        ");

        // Enabled stop words - spam detection only checks enabled words
        Execute.Sql(@"
            CREATE INDEX idx_enabled_stop_words_word
            ON stop_words(word)
            WHERE enabled = true;
        ");

        // Unexpired verification tokens - only check valid tokens
        Execute.Sql(@"
            CREATE INDEX idx_valid_verification_tokens
            ON verification_tokens(token, token_type)
            WHERE used_at IS NULL;
        ");

        // Note: Cannot create time-based partial index for recent messages
        // because NOW() is not IMMUTABLE in PostgreSQL (changes over time)
        // The regular chat_id index will suffice for message queries
    }

    public override void Down()
    {
        // Drop partial indexes
        Execute.Sql("DROP INDEX IF EXISTS idx_valid_verification_tokens;");
        Execute.Sql("DROP INDEX IF EXISTS idx_enabled_stop_words_word;");
        Execute.Sql("DROP INDEX IF EXISTS idx_enabled_spam_samples_text;");
        Execute.Sql("DROP INDEX IF EXISTS idx_pending_invites_expires;");
        Execute.Sql("DROP INDEX IF EXISTS idx_active_users_email;");
    }
}
