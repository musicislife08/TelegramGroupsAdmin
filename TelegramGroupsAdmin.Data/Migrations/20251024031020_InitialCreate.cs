using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ticker");

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<string>(type: "text", nullable: true),
                    target_user_id = table.Column<string>(type: "text", nullable: true),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "blocklist_subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    block_mode = table.Column<int>(type: "integer", nullable: false),
                    is_built_in = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_fetched = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entry_count = table.Column<int>(type: "integer", nullable: true),
                    refresh_interval_hours = table.Column<int>(type: "integer", nullable: false),
                    web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    added_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocklist_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cached_blocked_domains",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    domain = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    block_mode = table.Column<int>(type: "integer", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    source_subscription_id = table.Column<long>(type: "bigint", nullable: true),
                    first_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_verified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cached_blocked_domains", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_prompts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    custom_prompt = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    added_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_prompts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    spam_detection_config = table.Column<string>(type: "jsonb", nullable: true),
                    welcome_config = table.Column<string>(type: "jsonb", nullable: true),
                    log_config = table.Column<string>(type: "jsonb", nullable: true),
                    moderation_config = table.Column<string>(type: "jsonb", nullable: true),
                    bot_protection_config = table.Column<string>(type: "jsonb", nullable: true),
                    file_scanning_config = table.Column<string>(type: "jsonb", nullable: true),
                    background_jobs_config = table.Column<string>(type: "jsonb", nullable: true),
                    invite_link = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "CronTickers",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Expression = table.Column<string>(type: "text", nullable: true),
                    Request = table.Column<byte[]>(type: "bytea", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: false),
                    RetryIntervals = table.Column<int[]>(type: "integer[]", nullable: true),
                    Function = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    InitIdentifier = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronTickers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "domain_filters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    domain = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    filter_type = table.Column<int>(type: "integer", nullable: false),
                    block_mode = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    added_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_filters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_scan_quota",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    service = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quota_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quota_window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quota_window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    limit_value = table.Column<int>(type: "integer", nullable: false),
                    last_updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_scan_quota", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_scan_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scanner = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    threat_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    scan_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_scan_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "managed_chats",
                columns: table => new
                {
                    chat_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_name = table.Column<string>(type: "text", nullable: true),
                    chat_type = table.Column<int>(type: "integer", nullable: false),
                    bot_status = table.Column<int>(type: "integer", nullable: false),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settings_json = table.Column<string>(type: "text", nullable: true),
                    chat_icon_path = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_chats", x => x.chat_id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    message_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message_text = table.Column<string>(type: "text", nullable: true),
                    photo_file_id = table.Column<string>(type: "text", nullable: true),
                    photo_file_size = table.Column<int>(type: "integer", nullable: true),
                    urls = table.Column<string>(type: "text", nullable: true),
                    edit_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    photo_local_path = table.Column<string>(type: "text", nullable: true),
                    photo_thumbnail_path = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deletion_source = table.Column<string>(type: "text", nullable: true),
                    reply_to_message_id = table.Column<long>(type: "bigint", nullable: true),
                    media_type = table.Column<int>(type: "integer", nullable: true),
                    media_file_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    media_file_size = table.Column<long>(type: "bigint", nullable: true),
                    media_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    media_mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    media_local_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    media_duration = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "pending_notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    notification_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message_text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    prompt_text = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    generation_metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "spam_check_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    check_name = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    always_run = table.Column<bool>(type: "boolean", nullable: false),
                    confidence_threshold = table.Column<int>(type: "integer", nullable: true),
                    configuration_json = table.Column<string>(type: "text", nullable: true),
                    modified_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spam_check_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "spam_detection_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    config_json = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spam_detection_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tag_definitions",
                columns: table => new
                {
                    tag_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    color = table.Column<int>(type: "integer", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_definitions", x => x.tag_name);
                });

            migrationBuilder.CreateTable(
                name: "telegram_users",
                columns: table => new
                {
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    first_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_photo_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    photo_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    photo_file_unique_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_trusted = table.Column<bool>(type: "boolean", nullable: false),
                    bot_dm_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_users", x => x.telegram_user_id);
                });

            migrationBuilder.CreateTable(
                name: "TimeTickers",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LockHolder = table.Column<string>(type: "text", nullable: true),
                    Request = table.Column<byte[]>(type: "bytea", nullable: true),
                    ExecutionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    ElapsedTime = table.Column<long>(type: "bigint", nullable: false),
                    Retries = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    RetryIntervals = table.Column<int[]>(type: "integer[]", nullable: true),
                    BatchParent = table.Column<Guid>(type: "uuid", nullable: true),
                    BatchRunCondition = table.Column<int>(type: "integer", nullable: true),
                    Function = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    InitIdentifier = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeTickers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeTickers_TimeTickers_BatchParent",
                        column: x => x.BatchParent,
                        principalSchema: "ticker",
                        principalTable: "TimeTickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    security_stamp = table.Column<string>(type: "text", nullable: false),
                    permission_level = table.Column<int>(type: "integer", nullable: false),
                    invited_by = table.Column<string>(type: "character varying(450)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    totp_secret = table.Column<string>(type: "text", nullable: true),
                    totp_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    totp_setup_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    modified_by = table.Column<string>(type: "text", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    email_verification_token = table.Column<string>(type: "text", nullable: true),
                    email_verification_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    password_reset_token = table.Column<string>(type: "text", nullable: true),
                    password_reset_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InvitedByUserId = table.Column<string>(type: "character varying(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_users_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_users_users_invited_by",
                        column: x => x.invited_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "welcome_responses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    welcome_message_id = table.Column<int>(type: "integer", nullable: false),
                    response = table.Column<int>(type: "integer", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dm_sent = table.Column<bool>(type: "boolean", nullable: false),
                    dm_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    timeout_job_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_welcome_responses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "CronTickerOccurrences",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LockHolder = table.Column<string>(type: "text", nullable: true),
                    ExecutionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CronTickerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    ElapsedTime = table.Column<long>(type: "bigint", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronTickerOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CronTickerOccurrences_CronTickers_CronTickerId",
                        column: x => x.CronTickerId,
                        principalSchema: "ticker",
                        principalTable: "CronTickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_admins",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    is_creator = table.Column<bool>(type: "boolean", nullable: false),
                    promoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_admins", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_admins_managed_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "managed_chats",
                        principalColumn: "chat_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_edits",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    edit_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    old_text = table.Column<string>(type: "text", nullable: true),
                    new_text = table.Column<string>(type: "text", nullable: true),
                    old_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    new_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_edits", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_edits_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "admin_notes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    note_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    actor_web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    actor_telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    actor_system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_notes", x => x.id);
                    table.CheckConstraint("CK_admin_notes_exclusive_actor", "(actor_web_user_id IS NOT NULL)::int + (actor_telegram_user_id IS NOT NULL)::int + (actor_system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_admin_notes_telegram_users_actor_telegram_user_id",
                        column: x => x.actor_telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_admin_notes_telegram_users_telegram_user_id",
                        column: x => x.telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_admin_notes_users_actor_web_user_id",
                        column: x => x.actor_web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "detection_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detection_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    detection_method = table.Column<string>(type: "text", nullable: false),
                    is_spam = table.Column<bool>(type: "boolean", nullable: false, computedColumnSql: "(net_confidence > 0)", stored: true),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    used_for_training = table.Column<bool>(type: "boolean", nullable: false),
                    net_confidence = table.Column<int>(type: "integer", nullable: false),
                    check_results_json = table.Column<string>(type: "text", nullable: true),
                    edit_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detection_results", x => x.id);
                    table.CheckConstraint("CK_detection_results_exclusive_actor", "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_detection_results_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_detection_results_telegram_users_telegram_user_id",
                        column: x => x.telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_detection_results_users_web_user_id",
                        column: x => x.web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "impersonation_alerts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    suspected_user_id = table.Column<long>(type: "bigint", nullable: false),
                    target_user_id = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    total_score = table.Column<int>(type: "integer", nullable: false),
                    risk_level = table.Column<int>(type: "integer", nullable: false),
                    name_match = table.Column<bool>(type: "boolean", nullable: false),
                    photo_match = table.Column<bool>(type: "boolean", nullable: false),
                    photo_similarity_score = table.Column<double>(type: "double precision", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    auto_banned = table.Column<bool>(type: "boolean", nullable: false),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verdict = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_impersonation_alerts", x => x.id);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_telegram_users_suspected_user_id",
                        column: x => x.suspected_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_telegram_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "invites",
                columns: table => new
                {
                    token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_by = table.Column<string>(type: "character varying(450)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_by = table.Column<string>(type: "character varying(450)", nullable: true),
                    permission_level = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UserRecordDtoId = table.Column<string>(type: "character varying(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invites", x => x.token);
                    table.ForeignKey(
                        name: "FK_invites_users_UserRecordDtoId",
                        column: x => x.UserRecordDtoId,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_invites_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invites_users_used_by",
                        column: x => x.used_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "recovery_codes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(450)", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "FK_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<int>(type: "integer", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    report_command_message_id = table.Column<int>(type: "integer", nullable: true),
                    reported_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reported_by_user_name = table.Column<string>(type: "text", nullable: true),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reviewed_by = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    action_taken = table.Column<string>(type: "text", nullable: true),
                    admin_notes = table.Column<string>(type: "text", nullable: true),
                    web_user_id = table.Column<string>(type: "character varying(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_reports_users_web_user_id",
                        column: x => x.web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "stop_words",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    added_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stop_words", x => x.id);
                    table.CheckConstraint("CK_stop_words_exclusive_actor", "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_stop_words_telegram_users_telegram_user_id",
                        column: x => x.telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stop_words_users_web_user_id",
                        column: x => x.web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "telegram_link_tokens",
                columns: table => new
                {
                    token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    user_id = table.Column<string>(type: "character varying(450)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_by_telegram_id = table.Column<long>(type: "bigint", nullable: true),
                    UserRecordDtoId = table.Column<string>(type: "character varying(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_link_tokens", x => x.token);
                    table.ForeignKey(
                        name: "FK_telegram_link_tokens_users_UserRecordDtoId",
                        column: x => x.UserRecordDtoId,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_telegram_link_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_user_mappings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    telegram_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_username = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "character varying(450)", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_user_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_telegram_user_mappings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_actions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    action_type = table.Column<int>(type: "integer", nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: true),
                    web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_actions", x => x.id);
                    table.CheckConstraint("CK_user_actions_exclusive_actor", "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_user_actions_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_actions_telegram_users_telegram_user_id",
                        column: x => x.telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_actions_users_web_user_id",
                        column: x => x.web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_tags",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    tag_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actor_web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    actor_telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    actor_system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by_web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    removed_by_telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    removed_by_system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tags", x => x.id);
                    table.CheckConstraint("CK_user_tags_exclusive_actor", "(actor_web_user_id IS NOT NULL)::int + (actor_telegram_user_id IS NOT NULL)::int + (actor_system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_user_tags_telegram_users_actor_telegram_user_id",
                        column: x => x.actor_telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_tags_telegram_users_telegram_user_id",
                        column: x => x.telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_tags_users_actor_web_user_id",
                        column: x => x.actor_web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "verification_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(450)", nullable: false),
                    token_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_verification_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_notes_actor_telegram_user_id",
                table: "admin_notes",
                column: "actor_telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_admin_notes_actor_web_user_id",
                table: "admin_notes",
                column: "actor_web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_admin_notes_created_at",
                table: "admin_notes",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_admin_notes_is_pinned",
                table: "admin_notes",
                column: "is_pinned");

            migrationBuilder.CreateIndex(
                name: "IX_admin_notes_telegram_user_id",
                table: "admin_notes",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_block_mode",
                table: "blocklist_subscriptions",
                column: "block_mode",
                filter: "block_mode > 0");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_chat_id",
                table: "blocklist_subscriptions",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_enabled",
                table: "blocklist_subscriptions",
                column: "enabled",
                filter: "enabled = true");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_url",
                table: "blocklist_subscriptions",
                column: "url");

            migrationBuilder.CreateIndex(
                name: "IX_cached_blocked_domains_domain_block_mode_chat_id",
                table: "cached_blocked_domains",
                columns: new[] { "domain", "block_mode", "chat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cached_blocked_domains_last_verified",
                table: "cached_blocked_domains",
                column: "last_verified");

            migrationBuilder.CreateIndex(
                name: "IX_cached_blocked_domains_source_subscription_id",
                table: "cached_blocked_domains",
                column: "source_subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_admins_chat_id",
                table: "chat_admins",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_admins_telegram_id",
                table: "chat_admins",
                column: "telegram_id");

            migrationBuilder.CreateIndex(
                name: "IX_configs_chat_id",
                table: "configs",
                column: "chat_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences",
                column: "CronTickerId");

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                column: "ExecutionTime");

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "UQ_CronTickerId_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "CronTickerId", "ExecutionTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_Expression",
                schema: "ticker",
                table: "CronTickers",
                column: "Expression");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_detected_at",
                table: "detection_results",
                column: "detected_at");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_message_id",
                table: "detection_results",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_telegram_user_id",
                table: "detection_results",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_used_for_training",
                table: "detection_results",
                column: "used_for_training");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_web_user_id",
                table: "detection_results",
                column: "web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_domain_filters_chat_id",
                table: "domain_filters",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_domain_filters_domain",
                table: "domain_filters",
                column: "domain");

            migrationBuilder.CreateIndex(
                name: "IX_domain_filters_filter_type_block_mode",
                table: "domain_filters",
                columns: new[] { "filter_type", "block_mode" },
                filter: "enabled = true");

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_quota_service_quota_type_quota_window_end",
                table: "file_scan_quota",
                columns: new[] { "service", "quota_type", "quota_window_end" });

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_quota_service_quota_type_quota_window_start",
                table: "file_scan_quota",
                columns: new[] { "service", "quota_type", "quota_window_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_results_file_hash",
                table: "file_scan_results",
                column: "file_hash");

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_results_scanner_scanned_at",
                table: "file_scan_results",
                columns: new[] { "scanner", "scanned_at" });

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_chat_id",
                table: "impersonation_alerts",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_reviewed_by_user_id",
                table: "impersonation_alerts",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_risk_level_detected_at",
                table: "impersonation_alerts",
                columns: new[] { "risk_level", "detected_at" },
                filter: "reviewed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_suspected_user_id",
                table: "impersonation_alerts",
                column: "suspected_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_target_user_id",
                table: "impersonation_alerts",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_invites_created_by",
                table: "invites",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_invites_used_by",
                table: "invites",
                column: "used_by");

            migrationBuilder.CreateIndex(
                name: "IX_invites_UserRecordDtoId",
                table: "invites",
                column: "UserRecordDtoId");

            migrationBuilder.CreateIndex(
                name: "IX_message_edits_message_id",
                table: "message_edits",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_chat_id",
                table: "messages",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_content_hash",
                table: "messages",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "IX_messages_reply_to_message_id",
                table: "messages",
                column: "reply_to_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_timestamp",
                table: "messages",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_messages_timestamp_deleted_at",
                table: "messages",
                columns: new[] { "timestamp", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_user_id",
                table: "messages",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_pending_notifications_expires_at",
                table: "pending_notifications",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_pending_notifications_notification_type_created_at",
                table: "pending_notifications",
                columns: new[] { "notification_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_pending_notifications_telegram_user_id",
                table: "pending_notifications",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_codes_user_id",
                table: "recovery_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_web_user_id",
                table: "reports",
                column: "web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_stop_words_telegram_user_id",
                table: "stop_words",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_stop_words_web_user_id",
                table: "stop_words",
                column: "web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tag_definitions_usage_count",
                table: "tag_definitions",
                column: "usage_count");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_tokens_user_id",
                table: "telegram_link_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_tokens_UserRecordDtoId",
                table: "telegram_link_tokens",
                column: "UserRecordDtoId");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_user_mappings_telegram_id",
                table: "telegram_user_mappings",
                column: "telegram_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_user_mappings_user_id",
                table: "telegram_user_mappings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_users_is_trusted",
                table: "telegram_users",
                column: "is_trusted");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_users_last_seen_at",
                table: "telegram_users",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_users_username",
                table: "telegram_users",
                column: "username");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                column: "ExecutionTime");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTickers_BatchParent",
                schema: "ticker",
                table: "TimeTickers",
                column: "BatchParent");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_issued_at",
                table: "user_actions",
                column: "issued_at");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_message_id",
                table: "user_actions",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_telegram_user_id",
                table: "user_actions",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_user_id",
                table: "user_actions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_web_user_id",
                table: "user_actions",
                column: "web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_actor_telegram_user_id",
                table: "user_tags",
                column: "actor_telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_actor_web_user_id",
                table: "user_tags",
                column: "actor_web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_removed_at",
                table: "user_tags",
                column: "removed_at");

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_tag_name",
                table: "user_tags",
                column: "tag_name");

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_telegram_user_id",
                table: "user_tags",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_invited_by",
                table: "users",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "IX_users_InvitedByUserId",
                table: "users",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verification_tokens_user_id",
                table: "verification_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_welcome_responses_timeout_job_id",
                table: "welcome_responses",
                column: "timeout_job_id",
                filter: "timeout_job_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_notes");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "blocklist_subscriptions");

            migrationBuilder.DropTable(
                name: "cached_blocked_domains");

            migrationBuilder.DropTable(
                name: "chat_admins");

            migrationBuilder.DropTable(
                name: "chat_prompts");

            migrationBuilder.DropTable(
                name: "configs");

            migrationBuilder.DropTable(
                name: "CronTickerOccurrences",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "detection_results");

            migrationBuilder.DropTable(
                name: "domain_filters");

            migrationBuilder.DropTable(
                name: "file_scan_quota");

            migrationBuilder.DropTable(
                name: "file_scan_results");

            migrationBuilder.DropTable(
                name: "impersonation_alerts");

            migrationBuilder.DropTable(
                name: "invites");

            migrationBuilder.DropTable(
                name: "message_edits");

            migrationBuilder.DropTable(
                name: "pending_notifications");

            migrationBuilder.DropTable(
                name: "prompt_versions");

            migrationBuilder.DropTable(
                name: "recovery_codes");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "spam_check_configs");

            migrationBuilder.DropTable(
                name: "spam_detection_configs");

            migrationBuilder.DropTable(
                name: "stop_words");

            migrationBuilder.DropTable(
                name: "tag_definitions");

            migrationBuilder.DropTable(
                name: "telegram_link_tokens");

            migrationBuilder.DropTable(
                name: "telegram_user_mappings");

            migrationBuilder.DropTable(
                name: "TimeTickers",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "user_actions");

            migrationBuilder.DropTable(
                name: "user_tags");

            migrationBuilder.DropTable(
                name: "verification_tokens");

            migrationBuilder.DropTable(
                name: "welcome_responses");

            migrationBuilder.DropTable(
                name: "managed_chats");

            migrationBuilder.DropTable(
                name: "CronTickers",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "telegram_users");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
