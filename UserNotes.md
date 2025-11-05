# Notes

A list of my notes to talk about in the future

## Items

~~- bot caught falsely something it thought was spam by a non admin user in group a.  when it went to auto ban it worked a little too well.  it also banned him from group b where he was an admin which i find odd it was able to do so.  i propose a new logic.  all admin users are flagged as trusted automatically anytime it discovers a new admin~~

**RESOLVED (2025-01-04)**: Implemented admin protection with two layers:
- **Layer 1 (Ban Prevention)**: SpamActionService checks if user is admin in ANY managed chat before banning → skips entire ban process if yes
- **Layer 2 (Auto-Trust)**: ChatManagementService automatically trusts admins when promoted → prevents spam detection from running
- Admins are now protected globally across all chats, even when posting in chats where they're not admin
