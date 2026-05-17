# Auth Form Standards

## Purpose
Login, register, forgot-password, reset-password, and account-recovery flows use the same form standards as the rest of the application.

## Scope
Any authentication-related form.

## Load Conditions
A frontend or fullstack profile is active and authentication UI is in scope.

## Do Not Load When
Pure backend or no-auth public sites.

## Inputs
- The active manifest's declared form/validation primitives.
- The active manifest's declared notification system.

## Procedure
1. Use the shared input field component for email and password fields. No auth-only field components.
2. Email: required where applicable, validated for format, translated validation messages, no submit while invalid.
3. Password: use the shared input field or shared password-field pattern; support show/hide visibility with an eye icon (mandatory wherever a password field exists); translated validation and helper messages.
4. Submit: disabled until required fields are valid; prevent duplicate submission; show embedded loading/progress feedback; the loading/progress button is reusable beyond auth flows.
5. UX: auth forms feel consistent with the full design system — spacing, labels, helper text, error states, and action hierarchy match shared platform patterns. Do not mix different input/button styles across auth screens.
6. Security: include frontend attempt throttling or protective behavior where appropriate; structure the flow so backend enforcement can be added cleanly; do not treat frontend-only protection as sufficient; avoid request spam through careless submit handling.
7. Reuse the shared input field, password-visibility control, async/progress button, validation messages, form-shell, and error presentation.

## Output Expectations
- Auth screens are indistinguishable in style from the rest of the product.
- Submit cannot be triggered twice by accident.
- Password visibility is consistent across every screen with a password field.
