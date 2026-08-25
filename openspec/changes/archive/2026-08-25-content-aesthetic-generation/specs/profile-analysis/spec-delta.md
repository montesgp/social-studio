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
