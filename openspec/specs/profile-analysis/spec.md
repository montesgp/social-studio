# Profile Analysis Spec

## Purpose

Define the observable requirements for the profile analysis capability (scrape → AI analyze → persist), including the read-only seam that exposes aesthetic descriptors to content generation.

## Capabilities

- Scrape social profile data for a client via a configured Apify actor.
- Run AI analysis over the scraped data to extract content insights and aesthetic descriptors.
- Persist analysis results in `social.profile_analyses` with a guarded status lifecycle.
- Expose analysis results via read endpoints (start, poll, fetch, list).
- Expose a read-only Application port (`IClientAestheticDescriptorReader`) that returns `DetectedBackgroundImages` text descriptors from the most recent completed analysis, for consumption by the CASE C background sourcing strategy.

## Events

- `ProfileAnalysisStarted` — a profile analysis job has been queued.
- `ProfileAnalysisCompleted` — a profile analysis job has reached the `Analyzed` status.

## Constraints

- Only analyses in `Analyzed` status are considered "completed" for the descriptor read seam.
- The descriptor read seam is read-only: no write path, new pipeline step, or schema column is added by the CASE C seam.
- No Apify or image-retrieval call is made by the CASE C seam path.
- The read port interface (`IClientAestheticDescriptorReader`) contains no EF Core or Infrastructure types.

## Acceptance Criteria

- `IClientAestheticDescriptorReader.GetDescriptorsAsync(orgId, clientId, ct)` returns the `DetectedBackgroundImages` text from the most recently completed analysis, or an empty list when none exists.
- In-progress, partial, and failed analyses are excluded from the descriptor read.
- The profile analysis pipeline (scrape → analyze → persist → status lifecycle) is unchanged by the CASE C seam.
- Existing profile analysis API endpoints respond with the same contract as before the seam was added.

---

## CASE C Aesthetic Guidance Seam — Change: content-background-sourcing-policy

> Added by change `content-background-sourcing-policy`. Additive only — no existing profile-analysis requirement is modified.

### Requirement: PA-SEAM-01 — DetectedBackgroundImages Is Readable Through an Application Port for CASE C

The system MUST expose a read path in the Application layer that, given a `clientId`, retrieves the `DetectedBackgroundImages` text descriptors from the most recently completed (`Analyzed` status) `ProfileAiAnalysis` record for that client. The read path MUST be available for the CASE C strategy without any direct Infrastructure or EF Core dependency.

#### Scenario: Read path returns descriptors for a client with a completed analysis (PA-SEAM-01-a)

- GIVEN a client with a `ProfileAiAnalysis` record in status `Analyzed` containing `DetectedBackgroundImages = "Soft gradient washes"`
- WHEN the Application read port is called with that client's id
- THEN it returns `"Soft gradient washes"`

#### Scenario: Read path returns empty when no completed analysis exists (PA-SEAM-01-b)

- GIVEN a client with no `ProfileAiAnalysis` record (or only failed/in-progress)
- WHEN the Application read port is called
- THEN it returns null or an empty/absent result (no exception)

#### Scenario: Read path selects the most recent completed analysis (PA-SEAM-01-c)

- GIVEN two `Analyzed` records for the same client with dates `2026-01-01` and `2026-06-01`
- WHEN the read port is called
- THEN the `DetectedBackgroundImages` text from `2026-06-01` is returned

#### Scenario: In-progress or failed analyses are excluded (PA-SEAM-01-d)

- GIVEN only an in-progress `Analyzing` record for the client
- WHEN the read port is called
- THEN the result is null or empty

---

### Requirement: PA-SEAM-02 — Profile Analysis Pipeline and Persistence Are Unchanged

The profile-analysis scraping, AI analysis, metric computation, persistence schema, status lifecycle, and API endpoints MUST remain identical to their pre-change state.

---

### Requirement: PA-SEAM-03 — No Apify Image-Fetch Call in CASE C Seam Path

The CASE C seam MUST consume only the `DetectedBackgroundImages` text already stored in `ProfileAiAnalysis`. It MUST NOT add any call to Apify or any image-retrieval service.

---

*Merged from `openspec/changes/content-background-sourcing-policy/specs/profile-analysis/spec-delta.md` — change applied 2026-06-20.*

---

<!-- Merged from openspec/changes/content-aesthetic-generation/specs/profile-analysis/spec-delta.md — change applied 2026-06-21 -->
# Spec Delta: Profile Analysis — Carousel Child Counts, AverageSlidesPerCarousel, Wider Post Sample, Freshness Exposure

<!-- Change: content-aesthetic-generation -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md (additive delta) -->

## Purpose

Specify the **additive behavioral changes** this change introduces to the
`profile-analysis` capability:

1. **Carousel child-count capture** — `InstagramProfileApifyScrapeAdapter` maps the
   `childrenCount` field (when available from the actor) onto each carousel `ProfilePost`.
   This is a best-effort mapping: if the actor does not return `childrenCount` for a post,
   the field is null and the scrape still succeeds.

2. **`AverageSlidesPerCarousel` server-derived stat** — computed from carousel post
   child counts by `IProfileMetricCalculator` with no AI involvement. Result is nullable:
   null when no carousel posts with a known child count are present in the sample.

3. **`maxPostsPerProfile` raised to 18** — the Apify scrape configuration is updated
   to fetch up to 18 posts per profile (previously 12), giving the aesthetic signal a
   more representative window. Apify cost impact (~1.5x per scrape) is accepted as a
   system-level tradeoff.

4. **Freshness exposed via `CompletedAtUtc`** — the Application-layer read port for
   `ProfileAiAnalysis` MUST expose the `CompletedAtUtc` timestamp of the latest
   `Analyzed` record so the `ClientAestheticEnsureFresh` orchestration can perform
   the per-client staleness check (effective threshold: `client.AestheticRefreshThresholdDays ?? 30` days)
   without raw database access.

No profile-analysis AI prompts, existing pipeline stages, or persistence schema columns
are modified by this delta (except best-effort child-count data flowing through the
existing scrape JSON path). No new pipeline API endpoints are added.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAG-PA-01 | Proposal §Approach — capture carousel childrenCount in MapSinglePost |
| CAG-PA-02 | Proposal §Approach — compute AverageSlidesPerCarousel in IProfileMetricCalculator |
| CAG-PA-03 | Proposal §Goals — raise maxPostsPerProfile to 15–20 (locked at 18) |
| CAG-PA-04 | Proposal §Approach — expose CompletedAtUtc for freshness check |
| CAG-PA-05 | Proposal §Non-Goals — childrenCount best-effort; avg-slides null if absent |
| CAG-PA-06 | Proposal §Risks — actor childrenCount unconfirmed; treat as best-effort |

---

## ADDED Requirements

---

### Requirement: CAG-PA-01 — Carousel Posts Capture childrenCount When Available

`InstagramProfileApifyScrapeAdapter` (method `MapSinglePost` or equivalent) MUST
attempt to read the `childrenCount` integer field from the Apify actor response for
each post. When the field is present and valid:

- The `ProfilePost` domain object for that post MUST carry the child count.

When the field is absent or null in the actor response:

- The `ProfilePost` is created with `ChildrenCount = null`.
- The scrape MUST continue normally; the absence of `childrenCount` is NOT an error.

This is a **best-effort** mapping: the field's availability in profile-scraper mode is
unconfirmed at spec time. If the actor does not return it, all carousel posts will have
`ChildrenCount = null` and `AverageSlidesPerCarousel` will be null (see CAG-PA-02).

#### Scenario: childrenCount present in actor response — mapped to ProfilePost (CAG-PA-01-a)

- GIVEN an Apify actor response for a carousel post containing `childrenCount: 6`
- WHEN `MapSinglePost` (or equivalent) processes the post
- THEN the resulting `ProfilePost` has `ChildrenCount = 6`

#### Scenario: childrenCount absent from actor response — ProfilePost receives null (CAG-PA-01-b)

- GIVEN an Apify actor response for a carousel post where the `childrenCount` field
  is absent (not present in the JSON)
- WHEN `MapSinglePost` processes the post
- THEN the resulting `ProfilePost` has `ChildrenCount = null`
- AND no exception is thrown and the scrape pipeline continues

#### Scenario: Non-carousel post — ChildrenCount is null by definition (CAG-PA-01-c)

- GIVEN an Apify actor response for a single-image or video post (not a carousel)
- WHEN `MapSinglePost` processes the post
- THEN the resulting `ProfilePost` has `ChildrenCount = null`
- AND the mapping does not attempt to read `childrenCount` for non-carousel post types

