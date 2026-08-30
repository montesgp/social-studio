# Spec Delta: Background Sourcing Policy (New Capability)

<!-- Change: content-background-sourcing-policy -->
<!-- Applies to: openspec/specs/background-sourcing-policy/spec.md (new capability) -->

## Purpose

Define the observable requirements for the per-client **background sourcing policy**
introduced by this change. After this change is applied:

- Each `Client` carries an optional `BackgroundSourcingPolicy` that selects one of three
  strategies (A / B / C) for how backgrounds are sourced during content generation.
- A client with no policy (`UNSET` / null) continues to use the current legacy 3-tier
  background behavior — no change in observable output for existing clients.
- The system is structurally extensible: adding CASE D requires no modification to the
  A/B/C strategy implementations.
- The policy is managed through the client ABM API and the admin UI.

Carousel coherence (shared `CarouselBackgroundPlan`), the structured color-role scale,
and the advisory concision directives are specified in separate spec deltas for
`content-generation` and `profile-analysis` respectively.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| BSP-01 | Proposal §Decision Tree — CASE A |
| BSP-02 | Proposal §Decision Tree — CASE A (1-background strict rule) |
| BSP-03 | Proposal §Decision Tree — CASE B |
| BSP-04 | Proposal §Decision Tree — CASE C |
| BSP-05 | Proposal §Scope — UNSET/null default (legacy) |
| BSP-06 | Proposal §Scope — extensibility seam (CASE D) |
| BSP-07 | Proposal §Scope — policy placement on Client |
| BSP-08 | Proposal §Scope — ABM surface (API + UI) |
| BSP-REG-01 | Proposal §Risks — existing-client migration default |

---

## ADDED Requirements

---

### Requirement: BSP-01 — CASE A: Client-Defined Background SET, Multiple Backgrounds — Varied-but-Coherent

When a client's policy is `CaseA` and the client has **more than one** background in
its reference set, the strategy MUST select backgrounds from that set ensuring visual
coherence across a carousel (one plan shared across all slides — see BSP carousel
coherence in the `content-generation` delta). For single-slide formats, a single
background is selected from the set. Selection MUST be **deterministic** for a given
job: given the same job id, client background set, and generation date, the same
background(s) are always selected — retries must not produce a different outcome.

The deterministic seed MAY incorporate: job id, client id, and UTC calendar date.
Implementations MUST document the seed composition so tests can reproduce selections.

#### Scenario: CASE A carousel — deterministic varied selection from set (BSP-01-a)

- GIVEN a client with policy `CaseA` and 3 backgrounds in its reference set `[bg1, bg2, bg3]`
- AND a carousel generation job with `jobId = "J1"` on date `2026-06-19`
- WHEN the CASE A strategy computes the carousel background plan for a 4-slide carousel
- THEN slides receive backgrounds selected from `[bg1, bg2, bg3]` (may repeat for >3 slides)
- AND the selection is deterministic: re-running with the same `jobId` and date produces the same assignment
- AND NOT all 4 slides receive the same background (varied within the set when N > 1)

#### Scenario: CASE A single format — background selected from set (BSP-01-b)

- GIVEN a client with policy `CaseA` and 3 backgrounds in its reference set
- AND a single-slide (story or feed post) generation job
- WHEN the CASE A strategy selects a background
- THEN exactly one background from the reference set is selected
- AND the selection is deterministic for the same job id and date

#### Scenario: CASE A retry uses same deterministic background (BSP-01-c)

- GIVEN a CASE A carousel plan was computed for `jobId = "J1"` on `2026-06-19`
- WHEN the generation is retried with the same `jobId` and date (e.g., after a transient failure)
- THEN the background selection is identical to the original run
- AND no new or different background is chosen on retry

---

### Requirement: BSP-02 — CASE A: Client-Defined Background SET, Single Background — Strict

When a client's policy is `CaseA` and the client has **exactly one** background in its
reference set, the strategy MUST always use that single background for every slide of
every format. No variation or creativity is applied. This rule is unconditional and MUST
NOT be overridden by any weighting or seeding logic.

#### Scenario: CASE A strict — single background always used (BSP-02-a)

- GIVEN a client with policy `CaseA` and exactly 1 background `[bg1]`
- WHEN any format (carousel or single) is generated
- THEN every slide uses `bg1`
- AND no other background is selected, regardless of job id, date, or slide count

#### Scenario: CASE A strict — carousel all slides identical (BSP-02-b)

- GIVEN a client with policy `CaseA` and 1 background `[bg1]`
- AND a 5-slide carousel generation job
- WHEN the carousel background plan is computed
- THEN all 5 slides are assigned `bg1`

---

### Requirement: BSP-03 — CASE B: Fully AI-Generated, Brand-Palette-Weighted Backgrounds

When a client's policy is `CaseB`, the system MUST instruct the AI visual designer to
generate backgrounds freely while weighting the creative toward the client's brand-manual
color palette, specifically using the structured `ColorEntry.Role` ordering
(Primary → Secondary → Accent → Neutral, ascending rank) as a prevalence/weighting
signal. The AI designer MUST receive the full `(Hex, Role, rank)` tuple per palette
entry, not just the hex values.

