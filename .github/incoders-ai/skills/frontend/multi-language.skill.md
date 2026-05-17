# Multi-Language and Translation Quality

## Purpose
Keep every user-facing string translatable, locale trees aligned, and missing keys visible during development.

## Scope
Any frontend code that produces user-facing text.

## Load Conditions
A frontend or fullstack profile is active and the active manifest declares more than one locale (or expects future locales).

## Do Not Load When
A single-locale application with no foreseeable localization requirement.

## Inputs
- The locale source-of-truth and translation source-of-truth declared by the active project manifest.
- The active manifest's canonical locale enum/type.

## Procedure
1. Use the project's canonical locale type as the source of truth for required locale coverage.
2. Use the project's translation source-of-truth file as the only place translations live.
3. Never hardcode user-facing text in templates, screens, dialogs, forms, tables, states, or services that produce UI copy.
4. Define stable translation keys based on feature, scope, and intent (hierarchical dot notation; semantic rather than literal). Examples: `common.actions.save`, `common.states.loading`, `<feature>.list.filters.<filter>`, `<feature>.form.fields.<field>.label`, `<feature>.messages.createSuccess`.
5. Keep pluralization and formatting locale-aware.
6. Do not mix languages in the same UI path.
7. Translation entries must stay structurally consistent across all locales declared by the manifest.
8. Missing translations must fail visibly during development (explicit missing-translation marker) — do not silently render the raw key.
9. Every new key must exist in every locale declared by the manifest before the task is considered complete.

## Mandatory workflow when introducing or modifying UI
1. Reuse an existing translation key if one already exists.
2. Otherwise define a stable, semantic key.
3. Use the key in template/code instead of raw text.
4. Update the translation source-of-truth in the same task.
5. Add the new entry for every locale declared by the manifest.
6. Verify no unresolved key reaches the UI.

## Completion rule
A task is NOT complete if:
- User-facing text was introduced but not translated.
- A translation key is referenced in UI but missing from the translation source-of-truth.
- A key exists for one locale but not for all declared locales.
- Locale trees drift structurally.
- Unresolved keys can reach the rendered UI.

## CRUD/admin requirement
Admin CRUD modules must define translation keys for: page title, page subtitle, help content, filters, columns, form labels, placeholders, validation messages, success messages, delete confirmations, empty states, bulk actions, active/inactive states.

## Output Expectations
- No raw user-facing strings remain in templates or component code.
- Locale parity holds across every declared locale.
- Missing keys are caught at development time, not by users.
