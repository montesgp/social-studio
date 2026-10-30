# Archive Report: Client-Aesthetic Visual Sophistication

**Change ID**: `client-aesthetic-visual-sophistication`  
**Archive Date**: 2026-08-26  
**Delivery Status**: **PARTIAL** — Deliberate scope cut (WU-7/WU-8 gated, gate not passed)  
**Tracking Issue**: GitHub issue #82 (`Incoders-Tools/social-studio`) — remains OPEN

---

## Final-State Summary

This SDD change delivered 8 of 9 planned work units. **WU-7 and WU-8 were deliberately cut** (not partially started) because WU-6's reliability gate did not pass. The gate measured the vision model's bounding-box extraction accuracy against real production data and found zero real bounding-box measurements to evaluate.

### What Shipped (WU-0 through WU-6, WU-9)

#### Schema & Extraction (WU-0, WU-1, WU-1b, WU-2, WU-6)
- `DecorationMotifs` value object: `DividerRuleStyle?` (closed enum) + `CompositionArchetype?` (open normalised string) + nested `DecorativeOrnamentDetection`
- `DividerRuleStyle` enum: `Line`, `LineWithDiamond`, `LineWithDot`
- `CompositionArchetypes` static class with normalisers and token recognition
- Multimodal aesthetic extractor prompt, DTO, and `Map()` wiring for decoration motifs + ornament detection
- Extractor schema version bumped: `aesthetic-extractor/v7` (WU-2) → `aesthetic-extractor/v8` (WU-6)
- Ornament detection schema: `sourceImageIndex` → `StoragePath` resolution + bounding-box validation (reusing `BrandMarkBoundingBox` / `TryValidateBoundingBox` / `MinimumNormalizedArea` verbatim)
- Port rename: `IBrandMarkAssetExtractor` → `IAestheticAssetCropExtractor` (WU-1b), reflecting element-agnostic application
- Extraction persisted as inert additive-nullable fields; no renderer consuming the ornament crop path yet
- All pre-change profiles deserialise with `DecorationMotifs = null` without exception

#### Persistence (WU-3)
- `AestheticProfilePersistence.Read` tuple gains 15th trailing-nullable element (`DecorationMotifs`)
- `ClientAestheticProfile` and `ClientAestheticDesignContext` gain trailing-nullable `DecorationMotifs` passthrough
- Stored aesthetic JSON round-trips decoration motifs verbatim, including unrecognised archetype tokens
- No EF Core migration required; profile is JSON-serialised
- All existing fields deserialise intact from pre-change blobs

#### Deterministic Rendering (WU-4, WU-5)
- Divider rule primitive: `data-slot="divider"` element rendering rule + glyph (diamond/dot) in resolved accent or text colour
- Composition archetype geometry presets: token-resolved layout for accent, divider, title, subtitle, cta
- Identity preset `"left-aligned"` reproduces hardcoded literals exactly; unrecognised tokens fallback to identity
- Archetype extensibility: adding a fourth archetype is a code-only change (new `const string` + new preset branch)
- Byte-identity cold-start guarantee: `DecorationMotifs = null` renders markup byte-identical to pre-change output
- Accent pill unaffected in all cases; no `AccentShape` type added anywhere

#### Spec & Documentation Sync (WU-9)
- `profile-analysis` spec merged: CAVS-PA-01..05 (schema, extraction, ornament detection schema + reliability gate verdict recorded inline at CAVS-PA-04-c)
- `content-generation` spec merged: CAVS-CG-01..03 (divider render, archetype presets, byte-identity guarantee) shipped; CAVS-CG-04/05 explicitly marked "Not Implemented in This Delivery"
- `content-studio` spec: no new shipped requirements (CAVS-CS-01/02 remain unmerged spec-delta text)
- Binding schema-fatigue norm recorded: new ADR 0015 (`docs/architecture/adr/0015-schema-fatigue-norm.md`) establishes that no further additive field on `AestheticExtraction` / `ClientAestheticProfile` may land without either a consolidation pass or an ADR recorded first
- `profile-analysis` spec normative text updated to reference ADR 0015; one-line XML-doc pointer added to `AestheticExtraction` and `ClientAestheticProfile`
- Pre-existing PRD traceability maintained; §11 amendment unnecessary (ornament is reuse of existing `IGeneratedAssetStorage` / reference-image archival pattern from prior change)

