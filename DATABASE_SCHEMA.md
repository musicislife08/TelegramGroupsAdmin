# TelegramGroupsAdmin Database Schema

PostgreSQL 17 - Complete schema definition

---

## Tables

### users
                                                                Table "public.users"
               Column                |          Type          | Collation | Nullable | Default | Storage  | Compression | Stats target | Description 
-------------------------------------+------------------------+-----------+----------+---------+----------+-------------+--------------+-------------
 id                                  | character varying(36)  |           | not null |         | extended |             |              | 
 email                               | character varying(256) |           | not null |         | extended |             |              | 
 normalized_email                    | character varying(256) |           | not null |         | extended |             |              | 
 password_hash                       | character varying(256) |           | not null |         | extended |             |              | 
 security_stamp                      | character varying(36)  |           | not null |         | extended |             |              | 
 permission_level                    | integer                |           | not null | 0       | plain    |             |              | 
 invited_by                          | character varying(36)  |           |          |         | extended |             |              | 
 is_active                           | boolean                |           | not null | true    | plain    |             |              | 
 totp_secret                         | character varying(512) |           |          |         | extended |             |              | 
 totp_enabled                        | boolean                |           | not null | false   | plain    |             |              | 
 created_at                          | bigint                 |           | not null |         | plain    |             |              | 
 last_login_at                       | bigint                 |           |          |         | plain    |             |              | 
 status                              | integer                |           | not null | 1       | plain    |             |              | 
 modified_by                         | character varying(36)  |           |          |         | extended |             |              | 
 modified_at                         | bigint                 |           |          |         | plain    |             |              | 
 email_verified                      | boolean                |           | not null | false   | plain    |             |              | 
 email_verification_token            | text                   |           |          |         | extended |             |              | 
 email_verification_token_expires_at | bigint                 |           |          |         | plain    |             |              | 
 password_reset_token                | text                   |           |          |         | extended |             |              | 
 password_reset_token_expires_at     | bigint                 |           |          |         | plain    |             |              | 
 totp_setup_started_at               | bigint                 |           |          |         | plain    |             |              | 
Indexes:
    "PK_users" PRIMARY KEY, btree (id)
    "IX_users_email" UNIQUE, btree (email)
    "idx_active_users_email" btree (email, normalized_email) WHERE status = 1
    "idx_users_is_active" btree (is_active)
    "idx_users_modified_at" btree (modified_at DESC)
    "idx_users_normalized_email" btree (normalized_email)
    "idx_users_permission_level" btree (permission_level)
    "idx_users_status" btree (status)
Foreign-key constraints:
    "fk_users_invited_by" FOREIGN KEY (invited_by) REFERENCES users(id) ON DELETE SET NULL
    "fk_users_modified_by" FOREIGN KEY (modified_by) REFERENCES users(id) ON DELETE SET NULL
Referenced by:
    TABLE "detection_results" CONSTRAINT "FK_detection_results_added_by_users_id" FOREIGN KEY (added_by) REFERENCES users(id) ON DELETE SET NULL
    TABLE "invites" CONSTRAINT "FK_invites_created_by_users_id" FOREIGN KEY (created_by) REFERENCES users(id)
    TABLE "invites" CONSTRAINT "FK_invites_used_by_users_id" FOREIGN KEY (used_by) REFERENCES users(id)
    TABLE "recovery_codes" CONSTRAINT "FK_recovery_codes_user_id_users_id" FOREIGN KEY (user_id) REFERENCES users(id)
    TABLE "user_actions" CONSTRAINT "FK_user_actions_issued_by_users_id" FOREIGN KEY (issued_by) REFERENCES users(id) ON DELETE SET NULL
    TABLE "audit_log" CONSTRAINT "fk_audit_log_actor" FOREIGN KEY (actor_user_id) REFERENCES users(id) ON DELETE SET NULL
    TABLE "audit_log" CONSTRAINT "fk_audit_log_target" FOREIGN KEY (target_user_id) REFERENCES users(id) ON DELETE SET NULL
    TABLE "chat_prompts" CONSTRAINT "fk_chat_prompts_added_by" FOREIGN KEY (added_by) REFERENCES users(id) ON DELETE SET NULL
    TABLE "spam_check_configs" CONSTRAINT "fk_spam_check_configs_modified_by" FOREIGN KEY (modified_by) REFERENCES users(id) ON DELETE SET NULL
    TABLE "stop_words" CONSTRAINT "fk_stop_words_added_by" FOREIGN KEY (added_by) REFERENCES users(id) ON DELETE SET NULL
    TABLE "telegram_link_tokens" CONSTRAINT "fk_telegram_link_tokens_user_id" FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
    TABLE "telegram_user_mappings" CONSTRAINT "fk_telegram_user_mappings_user_id" FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
    TABLE "users" CONSTRAINT "fk_users_invited_by" FOREIGN KEY (invited_by) REFERENCES users(id) ON DELETE SET NULL
    TABLE "users" CONSTRAINT "fk_users_modified_by" FOREIGN KEY (modified_by) REFERENCES users(id) ON DELETE SET NULL
    TABLE "verification_tokens" CONSTRAINT "fk_verification_tokens_user" FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
