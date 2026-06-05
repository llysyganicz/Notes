# Review Follow-ups — templates

Items surfaced during impl-review and deferred to future work (not addressed in this change).

## FU-1 — Validate the form definition (duplicate field names, malformed fields)

- **Source**: Phase 2 impl-review F3 (`reviews/impl-review-phase-2.md`)
- **Origin code**: `Notes/ViewModels/TemplateFormViewModel.cs:47` — `Submit()` builds the result map via `ToDictionary`, which throws `ArgumentException` if two fields share a name.
- **Why deferred**: The Phase 1 parser deserializes a YAML map, whose keys are inherently unique, so duplicates can't reach the form today. It's a boundary assumption, not a live bug.
- **Proposed direction**: Validate a template's `form` definition up front (ideally when the template is saved/parsed) and surface a friendly validation error, rather than relying on the YAML map's implicit uniqueness or throwing deep in `Submit()`. Candidate checks: duplicate field names, unknown field `type`, dropdown missing `entries`.
- **Disposition**: Add to roadmap for future implementation (form-definition validation on save). Not in scope for the `templates` slice.

## FU-2 — Surface malformed `form:` instead of failing silently

- **Source**: Full-plan impl-review F3 (`reviews/impl-review.md`)
- **Origin code**: `Notes/Services/TemplateParser.cs` (broad `catch (Exception) → FormDefinition.Empty`) consumed by `Notes/ViewModels/NoteTreeViewModel.cs` `HandleNewFromTemplate` — a present-but-unparseable `form:` yields an empty definition, so the form dialog is skipped and the note is a static copy with literal `{{tokens}}`, with no signal to the user.
- **Why deferred**: Matches the locked design ("malformed/absent `form` → static copy"); the broad-catch is deliberate (`context/foundation/lessons.md`). Changing it now would add a net-new warning path to a closed slice.
- **Real-world trigger**: A user authored `form:` as a YAML sequence with tab indentation; it silently produced a static copy — confusing, looked like nothing happened.
- **Proposed direction**: When frontmatter *contains* a `form:` key but it parses to zero fields, surface a friendly warning (distinct from genuinely absent `form`, which should stay a silent static copy). Best solved by the post-MVP template designer/validator (see project memory `post-mvp-template-authoring-ux`).
- **Disposition**: Deferred to post-MVP (template designer or form validator). Not in scope for the `templates` slice.
