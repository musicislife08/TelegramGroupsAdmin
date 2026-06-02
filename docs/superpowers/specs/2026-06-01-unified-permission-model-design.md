# Unified Permission Model — Design

**Date:** 2026-06-01
**Branch:** `feat/unified-permission-model`
**Supersedes/expands:** Issue #507 (permission-label inconsistency + `GetPermissionName` duplication)
**Spawns follow-up:** new issue — "Per-chat scope analytics & detection data" (see *Out of Scope*)

## Origin

Issue #507 reported that the `/help` footer mislabels a per-chat Telegram admin as
"GlobalAdmin", plus a duplicated `GetPermissionName`. While confirming a related
real-world complaint ("a global admin was refused `/ban` in a chat they don't
administer"), we discovered the complaint was **operational, not a code bug**: the
user (a GlobalAdmin) had never linked their Telegram account, so
they resolved with no web permission and fell through to the per-chat admin path.

That investigation surfaced the real problem: **the bot has two overlapping permission
scales that assign different numbers to the same conceptual tier.** A Telegram chat
admin resolves to `1`; a web "Admin" is stored as `0` — yet they are meant to be the
*same* Admin tier. Every downstream inconsistency (mislabeling, a latent web-Admin
moderation gap, creator over-privilege, magic-int checks, cross-chat analytics leaks)
flows from that one mismatch.

This work codifies the permission model **that was always intended**, and treats every
deviation from it as a bug to fix.

## Canonical Permission Model (the source of truth)

A permission decision is two questions: **tier** ("how powerful are you?") and **scope**
("here?"). The intended model:

| Tier | Can do | Scope | How you get it |
|---|---|---|---|
| **Member** | public commands only (`/help`, `/report`, `/link`, `/start`, `/mystatus`, `/invite`) | the chat they're in | everyone, by default |
| **Admin** | all moderation *except* infra | **only chats their (linked or native) Telegram account administers** | being a Telegram admin/creator of a chat — *or* a web "Admin" role whose linked Telegram account is an admin there |
| **GlobalAdmin** | all moderation *except* infra | **every chat** | web role, linked account |
| **Owner** | everything **incl. infra/system settings** | every chat + web infra | web role, linked account |

Rules that fall out of this:

- **"Telegram chat admin" ≡ "web Admin."** Same tier, same powers, same chat-scope. The
  only difference is whether the person also has a web login for richer context. Per-chat
  command authority on Telegram comes from `chat_admins`; the web "Admin" *role* grants
  portal access scoped to those same chats.
- **Telegram creator → Admin tier** ("treated like admin"), not a higher tier.
- **GlobalAdmin and Owner are indistinguishable on Telegram** — there are no infra
  commands on Telegram. Owner's extra power is web-only (infra/system settings editing).
- **Effective tier in a chat = the canonical resolver below**, which naturally implements
  the "max of the user's web tier and their in-chat Telegram tier", scope-aware.

### Canonical resolver

```
EffectivePermission(telegramId, chatId):
    webTier = (linked active web account?) -> stored PermissionLevel : null   // Admin/GlobalAdmin/Owner
    if webTier is GlobalAdmin or Owner:        return webTier                  // global, any chat
    if isTelegramAdminOrCreator(chatId, telegramId): return Admin             // chat-scoped
    return Member
```

Consequences (all conformant with the model):
- A web **Admin in a chat they don't administer** resolves to **Member** (Admin is
  chat-scoped). In a chat they *do* administer → **Admin**.
- A native Telegram **creator** → **Admin** (not Owner).
- A **GlobalAdmin/Owner** → their tier in **every** chat, administered or not.

### Performance note (resolver ordering)

The resolver does up to two DB lookups (web mapping, then `chat_admins`); neither is
cached today, and it runs **only on slash-commands** (guarded by `IsCommand` in
`MessageProcessingService`), not per message — so it is not a throughput hot path.