Access method: heap


### invites
                                                     Table "public.invites"
      Column      |         Type          | Collation | Nullable | Default | Storage  | Compression | Stats target | Description 
------------------+-----------------------+-----------+----------+---------+----------+-------------+--------------+-------------
 token            | character varying(36) |           | not null |         | extended |             |              | 
 created_by       | character varying(36) |           | not null |         | extended |             |              | 
 created_at       | bigint                |           | not null |         | plain    |             |              | 
 expires_at       | bigint                |           | not null |         | plain    |             |              | 
 used_by          | character varying(36) |           |          |         | extended |             |              | 
 permission_level | integer               |           | not null | 0       | plain    |             |              | 
 status           | integer               |           | not null | 0       | plain    |             |              | 
 modified_at      | bigint                |           |          |         | plain    |             |              | 
Indexes:
    "PK_invites" PRIMARY KEY, btree (token)
    "idx_invites_created_by" btree (created_by)
    "idx_invites_creator_status" btree (created_by, status, created_at DESC)
    "idx_invites_expires" btree (expires_at)
    "idx_invites_status" btree (status)
    "idx_pending_invites_expires" btree (expires_at) WHERE status = 0
Foreign-key constraints:
    "FK_invites_created_by_users_id" FOREIGN KEY (created_by) REFERENCES users(id)
    "FK_invites_used_by_users_id" FOREIGN KEY (used_by) REFERENCES users(id)
Access method: heap


### audit_log
                                                              Table "public.audit_log"
     Column     |          Type          | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
----------------+------------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id             | bigint                 |           | not null | generated always as identity | plain    |             |              | 
 event_type     | integer                |           | not null |                              | plain    |             |              | 
 timestamp      | bigint                 |           | not null |                              | plain    |             |              | 
 actor_user_id  | character varying(36)  |           |          |                              | extended |             |              | 
 target_user_id | character varying(36)  |           |          |                              | extended |             |              | 
 value          | character varying(500) |           |          |                              | extended |             |              | 
Indexes:
    "PK_audit_log" PRIMARY KEY, btree (id)
    "idx_audit_log_actor" btree (actor_user_id)
    "idx_audit_log_event_type" btree (event_type)
    "idx_audit_log_target" btree (target_user_id)
    "idx_audit_log_target_event_time" btree (target_user_id, event_type, "timestamp" DESC)
    "idx_audit_log_timestamp" btree ("timestamp" DESC)
Foreign-key constraints:
    "fk_audit_log_actor" FOREIGN KEY (actor_user_id) REFERENCES users(id) ON DELETE SET NULL
    "fk_audit_log_target" FOREIGN KEY (target_user_id) REFERENCES users(id) ON DELETE SET NULL
Access method: heap


### verification_tokens
                                               Table "public.verification_tokens"
   Column   |  Type  | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
------------+--------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id         | bigint |           | not null | generated always as identity | plain    |             |              | 
 user_id    | text   |           | not null |                              | extended |             |              | 
 token_type | text   |           | not null |                              | extended |             |              | 
 token      | text   |           | not null |                              | extended |             |              | 
 value      | text   |           |          |                              | extended |             |              | 
 expires_at | bigint |           | not null |                              | plain    |             |              | 
 created_at | bigint |           | not null |                              | plain    |             |              | 
 used_at    | bigint |           |          |                              | plain    |             |              | 
Indexes:
    "PK_verification_tokens" PRIMARY KEY, btree (id)
    "IX_verification_tokens_token" UNIQUE, btree (token)
    "idx_valid_verification_tokens" btree (token, token_type) WHERE used_at IS NULL
    "idx_verification_tokens_token" btree (token)
    "idx_verification_tokens_type" btree (token_type)
    "idx_verification_tokens_user_id" btree (user_id)
    "idx_verification_tokens_user_type" btree (user_id, token_type, expires_at DESC)
Foreign-key constraints:
    "fk_verification_tokens_user" FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
Access method: heap


### recovery_codes
                                                         Table "public.recovery_codes"
  Column   |          Type          | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
-----------+------------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id        | bigint                 |           | not null | generated always as identity | plain    |             |              | 
 user_id   | character varying(36)  |           | not null |                              | extended |             |              | 
 code_hash | character varying(256) |           | not null |                              | extended |             |              | 
 used_at   | bigint                 |           |          |                              | plain    |             |              | 
