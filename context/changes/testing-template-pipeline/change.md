---
change_id: testing-template-pipeline
title: Template pipeline correctness tests (rollout Phase 1)
status: impl_reviewed
created: 2026-06-06
updated: 2026-06-07
archived_at: null
---

## Notes

Open a change folder for rollout Phase 1 of context/foundation/test-plan.md: "Template pipeline correctness".
Risks covered: #1 (create-from-template produces a wrong/corrupt note — leftover placeholders, wrong-slot substitution, dropped fields), #2 (malformed/edge-case template frontmatter silently fails or renders wrong), #6 (saved note is no longer valid/portable .md — frontmatter/encoding/line-ending corruption).
Test types planned: unit + integration (MockFileSystem).
Risk response intent:
- #1: prove a rendered note has zero {{...}} tokens and each value lands in its declared slot, with the oracle from the template definition + input (never copied from the renderer).
- #2: prove unknown/missing field types and empty/odd YAML are surfaced (error or defined fallback), never a silent half-form.
- #6: prove save→reload round-trips frontmatter + body faithfully and the output is valid markdown for other tools.
After creating the folder, follow the downstream continuation rule.
