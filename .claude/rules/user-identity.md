---
paths:
  - "TelegramGroupsAdmin*/**/*.cs"
  - "TelegramGroupsAdmin*/**/*.razor"
  - "docs/superpowers/plans/**/*.md"
  - "docs/superpowers/specs/**/*.md"
---

# User identity rule (MANDATORY)

- Get a `UserIdentity` from `IUserIdentityService`: `ObserveAsync` where an update or scan shows the
  user's current names (new and edited messages, joins, admin refresh, profile scans), otherwise
  `ResolveAsync` / `ResolveManyAsync` by id. `ObserveAsync` decides from the observation's source
  and the user whether a rename is rescanned; callers pass no rescan option.
- Repository code that reads users joins the `user_identities` view and builds identities only with
  `UserIdentityMapping.ToIdentity` (or `ToIdentityOrIdOnly` for a left join). Never join `telegram_users`
  for names that become an identity.
- Text the bot posts into a group chat uses `await configService.CreateChatMessageBuilderAsync(chatId)`
  (or `TelegramMessageBuilder.For(await configService.GetNameMaskingAsync(chatId))` when the masking
  value is needed on its own) and `Mention(identity)` / `identity.BotDisplayName(masking)`. Direct messages to a person are never
  masked: use `For(NameMasking.Off)` with a `// DM: never masked` comment. Exception: ban celebration
  subscriber DMs copy the chat's caption, so they keep that chat's masking.
  A builder that never mentions a user may use `For(NameMasking.Off)` with a "no user mentions" comment.
  Logs and the web UI use `identity.DisplayName`.
- Tests use `UserIdentity.ForTest`; settings previews use `UserIdentity.ForPreview`.
- `UserIdentityConstructionTests` fails on any other `new UserIdentity(` / `UserIdentity.FromId(`.
  Don't extend its allowlist without the maintainer's agreement.