Indexes:
    "PK_recovery_codes" PRIMARY KEY, btree (id)
    "idx_recovery_codes_user" btree (user_id)
Foreign-key constraints:
    "FK_recovery_codes_user_id_users_id" FOREIGN KEY (user_id) REFERENCES users(id)
Access method: heap


### messages
                                                       Table "public.messages"
        Column        |         Type          | Collation | Nullable | Default | Storage  | Compression | Stats target | Description 
----------------------+-----------------------+-----------+----------+---------+----------+-------------+--------------+-------------
 message_id           | bigint                |           | not null |         | plain    |             |              | 
 user_id              | bigint                |           | not null |         | plain    |             |              | 
 user_name            | text                  |           |          |         | extended |             |              | 
 chat_id              | bigint                |           | not null |         | plain    |             |              | 
 timestamp            | bigint                |           | not null |         | plain    |             |              | 
 message_text         | text                  |           |          |         | extended |             |              | 
 photo_file_id        | text                  |           |          |         | extended |             |              | 
 photo_file_size      | integer               |           |          |         | plain    |             |              | 
 urls                 | text                  |           |          |         | extended |             |              | 
 edit_date            | bigint                |           |          |         | plain    |             |              | 
 content_hash         | character varying(64) |           |          |         | extended |             |              | 
 chat_name            | text                  |           |          |         | extended |             |              | 
 photo_local_path     | text                  |           |          |         | extended |             |              | 
 photo_thumbnail_path | text                  |           |          |         | extended |             |              | 
 deleted_at           | bigint                |           |          |         | plain    |             |              | 
 deletion_source      | character varying(50) |           |          |         | extended |             |              | 
Indexes:
    "PK_messages" PRIMARY KEY, btree (message_id)
    "idx_chat_name" btree (chat_name)
    "idx_content_hash" btree (content_hash)
    "idx_messages_chat_id_timestamp" btree (chat_id, "timestamp" DESC)
    "idx_messages_deleted" btree (chat_id, deleted_at) WHERE deleted_at IS NOT NULL
    "idx_messages_user_chat_time" btree (user_id, chat_id, "timestamp" DESC)
    "idx_user_chat_photo" btree (user_id, chat_id, photo_file_id) WHERE photo_file_id IS NOT NULL
    "idx_user_chat_timestamp" btree (user_id, chat_id, "timestamp" DESC)
Referenced by:
    TABLE "detection_results" CONSTRAINT "FK_detection_results_message_id_messages_message_id" FOREIGN KEY (message_id) REFERENCES messages(message_id) ON DELETE CASCADE
    TABLE "message_edits" CONSTRAINT "FK_message_edits_message_id_messages_message_id" FOREIGN KEY (message_id) REFERENCES messages(message_id)
    TABLE "user_actions" CONSTRAINT "FK_user_actions_message_id_messages_message_id" FOREIGN KEY (message_id) REFERENCES messages(message_id) ON DELETE SET NULL
Access method: heap


### message_edits
                                                             Table "public.message_edits"
      Column      |         Type          | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
------------------+-----------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id               | bigint                |           | not null | generated always as identity | plain    |             |              | 
 message_id       | bigint                |           | not null |                              | plain    |             |              | 
 edit_date        | bigint                |           | not null |                              | plain    |             |              | 
 old_text         | text                  |           |          |                              | extended |             |              | 
 new_text         | text                  |           |          |                              | extended |             |              | 
 old_content_hash | character varying(64) |           |          |                              | extended |             |              | 
 new_content_hash | character varying(64) |           |          |                              | extended |             |              | 
Indexes:
    "PK_message_edits" PRIMARY KEY, btree (id)
    "idx_message_edits_msg" btree (message_id, edit_date DESC)
Foreign-key constraints:
    "FK_message_edits_message_id_messages_message_id" FOREIGN KEY (message_id) REFERENCES messages(message_id)
Access method: heap


### detection_results
                                                           Table "public.detection_results"
      Column      |          Type          | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
------------------+------------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id               | bigint                 |           | not null | generated always as identity | plain    |             |              | 
 message_id       | bigint                 |           | not null |                              | plain    |             |              | 
 detected_at      | bigint                 |           | not null |                              | plain    |             |              | 
 detection_source | character varying(50)  |           | not null |                              | extended |             |              | 
 is_spam          | boolean                |           | not null |                              | plain    |             |              | 
 confidence       | integer                |           |          |                              | plain    |             |              | 
 reason           | text                   |           |          |                              | extended |             |              | 
 detection_method | character varying(100) |           |          |                              | extended |             |              | 
 added_by         | character varying(36)  |           |          |                              | extended |             |              | 
