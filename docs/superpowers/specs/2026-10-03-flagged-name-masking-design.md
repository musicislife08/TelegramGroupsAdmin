# Flagged Name Masking

**Date:** 2026-10-03
**Issue:** #552 (part 2 of 2; requires `2026-10-03-user-identity-service-design.md`)
**Scope:** When a profile scan fails, still filter the profile on what we have: score the name
alone and act on it like a scan (clean / held for review / auto-ban). Both scans also judge whether
the name itself is spam or explicit and store that on the scan row; a flagged name is masked only
while the user is banned. Part 1 (merged in #575) already masks by verdict everything
the bot posts in a group chat, plus ban celebration subscriber DMs (a copy of the chat post), with
fixed wording (`[name removed: spam]` / `[name removed: explicit]`) and the "Mask flagged names"
setting (global default, per-chat override). Admin and personal DMs always show the real name.
**PR target:** `develop`.

---

## Context

Part 1 gives every name the bot posts in a group a `UserIdentity.BotDisplayName` driven by a
`NameVerdict` read from the latest `profile_scan_results` row (through the `user_identities` view).
After part 1 the only verdict source is still the AI's `explicit_display_text` flag, defined narrowly
as explicit or sexual text.

Most name spam is not explicit. It is an advertisement, a solicitation or a lure. When the profile
scanner bans such a user, the ban celebration still posts the name. Some users never reach the
AI at all: the rule-based score can reach the ban threshold first (`ProfileScoringEngine.cs:63-77`),
and scans can fail (no User API session, unresolvable user, timeout, `FLOOD_WAIT`).

A regex link check was prototyped first. It was dropped: a TLD list either misses domains or
flags names like `Mr.Bean`, and it cannot read meaning ("site dot com", ads in any language). The
model already reads the name on every scan and judges these better.

## Goals

1. A `promotional_display_text` flag beside `explicit_display_text`, set by the full scan.
2. A name-only fallback scan, scored and actioned like a full scan, for every case where the full
   scan gives no AI verdict.
3. Mask a flagged name only while the user is banned.

## Non-goals

- Deterministic link detection (dropped, see Context).
- Wording and config (part 1).
- Masking chat titles.

## Design

### What gets flagged

Both flags are defined by one block of prompt text, `ProfileScanPrompts.NameFlagDefinitions`, used
verbatim by the full scan and the name-only scan (which is the full scan with only the name). It replaces the full
scan's current "EXPLICIT DISPLAY-TEXT FLAG" section. The wording below was settled by evaluating it on
`gpt-5.6-luna` (the profile-scan model) against real spam names, real banned users' names and clean
names before implementation:

```text
══════════════════════════════════════
 NAME FLAGS (display name + username only)
══════════════════════════════════════

These two flags judge ONLY the visible name: the display name (first +
last name) and the @username. A name that says who or what the account
is (a person, a nickname, a farm, a shop, a studio, a podcast, a
project) is an identity and stays clean. A name that speaks to the
reader (sells, offers a service, solicits, recruits, lures, or points
somewhere else) gets flagged. Judge the display
name and the username each on their own: either one alone can set a
flag. Judge meaning in any language or script.

"explicit_display_text" — true ONLY when the name text itself reads as
explicit sexual content to an ordinary reader:
- Sexual solicitation phrases ("looking for F buddy", "DM me horny")
- Graphic sexual terminology or explicit slurs in the name
- Sexual roleplay handles ("sub4daddy", "kinky_milf")
Do NOT set it for suggestive but non-explicit names ("BeachBabe92",
"lonely_girl", "Hot Kristina"); judge those as lures below. A single
word that is also a surname, an ordinary word or obscure slang
("Dick", "Cox", "Wang", "Johnson") is not explicit on its own.

"promotional_display_text" — true when the name pitches to the reader
instead of naming someone: it sells, solicits, recruits, or offers a
service for hire. A business, farm, homestead, shop, craft, studio,
podcast or project used as a person's identity is NOT promotional by
itself ("Maple Ridge Farm", "@oakhollowhomestead", "@NorthForgeKnives",
"Pixel Studio", "@TheTrailPodcast"). Any one of these is enough:
- Advertises a product, service, business, channel or group, including
  clickbait ("Crypto Signals VIP", "Best Web Design", "Free — Join Now 👉")
- Describes a service for hire instead of naming anyone: a generic
  trade or role with no person or named thing behind it ("Expert
  Developer", "Pro Graphic Designer", "Digital Marketer"), or a name
  built from a common spam trade even without a call to action:
  e-commerce, SEO, marketing, growth, ads, web or app development,
  VoIP, call center, SIP, bulk SMS, crypto or trading signals, loans or
  funding, "supplier" or "provider" ("Ecom Expert Pro", "@cheap_seo_ads",
  "@callcenter_pro", "@voip_deals", "Bulk Supplier", "@lisa_capital_team")
- Solicits contact, money, loans, jobs, trading or investing ("DM me
  for loans", "Forex mentor – message me", "Sara Crypto Signals"). A
  trading or finance word on its own is an interest, not an offer
  ("Mike_FX", "@btc_sam"); flag it only when the name offers something
  (signals, team, capital, mentor, invest, VIP, profits).
- Makes health or miracle claims ("Natural cure for diabetes")
- Sells drugs or other contraband ("Delivery 🍁 💊")
- Presents itself as a role or an organization instead of a person:
  support, help desk, official or staff accounts ("Admin Support",
  "Help Desk", "Official Team", "<community name> Support"). You do
  not need to know who the real admins are; judge the role words in
  the name itself. "Official" next to a person's own name ("Official
  Mark Hayes", "@sara_official") is a vanity tag, not a role.
- Is a lure: romance or suggestive bait, including a name that
  advertises sexiness or availability ("Lonely Anna 💋 text me",
  "Sweet girl waiting for you", "Hot Kristina", "naughty_jess22")
- Points somewhere else: a link, domain, @handle, "see my bio", or an
  obfuscated variant ("site . com", "t me/xyz", "info in my profile")

Read emoji for what they suggest in context. Emoji used as sexual slang
(food or body-part innuendo, lips, hot or drooling faces) or to
signal availability make a name a lure on their own.
Emoji that stand for drugs, money, trading or urgency support other
promotional signals. Ordinary decoration (hearts, flowers, smiles,
animals, flags, sparkles) is not a flag.

Styled Unicode letters (fullwidth, mathematical bold) and look-alike
characters ("€" for "e", "0" for "o") strengthen other signals but are
not a flag on their own.

Leave both flags false for ordinary names: gamer tags, nicknames,
emoji-only names, names in any script, initials, abbreviations with
dots ("Mr.Bean", "Dr. Smith", "St.John"), a profession or hobby next to
a name ("Lisa | Nurse", "Coach Tom", "jen_knits", "Tom paints"), a
profession shown with a matching emoji ("Nurse Kim 💉", "Dr. Lee 🩺💊"),
and a normal name with a heart, flower or smiling emoji. A hobby or job is
only promotional when the name sells it ("Tom paints — commissions open").

Both flags may be true at once.
```

Notes on the wording:
- Identity, not pitch: a name that says who or what the account is (a person, nickname, farm,
  homestead, shop, studio, podcast, project) stays clean; a name that speaks to the reader is flagged.
  Community groups have many members who go by their own farm or business name.
- Service-for-hire names from common spam trades (e-commerce, SEO/marketing/ads, web/app development,
  VoIP/call centre/SIP/bulk SMS, crypto or trading signals, loans, "supplier"/"provider") count even
  without a call to action.
- A trading or finance word alone is an interest, not an offer; it counts only with an offer word
  (signals, team, capital, mentor, invest, VIP, profits).
- The display name and the username are judged separately; either alone can set a flag.
- "A role instead of a person" needs no list of admins: it judges role words in the name itself.
  Impersonating a specific admin stays with the impersonation check. "Official" next to a person's own
  name is a vanity tag, not a role.
- Emoji are described by what they suggest, not listed. A profession with a matching emoji stays
  clean; substance lists and selling words do not.
- The explicit flag requires text that reads as explicit to an ordinary reader; a word that is also a
  surname, an ordinary word or obscure slang is not explicit on its own.
- Explicit takes precedence when both flags are true.

### Full scan

`ProfileScanPrompts` swaps its explicit-flag section for `NameFlagDefinitions` and adds
`"promotional_display_text": true/false` to the response schema. `ProfileScanAIResponse`,
`ScoringResult`, `ProfileScanResult` and `ProfileScanResultRecord` carry it through to the row. The
flags do not change how the score is computed.

### Name-only scan

When a scan runs but the full scan produces no AI verdict, the name is often the only thing we have.
A name-only scan scores it, so the profile is still filtered before the user can act:

- no User API session (none connected at all, or none usable for this scan): the name-only scan runs
  directly;
- the user can't be resolved, or their full profile can't be fetched;
- timeout or `FLOOD_WAIT`.

Every scan path falls back the same way: join, first message, rename rescans, the rescan job and the
manual rescan. When profile scanning is on and no User API session is connected, the profile-scan
settings show a notice: full profile scans can't run, but name-only scans still run on new joiners,
first messages and renames.

(When the rule-based score short-circuits the AI, the rules already decided the outcome; no
name-only scan runs. Bots are not scanned; bot protection owns them.)

It is the full scan's own prompt with only the name filled in: the same system prompt (detection
criteria, score scale, `NameFlagDefinitions`) and the same JSON response, so a name-only score means
the same as a full one, made on less evidence. The user prompt marks the other fields unknown:

```text
Only the name could be retrieved for this account. The bio, photos,
personal channel and stories are UNKNOWN, not empty: do not treat their
absence as a clean empty profile, and do not treat it as suspicious.
Score on what the name and username show.
```

followed by the profile block with `<display_name>` and `<username>` (XML-escaped) and every other
field set to `Unknown (could not be retrieved)`.

The result is a scan like any other: it writes a `profile_scan_results` row with `source = NameOnly`
(score, outcome, AI reason and signals, both name flags), sets the user's `profile_scan_score` and
advances `profile_scanned_at`, and its outcome goes through the existing moderation path:

- score below the notify threshold → clean;
- at or above the notify threshold and below the **name-only ban threshold** → held for review (a
  profile-scan alert report, exactly as a full scan's held outcome);
- at or above the name-only ban threshold → auto-ban.

A name alone is weaker evidence than a whole profile, so it needs more certainty to auto-ban. The
name-only ban threshold is its own setting next to the ban and notify thresholds (`ProfileScanConfig
.NameOnlyBanThreshold`, default 4.5, overridable per chat like the others, validated to be at least
the notify threshold). In the evaluation on real names, unmistakable spam blurbs (drug menus, ad
text) scored 4.4–5.0, while the riskiest real-looking names scored 4.0–4.2.

A later successful full scan replaces the name-only verdict: the name-only row stores no bio, photo
or channel, so the full scan's change check sees a different profile and rescores.

If the AI feature is unavailable or the call fails, nothing is written (fail open, logged as a
warning; the exception text goes to the log only, never into a chat). Scanning disabled for a chat
means no name-only scan either.

### When scans run, retries and exclusion

| Trigger | Behaviour |
|---|---|
| Join / first message | Scan a new or never-scanned user, or an existing user who renamed since their last scan; otherwise skip. |
| Rename detected (part 1's rules) | Scan, even when the user is excluded: a rename is a change. |
| Manual rescan | Always runs, whatever the exclude flag says. |
| Rescan job | Retries incomplete scans only, to fill in missing information (below). |

Every scan is a full scan that falls back to name-only. A successful full scan is complete: nothing
rescans it automatically until the user renames (or an admin runs a manual rescan). The rescan job no
longer rescans fully scanned users on a timer.

The rescan job selects untrusted, unbanned, non-bot, not-excluded users whose scan is incomplete:
- no scan recorded yet (every earlier attempt failed outright), or
- the latest scan is `NameOnly` and fewer than the **name-only retry limit** `NameOnly` scans were
  recorded since the user's last `FullScan` row.

`RescanAfter` keeps its meaning as the wait before an incomplete scan is retried. The name-only retry
limit is a setting next to the job's other settings (`ProfileRescanSettings.NameOnlyRetryLimit`,
default 3). It is derived from `profile_scan_results` (the `NameOnly` rows since the last `FullScan`
row), so no counter column is stored. When the limit is reached the job stops retrying; the latest
name-only verdict stands until the user renames or an admin rescans manually.

The exclude flag (`profile_scan_excluded`) is the admin's switch: "don't scan this user
automatically unless something changes". The rescan job and join / first-message scans skip an
excluded user; a rename still scans and a manual rescan always runs. Scans no longer set or clear the
flag: an unresolvable user no longer excludes itself (the retry limit bounds the job instead), and a
successful full scan no longer clears an admin's exclusion.

### How it fits part 1's rename rules

Part 1 decides rescans in `IUserIdentityService.ObserveAsync` from the observation, not from callers:
a rename seen in a message or edit by an untrusted, unbanned, non-bot user is rescanned at once;
joins, admin updates and the scan's own observations record only; a full scan treats a rename
recorded after the last scan as a profile change; and the gate admits a join scan when the chat
scans on profile changes and the joiner renamed since the last scan. Part 2 adds nothing to that
decision. The name-only scan is a fallback *inside* a scan the gate already admitted, so:

- A renamed user's new name gets a verdict through whichever scan part 1 triggers (the inline
  rename rescan, or the join scan), full or name-only.
