# Background Sourcing Policy Spec

## Purpose

Define the observable requirements for the per-client background sourcing policy.

- Each `Client` carries an optional `BackgroundSourcingPolicy` that selects one of three
  strategies (A / B / C) for how backgrounds are sourced during content generation.
- A client with no policy (`UNSET` / null) continues to use the legacy 3-tier
  background behavior — no change in observable output for existing clients.
- The system is structurally extensible: adding CASE D requires no modification to the
  A/B/C strategy implementations.
- The policy is managed through the client ABM API and the admin UI.

Carousel coherence (shared `CarouselBackgroundPlan`), the structured color-role scale,
and the advisory concision directives are specified in `openspec/specs/content-generation/spec.md`.

---

## PRD Traceability

| Requirement ID | Description |
|---|---|
| BSP-01 | CASE A: deterministic varied selection from multi-background client set |
| BSP-02 | CASE A: strict single-background always used when only one background exists |
| BSP-03 | CASE B: AI-generated, brand-palette-weighted backgrounds |
| BSP-04 | CASE C: client aesthetic via profile-analysis text descriptors |
| BSP-05 | UNSET: legacy 3-tier behavior preserved unchanged |
| BSP-06 | Extensibility seam — CASE D plugs in without reworking A/B/C |
| BSP-07 | Policy lives on the Client aggregate (nullable, client-level) |
| BSP-08 | ABM surface: policy readable and writable via API and admin UI |
| BSP-REG-01 | Existing clients: migration default is UNSET, observable behavior unchanged |

---

## Capabilities

- A client can have one of four background sourcing policy values: `null/UNSET`, `ClientDefinedSet`, `AiPalette`, `ClientAesthetic`.
- The policy is resolved at generation time by `IBackgroundStrategyResolver`, which selects the corresponding `IBackgroundSourcingStrategy` implementation.
- Clients without a policy (`UNSET`) continue to use legacy 3-tier background selection.
- The policy can be read and written via the client API endpoints and is displayed in the admin UI client form.

## Constraints

- **BSP-01**: When policy is `ClientDefinedSet` and the client has more than one background, selection is deterministic for a given job (same job id + date → same assignment). Not all carousel slides receive the same background when the set has more than one entry.
- **BSP-02**: When policy is `ClientDefinedSet` and the client has exactly one background, every slide always uses that background — no variation applied.
- **BSP-03**: When policy is `AiPalette`, the AI visual designer receives the full `(Hex, Role, Rank)` palette tuple as a prevalence/weighting signal. No client background set is used.
- **BSP-04**: When policy is `ClientAesthetic`, text descriptors from the most recent completed `ProfileAiAnalysis.DetectedBackgroundImages` are passed to the AI. No Apify call is made. Empty descriptors degrade to `AiPalette` behavior with a logged warning.
- **BSP-05**: `UNSET` (null) policy invokes `LegacyTierStrategy`, which exactly reproduces pre-change 3-tier behavior. All clients migrated before this change have `UNSET` by default.
- **BSP-06**: Adding CASE D requires no changes to CASE A/B/C strategy classes; only a new DI registration is needed.
- **BSP-07**: `BackgroundSourcingPolicy` is a nullable column on `social.social_clients`. It is independent of the active brand-manual version.
- **BSP-08**: Invalid policy values on the write endpoint return HTTP 400.
- **BSP-REG-01**: The EF migration adds `background_sourcing_policy smallint NULL`; all pre-existing rows remain null.

## Events

None specific to this capability. Background sourcing is an internal generation-time decision; no domain event is emitted on policy change.

## Acceptance Criteria

- GET client response includes `backgroundSourcingPolicy` field (null or a policy string).
- PUT/PATCH client accepts `backgroundSourcingPolicy`; an invalid value returns 400.
- Admin UI client form displays a policy selector and submits the chosen value on save.
- EF migration adds `background_sourcing_policy smallint NULL` to `social.social_clients`; no pre-existing rows are assigned a non-null value by the migration.
- `IBackgroundSourcingStrategy` and `IBackgroundStrategyResolver` contain no Infrastructure types in their signatures.
- `LegacyTierStrategy.Plan(...)` output matches `BackgroundSelector.Select(...)` for the same inputs (parity test).