Indexes:
    "PK_detection_results" PRIMARY KEY, btree (id)
    "idx_detection_results_detected_at" btree (detected_at DESC)
    "idx_detection_results_is_spam_source" btree (is_spam, detection_source, detected_at DESC)
    "idx_detection_results_message_id" btree (message_id)
Foreign-key constraints:
    "FK_detection_results_added_by_users_id" FOREIGN KEY (added_by) REFERENCES users(id) ON DELETE SET NULL
    "FK_detection_results_message_id_messages_message_id" FOREIGN KEY (message_id) REFERENCES messages(message_id) ON DELETE CASCADE
Access method: heap


### user_actions
                                                           Table "public.user_actions"
   Column    |         Type          | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
-------------+-----------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id          | bigint                |           | not null | generated always as identity | plain    |             |              | 
 user_id     | bigint                |           | not null |                              | plain    |             |              | 
 chat_ids    | bigint[]              |           |          |                              | extended |             |              | 
 action_type | integer               |           | not null |                              | plain    |             |              | 
 message_id  | bigint                |           |          |                              | plain    |             |              | 
 issued_by   | character varying(36) |           |          |                              | extended |             |              | 
 issued_at   | bigint                |           | not null |                              | plain    |             |              | 
 expires_at  | bigint                |           |          |                              | plain    |             |              | 
 reason      | text                  |           |          |                              | extended |             |              | 
Indexes:
    "PK_user_actions" PRIMARY KEY, btree (id)
    "idx_user_actions_action_type" btree (action_type)
    "idx_user_actions_issued_at" btree (issued_at DESC)
    "idx_user_actions_user_id" btree (user_id)
Foreign-key constraints:
    "FK_user_actions_issued_by_users_id" FOREIGN KEY (issued_by) REFERENCES users(id) ON DELETE SET NULL
    "FK_user_actions_message_id_messages_message_id" FOREIGN KEY (message_id) REFERENCES messages(message_id) ON DELETE SET NULL
Access method: heap


### managed_chats
                                          Table "public.managed_chats"
    Column     |  Type   | Collation | Nullable | Default | Storage  | Compression | Stats target | Description 
---------------+---------+-----------+----------+---------+----------+-------------+--------------+-------------
 chat_id       | bigint  |           | not null |         | plain    |             |              | 
 chat_name     | text    |           |          |         | extended |             |              | 
 chat_type     | integer |           | not null |         | plain    |             |              | 
 bot_status    | integer |           | not null |         | plain    |             |              | 
 is_admin      | boolean |           | not null | false   | plain    |             |              | 
 added_at      | bigint  |           | not null |         | plain    |             |              | 
 is_active     | boolean |           | not null | true    | plain    |             |              | 
 last_seen_at  | bigint  |           |          |         | plain    |             |              | 
 settings_json | text    |           |          |         | extended |             |              | 
Indexes:
    "PK_managed_chats" PRIMARY KEY, btree (chat_id)
    "idx_managed_chats_active" btree (is_active)
    "idx_managed_chats_admin" btree (is_admin) WHERE is_active = true
Referenced by:
    TABLE "chat_admins" CONSTRAINT "fk_chat_admins_chat_id" FOREIGN KEY (chat_id) REFERENCES managed_chats(chat_id) ON DELETE CASCADE
Access method: heap


### chat_admins
                                                      Table "public.chat_admins"
      Column      |  Type   | Collation | Nullable |           Default            | Storage | Compression | Stats target | Description 
------------------+---------+-----------+----------+------------------------------+---------+-------------+--------------+-------------
 id               | bigint  |           | not null | generated always as identity | plain   |             |              | 
 chat_id          | bigint  |           | not null |                              | plain   |             |              | 
 telegram_id      | bigint  |           | not null |                              | plain   |             |              | 
 is_creator       | boolean |           | not null | false                        | plain   |             |              | 
 promoted_at      | bigint  |           | not null |                              | plain   |             |              | 
 last_verified_at | bigint  |           | not null |                              | plain   |             |              | 
 is_active        | boolean |           | not null | true                         | plain   |             |              | 
Indexes:
    "PK_chat_admins" PRIMARY KEY, btree (id)
    "idx_chat_admins_chat_id" btree (chat_id) WHERE is_active = true
    "idx_chat_admins_telegram_id" btree (telegram_id) WHERE is_active = true
    "uq_chat_admins_chat_telegram" UNIQUE CONSTRAINT, btree (chat_id, telegram_id)
Foreign-key constraints:
    "fk_chat_admins_chat_id" FOREIGN KEY (chat_id) REFERENCES managed_chats(chat_id) ON DELETE CASCADE
Access method: heap


### telegram_user_mappings
                                                  Table "public.telegram_user_mappings"
      Column       |  Type   | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