#### Testing & Verification
- Test suite: **3,912 passed, 0 failed, 2 pre-existing unrelated skips**
- Byte-identity golden regression contract verified: flat-carousel pre-change golden remains green
- Divider + archetype render primitives assert on actual markup, not visual inspection
- Cold-start parity asserted for `DecorationMotifs = null`
- Extractor defensive-parsing suite (11-case ornament scenarios + precedent brand-mark scenarios) all green
- Persistence round-trip tests confirm unrecognised archetype token preservation
- All pre-existing tests continue to pass unchanged (regression discipline)

### What Was Cut (WU-7, WU-8)

**Ornament crop port + render + degrade badge** — deliberately cut, not incomplete.

**Gate verdict** (task 6.4 / `wu6-reliability-gate.md`): The vision model's bounding-box extraction mechanism was measured against production data. Query of `social.profile_analyses.aesthetic_profile_json` found 14 rows with a non-null `brandMark` object; **zero carry a `boundingBox` value**. The one post-v6 row observed had `markType: "text"` (ineligible for bounding-box cropping by design). No real bounding-box measurement exists to evaluate.

Per task 6.4's stated fallback clause: "If the measurement is unusable, record the decision to cut WU-7/WU-8 explicitly and stop this track here."

**Result**:
- WU-7 (ornament crop port extension) — CUT
- WU-8 (ornament render + `SlideDegradeReason.OrnamentUnavailable` token + precedence + Content Studio badge label map) — CUT
- Related ADR (ornament crop-port + degrade contract decision) — NOT CREATED (dropped with WU-7/8)

**Consequence**: 
- `DecorationMotifs.Ornament` (source path + validated bounding box) ships as inert nullable field, unread by any renderer
- No `data-slot="ornament"` element renders in any markup
- No `ornament-unavailable` degrade token exists in the codebase
- `CAVS-CG-04` / `CAVS-CG-05` and `CAVS-CS-01` / `CAVS-CS-02` requirements remain as unmerged spec-delta text in the change folder for future reference

**Re-entry condition** (from `wu6-reliability-gate.md`): Re-attempt WU-7/WU-8 once either:
- A real client with a genuinely recurring icon-type brand mark (or ornament, once extraction has run online) is analysed through a live `ai-aesthetic` route and a human reviewer visually confirms the returned bounding box bounds the element on the source image for at least 2 real client feeds, OR
- The org configures and runs the aesthetic extractor online against archived reference images with a human reviewer comparing crops to source images directly.

### Human Task Pending (Task 9.5)

**Owner review checkpoint**: One real generation reviewed for each of two clients with materially different signalled archetypes, accept/reject recorded in writing. This is **an explicit human/owner action**, not an implementation task; `sdd-apply` correctly left this unchecked. It remains open in GitHub issue #82 until the owner performs and records the review.

---

## Specification Integration

### Profile-Analysis (`openspec/specs/profile-analysis/spec.md`)

**Merged**: CAVS-PA-01 through CAVS-PA-05

Lines 1160–1323 document merged requirements:
- **CAVS-PA-01**: `DecorationMotifs` as one record with two members (divider + archetype)
- **CAVS-PA-02**: `CompositionArchetype` as open normalised string, preserved verbatim
- **CAVS-PA-03**: `DividerRuleStyle` closed enum, unrecognised ⇒ null
- **CAVS-PA-04**: Nested `DecorativeOrnamentDetection` with source path + validated bounding box; gate verdict recorded at scenario CAVS-PA-04-c: "GATE RECORDED, NOT PASSED"
- **CAVS-PA-05**: Schema-fatigue norm binding for further additive fields; references ADR 0015

Merge metadata: `*Merged from openspec/changes/client-aesthetic-visual-sophistication/specs/profile-analysis/spec-delta.md — change applied 2026-08-26. WU-0 through WU-6 shipped (schema, extraction, persistence); WU-7/WU-8 (ornament crop/render) cut — reliability gate not passed, see wu6-reliability-gate.md.*`

### Content-Generation (`openspec/specs/content-generation/spec.md`)

**Merged (shipped)**: CAVS-CG-01 through CAVS-CG-03 (lines 2247–2364)

- **CAVS-CG-01**: Divider rule renders with resolved accent or text colour
- **CAVS-CG-02**: Composition archetype drives geometry presets
- **CAVS-CG-03**: `DecorationMotifs = null` renders byte-identical to pre-change output

**Not Implemented (lines 2339–2352)**: CAVS-CG-04 and CAVS-CG-05 explicitly documented as unmerged

Text: `## Not Implemented in This Delivery: Ornament Crop Render (CAVS-CG-04) and Ornament Degrade Precedence (CAVS-CG-05)`

