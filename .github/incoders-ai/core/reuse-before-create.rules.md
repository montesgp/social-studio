# Reuse Before Create (Global Rule)

## Purpose
Prevent parallel utility, helper, mapper, validator, adapter, and UI primitive ecosystems from forming inside a repository.

## Scope
All code repositories that load the Incoders AI ecosystem.

## Load Conditions
Always loaded when any profile is active.

## Do Not Load When
Never skipped.

## Rules
- MUST search the repository for an existing reusable abstraction before creating a new helper, mapper, utility, validator, service, adapter, dialog, button variant, filter panel, form shell, table/list shell, modal, loader, status badge, pagination control, notification primitive, or multi-step progress indicator.
- MUST prefer extending an existing primitive over duplicating its shape under a new name.
- MUST NOT create a parallel UI component when the only difference from an existing primitive is configuration (e.g., step labels, step count, visual variant). Extend via a new prop instead. See `core/ui-primitives.rules.md` §Extending primitives for the full contract.
- MUST NOT introduce a second equivalent abstraction unless the divergence is documented and reviewed.
- SHOULD centralize common date/time, error, mapping, key-generation, and validation behavior in shared modules.
- SHOULD keep shared abstractions simple, discoverable, and named after the role they play rather than the feature that introduced them.
- When an existing feature is partially broken but already points at a shared pattern: MUST stabilize the shared primitive first, then refactor the feature to consume it.

## Output Expectations
- Reviews flag duplicated utilities, dialogs, table shells, filter panels, and form shells.
- New helpers/components include a one-line note explaining why no existing primitive served the need.
- Pull requests that re-introduce a deleted parallel pattern are sent back for refactor.