-------------------+---------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id                | bigint  |           | not null | generated always as identity | plain    |             |              | 
 telegram_id       | bigint  |           | not null |                              | plain    |             |              | 
 telegram_username | text    |           |          |                              | extended |             |              | 
 user_id           | text    |           | not null |                              | extended |             |              | 
 linked_at         | bigint  |           | not null |                              | plain    |             |              | 
 is_active         | boolean |           | not null | true                         | plain    |             |              | 
Indexes:
    "PK_telegram_user_mappings" PRIMARY KEY, btree (id)
    "IX_telegram_user_mappings_telegram_id" UNIQUE, btree (telegram_id)
    "idx_telegram_user_mappings_user_id" btree (user_id)
Foreign-key constraints:
    "fk_telegram_user_mappings_user_id" FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
Access method: heap


### telegram_link_tokens
                                                Table "public.telegram_link_tokens"
       Column        |         Type          | Collation | Nullable | Default | Storage  | Compression | Stats target | Description 
---------------------+-----------------------+-----------+----------+---------+----------+-------------+--------------+-------------
 token               | character varying(64) |           | not null |         | extended |             |              | 
 user_id             | text                  |           | not null |         | extended |             |              | 
 created_at          | bigint                |           | not null |         | plain    |             |              | 
 expires_at          | bigint                |           | not null |         | plain    |             |              | 
 used_at             | bigint                |           |          |         | plain    |             |              | 
 used_by_telegram_id | bigint                |           |          |         | plain    |             |              | 
Indexes:
    "PK_telegram_link_tokens" PRIMARY KEY, btree (token)
    "idx_telegram_link_tokens_expires_at" btree (expires_at)
    "idx_telegram_link_tokens_user_id" btree (user_id)
Foreign-key constraints:
    "fk_telegram_link_tokens_user_id" FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
Access method: heap


### reports
                                                                     Table "public.reports"
          Column           |          Type          | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
---------------------------+------------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id                        | bigint                 |           | not null | generated always as identity | plain    |             |              | 
 message_id                | integer                |           | not null |                              | plain    |             |              | 
 chat_id                   | bigint                 |           | not null |                              | plain    |             |              | 
 report_command_message_id | integer                |           | not null |                              | plain    |             |              | 
 reported_by_user_id       | bigint                 |           | not null |                              | plain    |             |              | 
 reported_by_user_name     | character varying(255) |           |          |                              | extended |             |              | 
 reported_at               | bigint                 |           | not null |                              | plain    |             |              | 
 status                    | integer                |           | not null |                              | plain    |             |              | 
 reviewed_by               | character varying(450) |           |          |                              | extended |             |              | 
 reviewed_at               | bigint                 |           |          |                              | plain    |             |              | 
 action_taken              | character varying(50)  |           |          |                              | extended |             |              | 
 admin_notes               | character varying(500) |           |          |                              | extended |             |              | 
Indexes:
    "PK_reports" PRIMARY KEY, btree (id)
    "idx_reports_chat_status" btree (chat_id, status)
    "idx_reports_reported_at" btree (reported_at DESC)
    "idx_reports_status" btree (status)
Access method: heap


### stop_words
                                                            Table "public.stop_words"
   Column   |          Type          | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
------------+------------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id         | bigint                 |           | not null | generated always as identity | plain    |             |              | 
 word       | character varying(100) |           | not null |                              | extended |             |              | 
 enabled    | boolean                |           | not null | true                         | plain    |             |              | 
 added_date | bigint                 |           | not null |                              | plain    |             |              | 
 added_by   | character varying(36)  |           |          |                              | extended |             |              | 
 notes      | character varying(500) |           |          |                              | extended |             |              | 
Indexes:
    "PK_stop_words" PRIMARY KEY, btree (id)
    "IX_stop_words_word" UNIQUE, btree (word)
    "idx_enabled_stop_words_word" btree (word) WHERE enabled = true
    "idx_stop_words_enabled" btree (enabled)
Foreign-key constraints:
    "fk_stop_words_added_by" FOREIGN KEY (added_by) REFERENCES users(id) ON DELETE SET NULL
Access method: heap


### chat_prompts
                                                             Table "public.chat_prompts"
    Column     |          Type           | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
---------------+-------------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id            | bigint                  |           | not null | generated always as identity | plain    |             |              | 
 chat_id       | character varying(50)   |           | not null |                              | extended |             |              | 
 custom_prompt | character varying(2000) |           | not null |                              | extended |             |              | 
 enabled       | boolean                 |           | not null | true                         | plain    |             |              | 
 added_date    | bigint                  |           | not null |                              | plain    |             |              | 
 added_by      | character varying(36)   |           |          |                              | extended |             |              | 
 notes         | character varying(500)  |           |          |                              | extended |             |              | 