> Both were **gated** on `profile-analysis` `CAVS-PA-04-c`'s reliability measurement, which did **not pass** during `sdd-apply`. Per the change's own stated fallback, WU-7 (crop port extension) and WU-8 (render + degrade + badge) were **cut** from this delivery. No `data-slot="ornament"` element, `SlideDegradeReason.OrnamentUnavailable` token, or ornament-related `ResolveDegradeReason` precedence logic exists in the codebase as of this merge. Full requirement text for `CAVS-CG-04`/`CAVS-CG-05` remains available, unmerged, in `openspec/changes/client-aesthetic-visual-sophistication/specs/content-generation/spec-delta.md` for reference if/when the reliability gate is re-attempted and passes.

Merge metadata: `*Merged from openspec/changes/client-aesthetic-visual-sophistication/specs/content-generation/spec-delta.md — change applied 2026-08-26. CAVS-CG-01/02/03 shipped (WU-4/WU-5); CAVS-CG-04/05 NOT merged as shipped — gated on WU-6.4's reliability measurement, which did not pass; see wu6-reliability-gate.md.*`

### Content-Studio (`openspec/specs/content-studio/spec.md`)

**Not Merged**: CAVS-CS-01, CAVS-CS-02, and CAFF-CS-01 (modification) remain in the unmerged delta

No new capability shipped in content-studio for this delivery (all related work was in WU-8, which is cut).

---

## Commits & Verification

### Commits on `dev` (per final-state facts)

1. `bc4cb7a` — WU-0 (golden capture, pre-change)
2. `daed43b` — WU-1 (`DecorationMotifs` value objects)
3. `cb7a657` — WU-1b (port rename: `IBrandMarkAssetExtractor` → `IAestheticAssetCropExtractor`)
4. `7fd28ca` — WU-2 (extraction prompt/DTO/Map)
5. `11bb2b0` — docs sync
6. `3b99c6e` — WU-3 (persistence round-trip)
7. `d732c01` — WU-4 (divider render + golden guard)
8. `f94153a` — WU-5 (archetype geometry presets)
9. `31a6329` — WU-6 (ornament detection schema + reliability gate verdict)
10. `b571fc7` — WU-9 (spec merge + ADRs, including ADR 0015)

### Verification Evidence

- **Test suite**: 3,912 passed, 0 failed, 2 pre-existing unrelated skips
- **Golden regression contract**: byte-identity on flat-carousel pre-change golden — GREEN
- **Spec merge verification**: delta specs confirmed merged with "Not Implemented" sections properly documented
- **Task completion gate**: WU-0 through WU-6, WU-9 all checked `[x]`; WU-7 and WU-8 remain `[ ]` as deliberately cut (documented in task header)
- **Architecture gate**: No EF Core or Infrastructure types leak into Domain/Application; hexagonal boundaries maintained
- **ADR recorded**: ADR 0015 (`docs/architecture/adr/0015-schema-fatigue-norm.md`) documenting binding schema-fatigue norm

---

## Artifacts Archived

The following are now at `openspec/changes/archive/2026-08-26-client-aesthetic-visual-sophistication/`:

- `proposal.md` — revision 2, with all settled decisions and risk analysis
- `design.md` — technical approach, architecture decisions D1–D6, interfaces, geometry presets, data flow
- `exploration.md` — codebase map with file/line citations (pre-proposal foundation)
- `tasks.md` — complete task breakdown with per-WU checklist, including explicit WU-7/8 cut annotation and re-entry condition
- `wu6-reliability-gate.md` — full gate verdict with database query evidence and re-entry condition
- `specs/profile-analysis/spec-delta.md` — unmerged delta text for CAVS-PA-01–05 (already merged into main spec)
- `specs/content-generation/spec-delta.md` — unmerged delta text for CAVS-CG-04/05 (shipped CAVS-CG-01–03 already merged; unshipped text preserved for reference)
- `specs/content-studio/spec-delta.md` — unmerged delta text for CAVS-CS-01/02 (not shipped, preserved for reference)

---

## Key Decisions & Tradeoffs

1. **Ornament bounding-box reliability gate not passed**: Production data shows zero real bounding-box measurements. Shipping crop/render without measured accuracy risks mis-cropped or overlapping ornaments. Gate cuts WU-7/8 explicitly, leaving inert schema in place for future re-attempt.

2. **CompositionArchetype as open normalised string (not closed enum)**: Future archetypes require no schema change, no data migration, no re-extraction. Trade-off: unrecognised tokens silently render as `"left-aligned"` identity preset (test obligation, not compiler obligation).

