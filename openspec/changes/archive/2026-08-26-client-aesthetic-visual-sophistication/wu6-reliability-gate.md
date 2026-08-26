# WU-6 Reliability Gate — Ornament Bounding-Box Extraction (CAVS-PA-04-c)

Status: **GATE NOT PASSED — WU-7/WU-8 cut from this apply batch.**

## What this gate is for

Task 6.4 requires a written verdict on whether the vision model's bounding-box extraction
(brand-mark icon crop region today; the same mechanism reused for the ornament crop region in
this WU) is reliable enough to build the crop-port (WU-7) and render/degrade (WU-8) work on top
of. WU-7/WU-8 are explicitly gated on this verdict per `tasks.md` Phase 6/7/8.

## What was checked (per the apply prompt's explicit instruction)

Before concluding "not measurable" from this offline apply context, the local dev Postgres was
queried for real production `social.profile_analyses.aesthetic_profile_json` rows that might
already carry brand-mark bounding-box data from the already-shipped
`client-aesthetic-generation-hardening` change (WU-6a/WU-6b, which introduced the SAME
`sourceImageIndex`/`boundingBox` extraction mechanism this WU reuses for the ornament).

```
docker exec incoders-hive-dev-postgres psql -U incoders -d incoders_hive_dev -c \
  "select id, aesthetic_profile_json::jsonb -> 'brandMark' as brand_mark,
          aesthetic_profile_json::jsonb ->> 'promptVersion' as prompt_version
   from social.profile_analyses
   where aesthetic_profile_json is not null
     and (aesthetic_profile_json::jsonb -> 'brandMark') is not null;"
```

Result: **14 rows** with a non-null `brandMark` object.

- 6 rows are `aesthetic-extractor/v3`/`v4` (pre-WU-6a) — no `markType`/`boundingBox` key exists in
  that schema version at all; not applicable evidence.
- 1 row is `aesthetic-extractor/v6` (post-WU-6b, the version that introduced `boundingBox`) — but
  its `markType` is `"text"` (a signature/wordmark: *"Dra. Lucia Fernandez"*), and `SourceImagePath`/
  `BoundingBox` are contractually null for text marks (`MapBrandMark`'s icon-only guard,
  `CAGH-PA-02-b`). This row carries no bounding-box data by design, not by omission.
- A direct query for any row where `brandMark -> boundingBox` is non-null returned **0 rows**.

```
select id, aesthetic_profile_json::jsonb -> 'brandMark' -> 'boundingBox' as bbox
from social.profile_analyses
where (aesthetic_profile_json::jsonb -> 'brandMark' -> 'boundingBox') is not null;
-- (0 rows)
```

## Verdict

**No real bounding-box extraction exists in this environment's production data — for brand marks
or (necessarily, since it ships in this same batch) for ornaments.** Despite `v6`
(`CAGH-PA-04`, the bounding-box feature) having been live in the schema for the analyses observed
here, not a single analyzed client in this dataset has actually triggered the icon-mark +
bounding-box path yet: the only post-v6 analysis observed classified its recurring mark as a text
signature, which never exercises the bounding-box code path at all.

This is **not fabricated evidence** — it is the honest absence of evidence. It means:

1. The extraction code path (DTO parse → `TryValidateBoundingBox` → mapping) is verified correct
   by the 11-case defensive-parsing unit-test suite (this WU) and its WU-6b sibling, but the
   **vision model's actual real-world localisation accuracy** — whether the returned `x/y/width/
   height` values actually bound the icon/ornament tightly and correctly on a real image — has
   **never been measured against ground truth in this offline apply context**, and this database
   query does not change that: there is no real bounding box in the dataset to measure at all.
2. The proposal's own prerequisite for 6.4 ("run extraction against at least two real client feeds
   with archived reference images") requires an ONLINE run against a configured vision-capable AI
   route (OpenAI/Anthropic BYOK) and manual visual comparison against the source image — neither is
   available in this offline apply session (no configured `ai-aesthetic` route was exercised; no
   human reviewer compared a crop to its source image).

## Decision

Per `tasks.md` 6.4's own stated fallback ("If the measurement is unusable, record the decision to
cut WU-7/WU-8 explicitly and stop this track here"):

**WU-7 (ornament crop port + adapter) and WU-8 (ornament render + degrade + badge) are CUT from
this apply batch.** The ornament detection SCHEMA (WU-1, already shipped) and EXTRACTION (this
WU-6) remain shipped — they are inert additive-nullable fields that no renderer yet consumes, so
there is no user-visible risk in landing them. Building the crop/render/degrade pipeline on top of
an unmeasured localisation signal risks shipping mis-cropped or overlapping ornament images with
no regression guard, which is exactly the failure mode the gate exists to prevent (mirroring the
prior change's WU-6b precedent for brand-mark crops).

## Re-entry condition

Re-run this gate once either:
- A real client with a genuinely recurring icon-type brand mark (or, once this WU's ornament
  extraction has actually run online, a recurring ornament) is analyzed through a live
  `ai-aesthetic` route and a human reviewer visually confirms the returned bounding box actually
  bounds the element on the source image for at least 2 real client feeds, OR
- The org configures/runs the aesthetic extractor online against archived reference images in a
  session where a human reviewer can compare crops to source images directly.

## Files

This verdict document. No code outside the extraction/schema layer (Map(), DTOs, prompt, schema
version) was touched by WU-6; WU-7/WU-8 tasks remain `[ ]` in `tasks.md`, annotated as cut pending
the re-entry condition above.
