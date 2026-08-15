# Spec Delta: Integrations — Official-API Provider Category for YouTube Data API v3

<!-- Change: niche-discovery-engine -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Add a new provider category for official-API providers authenticated by an API key (no
precedent today — `.github/project-ai/providers/data/` has only Apify and data365, both
BringYourOwnKey/actor-mapping style), seed YouTube Data API v3 as its first member, register the
NicheDiscovery workflow steps in `WorkflowConfigurationCatalog` with a matching checklist group,
and route per-stage origin selection through the existing configuration-driven pattern extended
to cover multiple provider origins per stage (YouTube Data API v3 primary; Apify wire-capable as
a future secondary origin).

## ADDED Requirements

### Requirement: INT-ND-01 — Official-API Provider Category With API-Key Credential Schema

`IntegrationProvider` MUST support a new category for providers authenticated by a single
API-key credential (distinct from the Apify actor-mapping credential shape). This category MUST
reach `Connected` status once the API key is present and validated, without requiring an
`actor_mappings_json`-style credential.

#### Scenario: API-key provider connects with a single credential field

- GIVEN an organization supplies a valid API key for an official-API provider
- WHEN the integration is saved
- THEN the provider status becomes `Connected`
- AND no `actor_mappings_json` entry is required for that provider

#### Scenario: Missing API key leaves the provider unconfigured

- GIVEN an organization has not supplied an API key for an official-API provider
- WHEN the integration status is read
- THEN the status is `NotConfigured`, not `Connected`

### Requirement: INT-ND-02 — YouTube Data API v3 Seeded as the First Official-API Provider

`IntegrationCatalogSeeder` MUST seed `youtube-data-api-v3` as an official-API provider (per
INT-ND-01) available for NicheDiscovery sourcing. This is independent of the existing `youtube`
provider row used by publishing/Apify scrape flows.

#### Scenario: YouTube Data API v3 appears as a distinct connectable provider

- GIVEN the catalog seeder has run
- WHEN the provider catalog is listed
- THEN `youtube-data-api-v3` appears as a selectable official-API provider, distinct from the
  existing `youtube` publishing/scrape provider row

### Requirement: INT-ND-03 — NicheDiscovery Workflow Steps Are Registered With a Checklist Group

`WorkflowConfigurationCatalog` MUST register the NicheDiscovery sourcing step(s) under a
checklist group returned by the organization integrations overview handler, so the workflow key
is reachable and configurable in Settings rather than silently rendering "not configurable yet."

#### Scenario: NicheDiscovery sourcing step is configurable in Settings

- GIVEN an organization viewing the integrations settings overview
- WHEN the overview checklist groups are rendered
- THEN a NicheDiscovery checklist group is present
- AND the operator can configure the YouTube Data API v3 origin from that group

#### Scenario: A workflow key without a checklist group is not silently shipped

- GIVEN the NicheDiscovery workflow key has no matching checklist group entry
- WHEN this is discovered during implementation
- THEN it is treated as an incomplete implementation of this requirement, not an acceptable gap

### Requirement: INT-ND-04 — Per-Stage Origin Selection Is Exclusively Configuration-Driven

Origin selection for each NicheDiscovery sourcing stage MUST resolve exclusively through the
existing configuration-driven pattern (enabled workflow-step route first, raw mapping second,
empty → `Unconfigured` third), generalized beyond Apify actor mappings to also route to the new
official-API provider category. No literal actor id, endpoint, or API host MUST appear in
Application or Infrastructure code. Apify remains wire-capable as a future secondary origin
without requiring changes to this routing mechanism.

#### Scenario: Stage resolves to the configured official-API origin

- GIVEN an organization with `youtube-data-api-v3` configured as the origin for a NicheDiscovery
  sourcing stage
- WHEN the source chain resolves that stage's origin
- THEN the configured official-API provider is used, read from configuration

#### Scenario: Unconfigured stage fails structurally, not by literal fallback

- GIVEN an organization with no origin configured for a NicheDiscovery sourcing stage
- WHEN the source chain resolves that stage's origin
- THEN resolution reports `Unconfigured`
- AND no literal actor id, endpoint, or API host is substituted in code