#### Scenario: childrenCount null in actor response — ProfilePost receives null (CAG-PA-01-d)

- GIVEN an Apify actor response for a carousel post where `childrenCount` is explicitly
  `null` in the JSON
- WHEN `MapSinglePost` processes the post
- THEN the resulting `ProfilePost` has `ChildrenCount = null`
- AND the scrape continues normally

---

### Requirement: CAG-PA-02 — AverageSlidesPerCarousel Is a Server-Derived Stat, Computed Without AI

`IProfileMetricCalculator` (or its implementation) MUST compute `AverageSlidesPerCarousel`
as a pure arithmetic average of `ChildrenCount` across all carousel posts in the sample
that have a non-null `ChildrenCount`. No AI call is made for this stat.

The result MUST be:
- `null` if no carousel posts with a known `ChildrenCount` are in the sample.
- A `double` rounded to one decimal place when at least one qualifying carousel post exists.

The stat is stored in `ProfileStats` (or equivalent domain model) as a nullable `double`.

#### Scenario: Multiple carousel posts with child counts — average computed (CAG-PA-02-a)

- GIVEN a profile sample containing 3 carousel posts with `ChildrenCount` values of
  `4`, `6`, and `8`
- WHEN `IProfileMetricCalculator` computes `AverageSlidesPerCarousel`
- THEN the result is `6.0` (arithmetic average)

#### Scenario: Carousel posts with mixed null and known child counts — nulls excluded (CAG-PA-02-b)

- GIVEN a profile sample with carousel posts: `ChildrenCount = 4`, `ChildrenCount = null`,
  `ChildrenCount = 8`
- WHEN `IProfileMetricCalculator` computes `AverageSlidesPerCarousel`
- THEN the result is `6.0` (average of 4 and 8; the null post is excluded from both
  numerator and denominator)

#### Scenario: No carousel posts with known child count — result is null (CAG-PA-02-c)

- GIVEN a profile sample where all carousel posts have `ChildrenCount = null`
  (or no carousel posts exist at all)
- WHEN `IProfileMetricCalculator` computes `AverageSlidesPerCarousel`
- THEN the result is `null`
- AND no exception is thrown

#### Scenario: No AI call is made during stat computation (CAG-PA-02-d)

- GIVEN any profile sample
- WHEN `IProfileMetricCalculator.ComputeAsync` (or equivalent) runs
- THEN no call is made to any AI service, AI gateway, or external network endpoint
  as part of computing `AverageSlidesPerCarousel`

---

### Requirement: CAG-PA-03 — maxPostsPerProfile Raised to 18

The Apify scrape configuration constant (located in `ApifyProfileScrapeResolver` or
equivalent) MUST be updated from 12 to **18** posts per profile. This applies to all
profile scrapes that use this resolver, including on-demand refreshes triggered by the
ClientAesthetic orchestration.

The Apify cost increase (~1.5x per scrape versus the baseline of 12) is accepted as a
system-level tradeoff for a wider aesthetic signal window.

#### Scenario: Profile scrape requests 18 posts (CAG-PA-03-a)

- GIVEN a profile analysis job runs for any client
- WHEN `ApifyProfileScrapeResolver` (or equivalent) configures the Apify actor request
- THEN the `maxPostsPerProfile` parameter passed to the actor is 18
- AND no value of 12 (the prior default) is used

#### Scenario: On-demand refresh from ClientAesthetic also uses 18 posts (CAG-PA-03-b)

- GIVEN a ClientAesthetic orchestration step enqueues an on-demand profile refresh
- WHEN the resulting `ProfileAnalysisJob` executes and invokes the scraper
- THEN the scrape is configured for 18 posts (same resolver, same constant)

---

### Requirement: CAG-PA-04 — ProfileAiAnalysis Read Port Exposes CompletedAtUtc for Freshness Checks

The Application-layer read port for `ProfileAiAnalysis` (widened from the predecessor
change's `IClientAestheticDescriptorReader`, or a new sibling port) MUST expose the
`CompletedAtUtc` timestamp of the latest `Analyzed` `ProfileAiAnalysis` record in its
return value (`ClientAestheticProfile.AnalyzedAtUtc` or equivalent). This allows the
`ClientAestheticEnsureFresh` orchestration to compute staleness without coupling to
Infrastructure or database types.

When no `Analyzed` record exists, `AnalyzedAtUtc` MUST be null.

#### Scenario: Read port returns AnalyzedAtUtc when a completed analysis exists (CAG-PA-04-a)

- GIVEN a client with a completed `ProfileAiAnalysis` where
  `CompletedAtUtc = 2026-06-10T08:00:00Z`
- WHEN the Application read port is called with `(orgId, clientId)`
- THEN the returned profile includes `AnalyzedAtUtc = 2026-06-10T08:00:00Z`

#### Scenario: Read port returns AnalyzedAtUtc = null when no analysis exists (CAG-PA-04-b)

- GIVEN a client with no `Analyzed` `ProfileAiAnalysis` record
- WHEN the Application read port is called
- THEN the returned profile has `AnalyzedAtUtc = null`
- AND no exception is thrown

#### Scenario: Read port returns the most recent Analyzed record's timestamp (CAG-PA-04-c)

- GIVEN a client with two `Analyzed` records: `CompletedAtUtc = 2026-01-01` (older)
  and `CompletedAtUtc = 2026-06-01` (newer)
- WHEN the Application read port is called
- THEN `AnalyzedAtUtc = 2026-06-01T00:00:00Z`
- AND the older record's timestamp is NOT returned

---

### Requirement: CAG-PA-05 — AverageSlidesPerCarousel Flows Into ProfileAiAnalysis Output Accessible to the Profile Reader

`AverageSlidesPerCarousel` (computed by `IProfileMetricCalculator`) MUST be accessible
via the same Application read port that returns `ClientAestheticProfile`. It MUST NOT
require a separate read call. If the stat is null (no qualifying carousel posts), the
profile reader returns null for this field.

Provider types (EF Core, Npgsql) MUST NOT appear in the read port interface, its handler,
or the `ClientAestheticProfile` value object.

#### Scenario: Reader returns AverageSlidesPerCarousel when the stat is computed (CAG-PA-05-a)

- GIVEN a profile analysis whose `ProfileStats` includes `AverageSlidesPerCarousel = 5.2`
- WHEN the Application read port is called
- THEN the returned `ClientAestheticProfile.AverageSlidesPerCarousel = 5.2`

#### Scenario: Reader returns null for AverageSlidesPerCarousel when the stat is absent (CAG-PA-05-b)

- GIVEN a profile analysis whose `ProfileStats` has `AverageSlidesPerCarousel = null`
- WHEN the Application read port is called
- THEN `ClientAestheticProfile.AverageSlidesPerCarousel = null`
- AND no exception is thrown

---

## MODIFIED Requirements

### Requirement Delta: PA-SEAM-01 — Descriptor Read Now Also Returns Freshness and Richer Stats

`PA-SEAM-01` from the predecessor change specified that the Application read port returns
`DetectedBackgroundImages` text descriptors only. This change **widens** the return contract:

> The read port MUST return `ClientAestheticProfile` (replacing the flat descriptor list),
> which includes `BackgroundDescriptors`, `VoiceTone?`, `AverageSlidesPerCarousel?`, and
> `AnalyzedAtUtc?` from the latest `Analyzed` `ProfileAiAnalysis`.

The predecessor's `PA-SEAM-01-a / b / c / d` scenarios remain valid for the
`BackgroundDescriptors` sub-field. The new requirements `CAG-PA-04` and `CAG-PA-05`
extend the port contract without removing any previous guarantee.

### Requirement Delta: PA-SEAM-02 — Pipeline Gains Child-Count Capture (Additive)

`PA-SEAM-02` from the predecessor change stated that the pipeline is entirely unchanged.
This change introduces an additive extension:

> The scrape mapping step (`MapSinglePost`) now attempts to read `childrenCount` from
> the actor response for carousel posts. This is best-effort and does not alter any
> existing field mapping, status lifecycle, or API contract.

`PA-SEAM-02-a` and `PA-SEAM-02-b` remain valid for the API contract and status lifecycle.
The pipeline adds a zero-breaking-change field read.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative, for test authoring)

- **CAG-PA-01** MUST be covered by unit tests on the scrape adapter:
  - Given a raw actor JSON with `childrenCount: N` → assert `ProfilePost.ChildrenCount = N`.
  - Given a raw actor JSON without `childrenCount` → assert `ChildrenCount = null`,
    no exception.
  - Given a non-carousel post type → assert `ChildrenCount = null`.