No client-defined background set or profile-analysis data is used in CASE B.

#### Scenario: CASE B — AI receives structured palette weighting (BSP-03-a)

- GIVEN a client with policy `CaseB`
- AND a brand manual whose color palette contains entries with `Role = Primary` (rank 1),
  `Role = Secondary` (rank 2), and `Role = Accent` (rank 3)
- WHEN content generation runs
- THEN the AI visual designer receives all palette entries as `(Hex, Role, rank)` triples
- AND the prompt conveys that higher-rank roles (lower rank number) have greater prevalence/weight
- AND no client reference background set is passed to the designer

#### Scenario: CASE B — carousel coherence still applies (BSP-03-b)

- GIVEN a client with policy `CaseB` and a carousel job
- WHEN the carousel background plan is computed
- THEN the AI guidance is computed ONCE before the slide loop and applied to all slides
  (the background plan is shared, not re-requested per slide)

---

### Requirement: BSP-04 — CASE C: Client Aesthetic via Profile-Analysis Text Descriptors

When a client's policy is `CaseC`, the system MUST use the text descriptors from the
client's most recent completed `ProfileAiAnalysis.DetectedBackgroundImages` as aesthetic
guidance for the AI visual designer. The descriptors are passed as natural-language
aesthetic context, not as image URLs. No Apify image-fetch call is made. If no completed
`ProfileAiAnalysis` record exists for the client, CASE C MUST degrade gracefully to the
CASE B behavior (AI creative without aesthetic context) and MUST log a warning.

Real post-image URLs are OUT OF SCOPE for this change (open dependency; deferred to a
future phase).

#### Scenario: CASE C — AI receives aesthetic text descriptors (BSP-04-a)

- GIVEN a client with policy `CaseC`
- AND the client has a completed `ProfileAiAnalysis` with non-empty `DetectedBackgroundImages` text descriptors
- WHEN content generation runs
- THEN the AI visual designer receives the `DetectedBackgroundImages` text descriptors as aesthetic guidance
- AND no Apify call is made during this generation job
- AND no client reference background set is passed

#### Scenario: CASE C — no analysis present, degrades to CASE B behavior (BSP-04-b)

- GIVEN a client with policy `CaseC`
- AND no completed `ProfileAiAnalysis` exists for the client
- WHEN content generation runs
- THEN the system falls back to AI-creative-only generation (equivalent to CASE B palette weighting without aesthetic descriptors)
- AND a warning is logged identifying the client and the reason for degradation
- AND content generation is NOT aborted

#### Scenario: CASE C — real image URLs are never fetched (BSP-04-c)

- GIVEN a client with policy `CaseC`
- WHEN the CASE C strategy executes
- THEN no call is made to Apify or any image-fetch service to retrieve per-post image URLs
- AND only the stored `DetectedBackgroundImages` text content is read from the database

---

### Requirement: BSP-05 — UNSET Policy: Legacy 3-Tier Behavior (Migration Default)

When a client's `BackgroundSourcingPolicy` is `null` / `UNSET`, the system MUST apply
the current implicit 3-tier background selection logic (ForcedDefault → References →
Creative) unchanged. No new behavior is introduced for `UNSET` clients. All clients
existing before this change are migrated with `UNSET` by default.

#### Scenario: UNSET client — legacy behavior unchanged (BSP-05-a)

- GIVEN a client whose `BackgroundSourcingPolicy` is null
- WHEN content generation runs for that client
- THEN the background selection follows the pre-existing 3-tier model
  (ForcedDefault → References → Creative) exactly as before this change
- AND no strategy from the new policy system is invoked

#### Scenario: Existing client after migration — policy is null (BSP-05-b)

- GIVEN the EF migration for this change has been applied
- WHEN a client record that existed before the migration is read
- THEN its `BackgroundSourcingPolicy` column is null (not a case value)
- AND content generation for that client produces identical output to pre-migration behavior

---

### Requirement: BSP-06 — Extensibility Seam: CASE D Plugs In Without Reworking A/B/C

The strategy port (`IBackgroundSourcingStrategy`) and policy dispatch mechanism MUST be
structured so that adding a new CASE D (or later) strategy requires NO modification to
the existing CASE A, B, or C strategy classes, nor to the strategy dispatch logic (other
than registering the new case). This is an Open/Closed verification requirement.

#### Scenario: CASE D addition touches zero A/B/C files (BSP-06-a)

- GIVEN the completed implementation of strategies A, B, and C
- WHEN a hypothetical CASE D strategy is added (as a new class implementing `IBackgroundSourcingStrategy`)
  and registered in the DI container with `PolicyCase.D`
- THEN the CASE A, CASE B, and CASE C strategy classes are NOT modified
- AND the strategy dispatch/resolver logic requires only the addition of a new enum value
  and its DI registration — no changes to existing dispatch branches

---

