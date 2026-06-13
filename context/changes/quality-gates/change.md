---
change_id: quality-gates
title: Quality gates
status: impl_reviewed
created: 2026-06-13
updated: 2026-06-13
archived_at: null
---

## Notes

This change is **Phase 4 of the test plan** (`context/foundation/test-plan.md` §3) — "Quality-gates wiring".

Goal (per §3): lock the floor by mapping format/build/test to CI steps; post-edit hook is recommended-local. Covers cross-cutting risk.

Gates this phase enforces (per §5):
- `dotnet format --verify-no-changes` (format) — local + CI, **required after this phase**.
- post-edit hook (run affected tests) — local agent loop, **recommended after this phase**.
- build (`dotnet build`) and `dotnet test` gates already required from earlier phases; this phase wires them into CI alongside format.

Note: `.github/workflows/release.yml` already exists (test-plan §4 stack-grounding) — relevant prior art for the CI wiring.
