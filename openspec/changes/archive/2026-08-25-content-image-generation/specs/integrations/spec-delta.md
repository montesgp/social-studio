# Spec Delta: Integrations — generate-image Workflow Step (MediaGeneration) + OpenAI Seed

<!-- Change: content-image-generation -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Add the `generate-image` configurable workflow step to the `WorkflowConfigurationCatalog`
under the existing `MediaGeneration` provider category. Seed OpenAI as the first available
MediaGeneration provider option. The Integrations UI renders the new step automatically
from the catalog; no UI-specific change is required.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CIG-INT-01 | Proposal §Scope — generate-image step in WorkflowConfigurationCatalog (MediaGeneration) |
| CIG-INT-02 | Proposal §Scope — OpenAI seeded as first MediaGeneration adapter |
| CIG-INT-03 | Proposal §Scope — org selects provider/model; routing mirrors generate-content |
| CIG-INT-04 | Proposal §Scope — Integrations UI renders new step automatically |

---

## ADDED Requirements

### Requirement: CIG-INT-01 — generate-image Step Registered in WorkflowConfigurationCatalog

The `WorkflowConfigurationCatalog` MUST include a new step entry with:
- **Step key**: `generate-image` (or equivalent platform-canonical key)
- **Operation category**: `MediaGeneration`
- **Model selector applicable**: true (provider + model are required per route)
- **Network requirement**: none (format-level, not network-level)

The entry MUST be code-only (no database migration required). The backend catalog endpoint
MUST return the new step so the Integrations routing UI can render it without frontend changes.

#### Scenario: generate-image step appears in workflow catalog response (CIG-INT-01-a)

- GIVEN an org with the Integrations routing UI open
- WHEN `GET /api/integrations/workflow-configurations` is called
- THEN the response includes a workflow catalog entry with step key `generate-image`
  (or equivalent) under the `MediaGeneration` category
- AND the entry indicates model selector is applicable
- AND no database migration has been applied to produce this entry

#### Scenario: generate-image step absent from catalog when feature is not deployed (CIG-INT-01-b)

- GIVEN the pre-change codebase
- WHEN `GET /api/integrations/workflow-configurations` is called
- THEN no `generate-image` step appears in the catalog response

---

### Requirement: CIG-INT-02 — OpenAI Seeded as First MediaGeneration Provider Option

OpenAI (`dall-e-3` / `gpt-image-1` or the org's configured model list for that provider)
MUST appear as a selectable provider/model option in the Integrations routing UI for the
`generate-image` step. The OpenAI Integration provider MUST already be registered
(pre-existing); this requirement seeds the MediaGeneration capability linkage only.

#### Scenario: OpenAI available as image provider in routing UI (CIG-INT-02-a)

- GIVEN an org with an OpenAI integration connected
- WHEN the Integrations routing UI renders provider options for the `generate-image` step
- THEN OpenAI appears as a selectable provider
- AND models available for OpenAI (as returned by the provider's model list for that org) are selectable

#### Scenario: Unconnected provider does not appear for generate-image (CIG-INT-02-b)

- GIVEN an org with OpenAI NOT connected
- WHEN the Integrations routing UI renders provider options for the `generate-image` step
- THEN OpenAI does NOT appear as a selectable option for that org's `generate-image` step

---

### Requirement: CIG-INT-03 — Org-Level IMAGE Route Configured Like Any Text Route

An org MAY configure zero or more routes for the `generate-image` step, each specifying
a provider key and model. The `IWorkflowConfigurationResolver` MUST resolve the IMAGE
route for an org by `generate-image` step key, returning the org's configured
provider+model or null (unconfigured). Resolution behavior MUST mirror `generate-content`.
`OrganizationId` MUST be extracted from the authenticated principal; it MUST NOT be
accepted from the request body.

#### Scenario: Org A image route resolves to OpenAI dall-e-3 (CIG-INT-03-a)

- GIVEN org A has a `generate-image` route configured with provider = OpenAI, model = dall-e-3
- WHEN `IWorkflowConfigurationResolver` is asked for the IMAGE route for org A
- THEN it returns a route context with provider key = openai and model = dall-e-3

#### Scenario: Org B has no image route — resolver returns null (CIG-INT-03-b)

- GIVEN org B has no `generate-image` route configured
- WHEN `IWorkflowConfigurationResolver` is asked for the IMAGE route for org B
- THEN it returns null (or the absence-of-route sentinel)
- AND no error is raised; the caller determines the fallback behavior

---

### Requirement: CIG-INT-04 — Integrations UI Renders generate-image Step Without Frontend Code Change

The Integrations workflow routing UI MUST render the `generate-image` step and its
provider/model selectors automatically from the catalog API response, with no
frontend-specific UI code changes required to support the new step. This mirrors how
all existing workflow steps are rendered from the catalog.

#### Scenario: New generate-image step renders in routing UI without frontend code change (CIG-INT-04-a)

- GIVEN the backend catalog returns the `generate-image` step entry
- WHEN the Integrations routing UI loads workflow configuration
- THEN the `generate-image` step section appears in the UI with its provider and model selectors
- AND no new frontend component or conditional rendering code was added to make this work

---

## MODIFIED Requirements

None. All requirements above are additive. No existing `openspec/specs/integrations/spec.md`
requirement is changed in meaning or removed.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- CIG-INT-01: `WorkflowConfigurationCatalog.cs` gains one new `CatalogEntry` record for
  `generate-image`; code-only, no migration.
- CIG-INT-02: OpenAI is already a registered `IntegrationProvider`; only the MediaGeneration
  capability mapping is new.
- CIG-INT-03: `IWorkflowConfigurationResolver` is already the routing seam; extend its
  `generate-image` step key handling following the `generate-content` pattern.
- CIG-INT-04: the routing UI is catalog-driven; adding the step key to the catalog is
  sufficient for the UI to render it (verify against existing routing UI rendering logic).