We deliberately keep the **accurate, web-tier-first** ordering rather than a
"chat-admin-first, skip the web lookup" short-circuit. The short-circuit would save one
query for non-web chat admins, but would mislabel a Global/Owner web user who is *also* a
chat admin of a chat as "Admin" in that chat — re-introducing a smaller form of the very
#507 bug this work fixes (e.g., the Owner is a chat admin/creator in ~19 chats and would
show "Admin" in most of them).

If lower latency is ever wanted without sacrificing accuracy, the safe lever is to run the
two lookups **concurrently** (`Task.WhenAll`) or fold them into one joined query — not to
reorder-and-skip. A fully unified single-source lookup would be a larger storage-layer
change and is explicitly out of scope here (worth revisiting later).

### Reference implementation already in the codebase

`ManagedChatsRepository.GetUserAccessibleChatsAsync` already implements the scope rule
correctly on the web (GlobalAdmin/Owner → all chats; Admin → chats joined via
`telegram_user_mappings → chat_admins`; unlinked → none). It is the canonical
chat-scope rule; the analytics follow-up reuses it.

## The unified enum (no DB migration)

We **extend the existing `PermissionLevel` enum** (already used by the web, already
carrying `[Display]` names that match the role claims) with a `Member` floor, and use
that same enum on the bot side. We keep the name `PermissionLevel` to minimize churn.

```csharp
public enum PermissionLevel
{
    [Display(Name = "Member")]      Member      = -1,  // computed; never persisted
    [Display(Name = "Admin")]       Admin       = 0,
    [Display(Name = "GlobalAdmin")] GlobalAdmin = 1,
    [Display(Name = "Owner")]       Owner       = 2,
}
```

Why this is migration-free and safe:
- The persisted `users.permission_level` column only ever holds `0/1/2`
  (Admin/GlobalAdmin/Owner). `Member` is the *absence* of privilege — computed at
  request time, **never stored**. So the column needs no change.
- `Member = -1` matches the sentinel the Telegram side already uses for "no permission",
  so the two scales converge on the same numbers with zero data churn.
- The `[Display]` names remain exactly `"Admin"/"GlobalAdmin"/"Owner"`, so the web role
  claims and `RequireRole("GlobalAdmin","Owner")` / `"Owner"` policies keep working
  unchanged.

A `+1` renumber to a contiguous `Member=0` ladder was considered and **rejected**: a
permission-column migration is high-risk (a silent off-by-one is a security incident)
for a purely cosmetic gain, since the enum is the single source of truth either way.

**No orphaned enum.** The solution has exactly one permission enum (`PermissionLevel`);
unification *extends* it (adds `Member`) and adopts it on the bot side, rather than
collapsing two enums into one. So nothing is deleted *as an enum*. What is removed are the
redundant **bare-int** artifacts the unification replaces — most notably
`ModerationConstants.AdminPermissionLevel = 1` (an obsolete magic int that encodes the old
scale where Admin was `1`; under the unified enum Admin is `0`, so moderation commands
declare `PermissionLevel.Admin` directly and the constant is deleted).

## Architecture changes

### Bot side (Telegram)

1. **Single resolver.** `CommandRouter.GetPermissionLevelAsync` returns
   `PermissionLevel` (incl. `Member`) per the canonical resolver. This fixes:
   - the **early-return-on-web-link** bug (`CommandRouter.cs:187–191`) — it no longer
     skips `chat_admins`,
   - **creator → Owner** (`ChatAdminsRepository.cs:42`) — creator now maps to Admin tier,
   - **web-Admin in an unadministered chat** now correctly resolves to `Member`.
2. **Command thresholds re-typed.** `IBotCommand.MinPermissionLevel` becomes
   `PermissionLevel` (was `int`). `ExecuteAsync(... int userPermissionLevel ...)` becomes
   `ExecuteAsync(... PermissionLevel userPermission ...)`. Re-map across the ~15 commands:
   - public commands (`help`, `report`, `link`, `start`, `mystatus`, `invite`) →
     `PermissionLevel.Member`,
   - moderation commands (`ban`, `spam`, `mute`, `tempban`, `trust`, `unban`, `warn`,
     `delete`) → `PermissionLevel.Admin`.
   (`ModerationConstants.AdminPermissionLevel = 1` is **deleted** — moderation commands
   declare `PermissionLevel.Admin` directly.)
