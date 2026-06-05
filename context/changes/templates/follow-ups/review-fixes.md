# Review Follow-ups — templates

Items surfaced during impl-review and deferred to future work (not addressed in this change).

## FU-1 — Validate the form definition (duplicate field names, malformed fields)

- **Source**: Phase 2 impl-review F3 (`reviews/impl-review-phase-2.md`)
- **Origin code**: `Notes/ViewModels/TemplateFormViewModel.cs:47` — `Submit()` builds the result map via `ToDictionary`, which throws `ArgumentException` if two fields share a name.
- **Why deferred**: The Phase 1 parser deserializes a YAML map, whose keys are inherently unique, so duplicates can't reach the form today. It's a boundary assumption, not a live bug.
- **Proposed direction**: Validate a template's `form` definition up front (ideally when the template is saved/parsed) and surface a friendly validation error, rather than relying on the YAML map's implicit uniqueness or throwing deep in `Submit()`. Candidate checks: duplicate field names, unknown field `type`, dropdown missing `entries`.
- **Disposition**: Add to roadmap for future implementation (form-definition validation on save). Not in scope for the `templates` slice.
