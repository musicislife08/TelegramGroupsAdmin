-- ML Training Data for Integration Tests
-- Provides 20 spam + 20 ham samples for ML.NET SDCA classifier minimum threshold (20 per class)
-- Message IDs: 90001-90040 (to avoid conflicts with GoldenDataset 826XX range)
-- Uses GoldenDataset telegram_user_id: 549755813888 (User1), chat_id: -1002152207375 (MainChat)

-- Insert messages (spam samples 90001-90020, ham samples 90021-90040)
INSERT INTO messages (message_id, user_id, chat_id, timestamp, message_text, content_check_skip_reason)
VALUES
-- 20 Spam samples
(90001, 549755813888, -1002152207375, NOW() - INTERVAL '1 hour', 'Earn passive income with Bitcoin! Message me for details on this amazing opportunity.', 0),
(90002, 549755813888, -1002152207375, NOW() - INTERVAL '2 hours', 'I buy USDT cash in person or by transfer. Call me now for best rates.', 0),
(90003, 549755813888, -1002152207375, NOW() - INTERVAL '3 hours', 'FREE CRYPTO GIVEAWAY! Click here to claim your $1000 bonus now!', 0),
(90004, 549755813888, -1002152207375, NOW() - INTERVAL '4 hours', 'Work from home! Earn up to $5000 per week trading cryptocurrency.', 0),
(90005, 549755813888, -1002152207375, NOW() - INTERVAL '5 hours', 'Join our investment group! Guaranteed 10% daily returns on your deposit.', 0),
(90006, 549755813888, -1002152207375, NOW() - INTERVAL '6 hours', 'Looking for dates? Text me now for November availability and special pricing.', 0),
(90007, 549755813888, -1002152207375, NOW() - INTERVAL '7 hours', 'Check out this amazing crypto arbitrage opportunity between Binance and Bybit!', 0),
(90008, 549755813888, -1002152207375, NOW() - INTERVAL '8 hours', 'Need SIP trunk, DID numbers, or call center solutions? Contact @spam_bot now!', 0),
(90009, 549755813888, -1002152207375, NOW() - INTERVAL '9 hours', 'Anyone need site for ordering iPhones and PS5 consoles cheap? Message me.', 0),
(90010, 549755813888, -1002152207375, NOW() - INTERVAL '10 hours', 'Steady payouts every week thanks to our amazing trading experts!', 0),
(90011, 549755813888, -1002152207375, NOW() - INTERVAL '11 hours', 'NO RISK! We manage your funds under supervision. Withdraw profits anytime!', 0),
(90012, 549755813888, -1002152207375, NOW() - INTERVAL '12 hours', 'Join my Telegram for exclusive forex signals and guaranteed profits!', 0),
(90013, 549755813888, -1002152207375, NOW() - INTERVAL '13 hours', 'Selling verified Coinbase accounts with $500 balance. Best prices!', 0),
(90014, 549755813888, -1002152207375, NOW() - INTERVAL '14 hours', 'Get rich quick with our automated bot! No experience needed!', 0),
(90015, 549755813888, -1002152207375, NOW() - INTERVAL '15 hours', 'Binary options trading course - make $10k in your first week!', 0),
(90016, 549755813888, -1002152207375, NOW() - INTERVAL '16 hours', 'URGENT: Your account has been compromised. Click here to verify immediately!', 0),
(90017, 549755813888, -1002152207375, NOW() - INTERVAL '17 hours', 'Congratulations! You won the lottery. Send processing fee to claim prize.', 0),
(90018, 549755813888, -1002152207375, NOW() - INTERVAL '18 hours', 'Hot singles in your area want to meet you! Click to chat now!', 0),
(90019, 549755813888, -1002152207375, NOW() - INTERVAL '19 hours', 'Pharmacy discounts - get prescription meds without doctor visit!', 0),
(90020, 549755813888, -1002152207375, NOW() - INTERVAL '20 hours', 'Dropshipping course 90% off today only! Limited spots available!', 0),

