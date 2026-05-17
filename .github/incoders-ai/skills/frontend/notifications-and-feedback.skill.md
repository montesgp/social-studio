# Notifications and Feedback

## Purpose
Deliver user feedback (save/update/delete success, warning, info, error, destructive confirmation) through a single shared system.

## Scope
Any frontend code that produces user feedback in response to actions.

## Load Conditions
A frontend or fullstack profile is active.

## Do Not Load When
Headless backends or pure CLI tools.

## Inputs
- The action being completed and its severity.
- The translation keys declared by the project manifest.

## Procedure
1. Use the shared notification system (toast/snackbar, confirm-dialog, inline validation, loader overlay) instead of inventing per-feature ad-hoc alerts.
2. Use toast/snackbar for transient feedback (save/update/delete success, non-blocking warnings, transient info, recoverable async errors).
3. Use a shared confirm-dialog for every destructive delete action. Confirm and cancel are explicit. The dialog explains what will be deleted when needed.
4. Never execute a destructive action without confirmation unless explicitly justified.
5. Use inline validation/helper text for field-level guidance.
6. Use a loader overlay only for blocking async workflows when justified.
7. Support a consistent severity model: success, info, warning, error. Do not create overlapping severity taxonomies.
8. Keep toast content short, clear, translatable, and severity-consistent.
9. No hardcoded user-facing strings; every notification, confirmation title/body/button uses translation keys.
10. No hardcoded colors; severity visuals use semantic design tokens.

## Output Expectations
- One feedback system across the product.
- Destructive actions never fire without confirmation.
- Severity visuals are consistent across all themes the project declares.