Indexes:
    "PK_group_prompts" PRIMARY KEY, btree (id)
    "idx_chat_prompts_chat_id" btree (chat_id)
    "idx_group_prompts_enabled" btree (enabled)
Foreign-key constraints:
    "fk_chat_prompts_added_by" FOREIGN KEY (added_by) REFERENCES users(id) ON DELETE SET NULL
Access method: heap


### spam_check_configs
                                                             Table "public.spam_check_configs"
        Column        |          Type           | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
----------------------+-------------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id                   | bigint                  |           | not null | generated always as identity | plain    |             |              | 
 chat_id              | character varying(50)   |           | not null |                              | extended |             |              | 
 check_name           | character varying(50)   |           | not null |                              | extended |             |              | 
 enabled              | boolean                 |           | not null | true                         | plain    |             |              | 
 confidence_threshold | integer                 |           |          |                              | plain    |             |              | 
 configuration_json   | character varying(2000) |           |          |                              | extended |             |              | 
 modified_date        | bigint                  |           | not null |                              | plain    |             |              | 
 modified_by          | character varying(36)   |           |          |                              | extended |             |              | 
Indexes:
    "PK_spam_check_configs" PRIMARY KEY, btree (id)
    "idx_spam_check_configs_chat_id" btree (chat_id)
    "idx_spam_check_configs_check_name" btree (check_name)
    "uc_spam_check_configs_chat_check" UNIQUE CONSTRAINT, btree (chat_id, check_name)
Foreign-key constraints:
    "fk_spam_check_configs_modified_by" FOREIGN KEY (modified_by) REFERENCES users(id) ON DELETE SET NULL
Access method: heap


### spam_detection_configs
                                                       Table "public.spam_detection_configs"
    Column    |          Type           | Collation | Nullable |           Default            | Storage  | Compression | Stats target | Description 
--------------+-------------------------+-----------+----------+------------------------------+----------+-------------+--------------+-------------
 id           | bigint                  |           | not null | generated always as identity | plain    |             |              | 
 chat_id      | character varying(50)   |           |          |                              | extended |             |              | 
 config_json  | character varying(4000) |           | not null |                              | extended |             |              | 
 last_updated | bigint                  |           | not null |                              | plain    |             |              | 
 updated_by   | character varying(36)   |           |          |                              | extended |             |              | 
Indexes:
    "PK_spam_detection_configs" PRIMARY KEY, btree (id)
    "idx_spam_detection_configs_chat_id" btree (chat_id)
    "uc_spam_detection_configs_chat" UNIQUE CONSTRAINT, btree (chat_id)
Access method: heap



---

## All Indexes

                                                                 List of relations
 Schema |                 Name                  | Type  |  Owner  |         Table          | Persistence | Access method |    Size    | Description 
