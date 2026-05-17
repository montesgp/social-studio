# UTC and Localization (Global Rule)

## Purpose
Keep date/time handling canonical and outward-facing messages localization-ready across the stack.

## Scope
All code that emits, persists, or transforms timestamps; all code that produces user-facing strings.

## Load Conditions
Always loaded when any profile is active.

## Do Not Load When
Never skipped.

## Rules
- MUST store and process timestamps in UTC. Local-time conversion happens at the presentation boundary.
- MUST centralize date/time parsing, formatting, and conversion in a shared module. Do not repeat parsing logic in multiple places.
- MUST keep outward-facing messages localization-ready: stable machine-readable error codes are separated from the localized text that renders them.
- MUST support the locale set declared by the active project manifest. The manifest is the source of truth for which locales must be covered.
- MUST NOT hard-code user-facing strings in business or application flows.
- SHOULD design every new feature so adding a new locale does not require refactoring code paths.

## Output Expectations
- Timestamps in domain/application contracts are UTC.
- Outbound errors include both a stable error code and a separately-resolvable localized message.
- New user-facing copy is added to the project's translation source of truth in the same task that introduces it.
