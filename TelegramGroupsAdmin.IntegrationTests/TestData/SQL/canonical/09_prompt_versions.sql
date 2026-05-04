INSERT INTO prompt_versions (id, chat_id, version, prompt_text, is_active, created_at, created_by, generation_metadata) VALUES (1, -100026957614982, 1, '### SPAM indicators (mark as "spam"):
- Investment or financial-return solicitation: promises of guaranteed profits, "huge returns," "get rich quick," "guaranteed ROI," forex/crypto trading pitches, or recruitment to investment platforms.
- Crypto airdrop, token, or wallet-claim hype: "claim now," "last day," "free tokens," wallet-check prompts, or instructions to connect a wallet.
- Multi-level marketing, referral pyramids, or recruitment language: "DM me to start," "mentor," "signals," "training," "join my program," testimonials, or screenshots-of-earnings claims.
- Off-topic promotional content: ads, lead-generation, calls to subscribe, follow, or check external links unrelated to the group''s stated topic.
- Obfuscated or evasion patterns: zero-width characters, unicode confusables, letters separated by invisible characters, deliberate misspellings to bypass filters.
- Generic mass-broadcast greetings followed by a sales pitch or external link with no on-topic context.
- Impersonation or credibility padding: name-dropping unrelated authorities, fabricated credentials, or "I know them personally" used to push solicitation.
- Repeated posting of the same message, link, or pitch across short time windows.
- Bot-like patterns: copy-paste templates, no engagement with the conversation, no responses to other users.
### LEGITIMATE content (mark as "clean"):
- On-topic discussion related to the group''s stated purpose, including questions, answers, opinions, and constructive disagreement.
- Personal updates, introductions, and community banter that are not solicitations even when slightly off-topic.
- Sharing of relevant articles, tutorials, videos, events, or external resources that support the group''s topic.
- Technical or work-related discussion that is conversational and not used to sell a service.
- Fundraisers or requests for help that are clearly personal or community-based and do not use scam-style urgency or recruitment tactics.
- Constructive criticism, jokes, moderation-related comments, or meta-discussion about the group itself.', true, '2025-10-15 00:00:00+00', 'canonical@example.com', NULL);
SELECT pg_catalog.setval('prompt_versions_id_seq', 1, false);
