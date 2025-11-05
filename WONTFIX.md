# Won't Fix - TelegramGroupsAdmin

Items that were considered but decided against implementing. Kept for historical context and to prevent re-proposing the same features.

---

## SECURITY-3: CSRF Protection on API Endpoints

**Closed:** 2025-01-04
**Reason:** Not applicable - same-origin policy enforced, Cloudflare WAF protection, homelab deployment
**Context:** API endpoints use JSON (`[FromBody]`), not forms. Same-origin policy blocks cross-origin requests by default (no CORS configured). Logout endpoint has minimal impact (forced logout only). Defense layers: same-origin policy, Cloudflare WAF, auth cookies, intermediate tokens, rate limiting (SECURITY-5).

---
