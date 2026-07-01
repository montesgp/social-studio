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

## MODIFIED Requirements

None. All requirements above are additive. Pre-existing requirements
`PA-SEAM-01`–`PA-SEAM-03`, `CAG-PA-01`–`CAG-PA-06` remain valid and unchanged.
The `IClientAestheticStyleExtractor` input change (CACD-PA-03) replaces the loose
caption block with ordered pairs; the signature shape is a design-level decision
(OQ-3, ADR 0009) and does not alter any previously specified behavioral guarantee
for the output contract.

## REMOVED Requirements

None.

*Merged from `openspec/changes/client-aesthetic-conceptual-detection/specs/profile-analysis/spec-delta.md` — change applied 2026-07-01.*
