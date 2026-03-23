# Profile Scanner Redesign — Design Spec

## Problem

The current profile scanner is narrowly focused on adult/explicit content and known spam patterns. It misses entire categories of non-genuine profiles (commercial accounts, incoherent/manufactured identities, bot patterns, scam promotion, impersonation). The detection model needs to invert from "flag known-bad" to "flag anything that deviates from a genuine community member."

Additional issues with the current system:
- The nudity detection prompt language primes the AI to be conservative, causing it to rationalize away visible nudity
- Signal convergence is artificially capped at confidence 70, preventing multi-signal profiles from reaching ban threshold
- The 3-bucket confidence-to-score mapping (0 / 2.5 / 4.5) has no granularity — confidence 79 scores identically to 40

## Design Decisions

### Detection Model: Normalcy Assessment

- **Old question**: "Is this spam/adult content?"
- **New question**: "Does this profile belong to a genuine community member?"
- Flag deviation from normalcy; the score indicates severity

### AI Response Contract

The AI returns an assessment, not a decision. No threshold awareness, no flagging — just a score.

```json
{
  "score": 0.0-5.0,
  "reason": "clear explanation",
  "signals_detected": ["signal1", "signal2"],
  "contains_nudity": true/false
}
```

- **`score`**: Continuous 0.0–5.0 scale. The application decides outcomes based on configurable thresholds.
- **`reason`**: Human-readable explanation for admin review UI.
- **`signals_detected`**: Structured tags for logging/analytics.
- **`contains_nudity`**: Drives Gaussian blur censoring only, not scoring. Narrow definition: bare breasts, exposed genitalia, exposed buttocks. Lingerie/swimwear/cleavage do NOT set this flag.
- **Removed fields**: `spam` (score replaces the boolean gate), `confidence` (score IS the assessment on the application's scale).

### Prompt Architecture: Three-Part Sandwich

Code-controlled top and bottom, swappable middle section:

| Section | Method | Editable? |
|---------|--------|-----------|
| Top | `GetTechnicalContract()` | Code-controlled, always |
| Middle | `GetDefaultDetectionCriteria()` | Hardcoded now, wirable to config later |
| Bottom | `GetBehavioralGuardrails()` | Code-controlled, always |

```csharp
internal static string BuildSystemPrompt(string? customDetectionCriteria = null)
{
    var technical = GetTechnicalContract();
    var criteria = customDetectionCriteria ?? GetDefaultDetectionCriteria();
    var guardrails = GetBehavioralGuardrails();
    return $"{technical}\n\n{criteria}\n\n{guardrails}";
}
```

The `customDetectionCriteria` parameter is plumbed but never populated — all callers pass `null` for now. When UI configurability is added later, only the middle section changes. The response contract and behavioral guardrails remain code-controlled.

**Future UI configurability**: When ready, the custom detection criteria can be stored via a new `ConfigType` entry or the existing `prompt_versions` table with a feature type discriminator. The architectural seams are in place.

### Detection Categories (6)

1. **Adult / Explicit** (score 4.0–5.0): Visible nudity, implied nudity, adult services, adult-branded channels, explicit stories/content.

2. **Commercial / Service Account** (score 4.0–5.0): Business/brand display name, product/logo photos, ad-like bios, business usernames. Profile exists to promote commercial activity.

3. **Incoherent / Manufactured** (score 3.0–4.5): Name/bio/photo tell unrelated stories, gibberish bio, elements assembled from different identities.

4. **Bot / Mass-Created** (score 3.0–4.5): Template names (CATEGORY + KEYWORD), all-caps brand names, sequential/generated usernames, empty profile with promotional name.

5. **Scam / Scheme Promotion** (score 4.0–5.0): Crypto/investment schemes, guaranteed returns, gambling promotion, phishing, spam domains, URL shorteners to suspicious content. Note: Decentralized identity strings (Nostr npub keys, Lightning addresses, ENS .eth names) are legitimate social identifiers, not scam signals.

6. **Impersonation** (score 3.5–5.0): Pretending to be a public figure, celebrity, or well-known person. Note: Group admin impersonation is handled by a separate system in the welcome flow.

### Clean Profile Definition

A clean profile (score near 0.0) has:
- A name that sounds like a real person's name, in any language or culture
- A photo that is personal: selfie, pet, landscape, avatar, anime, artwork, group photo, or no photo
- A bio (if present) about personal interests, hobbies, work, education, a quote, or simply empty
- Profile elements that don't contradict each other

**Empty profiles are clean.** Most real users have minimal profiles. Do not penalize absence of information — penalize presence of wrong information.

### Signal Convergence

Multiple signals pointing in the same direction compound — do not average. Each additional aligned signal makes the case stronger.

- Suggestive photo alone → 2.0–2.5
- Suggestive photo + suggestive name → 3.0–3.5
- Suggestive photo + suggestive name + suggestive username + empty profile → 4.0–4.5
- Business name + product photo → 3.5–4.0
- Business name + product photo + incoherent bio → 4.0–4.5

Guiding question: "Could a reasonable person look at this entire profile and believe it belongs to a genuine community member?" If clearly no → 4.0+.

### Nudity Flag (`contains_nudity`)

Set to true ONLY for visible nudity that would violate public indecency laws:
- Bare breasts (not cleavage in clothing/lingerie)
- Exposed genitalia
- Exposed buttocks

Does NOT include: lingerie, swimwear, revealing clothing, suggestive poses, cleavage, implied nudity. Those are handled by the score. Purpose: triggers Gaussian blur censoring in admin review.

### User Prompt Update

Opening line changes from:
> "Analyze this Telegram user profile for spam/adult content indicators."

To:
> "Assess whether this Telegram user profile belongs to a genuine community member."

All prompt injection protections remain unchanged — `SanitizeForPrompt()` XML-escapes all user-generated content, wrapped in XML boundary tags.

## Scoring Engine Changes

### AI Score Passthrough

The 3-bucket confidence-to-score mapping is eliminated. The AI returns a score directly on the 0–5 scale, clamped to valid range:

```csharp
var score = Math.Clamp(response.Score, 0.0m, MaxScore);
```

### Layer 1 + Layer 2: Additive (Unchanged)

- Layer 1 runs first: Telegram scam/fake flags → 5.0, blocked URLs → +3.0, stop words → +1.5
- If Layer 1 >= ban threshold → skip AI, done
- Otherwise Layer 2 (AI) runs: `total = ruleScore + aiScore`, capped at 5.0

### Thresholds (Unchanged)

- Ban: score >= 4.0 (configurable per chat)
- Review: score >= 2.0 (configurable per chat)
- Clean: score < 2.0

### Record Changes

`ProfileScanAIResponse`:
```csharp
// Old
record ProfileScanAIResponse(bool Spam, int Confidence, string? Reason, string[]? SignalsDetected, bool ContainsNudity);

// New
record ProfileScanAIResponse(decimal Score, string? Reason, string[]? SignalsDetected, bool ContainsNudity);
```

`ScoringResult` — drop `AiConfidence`:
```csharp
record ScoringResult(decimal Score, ProfileScanOutcome Outcome, decimal RuleScore,
    decimal AiScore, string? AiReason, string[]? AiSignals, bool ContainsNudity);
```

### Database Migration

Drop `ai_confidence` column from `profile_scan_results` table. No data migration — existing `score` and `ai_score` values are already on the 0–5 scale and represent accurate historical records under the old model.

## Files Changed

| File | Change |
|---|---|
| `Telegram/Services/UserApi/ProfileScanPrompts.cs` | Three-part system prompt, updated user prompt opening, updated `ProfileScanAIResponse` record |
| `Telegram/Services/UserApi/ProfileScoringEngine.cs` | Score passthrough with clamp, drop `Confidence` from `AiScoringResult` |
| `Telegram/Services/UserApi/ScoringResult.cs` | Drop `AiConfidence` |
| `Telegram/Models/ProfileScanResultRecord.cs` | Drop `AiConfidence` |
| `Telegram/Repositories/Mappings/ProfileScanResultMappings.cs` | Remove `AiConfidence` from both mapping directions |
| `Telegram/Services/UserApi/ProfileScanService.cs` | Remove `AiConfidence` from `ScoringResult` usage and history insert |
| `Data/Models/ProfileScanResultDto.cs` | Drop `AiConfidence` property |
| `Components/Shared/ProfileScanHistoryDialog.razor` | Remove confidence display |
| `Docs/features/08-profile-scanning.md` | Update AI response docs, remove confidence references, document new detection categories |
| `UnitTests/.../ProfileScoringEngineTests.cs` | Replace AI-layer tests, update Layer 1 tests for record shape |
| New EF Core migration | Drop `ai_confidence` column |

## Test Plan

Unit tests in `ProfileScoringEngineTests.cs`:

**Replace existing AI-layer tests** — all tests using the old `{"spam": true/false, "confidence": N}` JSON format and 3-bucket mapping assertions are replaced with:

- AI returns score on 0–5 scale → verify passthrough
- AI returns score > 5.0 → verify clamped to 5.0
- AI returns score < 0.0 → verify clamped to 0.0
- AI returns clean profile (score 0.0) → verify 0.0 passthrough
- Layer 1 + Layer 2 additive: rule score 1.5 + AI score 3.0 = 4.5 → ban
- AI returns `contains_nudity: true` → verify `ScoringResult.ContainsNudity` is true
- AI returns `contains_nudity: false` → verify `ScoringResult.ContainsNudity` is false
- Nudity flag passes through independently of score value
- Malformed JSON fallback → `AiScoringResult.Empty`

**Update existing Layer 1 tests** — record shape changes only (remove `AiConfidence` from assertions). Test logic unchanged.

## Out of Scope

- Username blocklist stays in welcome flow (may move to profile scanner Layer 1 in future)
- "No shared groups" signal (deferred)
- Per-chat custom detection criteria (architectural seams in place, not wired to UI)
- UI for custom prompt editing (future work)

## Test Cases (From Discussion)

| Profile | Expected Score | Nudity | Reasoning |
|---------|---------------|--------|-----------|
| VOIP DEVELOPMENT — business name, @VOIPDEVELOP, bio "Atiku oo1", product photo | 4.0–5.0 (ban) | false | Commercial name + product photo + incoherent bio |
| Scarlett Lux — tongue photo with visible bare breasts, suggestive name/username | 4.5+ (ban) | true | Visible nudity + suggestive identity + empty profile |
| Scarlett Lux — lingerie on hotel bed, suggestive name/username | 4.0–4.5 (ban) | false | Lingerie photo + suggestive identity + empty profile. Lingerie != nudity |
| PeeJay / Morkai Paraka — real name, brand logo photo | 2.0–3.0 (review) | false | Brand logo as photo + identity mismatch. Could be a real DJ |
| Rehema Otieno — real name, photo in a shop, empty bio | 0.0–1.0 (clean) | false | Real name, personal photo, coherent |
| I_Am_Coded — handle-style name, anime avatar, "Coffee & good vibes" | 0.0–0.5 (clean) | false | Personal handle, anime avatar, coherent identity |
| Beatriz Figueira — real name, travel photos, polished aesthetic | 0.0–1.0 (clean) | false | Real name, personal photos, coherent. Undetectable at profile level |
