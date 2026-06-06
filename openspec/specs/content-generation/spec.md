# Content Generation Spec

## Purpose
Define capabilities, constraints, invariants, events, and acceptance criteria for content generation, packaging, and visual composition workflows.

## Capabilities
- Compose visual content specs (reels, carousels, text cards, etc.) from brand snapshot and AI-generated content.
- Render visual specs to multiple output formats (SVG, PNG, Markdown).
- Package generated assets into downloadable archives (frozen-ZIP, assemble-on-demand).
- Derive package folder names from client display names, sanitized for cross-OS compatibility.
- Deliver reel-script output as structured, readable Markdown with bold timestamps and scene separation.

## Events
- `GeneratedPackageAssembled` — a package has been assembled and is ready for download.
- `VisualCompositionCompleted` — visual composition is complete and ready for export.

## Constraints
- Folder names MUST be sanitized for Windows, macOS, and Linux path rules.
- Folder names MUST match the client's display name as shown in the frontend.
- Reel-script output MUST be human-readable with clear scene separation.
- Asset paths MUST NOT expose internal GUIDs to end users in downloadable archives.
- Renderer dispatch MUST be exhaustive over `CompositionOutputKind` enum values.

## Acceptance Criteria
- Downloadable package folder names match frontend client labels (no slugification or GUIDs).
- Reel-script is delivered as `.md` (Markdown) with newline separation and bold timestamps.
- All renderer output kinds are explicitly handled; no silent defaults or fallback strings.
- Cross-OS sanitization preserves client display names (accents, spaces, symbols) where safe.

---

## Packaging Folder Naming (PKG-01, PKG-02) — Change: single-analyzer-fase-a

> Added by change `single-analyzer-fase-a`. This section is additive; no existing requirements are modified.

### Requirement: PKG-01 — Folder Name Equals Client Display Name (Frozen-ZIP Path)

The `GeneratedPackageBuilder` MUST derive the package folder name from the client
display name by applying a cross-OS sanitize-only transform. The transform MUST:

