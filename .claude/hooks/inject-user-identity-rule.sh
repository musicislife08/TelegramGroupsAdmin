#!/usr/bin/env bash
# PreToolUse hook (Write|Edit|MultiEdit): when the target is production C#/Razor or a superpowers
# plan/spec, inject the user-identity rule once per session. Path-scoped .claude/rules only fire on
# Read, which Bash-based edits and plan authoring bypass. Exits 0 silently for every other path.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // .tool_input.filePath // empty' <<<"$input")"
[[ -z "$file_path" ]] && exit 0

root="${CLAUDE_PROJECT_DIR:-$(pwd)}"
rel="${file_path#"$root"/}"

case "$rel" in
  TelegramGroupsAdmin*Tests/*|TelegramGroupsAdmin.Testing.*) exit 0 ;;
  TelegramGroupsAdmin*/*.cs|TelegramGroupsAdmin*/*.razor) ;;
  docs/superpowers/plans/*.md|docs/superpowers/specs/*.md) ;;
  *) exit 0 ;;
esac

rule_file="$root/.claude/rules/user-identity.md"
[[ -f "$rule_file" ]] || exit 0

session_id="$(jq -r '.session_id // empty' <<<"$input")"
if [[ -n "$session_id" ]]; then
  marker="${TMPDIR:-/tmp}/claude-user-identity-rule-${session_id}"
  [[ -e "$marker" ]] && exit 0
  : > "$marker"
fi

body="$(awk 'BEGIN{fm=0} NR==1&&/^---$/{fm=1;next} fm==1&&/^---$/{fm=2;next} fm!=1{print}' "$rule_file")"

jq -n --arg ctx "$body" --arg f "$rel" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    additionalContext: ("[user-identity rule — injected because you are editing " + $f + "]\n\n" + $ctx)
  }
}'