3. **Uniform gating.** Gate on `effectiveTier >= command.MinPermissionLevel` (enum
   comparison). Because public commands require `Member`, everyone passes them naturally —
   the existing `bypassPermissionCheck`/`Math.Max` special-case (`CommandRouter.cs:92–93`)
   is **removed** as redundant. `/help` still receives the resolved tier to filter the
   command list and render the footer.
4. **Single name source.** Delete both bot-side `GetPermissionName` copies
   (`CommandRouter.cs:211`, `HelpCommand.cs:107`); render labels from
   `PermissionLevel.GetDisplayName()`. This fixes #507's headline: a per-chat admin now
   shows **"Admin"**, a creator shows **"Admin"** (not "GlobalAdmin"/"Owner").

### Web side

5. **Magic ints → enum compares.**
   - `NotificationPreferencesCard.razor:331` `UserPermissionLevel < 2` → `< PermissionLevel.Owner`.
   - `NavMenu.razor:55` `level >= 1` → `>= PermissionLevel.GlobalAdmin`.
6. **Policy constant.** Add `AuthenticationConstants.PolicyOwnerOnly = "OwnerOnly"`; replace
   the bare string literal at `ServiceCollectionExtensions.cs:90` and update references.
7. **Consistent authorization attribute.** `Audit.razor:11`
   `[Authorize(Roles="GlobalAdmin,Owner")]` → `[Authorize(Policy = PolicyGlobalAdminOrOwner)]`.
8. **Dedup web role-name mapping.** `Login.razor` and `AuthCookieService` each have a
   private `GetRoleName`; collapse to one source backed by `PermissionLevel.GetDisplayName()`.

### Interim leak containment (proper fix deferred — see *Out of Scope*)

9. **`/analytics` — hide only the leaky tabs for Admin (not a page gate).** The page has
   four tabs; **Message Trends is already correctly chat-scoped** (`MessageTrends` uses
   `GetUserAccessibleChatsAsync`) and stays visible to Admins. The three leaky tabs —
   **Content Detection, Performance, Welcome** (`ContentDetectionAnalytics`,
   `PerformanceMetrics`, `WelcomeAnalytics`, plus `SpamTrendComparison`) — are
   **conditionally rendered only for `WebUser.IsGlobalAdminOrHigher`**. Their
   `MudTabPanel`s are *not emitted* for Admins, so the unscoped repository methods are
   never invoked on their behalf → leak contained without removing Admins' working
   analytics. `Analytics.razor` keeps `[Authorize]` (all authenticated); the `/analytics`
   nav link stays visible to everyone. (This supersedes the earlier "gate the whole page"
   idea — gating would have discarded the already-correct Message Trends tab.)
10. **Dashboard (`Home.razor`, route `/`) — scope cheap, hide the rest for Admin.** The
    landing page can't be gated. For **Admin** tier:
    - **Scope** pending-reports count and user-tab-counts to the user's accessible chat
      IDs. `GetUserTabCountsAsync(chatIds: …)` already takes a collection (the dashboard
      currently passes `null`). `ReportsRepository.GetPendingCountAsync(long? chatId)`
      takes only a single `chatId`, so add a small overload accepting the accessible chat
      IDs (or sum across them) — cheap, no migration. *Pending reports is the
      operationally-useful card for Admins.*
    - **Hide** the global-aggregate widgets that need new overloads or are view-backed:
      `GetStatsAsync`, `GetDetectionStatsAsync`, `GetRecentAsync`, and
      `GetDailySpamSummaryAsync` — render them only for GlobalAdmin+.
    - **GlobalAdmin/Owner:** dashboard unchanged.

## Deviation inventory → where each is fixed

