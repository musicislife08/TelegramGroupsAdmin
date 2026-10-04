# Flagged Name Masking

**Date:** 2026-10-03
**Issue:** #552 (part 2 of 2; requires `2026-10-03-user-identity-service-design.md`)
**Scope:** Decide whether a user's name is spam, including names that never reach the AI today,
and store that verdict on the scan row. Part 1 (merged in #575) already masks by verdict everything
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
2. A name-only LLM check for every case where the full scan gives no AI verdict.
3. Measure the prompt against a fixed set of real names before shipping.

## Non-goals

- Deterministic link detection (dropped, see Context).
- Wording and config (part 1).
- Masking chat titles.

## Design

### What gets flagged

`promotional_display_text` is true when the name or username does any of these:

| Category | Generic examples (in the prompt) |
|---|---|
| Advertises a product, service, business or channel, including clickbait | "Crypto Signals VIP", "Best Web Design", "Free — Join Now 👉" |
| Solicits contact, money, loans, jobs, trading or investing | "DM me for loans", "Forex mentor – message me" |
| Makes health or miracle claims | "Natural cure for diabetes" |
| Sells drugs or other contraband | "Delivery 🍁 💊" |
| Poses as staff, support or an authority | "Admin Support", "Group Moderator" |
| Is a lure: romance or suggestive bait, including suggestive emoji (💦 🍑 🍆 and similar) | "Lonely Anna 💋 text me" |
| Points elsewhere: a link, domain, @handle, "see my bio", or an obfuscated variant | "site . com", "t me/xyz", "info in my profile" |

- Judge meaning in any language and script.
- Styled Unicode letters (fullwidth, mathematical bold) and look-alike characters (`€` for `e`,
  `0` for `o`) strengthen other signals but are not a flag on their own.
- Must stay clean: gamer tags, nicknames, emoji-only names, non-Latin names, initials,
  `Mr.Bean`, `Dr. Smith`, a normal name with a heart or smiling emoji.

`explicit_display_text` keeps its current definition. Explicit takes precedence when both are true.

### Full scan

`ProfileScanPrompts` adds the flag definition to the system prompt and
`"promotional_display_text": true/false` to the response schema. `ProfileScanAIResponse`,
`ScoringResult`, `ProfileScanResult` and `ProfileScanResultRecord` carry it through to the row.

### Name-only check

A text-only completion through the existing `IChatService`, with a short prompt containing only
the flag definitions above, the display name and the username. It returns the two flags. It runs
when the gate admitted a scan (or the account is a bot) but the full scan produced no AI verdict:

- no User API session, or the user can't be resolved;
- timeout or `FLOOD_WAIT`;
- the rule-based score short-circuited the AI;
- the account is a bot (the full scan skips bots).

It writes a `profile_scan_results` row with `source = NameOnly`, so the verdict keeps one source.
It does not change the user's score or outcome, and it does not advance `profile_scanned_at`: a
name-only verdict is a stopgap, so the next scan opportunity still attempts a full scan (part 1's
history check keeps forcing a rescore until one succeeds). If the AI feature is unavailable or the
call fails, nothing is written and the verdict stays as it was (fail open, logged as a warning; the
exception text goes to the log only, never into a chat). Scanning disabled for a chat means no
name-only check either.

### How it fits part 1's rename rules

Part 1 decides rescans in `IUserIdentityService.ObserveAsync` from the observation, not from callers:
a rename seen in a message or edit by an untrusted, unbanned, non-bot user is rescanned at once;
joins, admin updates and the scan's own observations record only; a full scan treats a rename
recorded after the last scan as a profile change; and the gate admits a join scan when the chat
scans on profile changes and the joiner renamed since the last scan. Part 2 adds nothing to that
decision. The name-only check is a fallback *inside* a scan the gate already admitted, so:

- A renamed user's new name gets a verdict through whichever scan part 1 triggers (the inline
  rename rescan, or the join scan), full or name-only.
- On a join, the check runs at the join scan step, after the joiner is muted. Nothing in part 2 runs
  before the mute.
- Trusted users (all chat admins) are never scanned, so they never get a name verdict, whatever
  they rename to.
- Bots: part 1 records bot renames without rescanning. A bot gets a name-only verdict when it is
  scanned on admission (above); a later rename keeps the old verdict until it is scanned again.