1. Remove the 9 cross-OS-illegal characters (`< > : " / \ | ? *`), which collectively cover the Linux `/`, macOS `:`, and Windows full reserved set.
2. Remove all ASCII control characters (code points 0x00–0x1F **and** 0x7F DEL).
3. Trim **leading** dots and spaces (a leading dot creates a Unix hidden file/directory; leading spaces are illegal on some OS path parsers).
4. Trim **trailing** dots and spaces (Windows disallows folder names ending in `.` or space).
5. Guard Windows reserved device names — when the **entire** sanitized result matches `CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, or `CLOCK$` (case-insensitive) — neutralize by appending `_` (e.g. `NUL` → `NUL_`). Names that merely **contain** a device name as a substring (e.g. `Conway`) are NOT affected.

The transform MUST NOT change case, remove accents, collapse internal spaces, remove emoji, or replace any character with a substitute (e.g. a dash or underscore). The resulting folder name MUST be as close as possible to the display name seen in the front end, modulo only the characters and edge cases listed above.

If the sanitized result is empty (e.g. the display name consisted entirely of reserved characters), the system MUST return an empty string without error.

#### Scenario: Display name with no reserved characters passes through unchanged

- GIVEN a client whose display name is `"Acme Corp"`
- WHEN `NormalizeFolderName("Acme Corp")` is called
- THEN the result is `"Acme Corp"` (case, space, and capitalisation preserved)

#### Scenario: Display name with accented characters and special symbols

- GIVEN a client whose display name is `"Café & Co."`
- WHEN `NormalizeFolderName("Café & Co.")` is called
- THEN the result is `"Café & Co"` (accent, ampersand, and space preserved; **trailing period trimmed**)
- AND no character is lowercased or replaced with a dash

#### Scenario: Middle dot is preserved

- GIVEN a client whose display name is `"Co. Ltd"`
- WHEN `NormalizeFolderName("Co. Ltd")` is called
- THEN the result is `"Co. Ltd"` (the dot is internal, not trailing — it is preserved)

#### Scenario: Reserved characters are stripped — no slugification

- GIVEN a client whose display name is `"Client:Name/Test?"`
- WHEN `NormalizeFolderName("Client:Name/Test?")` is called
- THEN the result is `"ClientNameTest"` (`:`, `/`, and `?` removed; remaining chars untouched)
- AND the result does NOT contain any dashes or underscores introduced by the sanitizer

#### Scenario: All-reserved name returns empty

- GIVEN a client whose display name is `"<>:\""`
- WHEN `NormalizeFolderName("<>:\"")` is called
- THEN the result is `""` (empty string, no error thrown)

#### Scenario: Leading dot is trimmed (Unix hidden-file prevention)

- GIVEN a client whose display name is `".config"`
- WHEN `NormalizeFolderName(".config")` is called
- THEN the result is `"config"` (leading dot removed)

#### Scenario: DEL character (0x7F) is stripped

- GIVEN a folder name containing the DEL character (code point 0x7F)
- WHEN `NormalizeFolderName` is called
- THEN the DEL character is removed from the result

#### Scenario: Windows device names are neutralized (whole-name match only)

- GIVEN a client whose display name is `"NUL"`
- WHEN `NormalizeFolderName("NUL")` is called
- THEN the result is `"NUL_"` (appended `_` to avoid OS collision)

- GIVEN a client whose display name is `"Conway"` (contains `"con"` as a substring)
- WHEN `NormalizeFolderName("Conway")` is called
- THEN the result is `"Conway"` (substring match does NOT trigger neutralization)

#### Scenario: Frozen-ZIP folder path uses display name

- GIVEN a client whose display name is `"Acme Corp"` and a generated package is assembled
- WHEN the frozen-ZIP archive is built
- THEN the archive contains entries under the path prefix `"Acme Corp/"` (not `"acme-corp/"` or a GUID)

---

### Requirement: PKG-02 — Folder Name Equals Client Display Name (Assemble Path)

The `AssembleGeneratedPackageQueryHandler` MUST derive the package folder segment from
the client display name, not from the raw client GUID. The display name MUST be sourced
via the query input or a read-only lookup that does not introduce persistence types into
the Application layer. The same `NormalizeFolderName` sanitizer defined in PKG-01 MUST
be applied to the display name before it is used as a folder segment.

#### Scenario: Assemble path uses display name, not GUID

- GIVEN a client with display name `"Acme Corp"` and GUID `"3f2a1b00-..."`
- WHEN the Assemble query handler resolves the folder segment
- THEN the folder segment is `"Acme Corp"` (sanitized display name)
- AND the GUID `"3f2a1b00-..."` does NOT appear as a folder segment in the output

#### Scenario: Assemble path applies the same sanitizer as the frozen-ZIP path

- GIVEN a client whose display name is `"Client:Name/Test?"`
- WHEN the Assemble query handler resolves the folder segment
- THEN the folder segment is `"ClientNameTest"` (matching PKG-01 behavior exactly)

---

## Reel-Script Markdown Output (RS-01, RS-02, RS-03) — Change: single-analyzer-fase-a

> Added by change `single-analyzer-fase-a`. This section is additive; no existing requirements are modified.

### Requirement: RS-01 — CompositionOutputKind Contains a Markdown Discriminator

The `CompositionOutputKind` enumeration MUST include a `Markdown` member. The renderer
MUST NOT branch on format-key strings (e.g. `"reel-script"`) to decide output format;
it MUST branch on the `OutputKind` discriminator value. All existing renderer branches
MUST remain exhaustively handled after the new member is added.

#### Scenario: Markdown discriminator exists on the enum

- GIVEN the `CompositionOutputKind` type definition
- WHEN its members are enumerated
- THEN `Markdown` is present as a named member

#### Scenario: Renderer handles Markdown case explicitly

- GIVEN a `VisualCompositionSpec` whose `OutputKind` is `CompositionOutputKind.Markdown`
- WHEN the renderer processes the spec
- THEN the renderer does NOT fall through to a default/unknown branch
- AND the output file is written as `reel-script.md`

---

### Requirement: RS-02 — Reel-Script Body Is Newline-Separated with Bold Timestamps

The `ReelScriptVisualComposer` MUST produce a Markdown body with the following
structure: an optional hook line, followed by one block per scene, followed by the CTA.
Each scene block MUST begin with the timestamp formatted as `**{timestamp}**` in
bold Markdown syntax. Voiceover text and on-screen text MUST be present in the body.
Scenes MUST be separated by newlines; the body MUST NOT be a single space-joined line.
Emojis present in `Voiceover` or `OnScreenText` fields MUST survive into the output
body unchanged.

The resulting `VisualCompositionSpec` MUST have `OutputKind == CompositionOutputKind.Markdown`.

#### Scenario: Multi-scene reel-script contains newlines between scenes

- GIVEN a reel-script payload with two or more scenes
- WHEN `ReelScriptVisualComposer` produces the output body
- THEN the body string contains at least one newline character (`\n`)
- AND the body is NOT a single space-joined line

#### Scenario: Each scene line begins with a bold timestamp

- GIVEN a scene with timestamp `"0:00"`
- WHEN the scene is rendered by the composer
- THEN the scene line starts with `**0:00**`

#### Scenario: Emojis are preserved verbatim

- GIVEN a scene whose `Voiceover` field contains the emoji `"🎬"`
- WHEN the composer produces the output body
- THEN the output body contains `"🎬"` unchanged
- AND no encoding or escaping is applied to the emoji codepoint

#### Scenario: CTA is present in the output body

- GIVEN a reel-script payload that includes a CTA value
- WHEN the composer produces the output body
- THEN the CTA text appears in the body after the last scene block

#### Scenario: OutputKind is set to Markdown

- GIVEN `ReelScriptVisualComposer` has run
- WHEN the resulting `VisualCompositionSpec` is inspected
- THEN `OutputKind` equals `CompositionOutputKind.Markdown`

---

### Requirement: RS-03 — Renderer Emits reel-script.md with Markdown MIME Type

The renderer (Infrastructure layer) MUST, when processing a `VisualCompositionSpec`
with `OutputKind == CompositionOutputKind.Markdown`, write the output to a file
named `reel-script.md` with MIME type `text/markdown; charset=utf-8`.
The frontend MUST resolve the reel-script download filename with a `.md` extension,
not `.txt`.

#### Scenario: Renderer writes reel-script.md

- GIVEN a spec with `OutputKind == CompositionOutputKind.Markdown`
- WHEN the renderer writes the output file
- THEN the file is named `reel-script.md`
- AND the associated MIME type is `text/markdown; charset=utf-8`

#### Scenario: Frontend resolves .md extension for reel-script

- GIVEN the front-end filename resolver is called for the `"reel-script"` format key
- WHEN the resolved filename is returned
- THEN the filename ends with `.md`
- AND it does NOT end with `.txt`

---

## Diagrams (DIAG-01) — Change: single-analyzer-fase-a

> Added by change `single-analyzer-fase-a`. This section is additive; no existing requirements are modified.

### Requirement: DIAG-01 — Single-Analyzer Flow Diagram Exists and Is Accurate (Docs-Only)

The repository MUST contain both `docs/architecture/diagrams/single-analyzer-flow.excalidraw`
and `docs/architecture/diagrams/single-analyzer-flow.md`. The `.excalidraw` file MUST be
valid Type 2 Excalidraw JSON following the `content-plan-flow` conventions (color palette,
rectangle+text+arrow schema, Virgil font). The `.md` companion MUST include a Mermaid
flowchart and legend. Together they MUST accurately depict the current single-analyzer
happy path through packaging and MUST include a DOWNLOAD vs EXPORT-TO-CANVA decision
node with the Canva branch explicitly marked as a Phase B placeholder.

*No runtime GIVEN/WHEN/THEN scenario — acceptance is verified by diagram review before merge.*

**Acceptance criterion:** Both files exist, follow `content-plan-flow` conventions, show
the complete happy path, and include a correctly labelled Phase B Canva decision node.

---

## Visual Quality — 3-Tier Background & Visual Designer (BG-01, BG-02, BG-03, VD-01, VD-02, VD-03, VD-04) — Change: single-analyzer-visual-quality

<!-- Change: single-analyzer-visual-quality (Fase B / B1a only) -->
<!-- Scope: Domain/Application contract only. Rendering engine (B1b) is OUT. -->

> Added by change `single-analyzer-visual-quality`. This section is additive; no existing requirements are modified.

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
    Guid organizationId,
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
