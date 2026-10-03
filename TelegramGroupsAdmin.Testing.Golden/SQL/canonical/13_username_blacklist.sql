INSERT INTO username_blacklist (id, pattern, match_type, enabled, created_at, notes) VALUES (999001, 'spambot_admin', 0, true, '2026-01-01 00:00:00+00', 'Exact match — known impersonation handle');
INSERT INTO username_blacklist (id, pattern, match_type, enabled, created_at, notes) VALUES (999005, 'archived_pattern', 0, false, '2026-01-01 00:00:00+00', 'Disabled — historical pattern kept for tests');
SELECT pg_catalog.setval('username_blacklist_id_seq', 999005, true);
