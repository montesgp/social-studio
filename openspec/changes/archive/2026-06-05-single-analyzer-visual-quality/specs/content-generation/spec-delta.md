# Spec Delta: Content Generation — Visual Quality (3-Tier Background + Visual Designer Stage)

<!-- Change: single-analyzer-visual-quality (Fase B / B1a only) -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->
<!-- Scope: Domain/Application contract only. Rendering engine (B1b) is OUT. -->

## Purpose

Specify the engine-agnostic Domain/Application contracts introduced by B1a:

1. The **3-tier background model** — `BackgroundEntry` domain extension with `Tier`, `FormatKey`, and `IsFormatDefault`.
2. **Tier-aware background selection** — updated `SelectBackgroundTemplate` invariants and dispatch rules.
3. The **Visual Designer port** (`IVisualDesigner`) — the Application port that introduces an LLM-backed composition stage between payload and renderer, with brand-binding and guardrail requirements.

Canva (#1/#4/#5), the rendering engine choice, and the B1b infrastructure adapter are explicitly OUT of scope for this delta.

---

## PRD Traceability

| Requirement ID | PRD Anchor |
|---|---|
| BG-01 | PRD `docs/PRD.md` — content-generation / professional downloadable output |
| BG-02 | PRD `docs/PRD.md` — content-generation / professional downloadable output |
| BG-03 | PRD `docs/PRD.md` — content-generation / professional downloadable output |
| VD-01 | PRD `docs/PRD.md` — content-generation / professional downloadable output |
| VD-02 | PRD `docs/PRD.md` — content-generation / professional downloadable output |
| VD-03 | PRD `docs/PRD.md` — content-generation / professional downloadable output |
| VD-04 | PRD `docs/PRD.md` — content-generation / professional downloadable output |

---

## ADDED Requirements

---

### Requirement: BG-01 — BackgroundEntry Extended with Tier Classification

The `BackgroundEntry` Domain value object MUST be extended with three new fields:

- `Tier` — an enum `BackgroundTier { ForcedDefault, Reference, Creative }` (MUST be present; no implicit default at the type level).
- `FormatKey` — an optional string designating which format key this entry applies to (MAY be null/empty for entries that are not format-specific).
- `IsFormatDefault` — a boolean indicating whether this entry is the single forced default for its `(FormatKey)` pair.

The `BrandBackgroundTemplateSnapshot` DTO MUST mirror all three fields so the Application layer can operate on the snapshot without referencing Domain types.

Persistence (`ClientBrandManualData` Postgres JSON column) MUST be additive: existing stored entries that have no `Tier` field MUST deserialize as `BackgroundTier.Creative` (backward-compatible migration, no data loss).

#### Scenario: New entry is created with explicit tier

- GIVEN a brand manual update that sets a background entry with `Tier = ForcedDefault`, `FormatKey = "flyer"`, `IsFormatDefault = true`
- WHEN the entry is persisted and reloaded
- THEN the reloaded entry has `Tier = ForcedDefault`, `FormatKey = "flyer"`, and `IsFormatDefault = true`

#### Scenario: Legacy entry without tier field deserializes as Creative

- GIVEN a stored JSON background entry that has no `Tier` property
- WHEN it is deserialized into `BackgroundEntry`
- THEN `Tier` equals `BackgroundTier.Creative`
- AND no exception is thrown and no data is lost

#### Scenario: Snapshot mirrors domain fields

- GIVEN a `BackgroundEntry` with `Tier = Reference`, `FormatKey = "carousel"`, `IsFormatDefault = false`
- WHEN the brand snapshot is built from the manual
- THEN `BrandBackgroundTemplateSnapshot.Tier` equals `Reference`
- AND `BrandBackgroundTemplateSnapshot.FormatKey` equals `"carousel"`
- AND `BrandBackgroundTemplateSnapshot.IsFormatDefault` equals `false`

---

### Requirement: BG-02 — At Most One IsFormatDefault per (FormatKey)

The Domain aggregate `ClientBrandManual` MUST enforce the invariant: for any given non-null `FormatKey`, at most ONE `BackgroundEntry` in `BackgroundTemplates` MAY have `IsFormatDefault = true`. Attempting to add or update a second entry violating this invariant MUST produce a failed `Result` (domain error), not a silent overwrite or exception.

#### Scenario: First forced default for a format is accepted

- GIVEN a brand manual with no existing ForcedDefault entry for `FormatKey = "flyer"`
- WHEN an entry with `IsFormatDefault = true` and `FormatKey = "flyer"` is added
- THEN the operation succeeds and the entry is stored

#### Scenario: Second forced default for the same format is rejected

- GIVEN a brand manual that already has one entry with `IsFormatDefault = true` for `FormatKey = "flyer"`
- WHEN a second entry with `IsFormatDefault = true` and `FormatKey = "flyer"` is added
- THEN the operation returns a failed `Result` containing a domain error
- AND the existing forced-default entry is unchanged

#### Scenario: Two forced defaults for different formats are accepted

- GIVEN a brand manual with one ForcedDefault entry for `FormatKey = "flyer"`
- WHEN an entry with `IsFormatDefault = true` and `FormatKey = "carousel"` is added
- THEN the operation succeeds
- AND both entries are stored with their respective `IsFormatDefault = true`

#### Scenario: IsFormatDefault = true with null FormatKey is rejected

- GIVEN a background entry with `IsFormatDefault = true` and `FormatKey = null`
- WHEN it is validated by the aggregate
- THEN the operation returns a failed `Result` (a forced default without a format key is meaningless)

---

### Requirement: BG-03 — Tier-Aware Background Selection

`SelectBackgroundTemplate(formatKey)` on composers MUST follow a strict tier-priority dispatch:

1. **Tier-1 (ForcedDefault)**: if a `BackgroundEntry` with `Tier = ForcedDefault` and `IsFormatDefault = true` exists for the given `formatKey`, that entry MUST be returned. No generation and no external call MUST be triggered.
2. **Tier-2 (Reference)**: if no ForcedDefault exists, all entries with `Tier = Reference` (regardless of `FormatKey` match) MUST be returned as a collection for use as multimodal style guides by the designer stage.
3. **Tier-3 (Creative)**: if neither ForcedDefault nor Reference entries exist, the selection result MUST indicate creative generation — the designer stage generates brand-bound output freely.

The method MUST return a discriminated result type (`BackgroundSelection`) that carries the tier and its payload (entry / reference list / none) so callers never need to re-inspect raw entries.

#### Scenario: Tier-1 forced default is returned without generation

- GIVEN a brand snapshot with one entry: `Tier = ForcedDefault`, `FormatKey = "flyer"`, `IsFormatDefault = true`, `Url = "https://cdn.example.com/bg.png"`
- WHEN `SelectBackgroundTemplate("flyer")` is called
- THEN the result is `BackgroundSelection.ForcedDefault` carrying `Url = "https://cdn.example.com/bg.png"`
- AND no generation flag is set and no external call is made

#### Scenario: Tier-2 reference list is returned when no forced default exists

- GIVEN a brand snapshot with two entries both having `Tier = Reference`
- AND no entry with `Tier = ForcedDefault`
- WHEN `SelectBackgroundTemplate("flyer")` is called
- THEN the result is `BackgroundSelection.References` carrying both reference entries

#### Scenario: Tier-3 creative is returned when no structured entries exist

- GIVEN a brand snapshot with no ForcedDefault and no Reference entries (only Creative or empty)
- WHEN `SelectBackgroundTemplate("flyer")` is called
- THEN the result is `BackgroundSelection.Creative`

#### Scenario: Forced default for a different format does not match

- GIVEN a brand snapshot with one ForcedDefault entry for `FormatKey = "carousel"`
- WHEN `SelectBackgroundTemplate("flyer")` is called
- THEN the result is NOT `BackgroundSelection.ForcedDefault`
- AND the carousel forced default is not used for the flyer format

---

### Requirement: VD-01 — IVisualDesigner Application Port Exists

The Application layer MUST define an `IVisualDesigner` port interface. The method signature MUST be:

```
Task<Result<VisualDesignOutput>> ComposeAsync(
    VisualCompositionSpec contentPayload,
    BrandManualSnapshot brandSnapshot,
    BackgroundSelection backgroundSelection,
    CancellationToken cancellationToken)
```

`VisualDesignOutput` MUST carry at minimum: the composition markup (string), the `PromptVersionKey` (string, identifies the prompt template version used), and an `IsValid` flag set by guardrail validation.

No Infrastructure type MUST appear in the port signature, parameters, or return types. The port MUST live in the Application layer boundary.

#### Scenario: Port interface is defined in Application layer

- GIVEN the compiled Application assembly
- WHEN its exported types are inspected
- THEN `IVisualDesigner` is present as a public interface
- AND no Infrastructure or persistence namespace appears in its parameter or return types

#### Scenario: ComposeAsync returns a VisualDesignOutput with PromptVersionKey

- GIVEN a stubbed `IVisualDesigner` implementation that returns a fixed composition
- WHEN `ComposeAsync` is called with valid inputs
- THEN the returned `Result<VisualDesignOutput>` is successful
- AND `VisualDesignOutput.PromptVersionKey` is a non-null, non-empty string

---

### Requirement: VD-02 — Brand Palette and Typography Guardrails

The composition output produced via `IVisualDesigner` MUST comply with brand guardrails before it is passed to the renderer. The Application pipeline MUST:

1. Verify that every color value in the design output is a member of `BrandManualSnapshot.ColorPalette` or is transparent/none. Off-palette colors MUST cause the `Result` to fail.
2. Verify that every font family referenced in the design output is a member of `BrandManualSnapshot.Typography.AllowedFontFamilies`. Arial (or any font not in the manual) MUST NOT appear unless it is explicitly listed in the manual's typography. Violation MUST cause the `Result` to fail.
3. Both checks MUST be run BEFORE the output is forwarded to the renderer. A failed guardrail MUST produce a failed `Result` with a structured error — it MUST NOT produce a broken or partially-rendered asset.

#### Scenario: Off-palette color is rejected

- GIVEN a `VisualDesignOutput` whose markup references a color not in the brand palette
- WHEN guardrail validation runs
- THEN the result is a failed `Result` containing a guardrail violation error
- AND the renderer is NOT called

#### Scenario: Non-manual font is rejected

- GIVEN a `VisualDesignOutput` whose markup references font-family `"Arial"` and the brand manual does not list Arial
- WHEN guardrail validation runs
- THEN the result is a failed `Result` containing a font violation error

#### Scenario: On-palette output passes guardrails

- GIVEN a `VisualDesignOutput` whose colors and fonts are all within the brand manual
- WHEN guardrail validation runs
- THEN the result is successful and is forwarded to the renderer

---

### Requirement: VD-03 — Text-Overflow Prevention

The Application pipeline MUST verify that the composition output does not declare text content that overflows its containing slot dimensions (as specified in `VisualCompositionSpec.Dimensions` and slot size constraints). Text overflow MUST cause the `Result` to fail with a structured error before reaching the renderer.

#### Scenario: Text within bounds passes

- GIVEN a `VisualDesignOutput` where all text content fits within its declared slot dimensions
- WHEN overflow validation runs
- THEN the result is successful

#### Scenario: Text overflow is rejected before render

- GIVEN a `VisualDesignOutput` where a text block's declared height exceeds its slot constraint
- WHEN overflow validation runs
- THEN the result is a failed `Result` containing an overflow error
- AND the renderer is NOT called

---

### Requirement: VD-04 — Design Output is Schema/Spec-Validated and Carries PromptVersionKey

The `VisualDesignOutput` markup MUST be validated against a declared schema (structural/syntax check) before being forwarded to the renderer. Schema validation failure MUST produce a failed `Result` — it MUST NOT silently forward malformed markup.

The `VisualDesignOutput` MUST carry a non-empty `PromptVersionKey` that identifies the prompt template version that produced it. This key MUST be preserved in the pipeline (passed to the renderer call and available in any downstream audit/log).

The `PromptVersionKey` MUST be deterministic for a given prompt template — the same template version MUST always produce the same key.

#### Scenario: Invalid markup is rejected before render

- GIVEN a `VisualDesignOutput` whose markup fails structural schema validation
- WHEN schema validation runs
- THEN the result is a failed `Result` containing a schema validation error
- AND the renderer is NOT called

#### Scenario: Valid markup with PromptVersionKey passes schema validation

- GIVEN a `VisualDesignOutput` with structurally valid markup and a non-empty `PromptVersionKey`
- WHEN schema validation runs
- THEN the result is successful
- AND `PromptVersionKey` is preserved in the output forwarded to the renderer

#### Scenario: Stubbed designer in tests produces deterministic PromptVersionKey

- GIVEN a test doubles implementation of `IVisualDesigner` returning a fixed stub output
- WHEN `ComposeAsync` is called and schema/guardrail validation runs against the stub output
- THEN all validation steps complete without making any real LLM call
- AND `PromptVersionKey` is a non-empty string in the test output

---

## MODIFIED Requirements

None. All requirements in this delta are additive; no existing `openspec/specs/content-generation/spec.md` requirements are modified.

---

## REMOVED Requirements

None.
