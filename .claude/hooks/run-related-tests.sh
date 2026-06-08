#!/usr/bin/env bash
#
# PostToolUse hook (matcher: Write|Edit).
# Runs the xUnit test class related to the C# file the agent just wrote/edited,
# and on failure feeds the output back into the agent's context (exit 2).
#
# Mapping (this repo's convention — see CLAUDE.md / test-plan.md §6.1):
#   Notes/**/Foo.cs            -> Notes.Tests/FooTests.cs     (run ~FooTests)
#   Notes/ViewModels/Fields/*  -> covered by FieldVmTests      (run ~FieldVm)
#   Notes.Tests/FooTests.cs    -> run that class directly       (run ~FooTests)
# Anything with no matching test class (interfaces, models, configs) is skipped.
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
base="$(basename "$file" .cs)"

# 2. Resolve the related test class.
class=""
if [[ "$rel" == Notes.Tests/*Tests.cs ]]; then
  class="$base"                                        # a test file was edited
elif [[ "$rel" == Notes/ViewModels/Fields/*.cs ]]; then
  class="FieldVm"                                      # field VMs share FieldVmTests
elif [ -f "$REPO_ROOT/Notes.Tests/${base}Tests.cs" ]; then
  class="${base}Tests"                                 # source file with a sibling test
fi

# 3. No related test class -> nothing to run.
[ -z "$class" ] && exit 0

# 4. Build + run only that test class.
echo "[run-related-tests] $rel -> dotnet test --filter FullyQualifiedName~$class"
if output="$(cd "$REPO_ROOT" && dotnet test --filter "FullyQualifiedName~${class}" --nologo 2>&1)"; then
  exit 0
fi

# 5. Failure: surface the result to the agent (stderr + exit 2).
{
  echo "Related tests FAILED for ${rel}"
  echo "Filter: FullyQualifiedName~${class}"
  echo
  printf '%s\n' "$output" | tail -n 40
} >&2
exit 2
