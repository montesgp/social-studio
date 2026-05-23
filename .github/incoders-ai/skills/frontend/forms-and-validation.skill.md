# Forms and Validation

## Purpose
Build forms, validators, helper texts, placeholders, tooltips, date ranges, and field-state behavior consistently across the application.

## Scope
Any frontend form: create, edit, filter, search, settings, auth.

## Load Conditions
A frontend or fullstack profile is active and form work is in scope.

## Do Not Load When
Pure backend or pure infrastructure repos.

## Inputs
- The fields to render.
- The validation rules (technical + business).
- The translation keys/locales declared by the project manifest.

## Procedure
1. Use the framework's reactive forms primitive (Angular Reactive Forms, React Hook Form, Formik, etc. — whatever the active stack declares).
   - **UI primitives**: always use `<Select>`, `<Input>`, `<Button>` from `@/components/ui/` — raw `<select>`, `<input>`, `<button>` are forbidden in feature code. See `core/ui-primitives.rules.md` for the full contract and size table.
2. Reuse shared validators when applicable (email, phone, URL, required-trimmed, min/max length, document ID patterns, numeric constraints, semantic date ranges). Do not duplicate regex rules across features.
3. Keep validators centralized (`shared/validators`, `core/validation`, or equivalent). Feature-local validators only for truly feature-specific rules.
4. For semantic date-range pairs (`start`/`end`, `from`/`to`, `date_from`/`date_to`, `valid_from`/`valid_to`): apply a reusable cross-field validator, prevent invalid submission, wire UI constraints (`min`/`max`/datepicker filter), preserve the rule in both create and edit, support optional open-ended ranges only when explicitly intended, and map failures to clear translated messages.
5. Map validation messages to translation keys; keep them consistent and reusable.
6. Disable primary actions until BOTH technical validation AND business prerequisites pass. Do not enable a button merely because a field has a value.
7. For admin/CRUD/dashboard/operator forms, provide helper guidance that explains: what the action does, why a button may be disabled, side effects, dependencies, visibility implications.
8. Use shared async-button (or equivalent) for submit; prevent duplicate submissions; expose loading/submitting/success/error states.
9. Use translation keys for every visible label, placeholder, hint, and tooltip.
10. Use semantic design tokens for state styles; no hardcoded colors.

## Output Expectations
- Forms across the app feel like one design system, not separate experiments.
- Date ranges cannot be submitted in an inconsistent state.
- Buttons reflect the real validity of the use case, not just field presence.
- Validation messages are translated and consistent.