- **CAG-PA-02** MUST be covered by pure unit tests on `IProfileMetricCalculator`:
  - Parameterized: several child counts → assert arithmetic average to 1 d.p.
  - Mixed nulls → assert nulls excluded from both numerator and denominator.
  - All nulls / empty → assert null result.
  - Assert no external call is made (mock injection / no network dependency).
- **CAG-PA-03** MUST be verified by a test that inspects the Apify actor request payload
  built by `ApifyProfileScrapeResolver` and asserts `maxPostsPerProfile == 18`.
- **CAG-PA-04** MUST be covered by unit tests on the read port handler: mock the
  repository to return a record with `CompletedAtUtc`; assert it surfaces in
  `ClientAestheticProfile.AnalyzedAtUtc`. Also: null repository result → null field.
- **CAG-PA-05** MUST be covered by an integration test on the EF read adapter seeded
  with a `ProfileAiAnalysis` row containing a computed stat; assert the Application
  port returns the correct value without any Infrastructure type leaking into the
  return value.
- Architecture gate: `ClientAestheticProfile` and the read port interface MUST NOT
  reference EF Core or Npgsql. The `pwsh -File scripts/gates/Run-Gates.ps1` gate
  enforces this at CI.
- `childrenCount` actor field availability is UNCONFIRMED at spec time. If absent during
  apply, all `ChildrenCount` values will be null and `AverageSlidesPerCarousel` will be
  null system-wide. This is the intended best-effort behavior; no spec is violated.

*Merged from `openspec/changes/content-aesthetic-generation/specs/profile-analysis/spec-delta.md` — change applied 2026-06-21.*

---

<!-- Merged from openspec/changes/client-aesthetic-conceptual-detection/specs/profile-analysis/spec-delta.md — change applied 2026-07-01 -->

# Delta: Profile Analysis — Conceptual Method Detection, Image-Type Ratio, Paired Input

## Purpose

Enrich the multimodal aesthetic extractor and its downstream model chain with three
additive capabilities:

1. **Conceptual/art-direction method signal** — captures HOW a client turns a message
   into an image (metaphorical-editorial vs. literal-illustration) rather than only
   the visual surface.
2. **Robust image-type composition ratio** — a statistically grounded photo-vs-flat
   frequency across the client's feed, robustifying the existing `SlideLayoutPattern`
   foundation.
3. **Ordered image↔caption pair input** — feeds the analyzer paired (image, caption)
   inputs so the model can learn the message→image metaphor mapping.

All new fields are additive nullable. Cold-start clients (no conceptual data) MUST
produce output identical to pre-change behavior.

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CACD-PA-01 | §Scope — Enrich detection (conceptual method) |
| CACD-PA-02 | §Scope — Enrich detection (robust ratio) |
| CACD-PA-03 | §Scope — Image↔caption pairing input |
| CACD-PA-04 | §Scope — Carry fields through AestheticExtraction (additive) |
| CACD-PA-05 | §Scope — Carry fields through ClientAestheticProfile + persistence |
| CACD-PA-06 | §Scope — Real vision model on ai-aesthetic step |
| CACD-PA-07 | §Success Criteria — Cold-start parity |

---

## Client Exemplars (Anchors for Acceptance Tests)

| Client | Method | Ratio | Visual Identity |
|---|---|---|---|
| Atlas (`@atlas.editorial`) | Metaphorical-editorial: idea-driven visual metaphors via mono-color still-life and staged photography | ≈50% photo / 50% flat-color | Saturated mono-color backgrounds; conceptual still-life; staged photography |
| Comet Comedy | Literal-illustration: tender watercolor illustrations of children with warm cream palette | Predominantly illustration (low photo frequency) | Warm cream; hand-drawn children; pastel decorative icons |

These two exemplars MUST be distinguishable by CACD-PA-01 and CACD-PA-02 outputs and
MUST be used as the canonical validation anchors in spec/integration tests.

---

## ADDED Requirements

### Requirement: CACD-PA-01 — Extractor DTO Gains Nullable `ConceptualMethod` Field

The multimodal aesthetic extractor response DTO MUST include a nullable `ConceptualMethod`
field. The field captures the client's art-direction method — the image↔message creative
relationship — as a structured value that carries a closed `ArtDirectionMethod` enum member
(`Unknown`, `MetaphoricalEditorial`, `LiteralIllustration`, `SymbolicStillLife`,
`DocumentaryPhotography`, `GraphicTypographic`, `Mixed`), a `Rationale` string, and an
`IReadOnlyList<string> Cues`. When the method is not determinable from the feed, the field
MUST be null. The addition MUST be backward-compatible; extractor consumers that do not
read this field are unchanged.

The field MUST distinguish a metaphorical-editorial method (Atlas exemplar) from a
literal-illustration method (Comet exemplar) when both are detectable.

#### Scenario: Metaphorical-editorial method detected — Atlas exemplar (CACD-PA-01-a)

- GIVEN a multimodal extraction run against Atlas's feed, where each image is a
  visual metaphor of a post message (e.g., staged still-life objects representing
  abstract psychology concepts, saturated mono-color backgrounds)
- WHEN the extractor produces its response DTO
- THEN `ConceptualMethod` contains a description identifying a metaphorical or
  editorial art-direction method (e.g., references conceptual still-life, staged
  photography, or metaphor-driven image construction)
- AND the description is NOT compatible with a literal-illustration characterization

#### Scenario: Literal-illustration method detected — Comet exemplar (CACD-PA-01-b)

- GIVEN a multimodal extraction run against Comet Comedy' feed, where images
  are tender hand-drawn watercolor illustrations of children with warm cream backgrounds
- WHEN the extractor produces its response DTO
- THEN `ConceptualMethod` contains a description identifying a literal or
  illustration-based art-direction method (e.g., references watercolor illustration,
  literal depiction, or tender character illustration)
- AND the description is NOT compatible with a metaphorical-editorial characterization

#### Scenario: No determinable method — `ConceptualMethod` null; extraction succeeds (CACD-PA-01-c)

- GIVEN a feed with ambiguous or inconsistent images that do not reveal a clear
  art-direction method, OR an unrecognized method token returned by the model
- WHEN the extractor produces its DTO
- THEN `ConceptualMethod` is null (or `Method = Unknown`)
- AND the extraction completes successfully with no error raised
- AND all other DTO fields are unaffected

---

### Requirement: CACD-PA-02 — Extractor DTO Gains Nullable `ImageTypeRatio` Field

The multimodal aesthetic extractor response DTO MUST include a nullable `ImageTypeRatio`
field representing the relative frequency of photographic/staged-photography slides
versus flat-color slides across the client's feed. The ratio MUST be statistically
grounded across multiple posts in the feed sample (not inferred from a single carousel).
`SampleSize` carries the total count of slides observed (photo + flat + decorator), which
is the confidence/grounding signal. When no reliable ratio can be determined, the field
MUST be null.

The field MUST distinguish Atlas's approximately equal photo/flat mix from Comet's
predominantly illustration (low photo frequency) composition.

#### Scenario: Photo/flat ratio detected — Atlas exemplar ≈50/50 (CACD-PA-02-a)

- GIVEN a feed sample from Atlas's profile with approximately equal counts of
  photographic/staged slides and flat-color/mono slides across multiple carousels
- WHEN the extractor produces its DTO
- THEN `ImageTypeRatio` reflects approximately 50% photographic and 50% flat-color
  (within a reasonable margin, e.g., ±20%)
- AND the ratio is grounded across multiple posts, not a single carousel

#### Scenario: Low-photo ratio detected — Comet exemplar (CACD-PA-02-b)

- GIVEN a feed sample from Comet Comedy with predominantly watercolor
  illustration slides and few photographic slides
- WHEN the extractor produces its DTO
- THEN `ImageTypeRatio` reflects a significantly lower photographic frequency than
  Atlas's ratio (the two values MUST be distinguishable)

#### Scenario: Insufficient sample — `ImageTypeRatio` null; extraction succeeds (CACD-PA-02-c)

- GIVEN a feed with too few posts to ground a statistically meaningful ratio
- WHEN the extractor produces its DTO
- THEN `ImageTypeRatio` is null
- AND the extraction completes successfully with no error raised

---

### Requirement: CACD-PA-03 — Extractor Receives Ordered Image↔Caption Pairs; Best-Effort Fallback When Captions Absent

