# Localization-Ready API

## Purpose
Return messages, validation text, and errors in a way that does not block future locale expansion.

## Scope
Any backend that returns user-visible text (errors, validation messages, notifications).

## Load Conditions
A backend or fullstack profile is active and outward-facing text is produced.

## Do Not Load When
Pure machine-to-machine APIs whose responses are never rendered to humans.

## Inputs
- The set of locales declared by the active project manifest.

## Procedure
1. Separate stable error codes from localized text. The code is the contract; the text is presentation.
2. Resolve localized text from a centralized translation source keyed by error code and locale.
3. Accept a locale signal (header, claim, or query parameter) at the API boundary and propagate it through the application layer.
4. Avoid hard-coded user-facing strings in business or application flows.
5. Design the resolver so adding a new locale does not require touching code paths.

## Output Expectations
- Error responses carry both `code` (stable) and `message` (localized).
- Validation messages localize per request without code changes.
- Adding a new locale is a configuration/data task, not a refactor.
