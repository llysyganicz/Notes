---
change_id: file-safety
title: File-safety & data-loss guardrails (rollout Phase 2)
status: impl_reviewed
created: 2026-06-08
updated: 2026-06-08
archived_at: null
---

## Notes

Open a change folder for rollout Phase 2 of context/foundation/test-plan.md: "File-safety & data-loss guardrails".
Risks covered: #3 (create-from-template overwrites an existing note on a name collision), #4 (an interrupted/partial save truncates or corrupts the existing file), #5 (path-traversal/absolute/reserved names bypass service-layer validation).
Test types planned: integration (MockFileSystem).
Risk response intent:
- #3: prove create-from-template refuses or safely disambiguates a name collision instead of overwriting — verify the collision guard actually exists, don't assume one is already there.
- #4: prove an interrupted/partial save never truncates the existing file (atomic temp-then-rename or equivalent) — verify atomicity exists before simulating a crash against it.
- #5: prove the service layer (not only the dialog) rejects traversal/absolute/reserved names — assert service parity, not just the UI validator.
After creating the folder, follow the downstream continuation rule.