--------+---------------------------------------+-------+---------+------------------------+-------------+---------------+------------+-------------
 public | IX_stop_words_word                    | index | tgadmin | stop_words             | permanent   | btree         | 16 kB      | 
 public | IX_telegram_user_mappings_telegram_id | index | tgadmin | telegram_user_mappings | permanent   | btree         | 16 kB      | 
 public | IX_users_email                        | index | tgadmin | users                  | permanent   | btree         | 16 kB      | 
 public | IX_verification_tokens_token          | index | tgadmin | verification_tokens    | permanent   | btree         | 16 kB      | 
 public | PK_audit_log                          | index | tgadmin | audit_log              | permanent   | btree         | 16 kB      | 
 public | PK_chat_admins                        | index | tgadmin | chat_admins            | permanent   | btree         | 16 kB      | 
 public | PK_detection_results                  | index | tgadmin | detection_results      | permanent   | btree         | 16 kB      | 
 public | PK_group_prompts                      | index | tgadmin | chat_prompts           | permanent   | btree         | 8192 bytes | 
 public | PK_invites                            | index | tgadmin | invites                | permanent   | btree         | 16 kB      | 
 public | PK_managed_chats                      | index | tgadmin | managed_chats          | permanent   | btree         | 16 kB      | 
 public | PK_message_edits                      | index | tgadmin | message_edits          | permanent   | btree         | 8192 bytes | 
 public | PK_messages                           | index | tgadmin | messages               | permanent   | btree         | 16 kB      | 
 public | PK_recovery_codes                     | index | tgadmin | recovery_codes         | permanent   | btree         | 8192 bytes | 
 public | PK_reports                            | index | tgadmin | reports                | permanent   | btree         | 16 kB      | 
 public | PK_spam_check_configs                 | index | tgadmin | spam_check_configs     | permanent   | btree         | 8192 bytes | 
 public | PK_spam_detection_configs             | index | tgadmin | spam_detection_configs | permanent   | btree         | 16 kB      | 
 public | PK_stop_words                         | index | tgadmin | stop_words             | permanent   | btree         | 16 kB      | 
 public | PK_telegram_link_tokens               | index | tgadmin | telegram_link_tokens   | permanent   | btree         | 16 kB      | 
 public | PK_telegram_user_mappings             | index | tgadmin | telegram_user_mappings | permanent   | btree         | 16 kB      | 
 public | PK_user_actions                       | index | tgadmin | user_actions           | permanent   | btree         | 8192 bytes | 
 public | PK_users                              | index | tgadmin | users                  | permanent   | btree         | 16 kB      | 
 public | PK_verification_tokens                | index | tgadmin | verification_tokens    | permanent   | btree         | 16 kB      | 
 public | UC_Version                            | index | tgadmin | VersionInfo            | permanent   | btree         | 16 kB      | 
 public | idx_active_users_email                | index | tgadmin | users                  | permanent   | btree         | 16 kB      | 
 public | idx_audit_log_actor                   | index | tgadmin | audit_log              | permanent   | btree         | 16 kB      | 
 public | idx_audit_log_event_type              | index | tgadmin | audit_log              | permanent   | btree         | 16 kB      | 
 public | idx_audit_log_target                  | index | tgadmin | audit_log              | permanent   | btree         | 16 kB      | 
 public | idx_audit_log_target_event_time       | index | tgadmin | audit_log              | permanent   | btree         | 32 kB      | 
 public | idx_audit_log_timestamp               | index | tgadmin | audit_log              | permanent   | btree         | 16 kB      | 
 public | idx_chat_admins_chat_id               | index | tgadmin | chat_admins            | permanent   | btree         | 16 kB      | 
 public | idx_chat_admins_telegram_id           | index | tgadmin | chat_admins            | permanent   | btree         | 16 kB      | 
 public | idx_chat_name                         | index | tgadmin | messages               | permanent   | btree         | 16 kB      | 
 public | idx_chat_prompts_chat_id              | index | tgadmin | chat_prompts           | permanent   | btree         | 8192 bytes | 
 public | idx_content_hash                      | index | tgadmin | messages               | permanent   | btree         | 16 kB      | 
 public | idx_detection_results_detected_at     | index | tgadmin | detection_results      | permanent   | btree         | 16 kB      | 
 public | idx_detection_results_is_spam_source  | index | tgadmin | detection_results      | permanent   | btree         | 16 kB      | 
 public | idx_detection_results_message_id      | index | tgadmin | detection_results      | permanent   | btree         | 16 kB      | 
 public | idx_enabled_stop_words_word           | index | tgadmin | stop_words             | permanent   | btree         | 16 kB      | 
 public | idx_group_prompts_enabled             | index | tgadmin | chat_prompts           | permanent   | btree         | 8192 bytes | 
 public | idx_invites_created_by                | index | tgadmin | invites                | permanent   | btree         | 16 kB      | 
 public | idx_invites_creator_status            | index | tgadmin | invites                | permanent   | btree         | 16 kB      | 
 public | idx_invites_expires                   | index | tgadmin | invites                | permanent   | btree         | 16 kB      | 
 public | idx_invites_status                    | index | tgadmin | invites                | permanent   | btree         | 16 kB      | 
 public | idx_managed_chats_active              | index | tgadmin | managed_chats          | permanent   | btree         | 16 kB      | 
 public | idx_managed_chats_admin               | index | tgadmin | managed_chats          | permanent   | btree         | 16 kB      | 
 public | idx_message_edits_msg                 | index | tgadmin | message_edits          | permanent   | btree         | 8192 bytes | 
 public | idx_messages_chat_id_timestamp        | index | tgadmin | messages               | permanent   | btree         | 16 kB      | 
 public | idx_messages_deleted                  | index | tgadmin | messages               | permanent   | btree         | 8192 bytes | 
 public | idx_messages_user_chat_time           | index | tgadmin | messages               | permanent   | btree         | 32 kB      | 
 public | idx_pending_invites_expires           | index | tgadmin | invites                | permanent   | btree         | 8192 bytes | 
 public | idx_recovery_codes_user               | index | tgadmin | recovery_codes         | permanent   | btree         | 8192 bytes | 
 public | idx_reports_chat_status               | index | tgadmin | reports                | permanent   | btree         | 16 kB      | 
 public | idx_reports_reported_at               | index | tgadmin | reports                | permanent   | btree         | 16 kB      | 
 public | idx_reports_status                    | index | tgadmin | reports                | permanent   | btree         | 16 kB      | 
 public | idx_spam_check_configs_chat_id        | index | tgadmin | spam_check_configs     | permanent   | btree         | 8192 bytes | 
 public | idx_spam_check_configs_check_name     | index | tgadmin | spam_check_configs     | permanent   | btree         | 8192 bytes | 
 public | idx_spam_detection_configs_chat_id    | index | tgadmin | spam_detection_configs | permanent   | btree         | 16 kB      | 
 public | idx_stop_words_enabled                | index | tgadmin | stop_words             | permanent   | btree         | 16 kB      | 
 public | idx_telegram_link_tokens_expires_at   | index | tgadmin | telegram_link_tokens   | permanent   | btree         | 16 kB      | 
 public | idx_telegram_link_tokens_user_id      | index | tgadmin | telegram_link_tokens   | permanent   | btree         | 16 kB      | 
 public | idx_telegram_user_mappings_user_id    | index | tgadmin | telegram_user_mappings | permanent   | btree         | 16 kB      | 
 public | idx_user_actions_action_type          | index | tgadmin | user_actions           | permanent   | btree         | 8192 bytes | 
 public | idx_user_actions_issued_at            | index | tgadmin | user_actions           | permanent   | btree         | 8192 bytes | 
 public | idx_user_actions_user_id              | index | tgadmin | user_actions           | permanent   | btree         | 8192 bytes | 
 public | idx_user_chat_photo                   | index | tgadmin | messages               | permanent   | btree         | 8192 bytes | 
 public | idx_user_chat_timestamp               | index | tgadmin | messages               | permanent   | btree         | 32 kB      | 
 public | idx_users_is_active                   | index | tgadmin | users                  | permanent   | btree         | 16 kB      | 
 public | idx_users_modified_at                 | index | tgadmin | users                  | permanent   | btree         | 16 kB      | 
 public | idx_users_normalized_email            | index | tgadmin | users                  | permanent   | btree         | 16 kB      | 
 public | idx_users_permission_level            | index | tgadmin | users                  | permanent   | btree         | 16 kB      | 
 public | idx_users_status                      | index | tgadmin | users                  | permanent   | btree         | 16 kB      | 
 public | idx_valid_verification_tokens         | index | tgadmin | verification_tokens    | permanent   | btree         | 16 kB      | 
 public | idx_verification_tokens_token         | index | tgadmin | verification_tokens    | permanent   | btree         | 16 kB      | 
 public | idx_verification_tokens_type          | index | tgadmin | verification_tokens    | permanent   | btree         | 16 kB      | 
 public | idx_verification_tokens_user_id       | index | tgadmin | verification_tokens    | permanent   | btree         | 16 kB      | 
 public | idx_verification_tokens_user_type     | index | tgadmin | verification_tokens    | permanent   | btree         | 16 kB      | 
 public | uc_spam_check_configs_chat_check      | index | tgadmin | spam_check_configs     | permanent   | btree         | 8192 bytes | 
 public | uc_spam_detection_configs_chat        | index | tgadmin | spam_detection_configs | permanent   | btree         | 16 kB      | 
 public | uq_chat_admins_chat_telegram          | index | tgadmin | chat_admins            | permanent   | btree         | 16 kB      | 
