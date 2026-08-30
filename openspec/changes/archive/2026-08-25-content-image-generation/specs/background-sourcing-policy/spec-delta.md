# Spec Delta: Background Sourcing Policy — ClientAesthetic Creative-vs-Flat Routing + Configurable Creative-Slide Set

<!-- Change: content-image-generation -->
<!-- Applies to: openspec/specs/background-sourcing-policy/spec.md (additive delta) -->

## Purpose

Extend the `ClientAesthetic` strategy (BSP-04, as modified by `content-aesthetic-generation`)
to route per-slide backgrounds as either **generated raster** (creative slides) or
**CSS designer** (flat slides), driven by a configurable creative-slide set persisted on
`ClientAestheticProfile`. The new routing is additive; the best-available / refresh-if-stale
contract from `content-aesthetic-generation` is preserved unchanged.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CIG-BSP-01 | Proposal §Scope — per-slide creative-vs-flat routing for ClientAesthetic |
| CIG-BSP-02 | Proposal §Scope — configurable creative-slide set on ClientAestheticProfile (v1 default) |
| CIG-BSP-03 | Proposal §Scope — extractor-ready data model (forward-compatible nullable field) |

---

## ADDED Requirements

### Requirement: CIG-BSP-01 — ClientAesthetic Strategy Emits Creative or Flat Background Per Slide

When a client's policy is `ClientAesthetic` AND a `generate-image` IMAGE route is
configured for the org, the strategy MUST classify each carousel slide as creative or flat
using `ClientAestheticProfile.CreativeSlideSet`. Creative slides MUST receive a
generated-image background; flat slides MUST receive the CSS designer background.
When no IMAGE route is configured, all slides take the CSS designer path regardless of
the creative-slide set.

#### Scenario: Creative slides get generated background; flat slides get CSS (CIG-BSP-01-a)

- GIVEN a `ClientAesthetic` client with `CreativeSlideSet = {0, 4}` (first + last)
- AND the org has a configured IMAGE route
- WHEN the strategy resolves slide backgrounds for a 5-slide carousel
- THEN slides 0 and 4 receive a `generated-image` background token
- AND slides 1, 2, 3 receive the CSS designer background token

#### Scenario: No IMAGE route — all slides CSS, no creative classification attempted (CIG-BSP-01-b)

- GIVEN a `ClientAesthetic` client with `CreativeSlideSet = {0, 4}`
- AND the org has NO configured IMAGE route
- WHEN the strategy resolves slide backgrounds
- THEN all slides receive the CSS designer background
- AND no image-gen call or classification is attempted

---

### Requirement: CIG-BSP-02 — Configurable Creative-Slide Set with v1 Default

`ClientAestheticProfile` MUST expose a nullable `CreativeSlideSet` (a collection of
zero-based slide index positions or a named configuration such as "first-and-last").
When the field is absent (null), the system MUST apply the v1 default (first and last
positions of the carousel). The set MUST be configurable per client; the default is
not hardcoded in the generation pipeline.

#### Scenario: Null creative-slide set falls back to v1 default (CIG-BSP-02-a)

- GIVEN a `ClientAesthetic` client whose `CreativeSlideSet` on `ClientAestheticProfile` is null
- WHEN the strategy resolves creative slides for a 5-slide carousel
- THEN slides at positions 0 and 4 (first + last) are treated as creative
- AND this default is derived from the v1 default rule, not from hardcoded literals in the generator

#### Scenario: Explicit set overrides the default (CIG-BSP-02-b)

- GIVEN a `ClientAesthetic` client with `CreativeSlideSet = {0, 1}` (first two)
- WHEN the strategy resolves creative slides for a 5-slide carousel
- THEN slides 0 and 1 are treated as creative
- AND slides 2, 3, 4 are treated as flat

#### Scenario: Creative set with index beyond carousel length is silently clamped (CIG-BSP-02-c)

- GIVEN a `ClientAesthetic` client with `CreativeSlideSet = {0, 9}` and a 5-slide carousel
- WHEN the strategy resolves creative slides
- THEN only slide 0 is treated as creative (index 9 is out of range and ignored)
- AND no error is raised; the job continues normally

---

### Requirement: CIG-BSP-03 — Creative-Slide Set Field Is Extractor-Ready (Nullable, Forward-Compatible)

`ClientAestheticProfile.CreativeSlideSet` MUST be a nullable field. Its absence (null)
is a fully valid state that activates the v1 default. The data model MUST NOT require
the extractor to be built or active for the field to function; v1 ships the configurable
set only. A future extractor MUST be able to populate the field without a schema change.

#### Scenario: Extractor-absent state works with v1 default (CIG-BSP-03-a)

- GIVEN a `ClientAesthetic` client with `CreativeSlideSet = null`
  (extractor has not yet run for this client)
- WHEN the strategy resolves creative slides
- THEN the v1 default rule applies and creative slides are identified correctly
- AND no error is raised for the absent extractor output

---

## MODIFIED Requirements

None. All requirements above are additive to `BSP-04` as currently specified in
`openspec/specs/background-sourcing-policy/spec.md`. The best-available / refresh-if-stale
contract (`CAG-BSP-01` through `CAG-BSP-04`) is preserved without change.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- CIG-BSP-01: `ClientAestheticProfile` gains a `CreativeSlideSet` nullable field (Domain value object).
  The IMAGE route presence check uses `IWorkflowConfigurationResolver` — no provider type leaks.
- CIG-BSP-02: v1 default logic lives in the Application strategy (or a dedicated resolver);
  the default is NOT a hardcoded constant in the carousel generator.
- CIG-BSP-03: no EF migration change needed if `CreativeSlideSet` is serialized into an existing
  JSON column on `ClientAestheticProfile`; design decides the persistence shape.
- Architecture gate: no Supabase, EF Core, or provider types in Domain or Application.