---

*Merged from `openspec/changes/content-background-sourcing-policy/specs/background-sourcing-policy/spec.md` — change applied 2026-06-20.*

---

<!-- Merged from openspec/changes/content-aesthetic-generation/specs/background-sourcing-policy/spec-delta.md — change applied 2026-06-21 -->
# Spec Delta: Background Sourcing Policy + Client Settings — CASE C Active Best-Available + Background Refresh + Per-Client Threshold ABM

<!-- Change: content-aesthetic-generation -->
<!-- Applies to: openspec/specs/background-sourcing-policy/spec.md (additive delta) -->
<!-- Also supplements: openspec/changes/content-background-sourcing-policy/specs/background-sourcing-policy/spec.md -->
<!-- Also covers: client-settings ABM surface for AestheticRefreshThresholdDays -->

## Purpose

Specify the **behavioral change** this change introduces to the `background-sourcing-policy`
capability for CASE C (`ClientAesthetic`):

Before this change, CASE C was a **passive seam**: it read whatever `DetectedBackgroundImages`
text descriptors happened to exist in the most recent `Analyzed` `ProfileAiAnalysis` and
passed them to the AI background designer. No scrape was ever triggered; stale or missing
data was silently used.

After this change, CASE C is **active**:

- At generation time, it checks analysis freshness against the client's effective threshold (`client.AestheticRefreshThresholdDays ?? 30` days; system default 30 days).
- It enqueues a background refresh (fire-and-forget) when the analysis is stale or absent.
- It always generates immediately with the best-available data (fresh > stale > degrade
  to Creative/AI-palette fallback).

The CASE A, CASE B, UNSET, and extensibility seam requirements from the predecessor change
(`content-background-sourcing-policy`) are **unchanged** by this delta.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAG-BSP-01 | Proposal §Capabilities — background-sourcing-policy: ClientAesthetic from passive to active |
| CAG-BSP-02 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — best-available priority order |
| CAG-BSP-03 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — degrade when no analysis |
| CAG-BSP-04 | Proposal §Goals — future-proof model; Phase 2 image URLs via nullable fields |

---

## ADDED Requirements

None. The behavioral changes in this delta are expressed entirely as modifications to
existing `BSP-04` semantics (see MODIFIED Requirements below).

---

## MODIFIED Requirements

---

### Requirement: BSP-04 (MODIFIED) — CASE C Is Now Active: Best-Available Data + Background Refresh

**Supersedes BSP-04 from `content-background-sourcing-policy`.**

When a client's policy is `CaseC` (`ClientAesthetic`), the system MUST apply a
**best-available** strategy with background refresh:

1. **Fresh analysis available** (completed within the client's effective threshold —
   `client.AestheticRefreshThresholdDays ?? 30` days): the `ClientAestheticProfile`
   is built from the existing analysis immediately. No refresh is enqueued.