- On a join, the name-only scan runs at the join scan step, after the joiner is muted. Nothing in part 2 runs
  before the mute.
- Trusted users (all chat admins) are never scanned, so they never get a name verdict, whatever
  they rename to.
- Bots are not scanned (bot protection owns them), so they get no name verdict.

### Storage

Migration on `profile_scan_results`:
- `ai_promotional_display_text boolean not null default false`
- `source smallint not null default 0` (`FullScan = 0`, `NameOnly = 1`)

Config: `ProfileScanConfig.NameOnlyBanThreshold` (decimal, default 4.5) in the welcome config JSON,
with the same global/per-chat behaviour as `BanThreshold` and `NotifyThreshold` (absent → default, so
no data migration). The profile-scan settings show it as "Name-only ban threshold" next to the other
two, with a caption: "A scan that could only read the name auto-bans at this score; below it, scores
at or above the notify threshold go to review."

A flagged name is masked only while the user is banned: the verdict decides how the name is masked,
the ban decides whether. A name held for review is shown normally until an admin decides; if the admin
bans, it is masked from then on; if the admin dismisses, it never is; an unban shows it again. The
`user_identities` view applies this in one place, so every bot message follows it:
banned and explicit → `Explicit`; else banned and promotional → `Promotional`; else `Clean`
(explicit names always auto-ban, so this never delays an explicit mask). The view's
`latest_scan_explicit` column becomes the verdict's inputs (`latest_scan_explicit`,
`latest_scan_promotional`, `is_banned`), and `UserIdentityMapping` stays the only place the rule lives.
The scan history dialog shows both flags and the source.

