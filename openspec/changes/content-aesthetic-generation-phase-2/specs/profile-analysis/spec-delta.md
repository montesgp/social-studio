# Spec Delta: Profile Analysis — Real Post Media Capture, Durable Persistence, Multimodal Style Extraction

<!-- Change: content-aesthetic-generation-phase-2 -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md (additive delta) -->

## Purpose

Phase 1 derived aesthetic signal from TEXT only. This delta adds the real visual + textual
substrate required to emulate a client:

1. **Capture** per-post image URL(s) and caption text (carousel children included) during the
   profile scrape — the current scrape persists metadata only.
2. **Persist durably** the captured images in object storage, because Apify presigned URLs
   expire.
3. **Multimodal style extraction** — analyze the durable images + captions with a multimodal
   model and produce a rich, **multi-signal** aesthetic payload where palette is OPTIONAL.

No blocking behavior is introduced; this runs inside the existing async profile-analysis pipeline.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAG2-PA-01 | Proposal §Approach 1 — capture media + captions |
| CAG2-PA-02 | Proposal §Approach 2 — durable image persistence |
| CAG2-PA-03 | Proposal §Approach 3 — multimodal style extraction |
| CAG2-PA-04 | Proposal §Goals — enriched profile is multi-signal, palette optional |
| CAG2-PA-05 | Proposal §Risks — actor field availability; best-effort degrade |
| CAG2-PA-06 | Proposal §Approach 3 — prompt-version bump |

---

## ADDED Requirements

### Requirement: CAG2-PA-01 — Profile Scrape Captures Per-Post Media URLs and Captions

The profile scrape mapping MUST capture, for each scraped post: the post's image URL(s)
(`displayUrl` and, for carousel/sidecar posts, each child's image URL via
`displayResources`/children), and the post `caption` text. These MUST be persisted on the
analysis (extending `ProfilePost` and the persisted scrape dataset). When the configured
actor does not return a field for a given post, that field is recorded as null/empty
(best-effort) and MUST NOT fail the scrape.

#### Scenario: Sidecar post captures all child image URLs (CAG2-PA-01-a)

- GIVEN a scraped post with `mediaType = Sidecar` and 3 children with image URLs
- WHEN the scrape mapping runs
- THEN the persisted `ProfilePost` carries 3 child image URLs
- AND the post's caption text is persisted

#### Scenario: Single-image post captures one image URL and caption (CAG2-PA-01-b)

- GIVEN a scraped single-image post with a `displayUrl` and a caption
- WHEN the scrape mapping runs
- THEN the persisted `ProfilePost` carries that one image URL and the caption

#### Scenario: Missing media field degrades best-effort, scrape does not fail (CAG2-PA-01-c)

- GIVEN the configured actor returns a post with no image URL field
- WHEN the scrape mapping runs
- THEN the post's image URL list is empty
- AND the scrape completes successfully (no exception, post still recorded)

---

### Requirement: CAG2-PA-02 — Captured Images Are Persisted Durably in Object Storage

Because Apify image URLs are short-lived, the pipeline MUST, on analysis completion,
download a bounded set of captured post images and re-upload them to object storage
(Supabase Storage) under an org/client/analysis-scoped path, persisting durable references
(storage path + content hash) on the analysis. The number of posts (N) and images per post
(M) is bounded by configuration. The pipeline MUST NOT persist raw Apify URLs as long-term
references.

#### Scenario: Captured images re-hosted and durable refs persisted (CAG2-PA-02-a)

- GIVEN a completed scrape with captured Apify image URLs for the N most recent posts
- WHEN durable persistence runs
- THEN each image is uploaded to object storage under the `(org, client, analysis)` path
- AND a durable reference (storage path + content hash) is persisted on the analysis
- AND no raw Apify URL is stored as a long-term reference

#### Scenario: Image download failure is best-effort, analysis still completes (CAG2-PA-02-b)

- GIVEN one captured image URL returns a transport error on download
- WHEN durable persistence runs
- THEN that image is skipped with a logged warning
- AND the remaining images are persisted
- AND the analysis completes (not failed by a single image failure)

#### Scenario: Persisted image count respects configured bounds (CAG2-PA-02-c)

- GIVEN a captured set exceeding the configured `N posts × M images` bound
- WHEN durable persistence runs
- THEN only up to the configured bound is downloaded and persisted

---

### Requirement: CAG2-PA-03 — Multimodal Style Extraction Produces a Multi-Signal Aesthetic Payload (Palette Optional)