### Requirement: BSP-07 — Policy Lives on the Client Aggregate (Nullable, Client-Level)

`BackgroundSourcingPolicy` MUST be a nullable field directly on the `Client` domain
aggregate. It MUST be independent of the active brand-manual version — changing the active
brand manual does NOT change the client's sourcing policy. The policy applies to all
content generation jobs for that client regardless of which brand-manual version is used.

#### Scenario: Policy independent of brand-manual version (BSP-07-a)

- GIVEN a client with policy `CaseB` and two brand-manual versions (v1 as active, v2 as draft)
- WHEN content generation runs against v1
- THEN the strategy used is CASE B
- AND switching the active version to v2 does NOT change the sourcing policy

#### Scenario: Policy persisted as a client-level column (BSP-07-b)

- GIVEN the EF migration has been applied
- WHEN a client's `BackgroundSourcingPolicy` is read from the database
- THEN it is stored in a column on the `social.social_clients` table (not on a brand-manual join table)
- AND it is nullable (no default value at the DB level; application default is null/UNSET)

---

### Requirement: BSP-08 — ABM Surface: Policy Readable and Writable via API and Admin UI

The client's `BackgroundSourcingPolicy` MUST be:
- Exposed as a readable field in the GET client endpoint response.
- Updatable via a PATCH / PUT endpoint (existing client-update surface).
- Rendered and editable in the Next.js admin client detail UI (policy selector).

Allowed values for write: `null` (UNSET), `CaseA`, `CaseB`, `CaseC`. An invalid value MUST
return a 400 validation error.

#### Scenario: GET client returns current policy (BSP-08-a)

- GIVEN a client with policy `CaseB`
- WHEN the client detail endpoint is called
- THEN the response includes `backgroundSourcingPolicy: "CaseB"`

#### Scenario: PATCH updates policy (BSP-08-b)

- GIVEN a client with policy `null`
- WHEN the client update endpoint is called with `backgroundSourcingPolicy: "CaseA"`
- THEN the client's policy is updated to `CaseA`
- AND a subsequent GET returns `backgroundSourcingPolicy: "CaseA"`

#### Scenario: Invalid policy value rejected (BSP-08-c)

- GIVEN a PATCH request with `backgroundSourcingPolicy: "CaseX"`
- WHEN the endpoint receives the request
- THEN the response is 400 with a validation error naming the invalid value
- AND the client's policy is unchanged

#### Scenario: Admin UI renders policy selector (BSP-08-d)

- GIVEN a client record is open in the admin client detail UI
- WHEN the "Background Sourcing" section is visible
- THEN a policy selector (dropdown or radio group) displays the current policy
- AND the options include UNSET (legacy), CASE A (client set), CASE B (AI + palette), CASE C (brand aesthetic)
- AND selecting and saving a new option calls the update endpoint

---

### Requirement: BSP-REG-01 — Existing Clients: Migration Default Is UNSET, Observable Behavior Unchanged

The EF migration introduced by this change MUST add the `background_sourcing_policy`
column to `social.social_clients` with a `null` default. No existing client row MUST
receive a non-null policy value from the migration itself. The observable content generation
behavior for ALL existing clients MUST be identical before and after migration.

#### Scenario: Migration adds nullable column, all existing rows remain null (BSP-REG-01-a)

- GIVEN the migration is applied against a database containing N existing client rows
- WHEN the `social.social_clients` table is inspected after migration
- THEN `background_sourcing_policy` is null for all N pre-existing rows
- AND no existing row has a policy value assigned by the migration

#### Scenario: Content generation for all pre-existing clients produces identical output post-migration (BSP-REG-01-b)

- GIVEN a pre-existing client (policy = null) and a content generation job
- WHEN the job runs after the migration is applied
- THEN the background selection logic executed is the legacy 3-tier model
- AND the output is observably equivalent to pre-migration output for the same inputs

---

## MODIFIED Requirements

None. This is a new capability; no existing spec requirement is modified by this delta.

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative, for test authoring)

- BSP-01 (deterministic CASE A) MUST be covered by unit tests that fix a seed (job id + date) and assert
  stable background assignment across repeated calls. The seed composition MUST be documented in code.
- BSP-02 (strict CASE A) is pure unit: 1-element set → always the same output.
- BSP-03 (CASE B palette weighting) MUST be verified by a unit test that inspects the prompt payload
  sent to `AiVisualDesigner` — confirm `(Hex, Role, rank)` tuples are present.
- BSP-04 (CASE C degradation) MUST be covered by a unit test: mock `IProfileAnalysisReadRepository`
  to return no analysis → confirm CASE B fallback path and logged warning.
- BSP-05 (UNSET = legacy) can be verified by confirming the strategy resolver returns the legacy
  `BackgroundSelector` adapter when policy is null.
- BSP-06 (extensibility) can be verified by a compile-time/architecture test: no `if`/`switch`
  branch in the A/B/C strategy classes references other cases.
- BSP-REG-01 MUST be covered by a migration rollback integration test confirming all rows remain null.
