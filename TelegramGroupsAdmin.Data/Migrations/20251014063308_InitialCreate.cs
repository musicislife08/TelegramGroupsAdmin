using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<string>(type: "text", nullable: true),
                    target_user_id = table.Column<string>(type: "text", nullable: true),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_prompts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    custom_prompt = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    added_date = table.Column<long>(type: "bigint", nullable: false),
                    added_by = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_prompts", x => x.id);
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
                    added_at = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_seen_at = table.Column<long>(type: "bigint", nullable: true),
                    settings_json = table.Column<string>(type: "text", nullable: true)
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
                    user_name = table.Column<string>(type: "text", nullable: true),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    timestamp = table.Column<long>(type: "bigint", nullable: false),
                    message_text = table.Column<string>(type: "text", nullable: true),
                    photo_file_id = table.Column<string>(type: "text", nullable: true),
                    photo_file_size = table.Column<int>(type: "integer", nullable: true),
                    urls = table.Column<string>(type: "text", nullable: true),
                    edit_date = table.Column<long>(type: "bigint", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    chat_name = table.Column<string>(type: "text", nullable: true),
                    photo_local_path = table.Column<string>(type: "text", nullable: true),
                    photo_thumbnail_path = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    deletion_source = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "spam_check_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    check_name = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    confidence_threshold = table.Column<int>(type: "integer", nullable: true),
                    configuration_json = table.Column<string>(type: "text", nullable: true),
                    modified_date = table.Column<long>(type: "bigint", nullable: false),
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
                    chat_id = table.Column<string>(type: "text", nullable: true),
                    config_json = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<long>(type: "bigint", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spam_detection_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stop_words",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    added_date = table.Column<long>(type: "bigint", nullable: false),
                    added_by = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stop_words", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training_samples",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_text = table.Column<string>(type: "text", nullable: false),
                    is_spam = table.Column<bool>(type: "boolean", nullable: false),
                    added_date = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    confidence_when_added = table.Column<int>(type: "integer", nullable: true),
                    chat_ids = table.Column<long[]>(type: "bigint[]", nullable: true),
                    added_by = table.Column<string>(type: "text", nullable: true),
                    detection_count = table.Column<int>(type: "integer", nullable: false),
                    last_detected_date = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_samples", x => x.id);
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
                    totp_setup_started_at = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    last_login_at = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    modified_by = table.Column<string>(type: "text", nullable: true),
                    modified_at = table.Column<long>(type: "bigint", nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    email_verification_token = table.Column<string>(type: "text", nullable: true),
                    email_verification_token_expires_at = table.Column<long>(type: "bigint", nullable: true),
                    password_reset_token = table.Column<string>(type: "text", nullable: true),
                    password_reset_token_expires_at = table.Column<long>(type: "bigint", nullable: true),
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
                name: "chat_admins",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    is_creator = table.Column<bool>(type: "boolean", nullable: false),
                    promoted_at = table.Column<long>(type: "bigint", nullable: false),
                    last_verified_at = table.Column<long>(type: "bigint", nullable: false),
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
                name: "detection_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    detected_at = table.Column<long>(type: "bigint", nullable: false),
                    detection_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    detection_method = table.Column<string>(type: "text", nullable: false),
                    is_spam = table.Column<bool>(type: "boolean", nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    added_by = table.Column<string>(type: "text", nullable: true),
                    used_for_training = table.Column<bool>(type: "boolean", nullable: false),
                    net_confidence = table.Column<int>(type: "integer", nullable: true),
                    check_results_json = table.Column<string>(type: "text", nullable: true),
                    edit_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detection_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_detection_results_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_edits",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    edit_date = table.Column<long>(type: "bigint", nullable: false),
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
                name: "user_actions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    action_type = table.Column<int>(type: "integer", nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: true),
                    issued_by = table.Column<string>(type: "text", nullable: true),
                    issued_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_actions_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "invites",
                columns: table => new
                {
                    token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_by = table.Column<string>(type: "character varying(450)", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    used_by = table.Column<string>(type: "character varying(450)", nullable: true),
                    permission_level = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    modified_at = table.Column<long>(type: "bigint", nullable: true),
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
                    used_at = table.Column<long>(type: "bigint", nullable: true)
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
                    reported_at = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reviewed_by = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<long>(type: "bigint", nullable: true),
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
                name: "telegram_link_tokens",
                columns: table => new
                {
                    token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    user_id = table.Column<string>(type: "character varying(450)", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    used_at = table.Column<long>(type: "bigint", nullable: true),
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
                    linked_at = table.Column<long>(type: "bigint", nullable: false),
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
                name: "verification_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(450)", nullable: false),
                    token_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    used_at = table.Column<long>(type: "bigint", nullable: true)
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
                name: "IX_chat_admins_chat_id",
                table: "chat_admins",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_admins_telegram_id",
                table: "chat_admins",
                column: "telegram_id");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_detected_at",
                table: "detection_results",
                column: "detected_at");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_message_id",
                table: "detection_results",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_used_for_training",
                table: "detection_results",
                column: "used_for_training");

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
                name: "IX_messages_timestamp",
                table: "messages",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_messages_user_id",
                table: "messages",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_codes_user_id",
                table: "recovery_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_web_user_id",
                table: "reports",
                column: "web_user_id");

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
                name: "IX_user_actions_issued_at",
                table: "user_actions",
                column: "issued_at");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_message_id",
                table: "user_actions",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_user_id",
                table: "user_actions",
                column: "user_id");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "chat_admins");

            migrationBuilder.DropTable(
                name: "chat_prompts");

            migrationBuilder.DropTable(
                name: "detection_results");

            migrationBuilder.DropTable(
                name: "invites");

            migrationBuilder.DropTable(
                name: "message_edits");

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
                name: "telegram_link_tokens");

            migrationBuilder.DropTable(
                name: "telegram_user_mappings");

            migrationBuilder.DropTable(
                name: "training_samples");

            migrationBuilder.DropTable(
                name: "user_actions");

            migrationBuilder.DropTable(
                name: "verification_tokens");

            migrationBuilder.DropTable(
                name: "managed_chats");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
