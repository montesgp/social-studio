# GitHub Issue #82 Closing Comment (to be posted manually)

**Post this comment on GitHub issue #82** — do NOT close the issue, leave it OPEN.

---

## Comment Text

**SDD Change Archived: `client-aesthetic-visual-sophistication` — PARTIAL Delivery (8 of 9 WU)**

### What Shipped

**Decoration motifs — deterministic and cold-start safe:**
- `DecorationMotifs` record: `DividerRuleStyle?` (closed enum: Line | LineWithDiamond | LineWithDot) + `CompositionArchetype?` (open normalised string, preserved verbatim for future extensibility) + nested `DecorativeOrnamentDetection` (inert, no crop rendering yet)
- **Divider rule render**: `data-slot="divider"` element with resolved accent or text colour, glyph (diamond/dot) centred on the rule
- **Composition archetype geometry presets**: token-resolved layout for accent, divider, title, subtitle, cta; `"left-aligned"` reproduces today's hardcoded literals exactly; unrecognised tokens safely fallback to identity
- **Cold-start parity**: `DecorationMotifs = null` renders markup byte-identical to pre-change output (verified by golden regression contract, all 3,912 tests pass)
- **Extraction + persistence**: multimodal extractor gains prompt/DTO/Map for decoration motifs; schema version bumped (v7 for divider+archetype, v8 for ornament detection); fields persist through JSON round-trip with no EF Core migration needed; pre-change profiles deserialise with `DecorationMotifs = null` without exception

### What Was Cut and Why

**Ornament crop/render (WU-7/WU-8) — deliberately cut, not incomplete:**
- **Gate verdict** (task 6.4 / `wu6-reliability-gate.md`): The vision model's bounding-box extraction mechanism was measured against production data. Query of 14 real `social.profile_analyses` rows with brand-mark data found **zero carrying a `boundingBox` value**. No real bounding-box measurement exists to evaluate.
- **Decision**: Per task 6.4's stated fallback clause, WU-7 (ornament crop port) and WU-8 (ornament render + degrade + badge) are cut from this delivery.
- **Consequence**: `DecorationMotifs.Ornament` (source path + validated bounding box) ships as inert nullable field, unread by any renderer. No `data-slot="ornament"` element, no `ornament-unavailable` degrade token, no crop rendering yet.
- **Spec integrity**: `CAVS-CG-04`/`CAVS-CG-05` and `CAVS-CS-01`/`CAVS-CS-02` remain as "Not Implemented in This Delivery" in the merged specs, with explicit reference to `wu6-reliability-gate.md` and re-entry condition.

### New Binding Convention

**Schema-fatigue norm (ADR 0015)**: No further additive field on `AestheticExtraction` / `ClientAestheticProfile` may land without either (a) a consolidation pass grouping signals into a container, or (b) an ADR recorded before the field is written. This change complies by nesting `DecorativeOrnamentDetection` inside `DecorationMotifs` rather than adding it as a separate field, and by cutting `AccentShape` rather than absorbing it.

### Issue Status

**Issue #82 remains OPEN** — two pending items:
1. **WU-7/WU-8 re-entry decision**: Re-attempt ornament crop/render once the reliability gate passes (re-entry condition: measure bounding-box accuracy against 2+ real client feeds with human visual comparison, or run extractor online against archived reference images)
2. **Task 9.5 owner review checkpoint**: Owner must review one real generation for each of two clients with materially different signalled archetypes and record accept/reject in writing (human action, correctly left unchecked by `sdd-apply`)

**Archive path**: `openspec/changes/archive/2026-08-26-client-aesthetic-visual-sophistication/`

---

*Commits on `dev`: bc4cb7a, daed43b, cb7a657, 7fd28ca, 11bb2b0, 3b99c6e, d732c01, f94153a, 31a6329, b571fc7*  
*Verification: PASS (0 CRITICAL, 0 WARNING, 0 SUGGESTION). Tests: 3,912 passed, 0 failed.*
