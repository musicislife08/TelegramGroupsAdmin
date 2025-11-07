## Description

<!-- Provide a clear and concise description of what this PR does -->

## Type of Change

<!-- Check all that apply -->

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Refactoring (code restructuring without changing behavior)
- [ ] Documentation update
- [ ] Performance improvement
- [ ] Test coverage improvement

## Related Issues

<!-- Link any related issues here -->

Fixes #(issue)

## Changes Made

<!-- List the specific changes made in this PR -->

-
-
-

## Testing Performed

<!-- Describe how you tested these changes -->

### Test Environment

- [ ] Tested with Docker Compose
- [ ] Tested with `dotnet run`
- [ ] Ran full test suite (`dotnet test`)
- [ ] Manual UI testing performed
- [ ] Tested with real Telegram bot

### Test Coverage

<!-- Describe specific test scenarios -->

-
-
-

## Database Migrations

<!-- If you modified Data models or DbContext -->

- [ ] N/A - No database changes
- [ ] Created new migration with `dotnet ef migrations add`
- [ ] Tested migration applies successfully
- [ ] Tested migration rollback (if applicable)
- [ ] Updated migration tests in TelegramGroupsAdmin.Tests

## Code Quality Checklist

- [ ] Code builds with **0 errors, 0 warnings** (`dotnet build`)
- [ ] All tests pass (`dotnet test`)
- [ ] Follows architectural patterns documented in [CLAUDE.md](../CLAUDE.md)
  - [ ] Used extension methods for service registration (if applicable)
  - [ ] Repository methods return/accept UI models, not Data models (if applicable)
  - [ ] Database-first configuration patterns followed (if applicable)
- [ ] No security vulnerabilities introduced (SQL injection, XSS, command injection, etc.)
- [ ] Secrets/API keys stored in database or environment variables (never hardcoded)
- [ ] Used `IMudDialogInstance` (interface) not `MudDialogInstance` (concrete class) for MudBlazor v8+

## Documentation

- [ ] Updated relevant documentation (README.md, CLAUDE.md, etc.)
- [ ] Added/updated code comments for complex logic
- [ ] Updated BACKLOG.md if this resolves a planned feature

## Screenshots (if applicable)

<!-- Add screenshots for UI changes -->

## Additional Notes

<!-- Any additional context, concerns, or areas you'd like reviewers to focus on -->

---

## For Reviewers

<!-- Maintainer use - contributors can ignore this section -->

- [ ] Code review completed
- [ ] Architecture/patterns review
- [ ] Security review
- [ ] Tested in development environment
- [ ] Ready to merge
