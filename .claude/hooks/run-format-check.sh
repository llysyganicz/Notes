#!/usr/bin/env bash
#
# PostToolUse hook (matcher: Write|Edit).
# Verifies that the C# file the agent just wrote/edited satisfies
# `dotnet format --verify-no-changes`, feeding any violation back into
# the agent's context as a non-zero exit (exit 2) so the agent corrects it.
#
# The check is verify-only — this hook never mutates the file.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# 1. Read the hook payload from stdin and pull out the edited file path.
payload="$(cat)"
file="$(printf '%s' "$payload" | jq -r '.tool_input.file_path // empty')"
[ -z "$file" ] && exit 0

# Only C# source files are relevant.
case "$file" in
  *.cs) ;;
  *) exit 0 ;;
esac

rel="${file#"$REPO_ROOT"/}"
[ "$rel" = "$file" ] && exit 0  # outside repo, skip

# 2. Run format verification scoped to the single edited file.
# --no-restore: assumes packages are already restored (run 'dotnet restore' first on a fresh checkout).
echo "[run-format-check] $rel -> dotnet format --verify-no-changes --include \"$rel\""
if output="$(cd "$REPO_ROOT" && dotnet format --verify-no-changes --no-restore --include "$rel" Notes.slnx 2>&1)"; then
  exit 0
fi

# 3. Violation: surface the diagnostics to the agent (stderr + exit 2).
{
  echo "Format check FAILED for ${rel}"
  echo "Run: dotnet format --include \"${rel}\" Notes.slnx"
  echo
  printf '%s\n' "$output"
} >&2
exit 2