-- 20 Ham samples
(90021, 549755813888, -1002152207375, NOW() - INTERVAL '21 hours', 'Has anyone tried the new restaurant downtown? Looking for recommendations.', 0),
(90022, 549755813888, -1002152207375, NOW() - INTERVAL '22 hours', 'I am working on a machine learning project for spam detection using ML.NET and finding it quite interesting.', 0),
(90023, 549755813888, -1002152207375, NOW() - INTERVAL '23 hours', 'The weather has been really nice lately. Perfect for hiking and outdoor activities.', 0),
(90024, 549755813888, -1002152207375, NOW() - INTERVAL '24 hours', 'Just finished reading a great book on software architecture patterns. Highly recommend it!', 0),
(90025, 549755813888, -1002152207375, NOW() - INTERVAL '25 hours', 'Does anyone know a good tutorial for learning Blazor Server? I am just getting started.', 0),
(90026, 549755813888, -1002152207375, NOW() - INTERVAL '26 hours', 'The latest conference was excellent. Learned a lot about distributed systems and microservices.', 0),
(90027, 549755813888, -1002152207375, NOW() - INTERVAL '27 hours', 'My team is hiring a senior software engineer. If interested, let me know and I can share details.', 0),
(90028, 549755813888, -1002152207375, NOW() - INTERVAL '28 hours', 'I have been using PostgreSQL for years and it has been rock solid for production workloads.', 0),
(90029, 549755813888, -1002152207375, NOW() - INTERVAL '29 hours', 'Anyone attending the meetup next week? Would be great to catch up with everyone.', 0),
(90030, 549755813888, -1002152207375, NOW() - INTERVAL '30 hours', 'Just deployed a new feature to production. Monitoring metrics closely to ensure stability.', 0),
(90031, 549755813888, -1002152207375, NOW() - INTERVAL '31 hours', 'Working from home has its benefits but I do miss the office collaboration sometimes.', 0),
(90032, 549755813888, -1002152207375, NOW() - INTERVAL '32 hours', 'The new .NET release has some really impressive performance improvements. Excited to try it out.', 0),
(90033, 549755813888, -1002152207375, NOW() - INTERVAL '33 hours', 'Code reviews are essential for maintaining quality, even though they can be time consuming.', 0),
(90034, 549755813888, -1002152207375, NOW() - INTERVAL '34 hours', 'I prefer using Entity Framework Core for database access. The migration system works really well.', 0),
(90035, 549755813888, -1002152207375, NOW() - INTERVAL '35 hours', 'Testing is important but finding the right balance between unit tests and integration tests can be tricky.', 0),
(90036, 549755813888, -1002152207375, NOW() - INTERVAL '36 hours', 'Docker has made deployment so much easier. Containers are a game changer for DevOps workflows.', 0),
(90037, 549755813888, -1002152207375, NOW() - INTERVAL '37 hours', 'GitHub Actions is quite powerful for CI/CD pipelines. We migrated from Jenkins and very happy.', 0),
(90038, 549755813888, -1002152207375, NOW() - INTERVAL '38 hours', 'Refactoring legacy code is challenging but very rewarding when you see the improvements.', 0),
(90039, 549755813888, -1002152207375, NOW() - INTERVAL '39 hours', 'Documentation is often overlooked but it is crucial for team collaboration and onboarding.', 0),
(90040, 549755813888, -1002152207375, NOW() - INTERVAL '40 hours', 'I have been learning Kubernetes and it is complex but the benefits for orchestration are clear.', 0);

-- Insert training labels (spam = 0, ham = 1)
INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at)
VALUES
-- 20 Spam labels
(90001, 0, NULL, NOW() - INTERVAL '1 hour'),
(90002, 0, NULL, NOW() - INTERVAL '2 hours'),
(90003, 0, NULL, NOW() - INTERVAL '3 hours'),
(90004, 0, NULL, NOW() - INTERVAL '4 hours'),
(90005, 0, NULL, NOW() - INTERVAL '5 hours'),
(90006, 0, NULL, NOW() - INTERVAL '6 hours'),
(90007, 0, NULL, NOW() - INTERVAL '7 hours'),
(90008, 0, NULL, NOW() - INTERVAL '8 hours'),
(90009, 0, NULL, NOW() - INTERVAL '9 hours'),
(90010, 0, NULL, NOW() - INTERVAL '10 hours'),
(90011, 0, NULL, NOW() - INTERVAL '11 hours'),
(90012, 0, NULL, NOW() - INTERVAL '12 hours'),
(90013, 0, NULL, NOW() - INTERVAL '13 hours'),
(90014, 0, NULL, NOW() - INTERVAL '14 hours'),
(90015, 0, NULL, NOW() - INTERVAL '15 hours'),
(90016, 0, NULL, NOW() - INTERVAL '16 hours'),
(90017, 0, NULL, NOW() - INTERVAL '17 hours'),
(90018, 0, NULL, NOW() - INTERVAL '18 hours'),
(90019, 0, NULL, NOW() - INTERVAL '19 hours'),
(90020, 0, NULL, NOW() - INTERVAL '20 hours'),

-- 20 Ham labels
(90021, 1, NULL, NOW() - INTERVAL '21 hours'),
(90022, 1, NULL, NOW() - INTERVAL '22 hours'),
(90023, 1, NULL, NOW() - INTERVAL '23 hours'),
(90024, 1, NULL, NOW() - INTERVAL '24 hours'),
(90025, 1, NULL, NOW() - INTERVAL '25 hours'),
(90026, 1, NULL, NOW() - INTERVAL '26 hours'),
(90027, 1, NULL, NOW() - INTERVAL '27 hours'),
(90028, 1, NULL, NOW() - INTERVAL '28 hours'),
(90029, 1, NULL, NOW() - INTERVAL '29 hours'),
(90030, 1, NULL, NOW() - INTERVAL '30 hours'),
(90031, 1, NULL, NOW() - INTERVAL '31 hours'),
(90032, 1, NULL, NOW() - INTERVAL '32 hours'),
(90033, 1, NULL, NOW() - INTERVAL '33 hours'),
(90034, 1, NULL, NOW() - INTERVAL '34 hours'),
(90035, 1, NULL, NOW() - INTERVAL '35 hours'),
(90036, 1, NULL, NOW() - INTERVAL '36 hours'),
(90037, 1, NULL, NOW() - INTERVAL '37 hours'),
(90038, 1, NULL, NOW() - INTERVAL '38 hours'),
(90039, 1, NULL, NOW() - INTERVAL '39 hours'),
(90040, 1, NULL, NOW() - INTERVAL '40 hours');
