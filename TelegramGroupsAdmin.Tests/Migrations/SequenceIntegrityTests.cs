using TelegramGroupsAdmin.Tests.TestHelpers;

namespace TelegramGroupsAdmin.Tests.Migrations;

/// <summary>
/// Tests to validate PostgreSQL sequence integrity after migrations.
/// Detects sequence mismatches that cause "duplicate key value violates unique constraint" errors.
/// </summary>
[TestFixture]
public class SequenceIntegrityTests
{
    [Test]
    public async Task AllSequencesShouldMatchMaxIds_AfterFreshMigrations()
    {
        // Arrange
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Act - Check all sequences
        var mismatches = await GetSequenceMismatchesAsync(helper);

        // Assert
        Assert.That(mismatches, Is.Empty,
            $"Found {mismatches.Count} sequence mismatches after fresh migrations:\n" +
            string.Join("\n", mismatches.Select(m =>
                $"  - {m.TableName}: sequence={m.SequenceValue}, max_id={m.MaxId} (off by {m.MaxId - m.SequenceValue})")));
    }

    [Test]
    public async Task AllSequencesShouldMatchMaxIds_AfterDataInsert()
    {
        // Arrange
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Insert test data across multiple tables to increment sequences
        await using (var context = helper.GetDbContext())
        {
            // Insert users
            context.Users.Add(new TelegramGroupsAdmin.Data.Models.UserRecordDto
            {
                Id = "test-user-1",
                Email = "test1@test.com",
                NormalizedEmail = "TEST1@TEST.COM",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = TelegramGroupsAdmin.Data.Models.PermissionLevel.Owner,
                Status = TelegramGroupsAdmin.Data.Models.UserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            });

            // Insert audit log entries
            context.AuditLogs.AddRange(
                new TelegramGroupsAdmin.Data.Models.AuditLogRecordDto
                {
                    EventType = TelegramGroupsAdmin.Data.Models.AuditEventType.UserRegistered,
                    ActorWebUserId = "test-user-1",
                    Timestamp = DateTimeOffset.UtcNow
                },
                new TelegramGroupsAdmin.Data.Models.AuditLogRecordDto
                {
                    EventType = TelegramGroupsAdmin.Data.Models.AuditEventType.UserLogin,
                    ActorWebUserId = "test-user-1",
                    Timestamp = DateTimeOffset.UtcNow
                }
            );

            await context.SaveChangesAsync();
        }

        // Act - Check all sequences after inserts
        var mismatches = await GetSequenceMismatchesAsync(helper);

        // Assert
        Assert.That(mismatches, Is.Empty,
            $"Found {mismatches.Count} sequence mismatches after data insert:\n" +
            string.Join("\n", mismatches.Select(m =>
                $"  - {m.TableName}: sequence={m.SequenceValue}, max_id={m.MaxId} (off by {m.MaxId - m.SequenceValue})")));
    }

    [Test]
    public async Task DetectSequenceMismatch_WhenManuallyCreated()
    {
        // Arrange
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();

        // Manually insert audit log entry with explicit ID, bypassing sequence
        await helper.ExecuteSqlAsync(@"
            INSERT INTO audit_log (id, event_type, actor_system_identifier, timestamp)
            VALUES (100, 0, 'manual-insert', NOW())
        ");

        // Act - Check sequences
        var mismatches = await GetSequenceMismatchesAsync(helper);
        var auditLogMismatch = mismatches.FirstOrDefault(m => m.TableName == "audit_log");

        Assert.That(auditLogMismatch, Is.Not.Null,
            "Expected to detect sequence mismatch in audit_log after manual insert with explicit ID");
        Assert.That(auditLogMismatch!.MaxId, Is.EqualTo(100),
            "Max ID should be 100 from manual insert");
        Assert.That(auditLogMismatch.SequenceValue, Is.LessThan(100),
            "Sequence should be less than 100 (mismatch detected)");
    }

    /// <summary>
    /// Gets all sequence mismatches in the database.
    /// A mismatch occurs when sequence.last_value < MAX(id) in the table.
    /// </summary>
    private async Task<List<SequenceMismatch>> GetSequenceMismatchesAsync(MigrationTestHelper helper)
    {
        // Step 1: Get all sequences and their corresponding tables
        // Query pg_sequences directly to avoid issues with tables that don't have 'id' column
        var getTablesWithSequencesSql = @"
            SELECT
                REPLACE(REPLACE(s.sequencename, '_id_seq', ''), '_message_id_seq', '') as table_name,
                'public.' || s.sequencename as sequence_name
            FROM pg_sequences s
            WHERE s.schemaname = 'public'
                AND s.sequencename LIKE '%_id_seq'
                AND s.sequencename NOT ILIKE '%efmigrationshistory%'
                AND s.sequencename NOT LIKE 'pg_%'
            ORDER BY s.sequencename";

        var tablesWithSequences = new List<(string TableName, string SequenceName)>();

        await using (var connection = new Npgsql.NpgsqlConnection(helper.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(getTablesWithSequencesSql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var tableName = reader.GetString(0);
                var sequenceName = reader.GetString(1);
                tablesWithSequences.Add((tableName, sequenceName));
            }
        }

        // Step 2: Check each table for sequence mismatch
        var mismatches = new List<SequenceMismatch>();

        foreach (var (tableName, sequenceName) in tablesWithSequences)
        {
            // Skip EF migrations history table and system tables (case-insensitive)
            if (tableName.ToLowerInvariant().Contains("efmigrationshistory") ||
                tableName.ToLowerInvariant().StartsWith("pg_"))
                continue;

            // Extract sequence name from full path (e.g., "public.audit_log_id_seq" -> "audit_log_id_seq")
            var seqName = sequenceName.Replace("public.", "");

            try
            {
                var checkSequenceSql = $@"
                    SELECT
                        COALESCE(s.last_value, 0) as sequence_value,
                        COALESCE((SELECT MAX(id) FROM {tableName}), 0) as max_id
                    FROM pg_sequences s
                    WHERE s.sequencename = '{seqName}'";

                await using var connection = new Npgsql.NpgsqlConnection(helper.ConnectionString);
                await connection.OpenAsync();
                await using var cmd = new Npgsql.NpgsqlCommand(checkSequenceSql, connection);
                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var sequenceValue = reader.GetInt64(0);
                    var maxId = reader.GetInt64(1);

                    // Detect mismatch: sequence should be >= max_id
                    if (sequenceValue < maxId)
                    {
                        mismatches.Add(new SequenceMismatch
                        {
                            TableName = tableName,
                            SequenceName = sequenceName,
                            SequenceValue = sequenceValue,
                            MaxId = maxId
                        });
                    }
                }
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
            {
                // Table doesn't exist - skip it (can happen with system catalogs)
                continue;
            }
        }

        return mismatches;
    }

    private record SequenceMismatch
    {
        public required string TableName { get; init; }
        public required string SequenceName { get; init; }
        public required long SequenceValue { get; init; }
        public required long MaxId { get; init; }
    }
}
