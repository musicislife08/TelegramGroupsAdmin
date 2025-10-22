# Telegram Users UI

**Status**: Phase 4.12 - 90% Complete

## Pending Work

### Settings Tag Management UI (/settings#tags)

**Database & Backend**: ✅ Complete (AdminNotesRepository, UserTagsRepository, Actor system)
**Users UI**: ✅ Complete (notes, tags, pin/unpin, add/remove)
**Settings UI**: 🔲 Pending

**Tasks**:
- [ ] /settings#tags tab structure
- [ ] Tag list view (name, color chip, usage count)
- [ ] Create tag dialog (name input + MudSelect color picker)
- [ ] Edit tag functionality (change color only)
- [ ] Delete tag validation (block if in use, show usage count)
- [ ] Lowercase enforcement (client-side validation)

**Technical Notes**:
- Tags use TagType enum (predefined colors)
- Tag names stored lowercase in DB
- Cannot delete tags currently in use
- Usage count: `SELECT COUNT(*) FROM user_tags WHERE tag_type = ?`

---

**See CLAUDE.md for overall roadmap**
