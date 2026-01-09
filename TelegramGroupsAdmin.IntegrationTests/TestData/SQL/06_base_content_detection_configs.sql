-- Base Content Detection Configs for Integration Tests
-- Contains global content detection configuration (chat_id = 0)
-- No FK dependencies - can be loaded independently
-- NOTE: Property names are PascalCase to match C# property names (EF Core ToJson requirement)

INSERT INTO content_detection_configs (id, chat_id, config_json, last_updated, updated_by)
VALUES
(1, 0, '{{
  "FirstMessageOnly": true,
  "FirstMessagesCount": 3,
  "AutoTrustMinMessageLength": 20,
  "AutoTrustMinAccountAgeHours": 24,
  "MinMessageLength": 10,
  "AutoBanThreshold": 80,
  "ReviewQueueThreshold": 50,
  "MaxConfidenceVetoThreshold": 85,
  "TrainingMode": false,
  "StopWords": {{ "UseGlobal": true, "Enabled": true, "ConfidenceThreshold": 50, "AlwaysRun": false }},
  "Similarity": {{ "UseGlobal": true, "Enabled": true, "Threshold": 0.5, "AlwaysRun": false }},
  "Cas": {{ "UseGlobal": true, "Enabled": true, "ApiUrl": "https://api.cas.chat", "TimeoutSeconds": 5, "AlwaysRun": false }},
  "Bayes": {{ "UseGlobal": true, "Enabled": true, "MinSpamProbability": 50, "AlwaysRun": false }},
  "InvisibleChars": {{ "UseGlobal": true, "Enabled": true, "AlwaysRun": false }},
  "Translation": {{ "UseGlobal": true, "Enabled": false, "AlwaysRun": false }},
  "Spacing": {{ "UseGlobal": true, "Enabled": true, "MinWordsCount": 5, "SpaceRatioThreshold": 0.3, "AlwaysRun": false }},
  "AIVeto": {{ "UseGlobal": true, "Enabled": true, "CheckShortMessages": false, "MessageHistoryCount": 3, "ConfidenceThreshold": 85, "AlwaysRun": false }},
  "UrlBlocklist": {{ "UseGlobal": true, "Enabled": true, "AlwaysRun": false }},
  "ThreatIntel": {{ "UseGlobal": true, "Enabled": true, "TimeoutSeconds": 30, "UseVirusTotal": true, "AlwaysRun": false }},
  "SeoScraping": {{ "UseGlobal": true, "Enabled": false, "AlwaysRun": false }},
  "ImageSpam": {{ "UseGlobal": true, "Enabled": false, "AlwaysRun": false }},
  "VideoSpam": {{ "UseGlobal": true, "Enabled": false, "AlwaysRun": false }},
  "FileScanning": {{ "UseGlobal": true, "Enabled": true, "AlwaysRun": true }}
}}'::jsonb, NOW() - INTERVAL '10 days', 'GoldenDataset');
