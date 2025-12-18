-- Base Web Users (ASP.NET Identity) for Integration Tests
-- Contains 4 users with various permission levels and statuses
-- FK: invited_by references users.id (self-referencing)
-- MUST be loaded after 00_base_telegram_users.sql (no direct FK, but follows convention)

INSERT INTO users (id, email, normalized_email, password_hash, security_stamp, permission_level, invited_by, is_active, totp_secret, totp_enabled, totp_setup_started_at, created_at, last_login_at, status, email_verified, "InvitedByUserId")
VALUES
-- User1: Owner (permission_level=2), Active, Email verified, TOTP enabled
('b388ee38-0ed3-4c09-9def-5715f9f07f56', 'owner@example.com', 'OWNER@EXAMPLE.COM', 'AQAAAAIAAYagAAAAEDummyHashForTestingOnly1234567890', 'TEST_SECURITY_STAMP', 2, NULL, TRUE, NULL, TRUE, NULL, NOW() - INTERVAL '14 days', NOW(), 1, TRUE, NULL),
-- User2: Admin (permission_level=0), Active, invited by User1, Email verified, TOTP enabled
('921637d5-0f65-4c66-b143-6f057dd06a1c', 'admin@example.com', 'ADMIN@EXAMPLE.COM', 'AQAAAAIAAYagAAAAEDummyHashForTestingOnly1234567890', 'TEST_SECURITY_STAMP', 0, 'b388ee38-0ed3-4c09-9def-5715f9f07f56', TRUE, NULL, TRUE, NULL, NOW() - INTERVAL '13 days', NOW(), 1, TRUE, 'b388ee38-0ed3-4c09-9def-5715f9f07f56'),
-- User3: Admin (permission_level=0), Deleted (status=3), invited by User1
('a8dc8371-afc5-4b61-9d71-d177f2dd9ddd', 'deleted@example.com', 'DELETED@EXAMPLE.COM', 'AQAAAAIAAYagAAAAEDummyHashForTestingOnly1234567890', 'TEST_SECURITY_STAMP', 0, 'b388ee38-0ed3-4c09-9def-5715f9f07f56', FALSE, NULL, FALSE, NULL, NOW() - INTERVAL '12 days', NULL, 3, FALSE, 'b388ee38-0ed3-4c09-9def-5715f9f07f56'),
-- User4: GlobalAdmin (permission_level=1), Deleted (status=3), invited by User2
('ba9ba542-3df6-4473-a820-578562780c57', 'globaladmin@example.com', 'GLOBALADMIN@EXAMPLE.COM', 'AQAAAAIAAYagAAAAEDummyHashForTestingOnly1234567890', 'TEST_SECURITY_STAMP', 1, '921637d5-0f65-4c66-b143-6f057dd06a1c', FALSE, NULL, FALSE, NULL, NOW() - INTERVAL '11 days', NULL, 3, FALSE, '921637d5-0f65-4c66-b143-6f057dd06a1c');