3. **Schema-fatigue norm binding**: No further additive field on `AestheticExtraction` / `ClientAestheticProfile` without consolidation or ADR. Projects the cost of per-field extraction degradation forward; records the norm in ADR 0015.

4. **Divider + archetype rendered in deterministic flat path only**: Widening to AI-designed or non-carousel paths increases blast radius. Scoped to proven-stable flat builder.

5. **Degrade precedence function (not inline statement order)**: Whole-slide degrade tokens take precedence over element-level `ornament-unavailable` (when WU-8 lands). Ornament failure unconditionally logged for diagnosis regardless of badge token.

6. **No new EF Core migration**: Profile JSON-serialised; all fields additive-nullable; cold-start parity preserved.

---

## Known Limitations & Deferred Work

| Item | Status | Next Steps |
|---|---|---|
| Ornament crop/render (WU-7/WU-8) | CUT — gate not passed | Re-attempt once reliability gate passes (see re-entry condition in `wu6-reliability-gate.md`) |
| Ornament ADR | NOT CREATED | Defer with WU-7/8; create if/when WU-7/8 re-attempted |
| Content Studio degrade badge token-aware labels | NOT SHIPPED (part of WU-8) | Defer with WU-8; implement when ornament crop renders |
| Owner review checkpoint (task 9.5) | PENDING | Owner must review real generations for 2+ clients, record accept/reject |
| Icon-row glyph crops | Explicitly deferred | Out of scope; considered and rejected in proposal |
| `Approach 2` (constrained AI design pass for flat slides) | Explicitly deferred | Reserved as fallback if A1 + A3 prove insufficient; requires separate change + ADR |

---

## Authority & Traceability

**Final-state authority hierarchy** (per SDD archive protocol):

1. **Native review**: Not applicable (no gentle-ai review used for this change)
2. **Persisted tasks artifact**: `openspec/changes/client-aesthetic-visual-sophistication/tasks.md` (now archived) — WU-0 through WU-6, WU-9 checked; WU-7/8 deliberately unchecked with gate verdict recorded
3. **Explicit final-state facts** (launch prompt): 8 of 9 WU shipped; WU-7/8 cut; commits all on `dev`; tests 3912 passed; tracking issue #82 OPEN
4. **Intermediate snapshots** (verify-report, apply-progress): intermediate states; superseded by final facts above

**Observation IDs** (for traceability if Engram is used):
- Proposal: indexed by SDD pipeline
- Spec (profile-analysis delta): merged into main spec at line 1160–1323
- Spec (content-generation delta): merged at line 2247–2364, with "Not Implemented" at line 2339–2352
- Design: indexed by SDD pipeline
- Tasks: archived in this folder; key sections: top-level status, task 6.4 gate, task 9.5 human action, key learnings
- Verify report: passed (0 CRITICAL, 0 WARNING, 0 SUGGESTION; byte-identity, CompositionArchetype verbatim preservation, divider/archetype rendering wired, spec-merge correctly marks WU-7/8 as "Not Implemented")

---

## Closing Status

**This change is complete and closed** for the executed 8 work units. Deterministic flat-carousel rendering now supports:
- Client divider-rule extraction and render (DividerRuleStyle closed enum)
- Client composition-archetype extraction as open extensible token (CompositionArchetype string, verbatim preservation)
- Archetype-driven geometry presets (accent, divider, title, subtitle, cta) replacing hardcoded offsets
- Byte-identical cold-start output when `DecorationMotifs = null`
- Binding schema-fatigue norm for future additive fields (ADR 0015)

**Deferred** pending reliability re-measurement:
- Ornament crop port extension
- Ornament render + degrade badge + label map

**GitHub issue #82 remains OPEN** for owner review checkpoint (task 9.5) and potential WU-7/8 re-entry decision.

---

## Archive Completion Checklist

- [x] All 8 shipped WU artifacts present
- [x] Spec deltas merged (shipped CAVS-PA-01–05, CAVS-CG-01–03) with "Not Implemented" sections for WU-7/8
- [x] Tasks artifact archived with WU-7/8 cut documented and re-entry condition stated
- [x] Gate verdict (wu6-reliability-gate.md) archived with full evidence and decision
- [x] Byte-identity regression contract verified (golden green)
- [x] Test suite green (3912 passed, 0 failed)
- [x] ADR 0015 (schema-fatigue norm) recorded and referenced in spec
- [x] All 9 commits on `dev` branch
- [x] Tracking issue #82 documented as OPEN (pending owner review + potential re-entry)
- [x] Verification report: PASS (0 CRITICAL, 0 WARNING, 0 SUGGESTION)
