# Entity Key and Catalog Governance

## Purpose
Standardize automatic technical-key generation, normalization, visibility, and editability for catalog/reference entities.

## Scope
Any entity that stores both a user-facing `name` and a technical `key` (catalogs, reference data, lookup tables, taxonomy entries).

## Load Conditions
A frontend, fullstack, or monorepo profile is active and catalog entities are in scope.

## Do Not Load When
Transactional entities that have no separate technical-key requirement.

## Inputs
- The user-facing name entered.
- The active manifest's declared source language for technical keys.
- The entity's uniqueness scope.

## Required default fields for catalog entities
- `name` (user-facing)
- `key` (technical identifier)
- `is_active`
- `sort_order` when ordering applies
- `preponderance` (or equivalent business-prominence weight) when business priority applies

## Procedure
1. The system MUST assign a normalized technical key automatically from the entity name.
2. Key requirements: always a technical identifier; always lowercase; always snake_case; stored in English (or the project's declared technical-key language); deterministic and stable; unique within the entity scope.
3. Generation pipeline: read display name → normalize display format if required → detect/infer source language when needed → translate or map to the declared technical-key language → normalize to snake_case → validate uniqueness → persist as the entity key.
4. The UI derives the key automatically through a shared key service. Do not implement key generation ad hoc per feature.
5. Visibility: `key` is hidden for non-admin users by default; visible to administrators; only administrators can manually edit the generated key; non-admin flows still persist the generated key even when it is hidden.
6. Active state: all catalog forms include `is_active`; all consumers filter active records by default; inactive records appear only when a screen explicitly enables "show inactive". Exposing inactive records is a governance decision, not a per-feature choice.
7. Name normalization follows the entity's declared rules (sentence-style or title normalization per repository convention).
8. Reuse: do not implement one-off `is_active` filtering, one-off key generators, or one-off catalog field visibility logic. These belong in shared services.
9. When useful, distinguish: `key`, `generated_key`, `key_generation_source`, `is_key_manually_overridden`. Minimally: `key` + deterministic auto-generation + admin-only manual override.

## UX rules
- Explain to admins that the key is a technical identifier.
- Show uniqueness and format validation when admins can edit it.
- Keep common users focused on business fields, not technical identifiers.

## Output Expectations
- Catalog entities share one key-generation pipeline, one active-state filter, and one visibility rule.
- New catalogs cost configuration, not a re-implementation of key generation.
- Inactive records are never accidentally exposed to consumers.
