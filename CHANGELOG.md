# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Pending Features

- Notification preferences UI
- Advanced analytics dashboards
- Webhook support for CI/CD integration
- Multi-bot management
- Custom rule engine

---

## [0.9.0] - 2025-10-31 (Pre-Release)

### Added

- **ML-6: Video Spam Detection** - 3-layer system with keyframe hash similarity, OCR on frames, and OpenAI Vision fallback
- **ML-5: Image Spam Detection** - 3-layer system with perceptual hash similarity, OCR text extraction, and OpenAI Vision fallback
- Tesseract OCR integration with graceful binary auto-detection
- FFmpeg 7.0.2 integration for video frame extraction
- `image_training_samples` and `video_training_samples` database tables
- Bot message tracking and database storage (Phase 1)
- Translation support for training samples (eliminates double translation)
- Passphrase prompt for encrypted backup restore workflow
- Auto-disable bot after successful backup restore (prevents dual instances)
- Consolidated spam DM notifications with media support
- Auto-detection report display with DM notifications
- Silent moderation mode - removes chat feedback from `/ban` and `/spam` commands
- Dynamic bot enable/disable toggle with clean network error logging
- Database-driven bot reconnection system

### Changed

- Rewrite training samples query for proper EF Core translation
- Improved admin cache refresh to be immediate on bot promotion
- Enhanced detection history and view edits menu functionality
- Better handling of username placeholders in DM welcome messages

### Fixed

- Detection History and View Edits menu items not working
- Username placeholder not creating clickable mention in DM welcome message
- LINQ translation error in training sample queries
- Form binding issues in check result serialization
- DbContext threading issue in BotGeneralSettings
- StopWordsSpamCheck OrderBy for deterministic `Take()` results

### Security

- **SECURITY-1: Git History Sanitization** - BFG purged sensitive files from 660 commits, added pre-commit hook with 8 secret patterns
- **SECURITY-5: Settings Page Authorization** - Fixed bypass with GlobalAdminOrOwner policy
- **SECURITY-6: User Management Permissions** - Added permission checks, prevent privilege escalation
- Comprehensive `.gitignore` for .NET secrets

### Refactoring

- **REFACTOR-6**: Split ModelMappings.cs (884 lines → 26 files in Mappings/ subdirectory)
- **REFACTOR-3**: Extract focused services from MessageHistoryRepository (Phase 1-3)
- **CODE-9**: Removed reflection in MessageProcessingService, extracted CheckResultsSerializer
- **CODE-8**: Removed 157× unnecessary `ConfigureAwait` calls
- **CODE-1 + CODE-2**: Complete code organization overhaul (~60 files → 140+ individual files)
- **CODE-10**: Pre-release legacy code cleanup
- Comprehensive C# 12 modernization and code cleanup
- Split interfaces from implementations (one type per file)

### Testing

- Added 39 unit tests for handler static methods
- Improved MessageHistoryRepository test suite quality
- Added comprehensive test coverage for MessageHistoryRepository

### Documentation

- Added cSpell configuration for domain-specific terms (29 terms, 0 warnings)
- Removed outdated TELEGRAM_USERS_UI.md
- Added Docker ZFS to Overlay2 migration plan

---

## [0.8.0] - 2025-10-27

### Added

- **ANALYTICS-4: Welcome System Analytics** - Join trends, response distribution, per-chat stats with timezone-aware queries
- `/analytics#welcome` tab with 4 new repository methods
- Audit log foreign key cascade rules for proper user deletion

### Changed

- Renamed TotpProtectionService → DataProtectionService for clarity
- Improved audit logging coverage across application

### Fixed

- **SCHEMA-1**: User deletion now works properly with cascade rules (migration 20251027002019)
- Fixed 7 file name mismatches during code organization

### Performance

- **PERF-CD-3**: Domain stats 2.24× faster (55ms → 25ms via GroupBy aggregation)
- **PERF-CD-4**: TF-IDF 2.59× faster with 44% less memory (Dictionary counting + pre-indexed vocabulary)
- **PERF-3**: Trust context early exit optimization

### Testing

- All 22/22 migration tests passing with Testcontainers.PostgreSQL

---

## [0.7.0] - 2025-10-26

### Added

- Missing `/logout` page (existed since Oct 6)

### Security