| # | Deviation | Location | Fixed by |
|---|---|---|---|
| 1 | Early-return on web link skips `chat_admins` (web-Admin who's also a TG admin can't moderate) | `CommandRouter.cs:187–191` | resolver (Arch §1) |
| 2 | Creator → `2` (Owner-equiv) instead of Admin | `ChatAdminsRepository.cs:42` | resolver (Arch §1) |
| 3 | Bare-int flatten → `/help` mislabels chat admin/creator | `CommandRouter.cs:88–93`, `HelpCommand.cs:80` | enum + name source (§1, §4) |
| 4 | Web-Admin in unadministered chat resolves to stored `0` not Member | `CommandRouter.cs:178–209` | resolver (§1) |
| 5–8 | `/analytics` widgets query globally (cross-chat leak) | `Analytics.razor` + `AnalyticsRepository`/`DetectionResultsRepository` | **interim: hide 3 leaky tabs for Admin (§9)**; proper fix in follow-up issue |
| 9 | Magic int `< 2` for Owner | `NotificationPreferencesCard.razor:331` | §5 |
| 10 | Magic int `>= 1` for GlobalAdmin | `NavMenu.razor:55` | §5 |
| 11 | `"OwnerOnly"` bare string literal | `ServiceCollectionExtensions.cs:90` | §6 |
| 12 | `Audit.razor` uses `Roles=` not `Policy` | `Audit.razor:11` | §7 |
| 13 | 3 copies of role/permission-name mapping | `CommandRouter`, `HelpCommand`, `AuthCookieService`/`Login` | §4, §8 |

## Out of Scope (follow-up issue #510)