### Storage

Migration on `profile_scan_results`:
- `ai_promotional_display_text boolean not null default false`
- `source smallint not null default 0` (`FullScan = 0`, `NameOnly = 1`)

Part 1's verdict mapping becomes: explicit → `Explicit`; else promotional → `Promotional`; else
`Clean`. The scan history dialog shows both flags and the source.

### Ban celebration

Uses `BotDisplayName` (part 1) for the chat caption, which subscriber DMs copy. The masked-username
metric records the verdict as a tag.

## Evaluation set

A fixture of real names, run against the full-scan prompt and the name-only prompt before
shipping. Expected verdicts:

| Name | Expected |
|---|---|
| Gateway Solution Help | Promotional |
| Tghub.co \| Telegram Ads 💎 | Promotional |
| Jamey FOREX trading team CEO | Promotional |
| Admin JAKE | Promotional |
| Am€ricans Can M€ssage Me For L0an | Promotional |
| Moore_FX | Promotional |
| Madelyn ❤️💦 | Promotional |
| karl's review cancer and parasites treatment protocols (demantia, alzheimer's, ms, sinus, herpes.. etc) | Promotional |
| Yo hit me up am down for any kind of fun Honey | Promotional |
| ＥＭＩＬＹ💦🍑🍆 | Explicit or Promotional (masked either way) |
| Free — Don't Miss!👉Stolen Cams, Raw Heat 🔥Massive Desi Sex Cache🟩Join Now!🌸It Now! | Explicit |
| Разработка Цифровых решений 📊 Информация В Описании Аккаунта | Promotional |
| 𝐃𝐄𝐋𝐈𝐕𝐄𝐑𝐘 🚘𝐂𝐎𝐊𝐄 🍚 𝐖𝐄𝐄𝐃 🍁☘️ 𝐇𝐀𝐒𝐇🍫𝐒𝐏𝐄𝐄𝐃😡👀𝐊𝐄𝐓𝐀𝐌𝐈𝐍𝐄🍚𝐇𝐄𝐑𝐎Ï𝐍𝐄🗿 𝐌𝐃𝐌𝐀 🪨✨️ 💊🍄 | Promotional |
| Dorcy 🥰 | Clean (borderline) |
| Mr.Bean, St.John, Dr. Smith | Clean |
| xX_Shadow_Xx, 🦊, Иван Петров, 李小龙, Anna 🌸 | Clean |

The set runs as a manual evaluation against the configured provider (it costs tokens and depends
on the model), with results recorded in the PR. Automated tests use a substituted chat service.

Tuning loop: when a name is misjudged, first add or sharpen a generic example for its category;
if it is still missed, add the real name to the prompt's examples. Re-run the whole set after each
change so a fix for one name doesn't flip a clean one. New spam names seen in production join the
set the same way.

## Testing

Unit:
- Prompt contains the flag definition; response parsing reads `promotional_display_text`, with a
  missing field defaulting to false.
- Name-only trigger, one test per case: no session, unresolvable, timeout, `FLOOD_WAIT`, rule
  short-circuit, bot. No trigger when scanning is disabled or the AI feature is unavailable.
- Verdict precedence: explicit beats promotional.
- A name-only row leaves `profile_scanned_at` unchanged, so the next eligible scan is a full one.
- A failed name-only call logs a warning and writes nothing; no exception text reaches a chat.

Integration:
- A name-only check writes a `NameOnly` row and leaves score and outcome unchanged. Anchor:
  9333810782137 @loucurtsinger (not trusted, no scan rows); the written row is the assertion subject.
- A user whose latest row is promotional renders `[name removed: spam]` in a welcome message posted
  in the group and in a ban celebration caption (and the subscriber DM that copies it), while an
  admin notification DM about the same user shows the real name. Anchor: 9143878698845 @LisoBran, whose only scan
  (row 531) gets `ai_promotional_display_text = true` (canonical edit 2026-10-03; unreferenced,
  banned, AI fields filled in). Recorded in `GoldenDatasetConstants` and `IntegrationTests/CLAUDE.md`,
  whose profile-scan notes also gain the new column.
- Explicit beats promotional: 9220500615182 @bagging_armado (row 534 explicit, read-only).

Each test reads its anchor back and asserts the flags first.