The `IClientAestheticStyleExtractor` (and its implementation `MultimodalAestheticStyleExtractor`)
MUST accept the client's content as an ordered sequence of `{Image, Caption?}` pairs rather
than a loose caption block. When a caption is present for an image, the extractor MUST
receive them together so it can infer the message→image metaphor mapping. When a caption
is absent or the caption list is shorter than the image list, the affected images MUST be
passed image-only. The extraction MUST NOT fail when captions are missing, fewer than
images, or misaligned.

#### Scenario: All image↔caption pairs present — paired input passed (CACD-PA-03-a)

- GIVEN a list of N images each paired with a matching caption
- WHEN the extractor is called with the ordered pairs
- THEN each image and its caption are passed together as a pair
- AND the extractor can reason about the message→image method across all pairs

#### Scenario: Some captions absent — image-only fallback for those positions (CACD-PA-03-b)

- GIVEN a list of 5 images where positions 1 and 3 have no matching caption
- WHEN the extractor is called
- THEN images at positions 0, 2, 4 are passed with their captions
- AND images at positions 1, 3 are passed image-only (no caption argument)
- AND the extraction completes successfully with no error raised

#### Scenario: Caption list shorter than image list — trailing images passed image-only (CACD-PA-03-c)

- GIVEN 5 images and 3 captions
- WHEN the extractor is called
- THEN the first 3 images are paired with captions
- AND the last 2 images are passed image-only
- AND no index-out-of-range or alignment exception is thrown

#### Scenario: No captions available — all images passed image-only (CACD-PA-03-d)

- GIVEN N images and 0 captions
- WHEN the extractor is called
- THEN all N images are passed image-only
- AND the extraction proceeds and produces a result
- AND no error is raised

---

### Requirement: CACD-PA-04 — `AestheticExtraction` Carries `ConceptualMethod` and `ImageTypeRatio` as Additive Nullable Fields

The Application-layer `AestheticExtraction` value object MUST include nullable
`ConceptualMethod` and `ImageTypeRatio` fields, populated from the extractor DTO.
Both fields MUST be additive; no existing field is modified or removed. When the
corresponding extractor DTO fields are null, the `AestheticExtraction` fields MUST be null.
No provider, EF Core, or Infrastructure type MAY appear in `AestheticExtraction`.

#### Scenario: Both fields populated from DTO (CACD-PA-04-a)

- GIVEN an extractor DTO with non-null `ConceptualMethod` and non-null `ImageTypeRatio`
- WHEN mapped to `AestheticExtraction`
- THEN `AestheticExtraction.ConceptualMethod` carries the DTO value
- AND `AestheticExtraction.ImageTypeRatio` carries the DTO value

#### Scenario: Both fields null in DTO — AestheticExtraction fields null (CACD-PA-04-b)

- GIVEN an extractor DTO with null `ConceptualMethod` and null `ImageTypeRatio`
- WHEN mapped to `AestheticExtraction`
- THEN `AestheticExtraction.ConceptualMethod` is null
- AND `AestheticExtraction.ImageTypeRatio` is null

#### Scenario: Existing fields are unaffected by the addition (CACD-PA-04-c)

- GIVEN the completed implementation
- WHEN `AestheticExtraction` is compiled and its fields enumerated
- THEN all pre-change fields (palette, styleDescriptors, motifs, captionVoice,
  slideMixPattern, etc.) retain their types and semantics
- AND no field is removed or renamed

---

### Requirement: CACD-PA-05 — `ClientAestheticProfile` Carries New Fields; Persistence Round-Trip Preserves Them; Old Blobs Read as Null

`ClientAestheticProfile` MUST carry nullable `ConceptualMethod` and `ImageTypeRatio`
fields populated from `AestheticExtraction`. Both fields MUST be persisted additively
by `AestheticProfilePersistence` as JSON keys `conceptualMethod` and `imageTypeRatio`
inside the existing `aesthetic_profile_json` column. A stored profile blob persisted
before this change (containing no `ConceptualMethod` or `ImageTypeRatio` entries) MUST
deserialize with those fields as null and MUST NOT raise an exception.

#### Scenario: New fields round-trip through persistence (CACD-PA-05-a)

- GIVEN a `ClientAestheticProfile` with non-null `ConceptualMethod` and `ImageTypeRatio`
- WHEN persisted and read back via `AestheticProfilePersistence`
- THEN `ConceptualMethod` and `ImageTypeRatio` match the persisted values with no data loss

#### Scenario: Pre-change blob reads as null without error (CACD-PA-05-b)

- GIVEN a stored profile blob that has no `ConceptualMethod` or `ImageTypeRatio` entries
  (persisted by a version of the system before this change)
- WHEN deserialized into `ClientAestheticProfile`
- THEN `ConceptualMethod` is null and `ImageTypeRatio` is null
- AND no exception is thrown and all pre-existing fields are intact

#### Scenario: Null fields persist and read back as null (CACD-PA-05-c)

- GIVEN a `ClientAestheticProfile` with `ConceptualMethod = null` and `ImageTypeRatio = null`
- WHEN persisted and read back
- THEN both fields remain null with no error

---

### Requirement: CACD-PA-06 — `ai-aesthetic` Step Routes to a Vision-Capable Model; `claude -p` Is Prohibited

The `ai-aesthetic` / `extract-aesthetic` workflow step MUST route to a model that
accepts image input (a genuine vision-capable model). The step MUST NOT route to
`claude -p` (the Claude Code CLI agent), which converses and does not honor JSON
contracts. This is the ONLY routing change in this change; all other workflow step
routing is unaffected.

#### Scenario: Vision model receives images and returns JSON contract (CACD-PA-06-a)

- GIVEN a configured `ai-aesthetic` step targeting a vision-capable model
- WHEN a profile analysis job runs the aesthetic extraction step
- THEN the model receives image inputs alongside the extraction instruction
- AND the model returns a response conforming to `AestheticExtractionDto`
- AND the `claude -p` binary is NOT invoked

#### Scenario: Other workflow steps are unaffected (CACD-PA-06-b)

- GIVEN the completed routing configuration
- WHEN workflow steps other than `ai-aesthetic` run
- THEN their model/provider routing is unchanged from the pre-change configuration

---

### Requirement: CACD-PA-07 — Cold-Start Parity: Null Conceptual Fields Produce Pre-Change Output

When `ConceptualMethod` and `ImageTypeRatio` are both null on `ClientAestheticProfile`,
every downstream consumer MUST produce output identical to the output produced before
this change. No default value, placeholder text, or behavior change MAY be applied
when these fields are absent.

#### Scenario: Cold-start client produces identical output (CACD-PA-07-a)

- GIVEN a `ClientAesthetic` client whose `ClientAestheticProfile` has
  `ConceptualMethod = null` and `ImageTypeRatio = null`
- WHEN content generation runs (image prompt writing and any downstream steps)
- THEN the image prompts and generated outputs are identical to the outputs produced
  by the pre-change system for the same inputs
- AND no ideation section, ratio section, or new instruction block appears in any output

---

## Added Requirements: Faithful Flat-Slide Rendering — Brand-Mark Detection (faithful-flat-slide-rendering, #50)

### Requirement: FFS-PA-01 — Extractor Detects Recurring Brand Mark

`MultimodalAestheticStyleExtractor` MUST detect whether a recurring identifying mark exists across
analyzed images and, when present, its approximate position and color (typically the text color),
returned as `brandMark { present, positionHint, colorHex }`. The extraction prompt/schema version
MUST be bumped (v2→v3), additive and backward-compatible with pre-v3 responses.

#### Scenario: Recurring mark detected (FFS-PA-01-a)

- GIVEN analyzed images consistently show the same mark
- WHEN extraction runs
- THEN `brandMark.present = true` with a position hint and color hex

#### Scenario: No recurring mark — extractor reports absent (FFS-PA-01-b)

- GIVEN no consistent mark across analyzed images
- WHEN extraction runs
- THEN `brandMark.present = false` and the LLM does not fabricate a mark

### Requirement: FFS-PA-02 — `BrandMark` Persisted as Additive Nullable Field, No EF Migration

`ClientAestheticProfile` MUST carry a nullable `BrandMark` field populated from the extractor
output, persisted additively as a JSON key inside the existing `aesthetic_profile_json` blob column
via `AestheticProfilePersistence`. No EF Core migration is required or permitted. Pre-change blobs
MUST deserialize with `BrandMark = null` and no exception.

#### Scenario: Round-trip preserves BrandMark (FFS-PA-02-a)

- GIVEN a profile with a populated `BrandMark`
- WHEN persisted and read back
- THEN `BrandMark` matches with no data loss

