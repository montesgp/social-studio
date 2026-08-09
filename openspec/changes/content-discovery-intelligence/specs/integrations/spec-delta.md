# Spec Delta: Integrations — `niche-search` ActorVariantKey and Discovery Workflow Visibility

<!-- Change: content-discovery-intelligence -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Add a new `ActorVariantKey` value, `"niche-search"`, to the existing `(networkKey, variantKey)`
→ actor-id mapping used for Apify provider resolution, so the TikTok niche discovery source can
resolve its actor the same way `"profile"` and `"social-extraction"` already do — and never by
a hardcoded fallback. Also make the discovery workflow/step visible and configurable in the
integrations settings UI, and register a default workflow definition with a seeded placeholder
mapping so the operator has something to configure.

> **Amendment (2026-08-09)** — owner correction: no actor may ever be forced in code; it must
> always come from workflow/step configuration. This delta adds INT-DISC-02..04 to make that
> configuration path, its default seed, and its settings-UI visibility explicit requirements,
> not implementation detail. See the proposal's `Scope Amendment` section.

## ADDED Requirements

### Requirement: INT-DISC-01 — `niche-search` ActorVariantKey Resolves an Apify Actor, Never a Hardcoded Fallback

The Apify `actor_mappings_json` resolution mechanism MUST accept `variantKey = "niche-search"`
for `networkKey = "tiktok"`, resolving to a configured actor id using the same mapping shape as
`"profile"` and `"social-extraction"`. An organization without a configured `"niche-search"`
entry for `tiktok` MUST fail fast with a structured configuration error, not an unhandled
exception, and MUST NOT prevent other discovery sources from running (see content-discovery
CD-01). No Application or Infrastructure code path MUST substitute a literal actor id when the
mapping is absent or empty — resolution either returns the configured id or fails structurally;
it never falls back to a code-level default (see content-discovery CD-12).

#### Scenario: Configured niche-search entry resolves the actor

- GIVEN an `actor_mappings_json` credential with a `(networkKey="tiktok", variantKey="niche-search")` entry
- WHEN the niche discovery source resolves its actor
- THEN the configured actor id is returned

#### Scenario: Missing niche-search entry fails fast without blocking other sources

- GIVEN an `actor_mappings_json` credential with no `"niche-search"` entry for `tiktok`
- WHEN the niche discovery source is asked to run
- THEN it returns a structured configuration failure for itself
- AND the discovery chain still reports results from other configured sources
- AND no literal actor id is substituted in code anywhere along that path

### Requirement: INT-DISC-02 — A Default Discovery Workflow/Step Definition Ships, Seeded, Not Hardcoded-Live

`WorkflowConfigurationCatalog` MUST define a `discover-niche-content` step (operation key
`"niche-search"`, `RequiresNetwork: true`, `SupportedNetworkKeys: ["tiktok"]`,
`SupportsActorSelection: true`, `ActorVariantKey: "niche-search"`) so the operator has a step
to configure. A placeholder `(tiktok, niche-search)` actor mapping MAY be seeded (e.g. via
backfill or an initial migration) so the configuration surface is non-empty by default; that
seed is data, not a code fallback, and remains fully editable/removable by the operator without
a code change. Removing or clearing the seeded mapping MUST degrade to INT-DISC-01's structured
configuration failure, never to a literal actor id resolved in code.

#### Scenario: Default step is present for the operator to configure

- GIVEN a freshly provisioned organization
- WHEN the operator opens the discovery workflow configuration
- THEN the `discover-niche-content` step for `tiktok` is present to configure, with or without
  a seeded actor mapping

#### Scenario: Clearing the seeded mapping degrades to a structured failure, not a code fallback

- GIVEN an organization whose seeded `(tiktok, niche-search)` mapping is cleared by the operator
- WHEN the niche discovery source runs
- THEN resolution fails structurally per INT-DISC-01
- AND no literal actor id is substituted in code

### Requirement: INT-DISC-03 — Discovery Workflow Is Visible and Configurable in the Integrations Settings UI

The settings UI's workflow selector is built from the integration overview checklist groups
returned by `GetOrganizationIntegrationsHandler` (currently only `("generator", "Content
Generator", generatorChecklist)`). A workflow key with no matching checklist group renders as
"not configurable yet" and is effectively invisible to the operator. The discovery workflow
key(s) that own the `discover-niche-content` step MUST have a matching checklist group entry
returned by `GetOrganizationIntegrationsHandler`, so the discovery stage's actor configuration
is reachable and editable from the integrations settings screen, not merely present in code.

#### Scenario: Discovery workflow appears as a configurable group in settings

- GIVEN an organization viewing the integrations settings overview
- WHEN the overview checklist groups are rendered
- THEN a group corresponding to the discovery workflow is present
- AND the operator can configure the `(tiktok, niche-search)` actor mapping from that group

#### Scenario: A workflow key without a checklist group is not silently shipped

- GIVEN the discovery workflow key does not yet have a matching checklist group entry
- WHEN this is discovered during implementation
- THEN it is treated as an incomplete implementation of this requirement, not an acceptable gap
  (per `WorkflowConfigurationCatalog.cs:123-126`, which documents exactly this trap)

### Requirement: INT-DISC-04 — `niche-search` Is a Distinct Actor Variant, Separate From `profile` and `social-extraction`

`niche-search` MUST be its own `ActorVariantKey`, routed as its own step, with its own actor
mapping entry. It MUST NOT reuse or be backfilled from the `profile` (profile-shaped) or
`social-extraction` (URL-shaped) variant mappings. `ApifyActorMappingBackfill.Transform`, which
retags legacy `default`/empty entries into `profile`/`social-extraction`, MUST NOT be extended
to also produce `niche-search` — doing so would silently re-tag existing extraction actors and
risk breaking the single analyzer. A `niche-search` mapping MUST be added explicitly by the
operator (or an explicit seed per INT-DISC-02), never inferred from another variant's data.

#### Scenario: `niche-search` mapping is independent of `profile`/`social-extraction`

- GIVEN an organization with configured `profile` and `social-extraction` mappings for `tiktok`
  but no `niche-search` mapping
- WHEN the niche discovery source resolves its actor
- THEN resolution fails per INT-DISC-01 rather than reusing the `profile` or
  `social-extraction` actor id

#### Scenario: Backfill does not invent a `niche-search` entry

- GIVEN `ApifyActorMappingBackfill.Transform` runs against legacy `default`/empty entries
- WHEN backfill completes
- THEN no `niche-search` entry is produced by backfill
- AND existing `profile`/`social-extraction` entries are unaffected