2. **Stale analysis available** (completed beyond the client's effective threshold):
   the `ClientAestheticProfile` is built from the stale analysis immediately. A
   background refresh job is enqueued (fire-and-forget) via `IProfileAnalysisJobQueue`.

3. **No analysis at all**: the `ClientAesthetic` path degrades to Creative
   (AI-palette behavior — equivalent to the no-aesthetic-context fallback previously
   specified as CASE B behavior in BSP-04-b). A structured warning is logged. A
   background refresh job is enqueued (fire-and-forget) if the client has a registered
   own account.

In all three cases, generation proceeds immediately. The generation job MUST NOT block,
wait, or poll on the background refresh pipeline.

This requirement extends and replaces the passive-read semantics of the original BSP-04.
The original BSP-04-c scenario (no Apify image fetch in CASE C) remains valid and is
unchanged.

#### Scenario: CASE C fresh analysis — used directly, no refresh (CAG-BSP-04-a)

- GIVEN a client with policy `CaseC` and `AestheticRefreshThresholdDays = null`
  (effective threshold: 30 days)
- AND the latest `Analyzed` `ProfileAiAnalysis` has `AnalyzedAtUtc = UtcNow - 3 days`
- WHEN the ClientAesthetic strategy executes during content generation
- THEN the `ClientAestheticProfile` is built from the existing analysis
- AND no background refresh job is enqueued
- AND the AI visual designer receives `BackgroundDescriptors` from that analysis
- AND generation completes immediately

#### Scenario: CASE C stale analysis — used immediately, refresh fires in background (CAG-BSP-04-b)

- GIVEN a client with policy `CaseC` and `AestheticRefreshThresholdDays = null`
  (effective threshold: 30 days)
- AND the latest `Analyzed` `ProfileAiAnalysis` has `AnalyzedAtUtc = UtcNow - 45 days`
- AND the client has a registered own account
- WHEN the ClientAesthetic strategy executes during content generation
- THEN the stale `ClientAestheticProfile` is used immediately
- AND a background refresh job is enqueued via `IProfileAnalysisJobQueue` (fire-and-forget)
- AND generation does NOT wait for the refresh pipeline
- AND the next generation job (after the refresh completes) will use the fresh analysis

#### Scenario: CASE C no analysis — degrades to Creative, refresh fires, warning logged (CAG-BSP-04-c)

- GIVEN a client with policy `CaseC`
- AND NO completed `ProfileAiAnalysis` exists for the client
- AND the client has a registered own account
- WHEN the ClientAesthetic strategy executes during content generation
- THEN the generation uses the Creative / AI-palette fallback (no background descriptors,
  no voice tone, no avg-slides hint)
- AND a structured warning is logged identifying the `clientId`, `orgId`, and reason
  ("no analysis available")
- AND a background refresh job is enqueued (fire-and-forget)
- AND content generation is NOT aborted; the job completes normally

#### Scenario: CASE C no own account — refresh skipped, degrade applies (CAG-BSP-04-d)

- GIVEN a client with policy `CaseC` and no registered own account
- AND the analysis is stale or absent
- WHEN the ClientAesthetic strategy executes
- THEN no background refresh job is enqueued (nothing to scrape without an own account)
- AND the strategy applies best-available data (stale profile) or Creative fallback
  (no analysis), as appropriate
- AND a warning is logged indicating the refresh was skipped due to missing own account
- AND generation proceeds and completes normally

#### Scenario: CASE C — real image URLs are never fetched (unchanged from BSP-04-c)

- GIVEN a client with policy `CaseC`
- WHEN the ClientAesthetic strategy executes (regardless of freshness state)
- THEN no call is made to Apify or any image-fetch service to retrieve per-post image URLs
- AND only stored text data (`DetectedBackgroundImages`, `VoiceTone`, computed stats)
  is read from the Application read port

#### Scenario: CASE C — ClientAestheticProfile is the richer type; not a flat string list (CAG-BSP-04-f)

- GIVEN a client with policy `CaseC` and a completed `ProfileAiAnalysis`
- WHEN the ClientAesthetic strategy builds its generation inputs
- THEN it returns a `ClientAestheticProfile` value object (not a flat `IReadOnlyList<string>`)
- AND the profile carries `BackgroundDescriptors`, `VoiceTone?`, `AverageSlidesPerCarousel?`,
  and `AnalyzedAtUtc?`

---

## ADDED Requirements — Client Settings ABM

---

### Requirement: CAG-CS-01 — `AestheticRefreshThresholdDays` Persisted and Round-Tripped via EfSocialClientRepository

`Client.AestheticRefreshThresholdDays` (`int?`) MUST be persisted to and loaded from
the `aesthetic_refresh_threshold_days` column on `social.clients` (nullable `smallint`).

The `EfSocialClientRepository` MUST map this field in ALL THREE of the following
methods — any omission silently drops the value:

1. **`ToData(Client client)`** — write `client.AestheticRefreshThresholdDays` to
   `SocialClientData.AestheticRefreshThresholdDays`.
2. **`Update(Client client)` (entity mutation)** — assign
   `entity.AestheticRefreshThresholdDays = client.AestheticRefreshThresholdDays` inside
   the Update method body (same pattern as `entity.BackgroundSourcingPolicy`).
3. **`ToDomain(SocialClientData row)` (restore)** — pass
   `row.AestheticRefreshThresholdDays` to `Client.Restore(...)` as
   `aestheticRefreshThresholdDays`.

> **Predecessor bug guard**: the `content-background-sourcing-policy` change experienced
> a bug where `BackgroundSourcingPolicy` was mapped in `ToData` and `ToDomain` but NOT
> in `Update`, causing updates to silently reset the policy. This requirement explicitly
> mandates all three mapping sites to prevent the same class of regression.

#### Scenario: Create → read round-trip preserves the threshold (CAG-CS-01-a)

- GIVEN a client created with `AestheticRefreshThresholdDays = 14`
- WHEN the client is persisted via `AddAsync` and then loaded via `GetByIdAsync`
- THEN the loaded domain object has `AestheticRefreshThresholdDays = 14`

#### Scenario: Update → read round-trip preserves the threshold (CAG-CS-01-b)

- GIVEN an existing client with `AestheticRefreshThresholdDays = null`
- AND the threshold is updated to 7 via `SetAestheticRefreshThresholdDays(7, now)` and saved
- WHEN the client is loaded via `GetByIdAsync`
- THEN `AestheticRefreshThresholdDays = 7` (not null; the Update method mapped it)

#### Scenario: Null threshold round-trips correctly (CAG-CS-01-c)

- GIVEN a client with `AestheticRefreshThresholdDays = null`
- WHEN persisted and reloaded
- THEN `AestheticRefreshThresholdDays = null`
- AND the effective staleness threshold used at generation time is the 30-day system default

---

### Requirement: CAG-CS-02 — ABM API Exposes `AestheticRefreshThresholdDays` in Create, Update, and Response

The client ABM API MUST expose `AestheticRefreshThresholdDays` as a nullable integer:

- **`SocialClientResponse`**: add `int? AestheticRefreshThresholdDays` property.
- **`CreateSocialClientCommand`**: add `int? AestheticRefreshThresholdDays` input; pass
  to `Client.Create` via `SetAestheticRefreshThresholdDays` after creation (same pattern
  as `BackgroundSourcingPolicy`).
- **`CreateSocialClientCommandHandler`**: apply the field if provided.
- **`UpdateSocialClientCommand`**: add `int? AestheticRefreshThresholdDays` input.
- **`UpdateSocialClientCommandHandler`**: call `client.SetAestheticRefreshThresholdDays`
  with the new value.
- **`MapSocialClientsEndpoints`**: bind the field in Create and Update route handlers.

Validation: reject values outside `[1, 365]` with a domain error. `null` is always valid
(means "use system default").

#### Scenario: Create with custom threshold — response reflects the value (CAG-CS-02-a)

- GIVEN a Create request with `AestheticRefreshThresholdDays = 14`
- WHEN the endpoint is called
- THEN the response includes `AestheticRefreshThresholdDays = 14`
- AND a subsequent GET returns the same value

#### Scenario: Update clears the threshold (null) — reverts to system default (CAG-CS-02-b)

- GIVEN a client with `AestheticRefreshThresholdDays = 14`
- AND an Update request with `AestheticRefreshThresholdDays = null`
- WHEN the endpoint is called
- THEN the persisted value is null
- AND the effective threshold at generation time becomes 30 days

#### Scenario: Invalid threshold rejected with validation error (CAG-CS-02-c)

- GIVEN an Update request with `AestheticRefreshThresholdDays = 0` (below minimum)
- WHEN the endpoint is called
- THEN a `400 Bad Request` (or domain validation error) is returned
- AND no client record is modified

---

### Requirement: CAG-CS-03 — Web ABM Form Includes `AestheticRefreshThresholdDays` Field

The Next.js client ABM form MUST expose `aestheticRefreshThresholdDays` as an optional
numeric input field (null = system default). This mirrors how `BackgroundSourcingPolicy`
is handled in the web form, `use-client-form` hook, and `types/social-clients` API lib.

#### Scenario: Form saves and displays the threshold (CAG-CS-03-a)

- GIVEN the user sets the threshold to `14` in the ABM form and submits
- WHEN the form submits
- THEN the Create/Update API receives `aestheticRefreshThresholdDays: 14`
- AND the form re-loads with `14` pre-filled

#### Scenario: Form sends null when the field is cleared (CAG-CS-03-b)

- GIVEN the user clears the threshold field in the form and submits
- WHEN the form submits
- THEN the API receives `aestheticRefreshThresholdDays: null`

---

## REMOVED Requirements

None. BSP-04-b from the predecessor change is SUPPLEMENTED by CAG-BSP-04-c (which adds
the refresh-enqueue obligation to the no-analysis degrade path). BSP-04-b is not removed;
both logging and enqueue obligations apply.

---

## Implementation Boundary Notes (non-normative, for test authoring)

- The modified BSP-04 MUST be covered by tests on the `ClientAestheticEnsureFresh`
  orchestration service (or equivalent Application service):
  - Fresh analysis (null threshold → 30d system default) → no enqueue (assert `IProfileAnalysisJobQueue.Enqueue` not called).
  - Stale analysis + own account present → profile returned, enqueue called once.
  - Custom threshold honored (e.g. 7d): analysis at 10d → stale under 7d threshold but fresh under 30d default — asserts custom value wins.
  - No analysis + own account present → Creative fallback, warning logged, enqueue called once.
  - No analysis + no own account → Creative fallback, warning logged, enqueue NOT called.
- **CAG-CS-01** MUST be covered by repository integration tests seeded with a real
  `social.clients` row: (a) create → reload → field survives; (b) update → reload →
  field survives; (c) null → reload → null. ALL THREE repository methods MUST be
  exercised — not just `ToData`/`ToDomain`.
- **CAG-CS-02** MUST be covered by API-level tests (handler unit tests or thin integration
  tests): create with value → response carries value; update to null → persisted null;
  invalid value (0 or 366) → validation error returned.
- **CAG-CS-03** is covered by existing web E2E tests; add a form-field assertion for
  `aestheticRefreshThresholdDays`.
- Architecture gate: CASE C strategy MUST NOT reference EF Core, Npgsql, or Apify types.
  The `pwsh -File scripts/gates/Run-Gates.ps1` gate enforces the hexagonal boundary.
- The original BSP-04-c (no Apify image fetch) test from the predecessor change MUST
  continue to pass without modification.

*Merged from `openspec/changes/content-aesthetic-generation/specs/background-sourcing-policy/spec-delta.md` — change applied 2026-06-21.*

---

<!-- Merged from openspec/changes/content-image-generation/specs/background-sourcing-policy/spec-delta.md — change applied 2026-06-24 (WU-10) -->

## Added Requirements: ClientAesthetic Creative-vs-Flat Routing + Configurable Creative-Slide Set (content-image-generation)

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

*Merged from `openspec/changes/content-image-generation/specs/background-sourcing-policy/spec-delta.md` — change applied 2026-06-24.*

---

<!-- Merged from openspec/changes/client-aligned-carousel-generation/specs/background-sourcing-policy/spec-delta.md — change applied 2026-06-28 -->

## Added Requirements: Structured Style Capture — Slide-Layout Pattern + Color Pairings (client-aligned-carousel-generation)

### Requirement: CAC-BSP-01 — Multimodal Extractor DTO Gains Nullable `SlideMixPattern` Field

The multimodal aesthetic extractor response DTO MUST include a nullable `SlideMixPattern`
field capturing the client's per-slide photo-vs-flat classification and interleave order
as observed in the feed. When no clear pattern is detectable the field MUST be null.
The addition MUST be backward-compatible: extractor pipeline stages that do not read
this field continue unchanged.

#### Scenario: Clear interleave detected — `SlideMixPattern` populated (CAC-BSP-01-a)

- GIVEN a multimodal extraction run against a client with consistent photographic/flat alternation
- WHEN the extractor produces its response DTO
- THEN `SlideMixPattern` is populated with the per-slide classification
- AND all other DTO fields are unaffected

#### Scenario: Ambiguous pattern — `SlideMixPattern` null; extraction succeeds (CAC-BSP-01-b)

- GIVEN a feed with no discernible slide structure
- WHEN the extractor produces its DTO
- THEN `SlideMixPattern` is null
- AND the extraction completes successfully with no error raised

---

### Requirement: CAC-BSP-02 — `ClientAestheticProfile` Gains Nullable `SlideLayoutPattern` and `TypicalSlideCount`

`ClientAestheticProfile` MUST carry two new nullable fields populated from the extractor output:

- `SlideLayoutPattern` — per-slide photo-vs-flat classification and interleave derived from `SlideMixPattern`.
- `TypicalSlideCount` — integer slide count typical for the client, derived from extractor output or
  analysis statistics (`AverageSlidesPerCarousel`).

Both fields MUST be persisted additively (nullable columns). A null value for either field MUST NOT
alter any existing generation behavior.

#### Scenario: Extractor output present — profile carries layout and count (CAC-BSP-02-a)

- GIVEN an analysis whose extractor output contains a 5-slide alternating photographic/flat layout
- WHEN `ClientAestheticProfile` is built from that analysis
- THEN `SlideLayoutPattern` reflects the per-slide classification
- AND `TypicalSlideCount = 5`

#### Scenario: Extractor output absent — profile fields null; consumers unaffected (CAC-BSP-02-b)

- GIVEN an analysis with no extractor slide-mix output
- WHEN `ClientAestheticProfile` is built
- THEN `SlideLayoutPattern = null` and `TypicalSlideCount = null`
- AND existing profile consumers compile and behave identically to their pre-change behavior

#### Scenario: Persistence round-trip preserves new fields (CAC-BSP-02-c)

- GIVEN an analysis persisted with a populated `SlideLayoutPattern`
- WHEN the analysis is reloaded and `ClientAestheticProfile` is reconstructed
- THEN `SlideLayoutPattern` matches the persisted value with no data loss

---

### Requirement: CAC-BSP-03 — `ClientAestheticProfile` Carries `{BackgroundHex, TextHex}` Color Pairings

`ClientAestheticProfile` MUST carry a nullable `ColorPairings` field — a collection of
`{BackgroundHex, TextHex}` value pairs — derived by joining the client's brand-manual
`ColorRole` entries with the multimodal extractor's extracted `palette.Role` output
according to the client's observed pairing criterion. When the join yields no pairings
(absent palette or absent brand-manual colors), the field MUST be null or empty and
the existing positional palette assignment remains as the active fallback.

#### Scenario: Joinable roles produce color pairings (CAC-BSP-03-a)

- GIVEN a client with brand-manual `ColorRole` entries and a matching extracted palette
- WHEN `ClientAestheticProfile` is built
- THEN `ColorPairings` contains at least one `{BackgroundHex, TextHex}` pair derived
  from the join of brand-manual and extracted roles

#### Scenario: No joinable roles — `ColorPairings` null; positional fallback active (CAC-BSP-03-b)

- GIVEN a client with no brand manual or an extracted palette with no joinable roles
- WHEN `ClientAestheticProfile` is built
- THEN `ColorPairings` is null or empty
- AND the existing positional array-position palette assignment remains the active method

---

### Requirement: CAC-BSP-04 — Cold-Start: Null Captured Fields Preserve Existing Generation Defaults

When `SlideLayoutPattern`, `TypicalSlideCount`, or `ColorPairings` are null, every downstream
consumer MUST apply its current default behavior:

- `CreativeSlideIndexResolver` MUST apply the `{first, last}` v1 default (per CIG-BSP-02).
- `CarouselGenerator` MUST use the existing `~3-5` slide range.
- `CompositionDefaults.ResolvePalette()` MUST apply positional palette assignment.

No error, warning, or degrade is issued for absent captured fields; this is a valid
progressive-capture state.

#### Scenario: Cold-start client — all null; current defaults active; job completes (CAC-BSP-04-a)

- GIVEN a `ClientAesthetic` client with `SlideLayoutPattern = null`, `TypicalSlideCount = null`,
  and `ColorPairings = null`
- WHEN a carousel generation job runs
- THEN `CreativeSlideIndexResolver` returns the `{first, last}` v1 default
- AND the carousel is generated within the existing `~3-5` slide range
- AND palette is assigned by the existing positional method
- AND the job completes normally with no errors or warnings

*Merged from `openspec/changes/client-aligned-carousel-generation/specs/background-sourcing-policy/spec-delta.md` — change applied 2026-06-28.*