(79 rows)


---

## Foreign Keys Summary

       table_name       |  column_name   | foreign_table_name | foreign_column_name |                   constraint_name                   
------------------------+----------------+--------------------+---------------------+-----------------------------------------------------
 audit_log              | actor_user_id  | users              | id                  | fk_audit_log_actor
 audit_log              | target_user_id | users              | id                  | fk_audit_log_target
 chat_admins            | chat_id        | managed_chats      | chat_id             | fk_chat_admins_chat_id
 chat_prompts           | added_by       | users              | id                  | fk_chat_prompts_added_by
 detection_results      | added_by       | users              | id                  | FK_detection_results_added_by_users_id
 detection_results      | message_id     | messages           | message_id          | FK_detection_results_message_id_messages_message_id
 invites                | created_by     | users              | id                  | FK_invites_created_by_users_id
 invites                | used_by        | users              | id                  | FK_invites_used_by_users_id
 message_edits          | message_id     | messages           | message_id          | FK_message_edits_message_id_messages_message_id
 recovery_codes         | user_id        | users              | id                  | FK_recovery_codes_user_id_users_id
 spam_check_configs     | modified_by    | users              | id                  | fk_spam_check_configs_modified_by
 stop_words             | added_by       | users              | id                  | fk_stop_words_added_by
 telegram_link_tokens   | user_id        | users              | id                  | fk_telegram_link_tokens_user_id
 telegram_user_mappings | user_id        | users              | id                  | fk_telegram_user_mappings_user_id
 user_actions           | issued_by      | users              | id                  | FK_user_actions_issued_by_users_id
 user_actions           | message_id     | messages           | message_id          | FK_user_actions_message_id_messages_message_id
 users                  | invited_by     | users              | id                  | fk_users_invited_by
 users                  | modified_by    | users              | id                  | fk_users_modified_by
 verification_tokens    | user_id        | users              | id                  | fk_verification_tokens_user
(19 rows)

