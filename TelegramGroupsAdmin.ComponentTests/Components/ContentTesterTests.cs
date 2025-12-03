// =============================================================================
// BLOCKED: Interfaces required for component testing
// =============================================================================
// This component cannot be tested with bUnit until interfaces are extracted
// for the following concrete services (NSubstitute cannot mock classes without
// parameterless constructors):
//
//   - TranslationHandler → ITranslationHandler
//
// See GitHub Issue #23: "REFACTOR-18: Extract Interfaces for Integration Testing"
// https://github.com/musicislife08/TelegramGroupsAdmin/issues/23
//
// Workaround: Test via Playwright E2E tests instead.
// =============================================================================

namespace TelegramGroupsAdmin.ComponentTests.Components;

// Component tests for ContentTester.razor will be added after issue #23 is resolved.