#### Scenario: Pre-change blob reads as null (FFS-PA-02-b)

- GIVEN a stored blob with no `brandMark` key (persisted before this change)
- WHEN deserialized into `ClientAestheticProfile`
- THEN `BrandMark` is null; no exception is thrown; all pre-existing fields intact

*Merged from SDD change `faithful-flat-slide-rendering` (issue #50) — applied 2026-07-06.*

## MODIFIED Requirements

None. All requirements above are additive. Pre-existing requirements
`PA-SEAM-01`–`PA-SEAM-03`, `CAG-PA-01`–`CAG-PA-06` remain valid and unchanged.
The `IClientAestheticStyleExtractor` input change (CACD-PA-03) replaces the loose
caption block with ordered pairs; the signature shape is a design-level decision
(OQ-3, ADR 0009) and does not alter any previously specified behavioral guarantee
for the output contract.

## REMOVED Requirements

None.

---

<!-- Merged from SDD change aesthetic-freshness-versioning (issue: single-analyzer-ui) -->

# Delta: Profile Analysis — Schema-Version and Completeness-Aware Freshness

## Purpose

Fix a freshness bug where a client analyzed BEFORE new extractor fields shipped (e.g.
`ColorPairing`/`BackgroundPattern`, later `BrandMark`) reads those fields as null, but the
purely AGE-based freshness check considers the analysis "fresh" — so generation never gets
the structured aesthetic data until the age threshold expires. This change makes the
freshness check schema-version-aware AND completeness-aware within the age window.

## ADDED Requirements

### Requirement: AFV-PA-01 — Serialize Stamps the Extraction's Actual Schema Version

`AestheticProfilePersistence.Serialize` MUST stamp `PromptVersion` from the extraction's own
`SchemaVersion` (the extractor that actually produced it), NOT a hardcoded constant. It falls
back to `"aesthetic-extractor/v2"` only when the extraction carries no `SchemaVersion`.
`AestheticProfilePersistence.Read` MUST also return the stored schema version so freshness
checks can compare it.

#### Scenario: Serialize/Read round-trips the actual schema version (AFV-PA-01-a)

- GIVEN an extraction with `SchemaVersion = "aesthetic-extractor/v3"`
- WHEN it is serialized then read back
- THEN the read result's schema version equals `"aesthetic-extractor/v3"`

#### Scenario: Old blob without a promptVersion key reads as null (AFV-PA-01-b)

- GIVEN a stored blob persisted before this change (no `promptVersion` key)
- WHEN read
- THEN the schema version is null; no exception is thrown

### Requirement: AFV-PA-02 — Schema-Version-Aware and Completeness-Aware Freshness Evaluation

Within the existing age-fresh window (age-expiry and missing-analysis paths are unchanged and
always refresh), `ClientAestheticEnsureFreshService` MUST evaluate a pure function
(`AestheticFreshness.Evaluate`) over the latest completed analysis's stored schema version and
completeness signal:

- IF the stored schema version is older than the current version (`AestheticFreshness.CurrentSchemaVersion`)
  → refresh (schema upgrade; the newer extractor may populate previously-missing fields).
- ELSE IF the analysis is materially complete (`ColorPairings`, `BackgroundPattern`, AND
  `ReferenceImages` all present) → Fresh, no refresh.
- ELSE (same version, incomplete) → Fresh, no refresh (already analyzed at the current/best
  extractor version; refreshing again would loop forever without new data — only age expiry or
  a future version bump refreshes it).

Unknown, null, or malformed stored versions parse as the oldest possible version and trigger a
refresh.

#### Scenario: Age-fresh analysis with an older schema version refreshes (AFV-PA-02-a)

- GIVEN a client's latest analysis is within the staleness threshold and carries an aesthetic
  payload stamped with an older schema version than current
- WHEN `EnsureFreshAsync` runs
- THEN a refresh is enqueued (schema upgrade)

#### Scenario: Age-fresh, current version, materially complete → Fresh (AFV-PA-02-b)

- GIVEN the latest analysis is within the staleness threshold, stamped with the current schema
  version, and has `ColorPairings`, `BackgroundPattern`, and `ReferenceImages` all present
- WHEN `EnsureFreshAsync` runs
- THEN the outcome is Fresh; no refresh is enqueued

#### Scenario: Age-fresh, current version, incomplete → Fresh, no loop (AFV-PA-02-c)

- GIVEN the latest analysis is within the staleness threshold, stamped with the current schema
  version, but missing one or more of `ColorPairings` / `BackgroundPattern` / `ReferenceImages`
- WHEN `EnsureFreshAsync` runs
- THEN the outcome is Fresh; no refresh is enqueued (avoids refreshing forever without new data)

### Requirement: AFV-PA-03 — Repository Exposes Latest Aesthetic Freshness Snapshot

`IProfileAnalysisRepository` MUST expose `GetLatestAestheticFreshnessAsync(orgId, clientId, ct)`
returning the stored schema version and the three completeness pillars for the latest
`Analyzed` analysis's aesthetic payload. Provider types (EF Core, Npgsql) MUST NOT appear in
the port signature or return type.

#### Scenario: EF adapter parses the stored aesthetic JSON for the freshness snapshot (AFV-PA-03-a)

- GIVEN the latest `Analyzed` analysis has an `aesthetic_profile_json` blob with
  `ColorPairings`, `BackgroundPattern`, and `ReferenceImages` all populated, stamped
  `"aesthetic-extractor/v3"`
- WHEN the EF adapter is queried
- THEN it returns `SchemaVersion = "aesthetic-extractor/v3"` and all three `Has*` flags `true`

## MODIFIED Requirements

None. All requirements above are additive; `CAG-PA-04` (age-based freshness exposure) and
`CAG-PA-05` remain valid and unchanged — this change layers schema-version and completeness
awareness INSIDE the existing age-fresh window, it does not alter age-expiry or
missing-analysis behavior.

## REMOVED Requirements

None.

*Merged from `openspec/changes/client-aesthetic-conceptual-detection/specs/profile-analysis/spec-delta.md` — change applied 2026-07-01.*

---

<!-- Merged from openspec/changes/client-aesthetic-fidelity-fixes/specs/profile-analysis/spec-delta.md — change applied 2026-08-24 -->

# Spec Delta: Profile Analysis — Three-Role Typography Extraction, Per-Slide Photo-Transparency Signal

<!-- Change: client-aesthetic-fidelity-fixes -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md (additive delta) -->

## Purpose

Specify the **additive** extraction/persistence changes this change introduces:

1. **Three-role typography extraction** — the multimodal aesthetic extractor gains heading,
   body, AND caption/overline typography extraction (family class, weight, italic, size
   ratio). Previously the extractor emitted no typography at all; `AestheticExtraction` /
   `AestheticProfilePersistence` / `ClientAestheticProfile.Typography` already carry a
   role-keyed contract, so this change extends the extractor's output, not the persisted
   shape.
2. **Per-slide photo-transparency signal** — a new additive field parallel and index-aligned
   to `BackgroundPattern`: boolean + qualitative descriptor per slide (never a numeric
   alpha, per settled decision 2), threaded through the extractor prompt, DTO, `Map()`,
   `AestheticExtraction`, `AestheticProfilePersistence`, and `ClientAestheticProfile`.

All new fields are additive-nullable. A client with no signal (typography or transparency)
produces extraction/persistence output that is unaffected downstream (see `content-generation`
delta for the cold-start render-time guarantee).

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CAFF-PA-01 | Settled decision 5 — three typography roles; Scope item 4 |
| CAFF-PA-02 | Scope item 4 — persistence round-trip, no DB migration |
| CAFF-PA-03 | Settled decision 2 & 4 — boolean + descriptor, per-slide; Scope item 5 |
| CAFF-PA-04 | Risks — transparency array length reconciliation vs. `BackgroundPattern` |

---

## ADDED Requirements

### Requirement: CAFF-PA-01 — Extractor Extracts Three Typography Roles

`MultimodalAestheticStyleExtractor` MUST extract typography for the heading, body, AND
caption/overline roles (family class, weight, italic, size ratio) whenever distinguishable
in the client's analyzed feed. Each role is independently additive-nullable: detecting
caption/overline MUST NOT require heading/body to also be detected, and vice versa. When a
role is not determinable, its value MUST be null and extraction MUST still succeed (no error).

#### Scenario: All three roles detected

- GIVEN a client feed with visually distinguishable heading, body, and caption/overline type
- WHEN extraction runs
- THEN the extractor DTO carries non-null family/weight/italic/size-ratio for all three roles

#### Scenario: Only heading and body detected — caption/overline absent (regression: two-role clients unaffected)

- GIVEN a client feed where caption/overline type is not distinguishable but heading and body are
- WHEN extraction runs
- THEN heading and body are populated exactly as before this change
- AND caption/overline is null
- AND no exception is thrown

#### Scenario: No typography role detected — cold start preserved

- GIVEN a client feed with no distinguishable typography
- WHEN extraction runs
- THEN heading, body, and caption/overline are all null
- AND downstream font resolution behaves exactly as it did before this change (see content-generation CAFF-CG-06/07)

---

### Requirement: CAFF-PA-02 — Typography Roles Persist Through AestheticExtraction and ClientAestheticProfile Round-Trip

`AestheticExtraction`, `AestheticProfilePersistence`, and `ClientAestheticProfile.Typography`
MUST carry all three roles additively, with no EF Core migration. A stored profile blob
persisted before this change (zero or two roles) MUST deserialize with any missing role as
null, with no exception.

#### Scenario: Round-trip preserves three roles

- GIVEN a `ClientAestheticProfile.Typography` with populated heading, body, and caption/overline
- WHEN persisted and read back
- THEN all three roles match the persisted values with no data loss

#### Scenario: Pre-change blob (no caption key) reads caption as null

- GIVEN a stored profile blob persisted before this change containing only heading/body (or no typography key at all)
- WHEN deserialized into `ClientAestheticProfile`
- THEN caption/overline is null
- AND no exception is thrown and heading/body values are intact

---

### Requirement: CAFF-PA-03 — Per-Slide Photo-Transparency Signal Extracted as Boolean + Qualitative Descriptor

The extractor MUST extract, per slide, a photo-transparency signal expressed as a boolean
plus a qualitative descriptor string, index-aligned and parallel to `BackgroundPattern`.
The signal MUST NEVER be expressed as a numeric opacity/alpha ratio (settled decision 2).
When the signal is not determinable for a slide, that slide's entry MUST be null.

#### Scenario: Client with a consistent per-slide blended-photo habit

- GIVEN a client feed where slides at index 1 and 3 show a faded/blended photo background
- WHEN extraction runs
- THEN the transparency array has `true` + a qualitative descriptor at indices 1 and 3
- AND indices without the treatment are `false` or null
- AND the array is index-aligned with `BackgroundPattern`

#### Scenario: Client with no transparency treatment — cold start unaffected

- GIVEN a client feed with no blended/faded background habit on any slide
- WHEN extraction runs
- THEN every index in the transparency array is `false` or null
- AND no exception is thrown

#### Scenario: Signal is never a numeric alpha value

- GIVEN any extraction run that detects a transparency treatment
- WHEN the extractor DTO is inspected
- THEN the transparency field type is boolean + string descriptor only
- AND no numeric opacity/alpha field is present in the contract

---

### Requirement: CAFF-PA-04 — Per-Slide Transparency Array Is Reconciled Against BackgroundPattern Length, Never Throws

When the extracted per-slide transparency array's length differs from `BackgroundPattern`'s
length, the reconciliation step (mirroring `BackgroundPatternReconciler`'s tolerance) MUST
index-align the two arrays and pad any missing index with null. It MUST NOT throw and MUST
NOT truncate or alter `BackgroundPattern` itself.

#### Scenario: Transparency array shorter than BackgroundPattern

- GIVEN `BackgroundPattern` has 5 entries and the transparency array has 3
- WHEN the profile is built
- THEN indices 0–2 use the extracted values and indices 3–4 are null
- AND no exception is thrown
- AND `BackgroundPattern` is unchanged

#### Scenario: Transparency array longer than BackgroundPattern

- GIVEN `BackgroundPattern` has 3 entries and the transparency array has 5
- WHEN the profile is built
- THEN only indices 0–2 are retained (aligned to `BackgroundPattern`'s length)
- AND no exception is thrown

#### Scenario: Equal length — straightforward index alignment

- GIVEN `BackgroundPattern` and the transparency array both have 4 entries
- WHEN the profile is built
- THEN each transparency entry aligns 1:1 with its `BackgroundPattern` index
- AND no reconciliation/padding is needed

---

## MODIFIED Requirements

None. All requirements above are additive; existing extraction, persistence, and API
behavior for typography-absent and transparency-absent clients is unchanged.

## REMOVED Requirements

None.

---

## Added Requirements: Brand-Mark Type, Text, Source Image, and Bounding Box (client-aesthetic-generation-hardening)

### Requirement: CAGH-PA-01 — BrandMarkDetection Gains an Additive Nullable MarkType Discriminator

`BrandMarkDetection`/`BrandMarkDto` MUST gain an additive nullable `MarkType` discriminator with
values `"text"`, `"icon"`, or `"unknown"`. Any value returned by the extractor that does not map to
`"text"` or `"icon"` MUST parse to `"unknown"`. A profile persisted before this change MUST
deserialize with `MarkType = null` and throw nothing.

#### Scenario: Extractor returns a recognised MarkType (CAGH-PA-01-a)

- GIVEN the extractor returns `markType: "text"` for a client's recurring mark
- WHEN the response is parsed into `BrandMarkDetection`
- THEN `MarkType = "text"`

#### Scenario: Extractor returns an unrecognised value — maps to unknown (CAGH-PA-01-b)

- GIVEN the extractor returns an unrecognised or malformed `markType` token
- WHEN the response is parsed
- THEN `MarkType = "unknown"`
- AND no exception is thrown

#### Scenario: Pre-change profile deserializes with MarkType null (CAGH-PA-01-c, regression/cold-start)

- GIVEN a profile blob persisted before this change (no `markType` key)
- WHEN deserialized into `BrandMarkDetection`
- THEN `MarkType = null`
- AND no exception is thrown; all pre-existing fields (`Present`, `PositionHint`, `ColorHex`) intact

---

### Requirement: CAGH-PA-02 — Text Field Carries the Literal Signature String for Text Marks

`BrandMarkDetection` MUST gain an additive nullable `Text` field carrying the literal signature
string, meaningful only when `MarkType = "text"`. When the extractor does not return a text value,
or `MarkType` is not `"text"`, `Text` MUST be null.

#### Scenario: Text mark carries the extracted literal (CAGH-PA-02-a)

- GIVEN the extractor detects a recurring text signature `"Studio Marisol"`
- WHEN the response is parsed
- THEN `MarkType = "text"` and `Text = "Studio Marisol"`

#### Scenario: Icon mark — Text is null (CAGH-PA-02-b)

- GIVEN the extractor detects an icon/symbol mark, not a text signature
- WHEN the response is parsed
- THEN `MarkType = "icon"` and `Text = null`

---

### Requirement: CAGH-PA-03 — SourceImagePath Resolves an Extractor Source Index to the Archived StoragePath

For an icon mark, the extractor returns a source image **index** referencing its own indexed input
images (`"Image 0"`, `"Image 1"`, …). The extractor — the only component that knows that ordering —
MUST resolve that index to the corresponding `ProfilePostImageRef.StoragePath` before returning, so
`BrandMarkDetection.SourceImagePath` carries a storage path, not a prompt-ordering index. No
downstream consumer MAY depend on prompt ordering. When the index cannot be resolved (out of range,
missing, or the referenced image has no archived path), `SourceImagePath` MUST be null.

#### Scenario: Valid source index resolves to a storage path (CAGH-PA-03-a)

- GIVEN the extractor returns source index `2` for an icon mark, and index 2 corresponds to an
  archived `ProfilePostImageRef` with `StoragePath = "clients/123/refs/img2.jpg"`
- WHEN the extraction response is mapped
- THEN `BrandMarkDetection.SourceImagePath = "clients/123/refs/img2.jpg"`

#### Scenario: Out-of-range or unresolvable index — SourceImagePath is null (CAGH-PA-03-b)

- GIVEN the extractor returns a source index that does not correspond to any archived reference
  image
- WHEN the extraction response is mapped
- THEN `SourceImagePath = null`
- AND no exception is thrown and the rest of `BrandMarkDetection` is unaffected

---

### Requirement: CAGH-PA-04 — BoundingBox Is Normalised, Validated, and Guarded by a Minimum-Area Threshold

`BrandMarkDetection` MUST gain an additive nullable `BoundingBox` field, normalised to the 0..1
coordinate range relative to the source image. A bounding box that is malformed, out of the 0..1
range, or below a configured minimum-area guard MUST be rejected and MUST result in
`BoundingBox = null`, not an exception.

#### Scenario: Valid normalised bounding box is retained (CAGH-PA-04-a)

- GIVEN the extractor returns a bounding box within 0..1 bounds and above the minimum-area guard
- WHEN the extraction response is parsed
- THEN `BoundingBox` carries the normalised coordinates unchanged

#### Scenario: Bounding box below the minimum-area guard is rejected (CAGH-PA-04-b)

- GIVEN the extractor returns a bounding box whose area is below the configured minimum-area
  threshold
- WHEN the extraction response is parsed
- THEN `BoundingBox = null`
- AND no exception is thrown

#### Scenario: Malformed or out-of-range bounding box is rejected (CAGH-PA-04-c)

- GIVEN the extractor returns coordinates outside the 0..1 range or a malformed shape
- WHEN the extraction response is parsed
- THEN `BoundingBox = null`
- AND no exception is thrown

#### Scenario: Bounding-box reliability is measured independently of rendering (CAGH-PA-04-d)

- GIVEN a batch of extraction runs against real client feeds
- WHEN `BoundingBox` acceptance/rejection outcomes are recorded (WU-6b measurement)
- THEN the reliability measurement is recorded and available before any brand-mark render change
  (`content-generation` `CAGH-CG-05`/`CAGH-CG-06`) depends on it

**Verdict recorded (non-normative)**: this measurement was NOT executed offline during this
change's `sdd-apply` session — no live vision-model access and no owner-supplied ground-truth
reference images were available. See `openspec/changes/client-aesthetic-generation-hardening/tasks.md`,
"Bounding-Box Reliability Verdict (WU-6b)", and ADR 0014's "WU-6b Bounding-Box Reliability Verdict"
section, for the full rationale and the required owner-run follow-up. WU-7b/WU-7c shipped anyway on
the strength of the defensive-parsing guarantee (CAGH-PA-04-b/c) plus `IBrandMarkAssetExtractor`'s
own never-throws/degrade-to-chip contract — a poor bounding box degrades to a failed/undersized crop,
which degrades to the pre-existing chip, never to a job failure.

---

## Modified Requirements (client-aesthetic-generation-hardening)

### Requirement: FFS-PA-02 — BrandMark Persisted as Additive Nullable Field, No EF Migration

`ClientAestheticProfile` MUST carry a nullable `BrandMark` field populated from the extractor
output, persisted additively as a JSON key inside the existing `aesthetic_profile_json` blob column
via `AestheticProfilePersistence`. No EF Core migration is required or permitted. Pre-change blobs
MUST deserialize with `BrandMark = null` and no exception. `BrandMark` now additionally carries
`MarkType`, `Text`, `SourceImagePath`, and `BoundingBox` (`CAGH-PA-01`..`CAGH-PA-04`), each
independently additive-nullable and defensively parsed; a blob persisted before this change, or by
an extractor version that did not populate these four fields, MUST deserialize with all four as
null and MUST NOT throw.
(Previously: `BrandMark` carried only `Present`, `PositionHint`, and `ColorHex`.)

#### Scenario: Blob with only the pre-hardening BrandMark shape deserializes the four new fields as null (FFS-PA-02-c, new)

- GIVEN a stored blob with a `brandMark` object carrying only `present`/`positionHint`/`colorHex`
  (persisted by the version before this change)
- WHEN deserialized into `ClientAestheticProfile`
- THEN `MarkType`, `Text`, `SourceImagePath`, and `BoundingBox` are all null
- AND `Present`, `PositionHint`, and `ColorHex` deserialize exactly as before
- AND no exception is thrown

The pre-existing FFS-PA-02-a/b scenarios (round-trip preservation, pre-change blob reads as null)
are unchanged regression guards and remain in force.

## Removed Requirements (client-aesthetic-generation-hardening)

None.

*Merged from `openspec/changes/client-aesthetic-generation-hardening/specs/profile-analysis/spec-delta.md` — change applied 2026-08-25.*

*Merged from `openspec/changes/client-aesthetic-fidelity-fixes/specs/profile-analysis/spec-delta.md` — change applied 2026-08-24.*

---

<!-- Merged from openspec/changes/client-aesthetic-visual-sophistication/specs/profile-analysis/spec-delta.md — change applied 2026-08-26 (WU-0..WU-6 only; WU-7/WU-8 cut, see note below) -->

## Added Requirements: DecorationMotifs (Divider Rule, Composition Archetype, Ornament Detection) — client-aesthetic-visual-sophistication

### Requirement: CAVS-PA-01 — DecorationMotifs Is One Additive-Nullable Record With Two Members

`AestheticExtraction` and `ClientAestheticProfile` MUST each gain exactly one additive-nullable
field, `DecorationMotifs? DecorationMotifs`, carrying exactly two members: `DividerRuleStyle?
DividerRule` and `string? CompositionArchetype`. No `AccentShape` field or type MUST exist anywhere
in the codebase. A profile persisted before this change MUST deserialize with `DecorationMotifs =
null` and throw nothing.

#### Scenario: Cold-start client has no decoration signal (CAVS-PA-01-a, regression/cold-start)

- GIVEN a client whose extraction produced no divider or archetype signal
- WHEN the extraction response is mapped
- THEN `DecorationMotifs = null`
- AND no exception is thrown

#### Scenario: Pre-change profile deserializes with DecorationMotifs null (CAVS-PA-01-b, regression)

- GIVEN a profile blob persisted before this change (no `decorationMotifs` key)
- WHEN deserialized into `ClientAestheticProfile`
- THEN `DecorationMotifs = null`
- AND no exception is thrown; all pre-existing fields deserialize intact

#### Scenario: Both members populate independently (CAVS-PA-01-c)

- GIVEN the extractor reports a divider style and a composition archetype for the same client
- WHEN the extraction response is mapped
- THEN `DecorationMotifs.DividerRule` and `DecorationMotifs.CompositionArchetype` both carry their
  respective values
- AND no `AccentShape` member or value is present anywhere in the mapped record

### Requirement: CAVS-PA-02 — CompositionArchetype Is an Open Token, Preserved Verbatim When Unrecognised

`CompositionArchetype` MUST be a defensively-normalised `string?` (trim + lowercase; null/blank ⇒
`null`), never a closed enum. Unlike `BrandMarkDetection.NormalizeMarkType`, which collapses an
unrecognised token to `"unknown"`, `CompositionArchetype` normalisation MUST preserve an
unrecognised token verbatim (post trim/lowercase) rather than discarding or collapsing it, so a
future recognised archetype requires no schema or data migration.

#### Scenario: Recognised archetype token is captured (CAVS-PA-02-a)

- GIVEN the extractor reports a composition archetype token that matches a recognised value in
  `CompositionArchetypes`
- WHEN the extraction response is mapped
- THEN `CompositionArchetype` equals that recognised token

#### Scenario: Unrecognised archetype token round-trips verbatim (CAVS-PA-02-b)

- GIVEN the extractor reports a novel token not present in `CompositionArchetypes` (e.g.
  `"diagonal-split"`)
- WHEN the extraction response is mapped, persisted, and read back through
  `ClientAestheticProfile`
- THEN `CompositionArchetype` equals the normalised novel token verbatim (trimmed, lowercased) at
  every stage
- AND the token is NOT collapsed to `"unknown"` or any other placeholder
- AND no exception is thrown

#### Scenario: Null or blank archetype normalises to null (CAVS-PA-02-c)

- GIVEN the extractor returns a null or blank/whitespace-only archetype value
- WHEN the extraction response is mapped
- THEN `CompositionArchetype = null`

### Requirement: CAVS-PA-03 — DividerRuleStyle Is a Closed Enum, Unrecognised Maps to Null

`DividerRuleStyle` MUST remain a closed enum with exactly three values (`Line`,
`LineWithDiamond`, `LineWithDot`), because its values are bounded by the CSS render primitive, not
by clients. An unrecognised, missing, or malformed divider-style token MUST map to `DividerRule =
null`, never throw.

#### Scenario: Recognised divider style is captured (CAVS-PA-03-a)

- GIVEN the extractor reports a divider style matching one of the three enum values
- WHEN the extraction response is mapped
- THEN `DecorationMotifs.DividerRule` equals that enum value

#### Scenario: Unrecognised divider token maps to null (CAVS-PA-03-b)

- GIVEN the extractor returns a divider-style token that does not match any of the three values
- WHEN the extraction response is mapped
- THEN `DecorationMotifs.DividerRule = null`
- AND no exception is thrown

### Requirement: CAVS-PA-04 — DecorativeOrnamentDetection Nests Source Path and Validated Bounding Box

`DecorationMotifs` MUST gain a nested `DecorativeOrnamentDetection?` member carrying a source image
path (resolved from the extractor's own index→`StoragePath` resolution, mirroring `CAGH-PA-03`) and
a `BrandMarkBoundingBox`, validated by the existing `TryValidateBoundingBox` / `MinimumNormalizedArea`
guard. A malformed, out-of-range, or below-minimum-area box MUST result in a null bounding box, not
an exception. The bounding-box reliability outcome across a batch of real extraction runs MUST be
recorded before any ornament render work depends on it.

#### Scenario: Valid ornament bounding box is retained (CAVS-PA-04-a)

- GIVEN the extractor returns an ornament bounding box within 0..1 bounds and above the
  minimum-area guard, with a resolvable source index
- WHEN the extraction response is parsed
- THEN `DecorativeOrnamentDetection.BoundingBox` carries the normalised coordinates
- AND `SourceImagePath` carries the resolved storage path

#### Scenario: Below-minimum-area or malformed ornament box is rejected (CAVS-PA-04-b)

- GIVEN the extractor returns an ornament bounding box below the minimum-area guard, out of 0..1
  range, or malformed
- WHEN the extraction response is parsed
- THEN `DecorativeOrnamentDetection.BoundingBox = null`
- AND no exception is thrown

#### Scenario: Ornament bounding-box reliability is measured before render work depends on it (CAVS-PA-04-c) — GATE RECORDED, NOT PASSED

- GIVEN a batch of extraction runs against real client feeds
- WHEN ornament `BoundingBox` acceptance/rejection outcomes are recorded
- THEN the reliability measurement is recorded and available before any ornament render
  requirement (`content-generation` ornament-render scenarios) depends on it
- AND if the measurement is unusable, the decision to cut ornament rendering is recorded rather
  than silently absorbed

**Verdict recorded (non-normative)**: this scenario's gate ran during `sdd-apply` (task 6.4) and
did **NOT pass** — a live production-data query found zero brand-mark rows (the same shared
extraction mechanism) carrying a `boundingBox` value; the only post-schema-v6 mark observed
classified as `markType: "text"`, which is contractually bounding-box-ineligible. Per this
scenario's own stated fallback, the ornament crop/render requirements downstream in
`content-generation` (`CAVS-CG-04`, `CAVS-CG-05`) and `content-studio` (`CAVS-CS-01`, `CAVS-CS-02`)
are **NOT implemented** in this delivery; only the detection schema and defensive parsing above
(`CAVS-PA-04-a/b`) ship. Full verdict, queries, and re-entry condition:
`openspec/changes/client-aesthetic-visual-sophistication/wu6-reliability-gate.md`.

### Requirement: CAVS-PA-05 — Schema-Fatigue Norm Is Binding for Further Additive Fields

No further additive field MAY be added to `AestheticExtraction` or `ClientAestheticProfile` without
either (a) a consolidation pass grouping accumulated signals into a container, or (b) an ADR
recorded before the field is written. `DecorationMotifs` itself complies by nesting
`DecorativeOrnamentDetection` inside it rather than adding it as a separate top-level field, and by
cutting `AccentShape` rather than absorbing it as a third member. See
[`docs/architecture/adr/0015-schema-fatigue-norm.md`](../../../docs/architecture/adr/0015-schema-fatigue-norm.md)
for the recorded norm.

#### Scenario: A field added without consolidation or an ADR is non-compliant (CAVS-PA-05-a)

- GIVEN a future change proposes a new additive field directly on `AestheticExtraction` or
  `ClientAestheticProfile`
- WHEN that change is reviewed against this norm
- THEN it MUST cite either a consolidation pass or a recorded ADR
- AND absent either, the change is non-compliant

## Modified Requirements (client-aesthetic-visual-sophistication)

None. `DecorationMotifs` is fully additive; no existing `profile-analysis` requirement changes
behavior.

## Removed Requirements (client-aesthetic-visual-sophistication)

None.

## Implementation Boundary Notes (non-normative)

- `DecorationMotifs`, `DividerRuleStyle`, `CompositionArchetypes`, and
  `DecorativeOrnamentDetection` live in `Features/Social/ContentGeneration/Shared/` as Application
  value objects.
- Extractor prompt/schema/`Map()` changes and index→`StoragePath` resolution live in
  `MultimodalAestheticStyleExtractor.cs`, reusing the same resolution mechanism `CAGH-PA-03`
  established for brand marks.
- Ornament crop derivation (the Application port + SkiaSharp adapter that would CONSUME
  `SourceImagePath`/`BoundingBox`) was scoped to the `content-generation` delta's render-time
  requirements, but was **cut** along with those requirements — see `CAVS-PA-04-c` above.

*Merged from `openspec/changes/client-aesthetic-visual-sophistication/specs/profile-analysis/spec-delta.md` — change applied 2026-08-26. WU-0 through WU-6 shipped (schema, extraction, persistence); WU-7/WU-8 (ornament crop/render) cut — reliability gate not passed, see `wu6-reliability-gate.md`.*

---

## Client Style Fidelity Requirements

### Requirement: CSF-PA-01 — Catalog-Constrained Typography Extraction

The aesthetic extractor (`aesthetic-extractor/v9`) MUST ask the model to name each typography role
with one family from the bundled font catalog, plus up to four ranked catalog candidates, the
role's width class, the exact sample text and the location of its clearest occurrence. The heading
role also reports `blockWidthRatio`. `sizeRatio` is baseline-to-baseline distance over slide height.

### Requirement: CSF-PA-02 — Visual Font Match

When a role has a locatable sample and at least two candidates, the extractor MUST render the
sample text in each candidate face and ask the multimodal route which candidate matches the cropped
sample. The chosen candidate replaces the role's family. Any failure keeps the extractor's family
and never fails the extraction.

### Requirement: CSF-PA-03 — Observed Text Colour Combinations

The extractor MUST capture the text colour combinations the client actually uses (background kind,
background, text, optional highlight, slide count) and persist them as `ColorPairing` entries with
`SlideCount` and `Kind`.

### Requirement: CSF-PA-04 — Pixel-Grounded Pairings

A flat pairing MUST be kept only when its background colour covers at least 30 % of at least one
reference image. This applies to observed and derived pairings alike.

#### Scenario: invented accent background is dropped (CSF-PA-04-a)

- GIVEN reference images that are near-black flat slides and photos, never lime backgrounds
- AND a derived pairing with background `#A6CE3C`
- WHEN pairings are grounded
- THEN the lime-background pairing is removed

### Requirement: CSF-PA-05 — Divider Rule Requires Recurrence

`decorationMotifs.dividerRule` MUST be reported only when the rule appears on at least a third of
the client's text slides.

*Merged from `openspec/changes/client-style-fidelity/specs/profile-analysis/spec-delta.md` — change applied 2026-10-10.*

---

## Client Style Fidelity — Follow-up (2026-10-11)

### Requirement: CSF-PA-06 — A Subject on a Solid Backdrop Is an Image Slide

The extractor (`aesthetic-extractor/v10`) MUST classify a slide as `photo` when it carries any
photographic or rendered subject (product, object, person, cut-out), even on a solid backdrop;
`flat` is reserved for slides with no photographic element. Each per-slide entry reports
`photographicSubject`, and a `FlatColor` slide flagged with a subject MUST be promoted to `Photo`,
with the image-type ratio recomputed from the corrected pattern.

#### Scenario: product on seamless black (CSF-PA-06-a)

- GIVEN a slide showing a coffee bag on a seamless black backdrop
- WHEN the profile is extracted
- THEN that slide is `Photo` and receives a generated image

### Requirement: CSF-PA-07 — Font Comparison Is Measured, Bounded and Non-Destructive

The visual font comparison MUST: locate an unlocated sample on its own slide first; rank catalog
candidates of the same width and class by per-line condensation measured from pixels (ink width over
x-height, isolating the text block from photo highlights); take a clear winner directly and ask the
vision model only to break a close call among the closest three; compare roles sequentially,
heading first, within its own time slice; and never discard a completed extraction when it runs out
of time. A sample box in pixels, with alias keys, or spilling past an edge MUST be normalised;
an unusable box falls back to the whole slide.

### Requirement: CSF-PA-08 — Last Real Aesthetic Survives a Failed Extraction

When the latest analyzed profile has no aesthetic (the extraction failed, which is non-fatal), the
profile reader MUST use the most recent analysis that has one.