When a multimodal AI route is configured for profile analysis, the analyzer MUST run a
multimodal step that sends the durable post images + captions to the model and produces an
enriched aesthetic payload carrying, each independently optional: a role-ordered colour
palette (hex), style/mood/composition descriptors, recurring visual motifs, and voice/tone.
Palette MUST NOT be treated as mandatory: when the client exhibits no marked palette, the
palette signal MAY be empty and this is a VALID result, not a failure or a degrade.

#### Scenario: Strong-palette client yields a populated palette plus descriptors (CAG2-PA-03-a)

- GIVEN durable images for a client with a consistent colour identity
- WHEN the multimodal extraction step runs
- THEN the payload carries a role-ordered hex palette
- AND it carries style/mood/composition descriptors and voice

#### Scenario: Weak/absent-palette client yields empty palette, descriptors still populated (CAG2-PA-03-b)

- GIVEN durable images for a client whose identity lives in varied backgrounds, not colour
- WHEN the multimodal extraction step runs
- THEN the payload palette MAY be empty
- AND the payload still carries style/composition descriptors and motifs
- AND the extraction is treated as SUCCESSFUL (not degraded, not failed)

#### Scenario: No multimodal route configured — extraction skipped, Phase 1 signal retained (CAG2-PA-03-c)

- GIVEN the `analyze-profile` route is a non-multimodal provider
- WHEN profile analysis runs
- THEN the multimodal extraction step is skipped with a logged notice
- AND the analysis still completes with the Phase 1 (text-descriptor) signal
- AND no exception is thrown

---

### Requirement: CAG2-PA-04 — Enriched Aesthetic Payload Persisted on the Analysis (Forward-Compatible)

The enriched multimodal payload (palette, descriptors, motifs, voice, durable image refs)
MUST be persisted on `ProfileAiAnalysis` such that the existing `ClientAestheticProfile`
reader can surface it. New fields MUST be additive and nullable so existing Phase 1 readers
and consumers continue to compile and behave unchanged when the payload is absent.

#### Scenario: Existing consumers unaffected when enriched payload is absent (CAG2-PA-04-a)

- GIVEN an older analysis with no enriched payload
- WHEN the `ClientAestheticProfile` reader loads it
- THEN it returns the Phase 1 shape with the new fields null/empty
- AND no consumer requires modification to read it

---

### Requirement: CAG2-PA-05 — Actor Field Availability Is Confirmed Before Build (Spike Gate)

The implementation MUST confirm, via the chosen Apify actor, that per-post `displayUrl`/
`displayResources` and `caption` are actually returned in profile-scrape mode before the
capture mapping is built. If the chosen actor cannot return media/captions, the change MUST
record the actor decision (switch actor or descope) rather than silently shipping a mapping
that captures nothing.

#### Scenario: Spike confirms fields before capture mapping ships (CAG2-PA-05-a)

- GIVEN the actor-selection spike
- WHEN the chosen actor is run against a representative profile
- THEN the presence of `displayUrl`/`displayResources` and `caption` is confirmed and recorded
- AND the capture mapping is built against the confirmed field shape

---

### Requirement: CAG2-PA-06 — Profile Analysis Prompt Version Bumped for Multimodal Step

When the multimodal extraction step changes the profile-analysis prompt/schema, its
prompt-version key MUST be incremented so cached outputs are invalidated.

#### Scenario: Prompt version incremented for multimodal analysis (CAG2-PA-06-a)

- GIVEN the multimodal extraction step is added
- WHEN the profile-analysis prompt versions are inspected
- THEN the affected prompt-version key is higher than its pre-change value

---

## MODIFIED Requirements

None superseded. Phase 1 server-derived `AverageSlidesPerCarousel` and text descriptors
remain valid and coexist with the enriched payload.

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- CAG2-PA-01/02: mapping + durable-storage adapter live in Infrastructure; `ProfilePost`
  image/caption fields live in Domain (no Apify types leak inward).
- CAG2-PA-03/04: the enriched payload is an Application/Domain shape; the multimodal call
  goes through the existing `IAiCompletionGateway` multimodal path (Claude). No provider
  types in Domain/Application.
- CAG2-PA-02 bounds (N posts, M images, thumbnail vs full-res) are configuration; defaults
  decided in design per OQ-2/OQ-3.
- Architecture gate enforces no EF/Npgsql/Apify types in Domain or Application.
