# Spec Delta: Content Generation — Read Contract for Content Studio Materialization

<!-- Change: content-studio (Phase 1) -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Specify the **read-only contract** that Content Studio relies on to materialize editable `ContentItem`s from a completed generation run. This delta does NOT change generation behavior; it pins the fields a per-format result MUST expose for read so the materializer can initialize a draft deterministically. The generation pipeline already persists all of these (durable per-format assets; persisted designer markup per PPTX-01; publication description in the manifest).

## PRD Traceability

| Requirement ID | Anchor |
|---|---|
| CG-READ-01 | Enables `content-studio` CS-04 (materialization) on existing `content-generation` output |
| CG-READ-02 | Preserves `content-generation` immutability (CP-05 / ADR 0002) under Content Studio consumption |

---

## ADDED Requirements

---

### Requirement: CG-READ-01 — Per-Format Result Exposes a Stable Read Shape for Materialization

A completed, successful per-format result MUST be readable (through an existing repository/read port, without re-composition or re-render) with at least: the `FormatKey`, the rendered asset reference (storage URI + MIME), the persisted guardrail-validated designer markup when the format produced one, and the publication description when present. This read shape MUST be available for every successfully rendered format of a completed run.

This requirement does not add a new write path; it asserts that the already-persisted per-format data is retrievable as a coherent read contract for downstream consumers.

#### Scenario: A completed run's formats are readable for materialization (CG-READ-01-a)

- GIVEN a completed generation run that successfully produced `story`, `carousel`, and `reel-script`
- WHEN the per-format results are read for materialization
- THEN each result exposes its `FormatKey`, rendered asset URI + MIME, and (for visual formats) its persisted designer markup
- AND no AI composition or Chromium render is triggered by the read

#### Scenario: A failed format is not exposed as a materializable result (CG-READ-01-b)

- GIVEN a run where the `story` format failed before a rendered asset was persisted
- WHEN the per-format results are read for materialization
- THEN the failed `story` format is not returned as a successful materializable result
- AND only successfully rendered formats are available to materialize

---

### Requirement: CG-READ-02 — Reading for Materialization Is Non-Mutating

Reading a generation run's results for Content Studio materialization MUST NOT modify any `social.generated_packages`, content-generation job, format-result, or format-asset row, and MUST NOT change download/Canva action counters.

#### Scenario: Materialization read leaves generation state intact (CG-READ-02-a)

- GIVEN a completed run with a generated package and persisted format results
- WHEN Content Studio reads the run to materialize items
- THEN no generation row is updated
- AND the package download count, Canva send count, and last-action timestamps are unchanged

---

## MODIFIED Requirements

None. This delta is additive; no existing `openspec/specs/content-generation/spec.md` requirement is modified.

## REMOVED Requirements

None.
