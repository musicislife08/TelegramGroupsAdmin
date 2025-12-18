-- Base Messages for Integration Tests
-- Contains 11 messages from real production data (PII redacted)
-- FK: user_id references telegram_users.telegram_user_id
-- FK: chat_id references managed_chats.chat_id
-- MUST be loaded after 00_base_telegram_users.sql and 02_base_managed_chats.sql
-- Message IDs: 82581, 82594, 82596, 82603, 82606, 82612, 82615-82619

INSERT INTO messages (message_id, user_id, chat_id, timestamp, message_text, media_type, content_check_skip_reason)
VALUES
-- Msg1 (82619): Short conversational message
(82619, 100002, -1001322973935, NOW() - INTERVAL '1 hour', 'Fair enough', NULL, 2),
-- Msg2 (82618): Casual chat with emoji (additional user)
(82618, 1232994248, -1001322973935, NOW() - INTERVAL '2 hours', E'He was old and crusty 20yrs ago 😂.  I''m guessing he''s had enough.', NULL, 2),
-- Msg3 (82617): Question format
(82617, 100002, -1001322973935, NOW() - INTERVAL '3 hours', E'Get while the getting''s good?', NULL, 2),
-- Msg4 (82616): Longer opinion message
(82616, 100002, -1001322973935, NOW() - INTERVAL '4 hours', 'Sounds like he may wanna do it again at some point. I know looking at the state of things I might want to be in it while watching whatever is gonna go down in the economy', NULL, 2),
-- Msg5 (82615): Professional observation
(82615, 1232994248, -1001322973935, NOW() - INTERVAL '5 hours', 'Sad to see him ride out, he definitely knows how to run an org.', NULL, 2),
-- Msg6 (82612): Media only (photo, no text)
(82612, 1232994248, -1001322973935, NOW() - INTERVAL '6 hours', NULL, 1, 2),
-- Msg7 (82603): Multi-paragraph technical opinion
(82603, 100002, -1001322973935, NOW() - INTERVAL '7 hours', E'OK, I feel as though I can now say this from a position of competency, trial and error success.\n\nJust pay the feckin $20.\n\nEven with a used car value worth of GPUs, there is not a single sumbitchin local LLM to approach the effectiveness of even the $20 Claude models with Code CLI.\n\nI feel like I have a very expensive chat bot that feeds me a chain of errors to correct in my shed.', NULL, 2),
-- Msg8 (82606): Professional networking (different skip reason)
(82606, 934156131, -1001322973935, NOW() - INTERVAL '8 hours', E'I''ve been active with the American Institute of Architects large firm Roundtable for years. We have an email list. I emailed every single General Counsel at the top 20 architectural firms, and that''s how I got this job and why I''m talking to the other firm too.', NULL, 1),
-- Msg9 (82596): Healthcare tech experience
(82596, 468009795, -1001322973935, NOW() - INTERVAL '9 hours', 'I have close to 8 years of experience in Healthcare tech. So I have that momentum.', NULL, 2),
-- Msg10 (82594): Software engineering context
(82594, 468009795, -1001322973935, NOW() - INTERVAL '10 hours', E'I''m a software engineering manager.  Like I''m the boss of people who write the code.', NULL, 2),
-- Msg11 (82581): Short personal message (used for detection result)
(82581, 100001, -1001322973935, NOW() - INTERVAL '11 hours', 'I hit 30 last summer.', NULL, 0);