- **SECURITY-2**: Fixed open redirect vulnerability with `UrlHelpers.IsLocalUrl()` validation on all auth redirects

---

## [0.6.0] - 2025-10-25

### Added

- **BUG-1**: False negative tracking - analytics now shows both FP and FN rates with overall accuracy metrics

---

## [0.5.0] - 2025-10-24

### Added

- **ARCH-2**: Actor exclusive arc migration complete
- 6 new columns in audit_log table
- 35+ call sites updated for new audit architecture

### Changed

- Successfully migrated 84 audit log rows to new schema
- Updated UI for new audit log structure

---

## [0.4.0] - 2025-10-22

### Performance

- **PERF-CD-3**: Domain statistics 2.24× faster (55ms → 25ms)
- **PERF-CD-4**: TF-IDF similarity 2.59× faster, 44% less memory usage

---

## [0.3.0] - 2025-10-21

### Added

- **ARCH-1**: Core library extraction (eliminated 544 lines of duplication)
- **DI-1**: Created interfaces for 4 repositories
- Comprehensive audit logging coverage across application
- BlazorAuthHelper DRY refactoring (19 instances consolidated)

### Performance

- **PERF-APP-1**: User management N+1 query elimination
- **PERF-APP-3**: Configuration caching improvements
- Parallel ban operations
- Composite index optimization
- MudVirtualize for message list
- Record type conversion optimization
- Memory leak fix in message processing
- Allocation optimization in spam detection

### Testing

- Empirical performance testing framework
- Removed PERF-CD-1 via PostgreSQL profiling (original estimates were 100-300× off)

---

## [0.2.0] - 2025-10-19

### Performance

- 8 major performance optimizations:
  1. Users page N+1 query elimination
  2. Configuration caching implementation
  3. Parallel ban operations
  4. Composite index for messages table
  5. MudVirtualize for message list
  6. Record type conversion optimization
  7. Memory leak fix
  8. Allocation optimization

---

## [0.1.0] - Initial Release

### Added

- 9-algorithm spam detection system
- Multi-chat support with automatic discovery
- Blazor Server web UI with MudBlazor
- PostgreSQL database with EF Core
- TOTP 2FA (mandatory for all accounts)
- Email verification system
- Encrypted API key storage
- User permission hierarchy (Admin → GlobalAdmin → Owner)
- Backup/restore with encryption
- Message monitoring with infinite scroll
- User management (temp bans, permanent bans, cross-chat enforcement)
- Welcome system with auto-kick
- Spam reports for borderline cases
- Audit logging
- Analytics dashboard
- File scanning (ClamAV + VirusTotal)
- Impersonation detection
- Edit detection and re-scanning
- DM notification system
- URL filtering with blocklists
- Self-learning spam detection
- AI-powered image analysis
- Translation for non-Latin messages
- Prompt builder for custom spam detection
- Docker containerized deployment

### Tech Stack

- .NET 9.0
- Blazor Server
- PostgreSQL 17
- TickerQ background jobs
- OpenAI GPT-4 and Vision API
- VirusTotal integration
- SendGrid email service
- ClamAV virus scanning

---

## Version History Summary

| Version | Date | Highlights |
|---------|------|------------|
| 0.9.0 | 2025-10-31 | Video/Image spam detection, security fixes, major refactoring |
| 0.8.0 | 2025-10-27 | Welcome analytics, audit improvements, performance gains |
| 0.7.0 | 2025-10-26 | Security fixes (open redirect) |
| 0.6.0 | 2025-10-25 | False negative tracking |
| 0.5.0 | 2025-10-24 | Audit architecture overhaul |
| 0.4.0 | 2025-10-22 | Performance optimizations |
| 0.3.0 | 2025-10-21 | Core library, DI improvements, 8 performance fixes |
| 0.2.0 | 2025-10-19 | Major performance optimization release |
| 0.1.0 | - | Initial feature-complete release |

---

[Unreleased]: https://github.com/weekenders/TelegramGroupsAdmin/compare/v0.9.0...HEAD
[0.9.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.9.0
[0.8.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.8.0
[0.7.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.7.0
[0.6.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.6.0
[0.5.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.5.0
[0.4.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.4.0
[0.3.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.3.0
[0.2.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.2.0
[0.1.0]: https://github.com/weekenders/TelegramGroupsAdmin/releases/tag/v0.1.0