### Ban celebration

Uses `BotDisplayName` (part 1) for the chat caption, which subscriber DMs copy. The masked-username
metric records the verdict as a tag.

## Testing

Unit:
- Prompt contains the flag definition; response parsing reads `promotional_display_text`, with a
  missing field defaulting to false.
- Name-only trigger, one test per case: no session, unresolvable, timeout, `FLOOD_WAIT`. No trigger
  on a rule short-circuit, for bots, when scanning is disabled, or when the AI feature is unavailable.
- Name-only outcome: below notify → clean; notify ≤ score < name-only ban threshold → held for review;
  ≥ name-only ban threshold → auto-ban (a score between the regular ban threshold and the name-only
  one is held, not banned). Per-chat override of the name-only threshold is honoured.
- The name-only user prompt marks bio, photos, channel and stories unknown.
- Verdict mapping: explicit beats promotional; a flagged name of a user who is not banned maps to
  `Clean`.
- A failed name-only call logs a warning and writes nothing; no exception text reaches a chat.

Integration:
- A name-only scan writes a `NameOnly` row with score and outcome, sets `profile_scan_score` and
  advances `profile_scanned_at`. Anchor: 9333810782137 @loucurtsinger (not trusted, no scan rows);
  the written row and user fields are the assertion subject.
- A user whose latest row is promotional renders `[name removed: spam]` in a welcome message posted
  in the group and in a ban celebration caption (and the subscriber DM that copies it), while an
  admin notification DM about the same user shows the real name. Anchor: 9143878698845 @LisoBran, whose only scan
  (row 531) gets `ai_promotional_display_text = true` (canonical edit 2026-10-03; unreferenced,
  banned, AI fields filled in). Recorded in `GoldenDatasetConstants` and `IntegrationTests/CLAUDE.md`,
  whose profile-scan notes also gain the new column.
- A not-banned user whose latest row is promotional (held for review) is shown by real name in group
  posts. Anchor: an unreferenced, not-banned canonical user with a scan row, flag-edited to
  `ai_promotional_display_text = true` (candidate: 9213195802818 @splendorfraying, row 526).
- Explicit beats promotional: 9220500615182 @bagging_armado (row 534 explicit, banned, read-only).

Each test reads its anchor back and asserts the flags first.