**"Per-chat scope analytics & detection data"** ([#510](https://github.com/musicislife08/TelegramGroupsAdmin/issues/510)) — fix deviations #5–#8 properly: scope the
three leaky analytics tabs' repository methods, **un-hide those tabs for Admin**, and
un-hide the dashboard widgets. This is deferred because it requires **DB view migrations**,
which would otherwise pull migration risk into this migration-free PR:
- `detection_accuracy` view (feeds `PerformanceMetrics`) has **no `chat_id`** column.
- `hourly_detection_stats` and the spam-trend aggregates **pre-aggregate without
  `chat_id`** — scoping them changes the view grain (row cardinality), rippling into
  consumers.
- `enriched_detections` *does* carry `chat_id` (query-level `WHERE` is enough there).
- Requires threading accessible-chat-IDs into `AnalyticsRepository` and
  `DetectionResultsRepository` method signatures, then re-verifying every `/analytics`
  widget scopes like `MessageTrends` already does.

Also out of scope: any change to the persisted `users.permission_level` representation
(no migration); the previously-discussed `#478` Snackbar/`ex.Message` sweep (dropped).

## Acceptance Criteria

- [ ] A single `PermissionLevel` enum (with `Member = -1`) is used by **both** the bot and
      the web; no separate bot-side numeric scale remains.
- [ ] No DB migration; `users.permission_level` values unchanged; web role claims and
      `RequireRole`/policy strings unchanged.
- [ ] `CommandRouter.GetPermissionLevelAsync` implements the canonical resolver: web
      Global/Owner → tier in any chat; else Admin if TG admin/creator here; else Member.
      A web-Admin who is also a TG admin of a chat **can** moderate that chat. A creator
      resolves to **Admin**.
- [ ] `/help` footer (and permission-denied messages) label a per-chat admin as **"Admin"**
      and a creator as **"Admin"** — never "GlobalAdmin"/"Owner" by accident.
- [ ] All bot command `MinPermissionLevel` values are typed `PermissionLevel`; public →
      `Member`, moderation → `Admin`. Gating is `tier >= MinPermissionLevel`; the
      `bypassPermissionCheck`/`Math.Max` special-case is removed without behavior change
      for public commands.
- [ ] Both bot-side `GetPermissionName` copies and the duplicated web `GetRoleName` are
      eliminated in favor of `PermissionLevel.GetDisplayName()` (one source).
- [ ] Magic-int permission comparisons (`< 2`, `>= 1`) are replaced with enum comparisons;
      `OwnerOnly` is referenced via `AuthenticationConstants.PolicyOwnerOnly`;
      `Audit.razor` uses the policy attribute.
- [ ] On `/analytics`, an Admin sees **only the Message Trends tab** (Content Detection,
      Performance, Welcome tabs are not rendered for Admin); GlobalAdmin/Owner see all
      four. The page stays `[Authorize]`; the nav link stays visible to all.
- [ ] The dashboard scopes pending-reports and user-tab-counts for Admins and hides the
      global-aggregate widgets (message stats, spam-today, recent-activity) from Admins,
      keeping a non-empty scoped stats grid; GlobalAdmin/Owner dashboard unchanged.
- [ ] Existing web authorization behavior (infra Owner-only, account mgmt GlobalAdmin+,
      page gates) is unchanged/verified.
- [ ] A new issue is filed for the analytics/detection per-chat scoping follow-up.

## Testing

- **Unit:** resolver truth table — for each (web role ∈ {none, Admin, GlobalAdmin, Owner}) ×
  (TG status in chat ∈ {none, admin, creator}) × (chat administered? y/n), assert the
  expected effective `PermissionLevel`. Explicitly cover: web-Admin + TG-admin-here → Admin;
  web-Admin + not-admin-here → Member; creator → Admin; GlobalAdmin/unlinked-here → GlobalAdmin.
- **Unit:** command gating — Member denied moderation, allowed public; Admin allowed
  moderation; label rendering via `GetDisplayName()`.
- **Integration:** a web-Admin linked to a TG account that admins chat X can run `/ban` in
  X and is denied in chat Y; an unlinked GlobalAdmin (the unlinked-global-admin case) — confirm the
  *operational* expectation is documented (link required) and not masked by code.
- **Web (component):** verify the dashboard renders scoped pending-reports for an Admin and
  omits the global widgets; verify `Analytics.razor` emits only the Message Trends tab for
  Admin and all four for GlobalAdmin+.

### E2E (Playwright) — existing suite impact

**Important:** the default E2E user is **`PermissionLevel.Admin`**
(`TestUserBuilder._permissionLevel`), and tests authenticate per-tier via
`LoginAsAdminAsync` / `LoginAsGlobalAdminAsync` / `LoginAsOwnerAsync`. The Admin-tier
changes therefore directly touch Admin-authenticated tests.

Tests to **update**:
- `DashboardTests.Dashboard_AccessibleByAdmin` — currently asserts `AreStatsVisibleAsync`
  (`.mud-paper .mud-grid`). Adjust to assert the *scoped* cards (Pending Reports) are shown
  and the global ones (Total Messages / Spam Today / Recent Activity) are **not** — and keep
  the stats grid non-empty so the locator still resolves.

Tests expected to **still pass** (verify):
- `AnalyticsTests.Analytics_PageLoads_ForAdmin` — asserts the tab *container* is visible;
  Message Trends still renders for Admin, so this should remain green. Confirm
  `IsTabsVisibleAsync` doesn't require a leaky tab.
- `PermissionBoundaryTests` (nav menu) — survives iff the `NavMenu` `level >= 1` →
  `>= PermissionLevel.GlobalAdmin` refactor is behavior-preserving.
- `NavigationTests` — first-run redirect tests, unaffected.
- `AnalyticsTests`/`DashboardTests` GlobalAdmin & Owner cases — those tiers keep full access.

Tests to **add**:
- `Analytics_Admin_SeesOnlyMessageTrends` — Admin: Message Trends tab present; Content
  Detection / Performance / Welcome tabs absent. `Analytics_GlobalAdmin_SeesAllFourTabs`.
- `Dashboard_Admin_HidesGlobalWidgets_ShowsPendingReports` — Admin: Pending Reports + Active
  Bans + Trusted Users visible (scoped); Total Messages / Spam Today / Recent Activity
  hidden. `Dashboard_GlobalAdmin_ShowsAllWidgets`.

## Risks

- **Security-sensitive.** Mistakes change who can run moderation commands / see data.
  Mitigated by the resolver truth-table tests and by keeping persisted values + web claims
  untouched.
- **Removing the bypass special-case** must preserve public-command access for `Member`;
  covered by gating tests.
- **Breadth of the enum rename touchpoints** (~20 web sites + 15 commands) is mechanical
  but wide; rely on the compiler (typed `MinPermissionLevel`) to surface every site.
