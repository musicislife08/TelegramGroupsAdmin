#!/usr/bin/env bash
# PreToolUse hook (Write|Edit|MultiEdit): when the target file is an integration/E2E test
# source, canonical SQL, or a superpowers plan/spec, inject the integration-test data rules
# into the model's context. Path-scoped .claude/rules only fire on Read, which Bash-based
# grep/sed and plan authoring bypass — this hook closes that gap deterministically.
# Exits 0 silently for every other path.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // .tool_input.filePath // empty' <<<"$input")"
[[ -z "$file_path" ]] && exit 0

root="${CLAUDE_PROJECT_DIR:-$(pwd)}"
rel="${file_path#"$root"/}"

case "$rel" in
  TelegramGroupsAdmin.IntegrationTests/*.cs|TelegramGroupsAdmin.IntegrationTests/*/*.cs|TelegramGroupsAdmin.IntegrationTests/*/*/*.cs|TelegramGroupsAdmin.IntegrationTests/*/*/*/*.cs) ;;
  TelegramGroupsAdmin.IntegrationTests/*.sql|TelegramGroupsAdmin.IntegrationTests/*/*.sql|TelegramGroupsAdmin.IntegrationTests/*/*/*.sql|TelegramGroupsAdmin.IntegrationTests/*/*/*/*.sql) ;;
  TelegramGroupsAdmin.E2ETests/*.cs|TelegramGroupsAdmin.E2ETests/*/*.cs|TelegramGroupsAdmin.E2ETests/*/*/*.cs) ;;
  docs/superpowers/plans/*.md|docs/superpowers/specs/*.md) ;;
  *) exit 0 ;;
esac

rule_file="$root/.claude/rules/integration-test-data.md"
[[ -f "$rule_file" ]] || exit 0

# Inject once per session: the rule stays in context after the first injection, and repeating
# it on every edit of a plan or test file is pure noise. Subagents that share the session id
# still get the rule from the root CLAUDE.md pointer.
session_id="$(jq -r '.session_id // empty' <<<"$input")"
if [[ -n "$session_id" ]]; then
  marker="${TMPDIR:-/tmp}/claude-test-data-rules-${session_id}"
  [[ -e "$marker" ]] && exit 0
  : > "$marker"
fi

# Strip the YAML front matter; the rule body is what the model needs.
body="$(awk 'BEGIN{fm=0} NR==1&&/^---$/{fm=1;next} fm==1&&/^---$/{fm=2;next} fm!=1{print}' "$rule_file")"

jq -n --arg ctx "$body" --arg f "$rel" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    additionalContext: ("[integration-test-data rules — injected because you are editing " + $f + "]\n\n" + $ctx)
  }
}'
