# Content Generation Spec

## Purpose
Define capabilities, constraints, invariants, events, and acceptance criteria for content generation, packaging, and visual composition workflows.

## Capabilities
- Compose visual content specs (reels, carousels, text cards, etc.) from brand snapshot and AI-generated content.
- Produce visual deliverables as editable PPTX for story/carousel and Markdown for scripts.
- Use image files only as brand/manual background inputs or internal non-deliverable implementation details.
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
- Visual guardrail schema validation MUST tolerate browser-renderable HTML syntax in the parser-only validation copy (for example raw `<` characters inside attribute values) while still rejecting unsafe/off-schema tags.

## Acceptance Criteria
- Downloadable package folder names match frontend client labels (no slugification or GUIDs).
- Reel-script is delivered as `.md` (Markdown) with newline separation and bold timestamps.
- All renderer output kinds are explicitly handled; no silent defaults or fallback strings.
- Cross-OS sanitization preserves client display names (accents, spaces, symbols) where safe.
- Failed generation jobs must not create visible package file entries for formats that did not render.
- Regeneration for the same analysis/client/format is blocked after two failed attempts to prevent repeated AI-token spend.
- Retry-AI analysis for a single-content analysis is blocked after two failed AI-stage attempts and the attempt counter is persisted server-side.
- Package-level "Send to Canva" must mirror the package download ready/disabled state.
- Package downloads assembled from stored assets MUST always include `manifest.json` at the ZIP root.
- Package downloads assembled from stored assets MUST organize deliverables as `{client-folder}/{format-folder}/{file}`.
- Package downloads assembled from stored assets MUST deliver `story-single`, `story-carousel`, `feed-single`, and `feed-carousel` as editable PPTX files, not PNG/JPG primary deliverables.
- Package downloads assembled from stored assets MUST deliver `reel-script` as `script/script.md`.
- Package downloads assembled from stored assets MUST NOT include `editable-package.pptx` at the ZIP root.
- If a client has an applicable background template for the requested visual format, that template MUST be hard-enforced as the PPTX background even if the AI proposes a different background.
- If a client has no forced background template, the creative background may be generated from the client's brand manual and the AI proposal.
- Package download and package-level "Send to Canva" actions MUST persist action counts/timestamps in the database.
- Story visual generation must not fail solely because the visual designer emitted browser-renderable HTML that is stricter than XML.

---

## Single Analyzer Package Failure Safety — Change: single-analyzer-failure-safety

### Requirement: SAFETY-01 — No Phantom Package Entries After Failed Generation

The Package tab MUST render generated format files only from successful persisted assets or successful
job status, never from a generation request being merely queued. A failed job that produced no rendered
asset MUST leave the package tree empty for that format.

#### Scenario: Failed story generation does not show story.png

- GIVEN a single-analyzer generation job requested the `story-single` format
- AND the job failed before a rendered story asset was persisted
- WHEN the user opens the Package tab
- THEN no `story-single` deliverable is shown under the client folder
- AND the package download remains disabled until at least one format has succeeded

### Requirement: SAFETY-02 — Regeneration Locks After Two Failed Attempts

For each `(analysis, client, format)` scope, the system MUST count failed per-format attempts across
jobs. Once the count reaches `2`, new generation/regeneration requests for that format MUST be rejected
by the backend and the frontend MUST disable the relevant regenerate controls with an explanatory hint.

#### Scenario: Second failed story attempt locks regeneration

- GIVEN `story-single` generation failed once for an analysis/client pair
- AND a subsequent `story-single` regeneration failed again for the same analysis/client pair
- WHEN the user attempts to regenerate `story-single` a third time
- THEN the backend rejects the request with a handled conflict error
- AND the frontend disables the regenerate control
- AND the frontend explains that regeneration is blocked to avoid burning AI tokens

### Requirement: SAFETY-03 — Canva Package Action Mirrors Download Readiness

When the organization has Canva configured, the Package tab's "Send to Canva" action MUST be visible
beside the package download action and MUST use the same generated-content readiness state.

#### Scenario: Package not ready disables both package actions

- GIVEN no generated package content is ready
- WHEN the Package tab is rendered
- THEN the package download action is disabled
- AND the "Send to Canva" action is also disabled

#### Scenario: Package ready enables both package actions

- GIVEN at least one package format is successfully generated and the package can be downloaded
- WHEN the Package tab is rendered
- THEN the package download action is enabled
- AND the "Send to Canva" action is enabled when Canva is configured

### Requirement: SAFETY-04 — URL Input Preserves Long Social URLs

The single-content URL analyzer input MUST allocate the available row width to the URL field so common
Instagram and TikTok URLs can be reviewed before submission without shrinking surrounding containers.

### Requirement: SAFETY-05 — Retry-AI Locks After Two Failed AI Attempts

For each single-content analysis, the backend MUST persist failed AI-analysis attempts. Once the count
reaches `2`, the Retry AI action MUST be rejected by the backend and disabled in the frontend with a
hint explaining that the lock prevents repeated AI-credit spend.

#### Scenario: Second failed AI retry locks further retry

- GIVEN an analysis has a persisted content snapshot
- AND the initial AI analysis failed
- AND one Retry AI attempt also failed
- WHEN the user attempts Retry AI again
- THEN the backend rejects the request with a conflict error
- AND the frontend disables Retry AI with a cost-control hint

### Requirement: PKG-03 — Assemble Package Uses Client-First Editable Deliverables

Assemble-on-demand package downloads MUST include `manifest.json` at the ZIP root. For every client
with generated content, the ZIP MUST contain one folder per generated format. Visual formats are
delivered as editable PPTX files under their format folder; script content is delivered as Markdown.
The assembled package MUST NOT place `editable-package.pptx` or visual PNG/JPG primary deliverables
at the ZIP root or directly inside the client folder.

#### Scenario: Stored-asset package contains client-first manifest and format folders

- GIVEN a client named `"Incoders"` has generated `story-single`, `story-carousel`, and `reel-script` formats
- AND the visual formats have persisted composition specs
- WHEN the user downloads the assembled package
- THEN the ZIP contains `manifest.json`
- AND the ZIP contains `Incoders/story-single/story-single.pptx`
- AND the ZIP contains `Incoders/story-carousel/slide_1.pptx`
- AND the ZIP contains `Incoders/script/script.md`
- AND the ZIP does not contain `editable-package.pptx` at the root
- AND `manifest.json` includes the generated publication description when present
- AND `manifest.json` categorizes formats under `clients.Incoders.formats`

### Requirement: PKG-04 — Package Actions Are Persisted

Every successful package download or package-level Send to Canva action MUST increment its persisted
counter and store the last action timestamp/user for the generated package row.

#### Scenario: Download and Canva send update package action state

- GIVEN a generated package exists for a completed job
- WHEN the user downloads the package
- THEN the package download count and last downloaded timestamp are updated
- WHEN the user sends the package to Canva
- THEN the Canva send count and last sent timestamp are updated

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
valid Type 2 Excalidraw JSON using a simple rectangle+text+arrow schema. The `.md`
companion MUST include a Mermaid sequence/flow diagram and a short responsibility legend.
Together they MUST accurately depict the current single-analyzer happy path from URL
analysis through generation and delivery. The diagram MUST show that AI provider/model
selection is organization-integration-driven, that data extraction is owned by the data
provider adapter, that Incoders Social owns persistence/guardrails/rendering, and that
Download and Send to Canva reuse persisted assets/composition specs instead of re-running
the visual designer.

*No runtime GIVEN/WHEN/THEN scenario — acceptance is verified by diagram review before merge.*

**Acceptance criterion:** Both files exist, show the complete happy path, and make the
responsibility split understandable without knowing the implementation classes.

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

- GIVEN a brand manual update that sets a background entry with `Tier = ForcedDefault`, `FormatKey = "story"`, `IsFormatDefault = true`
- WHEN the entry is persisted and reloaded
- THEN the reloaded entry has `Tier = ForcedDefault`, `FormatKey = "story"`, and `IsFormatDefault = true`

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

- GIVEN a brand manual with no existing ForcedDefault entry for `FormatKey = "story"`
- WHEN an entry with `IsFormatDefault = true` and `FormatKey = "story"` is added
- THEN the operation succeeds and the entry is stored

#### Scenario: Second forced default for the same format is rejected

- GIVEN a brand manual that already has one entry with `IsFormatDefault = true` for `FormatKey = "story"`
- WHEN a second entry with `IsFormatDefault = true` and `FormatKey = "story"` is added
- THEN the operation returns a failed `Result` containing a domain error
- AND the existing forced-default entry is unchanged

#### Scenario: Two forced defaults for different formats are accepted

- GIVEN a brand manual with one ForcedDefault entry for `FormatKey = "story"`
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
2. **Tier-2 (ManualTemplate/Reference)**: if no ForcedDefault exists, all applicable manual background entries MUST be returned as a collection. Applicable means `FormatKey` is empty/global or matches the requested format. This includes legacy/default `Creative` entries because old manuals did not classify templates.
3. **Tier-3 (Creative)**: if no applicable manual background entry exists, the selection result MUST indicate creative generation — the designer stage/generator can create a brand-bound background freely.

The method MUST return a discriminated result type (`BackgroundSelection`) that carries the tier and its payload (entry / reference list / none) so callers never need to re-inspect raw entries.

#### Scenario: Tier-1 forced default is returned without generation

- GIVEN a brand snapshot with one entry: `Tier = ForcedDefault`, `FormatKey = "story"`, `IsFormatDefault = true`, `Url = "https://cdn.example.com/bg.png"`
- WHEN `SelectBackgroundTemplate("story")` is called
- THEN the result is `BackgroundSelection.ForcedDefault` carrying `Url = "https://cdn.example.com/bg.png"`
- AND no generation flag is set and no external call is made

#### Scenario: Tier-1 forced default is hard-enforced before render/export

- GIVEN a client has a forced default background template for `story-single`
- AND the AI designer returns markup with a different generated background
- WHEN the content generation pipeline renders the story
- THEN the background layer passed to rendering uses the forced brand-manual template URL
- AND the AI-generated background layer is not used as the final background
- AND text/content components remain available for editable PPTX export

#### Scenario: Tier-2 reference list is returned when no forced default exists

- GIVEN a brand snapshot with two entries both having `Tier = Reference`
- AND no entry with `Tier = ForcedDefault`
- WHEN `SelectBackgroundTemplate("story")` is called
- THEN the result is `BackgroundSelection.References` carrying both reference entries

#### Scenario: Legacy/default manual background is still treated as a template

- GIVEN a brand snapshot with one background image entry whose `Tier = Creative`
- AND the entry has no `FormatKey`
- WHEN `SelectBackgroundTemplate("story")` is called
- THEN the result is `BackgroundSelection.References` carrying that entry
- AND the pipeline uses that manual image as the rendered background

#### Scenario: Tier-3 creative is returned when no manual templates exist

- GIVEN a brand snapshot with no background template entries
- WHEN `SelectBackgroundTemplate("story")` is called
- THEN the result is `BackgroundSelection.Creative`

#### Scenario: Forced default for a different format does not match

- GIVEN a brand snapshot with one ForcedDefault entry for `FormatKey = "carousel"`
- WHEN `SelectBackgroundTemplate("story")` is called
- THEN the result is NOT `BackgroundSelection.ForcedDefault`
- AND the carousel forced default is not used for the story format

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

---

## Format Taxonomy — Change: content-format-taxonomy

> Added by `content-format-taxonomy`. This section is additive; no existing requirements are modified.
> Commits: a2f82fa (domain), b1133f0 (backend), 7edda4b (frontend), S5 (extensibility proof + doc sync).

### Taxonomy Model

Content formats are described by three orthogonal axis types in `Domain/Social/ContentFormats/`:

- `Surface` — Script | Story | Feed
- `Layout` — Single | Carousel (null for Script)
- `Medium` — Image | Video | Text

These are combined into `FormatDefinition` rows held by the immutable `FormatCatalog` static class.

### v1 Catalog Keys

Valid API write keys (accepted by `POST /api/social/content-generation/packages`):

| FormatKey | Surface | Layout | Medium | Channel | IsMultiAsset |
|---|---|---|---|---|---|
| reel-script | Script | — | Text | instagram/reel | false |
| story-single | Story | Single | Image | instagram/story | false |
| story-carousel | Story | Carousel | Image | instagram/story | true |
| feed-single | Feed | Single | Image | instagram/feed | false |
| feed-carousel | Feed | Carousel | Image | instagram/feed | true |

Plus auxiliaries: content-idea, cta-variation, caption, hook (Text medium; included in API whitelist, excluded from frontend picker).

The legacy keys `"story"` and `"feed-post"` are normalized to `"story-single"` and `"feed-single"` on write for compatibility, but new UI requests MUST emit the canonical keys. Existing single-format rows/assets using the legacy keys remain read-compatible. The legacy key `"carousel"` is NOT a valid write key. It is a read-only alias resolved on the assemble path only.

### Catalog Endpoint

`GET /api/social/content-generation/formats` returns a list of `FormatCatalogEntryDto`:

```json
{
  "entries": [
    { "formatKey": "story-single", "surface": "Story", "layout": "Single", "medium": "Image",
      "displayLabel": "Story", "isMultiAsset": false, "isAuxiliary": false },
    ...
  ]
}
```

The frontend format picker is driven by this endpoint (3 root buttons: Script / Story / Feed; Single↔Carousel toggle for Story+Feed).

### Requirement: Attribution Guardrail

Prompt generators MUST state that source-origin identity (source author, URL, channel, referent accounts, and competitor accounts) is inspiration/metadata only and must never become target attribution/signature. Visual composers MUST prefer deterministic target-client/brand attribution and reject model-provided signatures that match referent or competitor account handles, labels, or URLs.

### Requirement: FMT-01 — Catalog Is the Single Source of Truth

All downstream consumers MUST derive from `FormatCatalog.All()` rather than maintaining separate literal lists:
- `KnownFormatKeys` API validation whitelist
- `DesignedRasterFormats` (formats that produce visual raster assets)
- `PackagePptxComposer.VisualFormatOrder`
- `ContentFormatCatalog.MediumFor` / `ChannelFor`
- `ContentGenerationServiceExtensions` registrations

Adding a new catalog row MUST automatically flow into all of the above without additional edits.

### Requirement: FMT-02 — Legacy "carousel" Rows Assemble Without Migration

Stored rows with `format_key = 'carousel'` and assets at `carousel/slide_N.png` MUST continue to assemble correctly via the alias layer (`FormatCatalog.ResolveForAssembly("carousel")`). No schema migration is required.

#### Scenario: FMT-02a — Legacy carousel assembles to {client}/carousel/

- GIVEN a stored package row where `format_key = 'carousel'`
- AND assets at `carousel/slide_1.png`, `carousel/slide_2.png`
- WHEN the package is assembled
- THEN the ZIP contains entries under `{client}/carousel/`
- AND no exception is thrown

### Requirement: FMT-03 — New Carousel Keys Use Surface-Scoped Paths

New generations MUST write to surface-scoped storage prefixes that do NOT collide with the legacy `carousel/` prefix.

#### Scenario: FMT-03a — story-carousel writes under story-carousel/

- GIVEN a new package generation with `format_key = 'story-carousel'`
- WHEN the job completes
- THEN assets are stored under `story-carousel/slide_N.png`
- AND the assembled ZIP contains entries under `{client}/story-carousel/`

#### Scenario: FMT-03b — feed-carousel writes under feed-carousel/

- GIVEN a new package generation with `format_key = 'feed-carousel'`
- WHEN the job completes
- THEN assets are stored under `feed-carousel/slide_N.png`
- AND the assembled ZIP contains entries under `{client}/feed-carousel/`

### Requirement: FMT-04 — Extensibility: Adding Video Requires One Catalog Row

Adding a future format (e.g. Story + Single + Video, key `"story-video"`) MUST require:
1. One `FormatDefinition` row in `FormatCatalog`
2. Wiring of a new generator and/or composer for that key

It MUST NOT require changes to `Surface` / `Layout` / `Medium` axis types, the catalog API (`TryGet` / `All` / `BySurface` / `ValidLayoutsFor`), or the derive/normalize layer (`DeriveFormatKey` / `NormalizePackageFormatKey`).

---

## Carousel Coherence, Structured Color-Role Scale, and Advisory Concision — Change: content-background-sourcing-policy

> Added by change `content-background-sourcing-policy`. This section is additive; no existing requirements are modified.

### Requirement: CG-COH-01 — Carousel Background Plan Is Computed Once Before the Slide Loop

For every carousel generation job, a `CarouselBackgroundPlan` MUST be computed exactly ONCE before the per-slide iteration begins. Every slide MUST read its background from this pre-computed plan. No slide MAY trigger an independent `BackgroundSelector.Select()` call after the plan has been computed.

#### Scenario: Pre-loop plan covers all carousel slides (CG-COH-01-a)

- GIVEN a carousel generation job that will produce 5 slides
- WHEN the job processor prepares the carousel
- THEN a single `CarouselBackgroundPlan` is computed before the first slide iteration begins
- AND the background selection function is called exactly ONCE (not once per slide)

#### Scenario: All carousel slides use backgrounds from the shared plan (CG-COH-01-b)

- GIVEN a `CarouselBackgroundPlan` has been computed for a 3-slide carousel
- WHEN each slide's visual composition is executed in sequence
- THEN slide i uses the background assigned to position i in the plan
- AND no slide initiates a new background resolution call

---

### Requirement: CG-COH-02 — CarouselVisualComposer Reads from the Shared Plan; Does Not Independently Select

`CarouselVisualComposer` MUST receive the `CarouselBackgroundPlan` as input and consume per-slide assignments from it. It MUST NOT call `BackgroundSelector.Select()` or any strategy method to re-derive a background independently.

#### Scenario: CarouselVisualComposer uses injected plan, not independent selection (CG-COH-02-a)

- GIVEN a `CarouselBackgroundPlan` is passed to `CarouselVisualComposer`
- WHEN it composes a specific slide
- THEN it reads the background from the injected plan
- AND it does NOT call any background selection or strategy method independently

---

### Requirement: CG-COL-01 — BrandManualSnapshot.ColorPalette Carries Structured (Hex, Role, Rank) Entries

`BrandManualSnapshot.ColorPalette` MUST be `IReadOnlyList<WeightedColor>` where each entry carries `Hex`, `Role` (enum: `Primary / Secondary / Accent / Neutral`), and `Rank` (int, 0 = highest priority within role). The previous flat-hex shape MUST NOT be used anywhere a `BrandManualSnapshot` is consumed.

---

### Requirement: CG-COL-02 — EfClientBrandSnapshotLoader Propagates Role and Rank

`EfClientBrandSnapshotLoader` MUST load `ColorEntry.Role` and rank from persisted data and include them in the constructed `BrandManualSnapshot`. Discarding or ignoring Role / rank is a conformance failure.

---

### Requirement: CG-COL-03 — AI Visual Designer Receives Palette as Prevalence/Weighting Signal

The `AiVisualDesigner` prompt MUST include structured palette entries conveying role prevalence. The Primary color MUST be presented as most dominant, Secondary as second, Accent as accent-level, and Neutral as background/neutral. Weighting language MUST be explicit.

---

### Requirement: CG-CON-01 — Advisory Concision Directive on Single-Format Copy Generators

Copy-generator prompts for story and feed-post formats MUST include an advisory concision directive specifying that copy should be a super-summary. No AI output is rejected on concision grounds.

---

### Requirement: CG-CON-02 — Advisory Concision Directive on Carousel Copy Generators

Carousel copy-generator prompts MUST include an advisory concision directive specifying that all slides serve ONE core concept, with tightly bounded per-slot copy.

---

### Requirement: CG-CON-03 — Concision Violations Are Logged, Never Hard-Rejected

If generated copy exceeds advisory concision guidance, a structured warning MUST be logged identifying job id, format key, slot name, advisory bound, and actual length. The pipeline MUST continue and the copy MUST NOT be discarded or cause a retry on concision grounds alone.

---

### Requirement: CG-CON-04 — ContentGenerationPromptVersions Bumped for All Touched Generators

Every copy-generator whose prompt is modified by this change MUST have its entry in `ContentGenerationPromptVersions` incremented. Bumped constants: `Carousel = "carousel.v3"`, `Story = "story-single.v2"`, `FeedPost = "feed-single.v2"`.

---

*Merged from `openspec/changes/content-background-sourcing-policy/specs/content-generation/spec-delta.md` — change applied 2026-06-20.*

---

<!-- Merged from openspec/changes/content-aesthetic-generation/specs/content-generation/spec-delta.md — change applied 2026-06-21 -->
# Spec Delta: Content Generation — Active ClientAesthetic Path, Richer Profile, Advisory Hints

<!-- Change: content-aesthetic-generation -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Specify the **additive behavioral changes** this change introduces to the
`content-generation` capability:

1. **Active ClientAesthetic orchestration** — the `ClientAesthetic` generation path
   (`BackgroundSourcingPolicy.CaseC`) transitions from a passive descriptor read to an
   active refresh-if-stale orchestration. At generation time the processor resolves the
   client's own account, checks analysis freshness against the client's effective
   staleness threshold (`client.AestheticRefreshThresholdDays ?? 30` days; system
   default 30 days), and fires a non-blocking background refresh when the analysis is
   stale or absent. Generation NEVER waits; it proceeds immediately with best-available data.

2. **Richer `ClientAestheticProfile` value object** — replaces the flat
   `IReadOnlyList<string>` descriptor return. Carries: `BackgroundDescriptors`,
   `VoiceTone?`, `AverageSlidesPerCarousel?`, `AnalyzedAtUtc?`. Designed with nullable
   fields so Phase 2 image URLs slot in without a breaking change.

3. **Profile threaded into copy generators (advisory)** — `VoiceTone` and
   `AverageSlidesPerCarousel` are carried in `GenerationContext` / `GenerationPromptContext`
   as advisory hints. They are ADDITIVE to the existing advisory `ConcisionConstraint`
   and MUST coexist with it; concision guidance is never replaced or hard-blocked by
   aesthetic hints.

4. **Refresh debounce** — at most one in-flight background refresh is enqueued per client
   at any given time; repeated generations within a short window do not duplicate enqueue
   calls.

No rendering, guardrail, or publishing behavior is modified. Phase 2 (real image URLs)
is out of scope.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAG-CG-01 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — orchestration overview |
| CAG-CG-02 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — own account resolution |
| CAG-CG-03 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — freshness check (per-client threshold; system default 30 days) |
| CAG-CG-04 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — fire-and-forget enqueue |
| CAG-CG-05 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — generate NOW regardless |
| CAG-CG-06 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — no analysis degrade |
| CAG-CG-07 | Proposal §ClientAestheticProfile Model — value object shape |
| CAG-CG-08 | Proposal §Approach — profile threaded to background designer AND copy generators |
| CAG-CG-09 | Proposal §Non-Goals — advisory only; concision coexistence |
| CAG-CG-10 | Proposal §Open Questions OQ-8 — refresh debounce |
| CAG-CG-11 | Proposal §ClientAestheticProfile Model — image-URL-ready nullable fields |

---

## ADDED Requirements

---

### Requirement: CAG-CG-01 — ClientAesthetic Path Orchestrates Refresh-if-Stale Before Every Generation

When `policy == ClientAesthetic` (`CaseC`), the `ContentGenerationJobProcessor`
pre-loop seam MUST run a **ClientAestheticEnsureFresh** orchestration step before
any slide generation begins. This step:

- Resolves the client's own account via an Application-layer port
  (`IClientOwnAccountRepository` or equivalent).
- Reads the freshness of the latest `Analyzed` `ProfileAiAnalysis` for the
  `(orgId, clientId)` pair against the client's effective threshold
  (`client.AestheticRefreshThresholdDays ?? 30` days).
- Enqueues a background refresh when the analysis is stale or absent (fire-and-forget).
- Returns the best-available `ClientAestheticProfile` immediately, without awaiting the
  enqueued refresh.

The generation job MUST proceed regardless of freshness state. The orchestration step
MUST NOT be on the critical path that could delay or block slide generation.

#### Scenario: Fresh analysis — used directly, no refresh enqueued (CAG-CG-01-a)

- GIVEN a `CaseC` client with no custom threshold (`AestheticRefreshThresholdDays = null`;
  effective threshold = 30 days)
- AND the latest `Analyzed` `ProfileAiAnalysis` completed `AnalyzedAtUtc = UtcNow - 3 days`
  (within the 30-day system-default threshold)
- WHEN `ContentGenerationJobProcessor` processes a generation job for that client
- THEN the `ClientAestheticProfile` is built from the existing analysis data
- AND no refresh job is enqueued
- AND generation proceeds immediately using the fresh profile

#### Scenario: Stale analysis — used immediately, refresh enqueued in background (CAG-CG-01-b)

- GIVEN a `CaseC` client with no custom threshold (effective threshold = 30 days)
- AND the latest `Analyzed` `ProfileAiAnalysis` completed
  `AnalyzedAtUtc = UtcNow - 45 days` (beyond the 30-day threshold)
- WHEN `ContentGenerationJobProcessor` processes a generation job for that client
- THEN the `ClientAestheticProfile` is built from the stale analysis data
- AND a background refresh job is enqueued (fire-and-forget) via `IProfileAnalysisJobQueue`
- AND generation proceeds immediately without waiting for the enqueued refresh
- AND the generated content is NOT blocked or delayed by the background refresh

#### Scenario: No analysis — degrades to Creative, refresh still enqueued (CAG-CG-01-c)

- GIVEN a `CaseC` client with NO `Analyzed` `ProfileAiAnalysis` record
- WHEN `ContentGenerationJobProcessor` processes a generation job for that client
- THEN the ClientAesthetic path degrades to Creative (AI-palette behavior equivalent
  to the CaseB / no-aesthetic-context fallback)
- AND a structured warning is logged identifying the `clientId`, `orgId`, and the
  reason ("no analysis available")
- AND a background refresh job is enqueued (fire-and-forget)
- AND generation proceeds immediately; the job is NOT aborted

---

### Requirement: CAG-CG-02 — Own Account Resolution; Degrade Safely When Absent

The orchestration step MUST resolve the client's own Instagram account via
`IClientOwnAccountRepository` (or equivalent Application port). The processor has
only a `ClientId`; it MUST NOT hardcode or guess the account. If no own account is
found for the client, the step MUST skip the refresh enqueue (there is nothing to
scrape), log a warning, and degrade to the best-available aesthetic data (or Creative
if no analysis exists). The generation job MUST continue.

#### Scenario: Own account found — refresh can be enqueued (CAG-CG-02-a)

- GIVEN a `CaseC` client with a registered own account `A1`
- AND the analysis is stale
- WHEN the orchestration step runs
- THEN the own account `A1` is resolved via the Application port
- AND a refresh job targeting `A1` is enqueued via `IProfileAnalysisJobQueue`
- AND generation continues

#### Scenario: No own account — refresh skipped, generation continues (CAG-CG-02-b)

- GIVEN a `CaseC` client with NO registered own account
- WHEN the orchestration step runs
- THEN the orchestration step logs a warning that no own account was found
- AND NO refresh job is enqueued
- AND generation continues with the best-available aesthetic data (or Creative if no
  analysis exists)
- AND no exception is thrown and the job is NOT aborted

---

### Requirement: CAG-CG-03 — Staleness Threshold Is Per-Client Configurable; System Default Is 30 Days

The effective staleness threshold for a `CaseC` client MUST be resolved as:

```
effectiveThreshold = client.AestheticRefreshThresholdDays ?? AestheticFreshness.DefaultStalenessThresholdDays
```

where `AestheticFreshness.DefaultStalenessThresholdDays = 30`.

An analysis whose `AnalyzedAtUtc` is more than `effectiveThreshold` days before the
current UTC time is considered stale. An analysis within `effectiveThreshold` days is
considered fresh.

`AestheticRefreshThresholdDays` is a nullable `int?` on the `Client` domain aggregate.
Validation: minimum 1 day, maximum 365 days. `null` means "use the 30-day system default."
The field is persisted on `social.clients` (additive nullable column
`aesthetic_refresh_threshold_days smallint NULL`) and exposed in the client ABM
(Create/Update request + response + web form).

#### Scenario: Client with null threshold uses 30-day system default (CAG-CG-03-a)

- GIVEN a `CaseC` client with `AestheticRefreshThresholdDays = null`
- AND an analysis completed at exactly `UtcNow - 30 days` (boundary)
- WHEN the freshness check runs
- THEN the effective threshold is 30 days
- AND the analysis is treated as fresh (no refresh enqueued)

#### Scenario: One second beyond the 30-day system-default threshold — treated as stale (CAG-CG-03-b)

- GIVEN a `CaseC` client with `AestheticRefreshThresholdDays = null`
- AND an analysis completed at `UtcNow - 30 days - 1 second`
- WHEN the freshness check runs
- THEN the effective threshold is 30 days
- AND the analysis is treated as stale (refresh is enqueued)

#### Scenario: Client with custom threshold — custom value is honored (CAG-CG-03-c)

- GIVEN a `CaseC` client with `AestheticRefreshThresholdDays = 7`
- AND an analysis completed at `UtcNow - 10 days` (beyond the 7-day custom threshold
  but within the 30-day system default)
- WHEN the freshness check runs
- THEN the effective threshold is 7 days (client-specific, not the system default)
- AND the analysis is treated as stale (refresh is enqueued)

#### Scenario: Client with custom threshold at boundary — treated as fresh (CAG-CG-03-d)

- GIVEN a `CaseC` client with `AestheticRefreshThresholdDays = 60`
- AND an analysis completed at exactly `UtcNow - 60 days`
- WHEN the freshness check runs
- THEN the effective threshold is 60 days
- AND the analysis is treated as fresh (no refresh enqueued)

---

### Requirement: CAG-CG-04 — Refresh Is Fire-and-Forget; Generation Never Waits

The background refresh enqueue MUST be asynchronous and non-blocking relative to the
generation job. The generation pipeline MUST NOT await, poll, or coordinate with the
enqueued `ProfileAnalysisJob`. The enqueue call MUST complete promptly (a channel
write or equivalent) and the generation pipeline continues on the NEXT line of code.
Any failure in the enqueue call MUST be caught and logged; it MUST NOT propagate to
the generation job and cause it to fail.

#### Scenario: Refresh enqueue failure does not abort generation (CAG-CG-04-a)

- GIVEN a stale-analysis `CaseC` client
- AND the `IProfileAnalysisJobQueue` enqueue call throws an exception
- WHEN the orchestration step runs
- THEN the exception is caught and logged as a warning
- AND the generation job continues without the refresh
- AND the generation pipeline does NOT propagate the enqueue exception

#### Scenario: Generation proceeds on the next operation after enqueue without polling (CAG-CG-04-b)

- GIVEN a stale-analysis `CaseC` client and a successful enqueue
- WHEN the orchestration step returns
- THEN the processor immediately proceeds to the slide generation loop
- AND no await, Task.Delay, or polling loop is inserted between the enqueue and generation
- AND the generation job completes in a time unaffected by the async analysis pipeline

---

### Requirement: CAG-CG-05 — ClientAestheticProfile Value Object Carries Richer Data

The `ClientAestheticProfile` value object (Application layer) MUST carry:
- `BackgroundDescriptors: IReadOnlyList<string>` — text descriptors from
  `ProfileAiAnalysis.DetectedBackgroundImages`.
- `VoiceTone: string?` — copy-style/voice signal from the profile analysis; nullable.
- `AverageSlidesPerCarousel: double?` — server-derived stat; nullable.
- `AnalyzedAtUtc: DateTime?` — freshness provenance; nullable (null when no analysis).

The type MUST be designed with nullable Phase 2 fields (e.g., `BackgroundImageUrls`) as
a forward-compatibility contract so image URLs can be added later WITHOUT a breaking
change to any consumer. No image-URL field is populated or read in Phase 1.

#### Scenario: Profile built from a completed analysis carries all four fields (CAG-CG-05-a)

- GIVEN a completed `ProfileAiAnalysis` with `DetectedBackgroundImages = ["soft gradients"]`,
  `VoiceTone = "Warm and motivational"`, `AverageSlidesPerCarousel = 5.3`,
  `CompletedAtUtc = 2026-06-01T10:00:00Z`
- WHEN `ClientAestheticProfile` is constructed from that analysis
- THEN `BackgroundDescriptors = ["soft gradients"]`
- AND `VoiceTone = "Warm and motivational"`
- AND `AverageSlidesPerCarousel = 5.3`
- AND `AnalyzedAtUtc = 2026-06-01T10:00:00Z`

#### Scenario: Profile built from no analysis — all nullable fields are null (CAG-CG-05-b)

- GIVEN no completed `ProfileAiAnalysis` exists for the client
- WHEN a degraded `ClientAestheticProfile` is constructed for the Creative fallback path
- THEN `BackgroundDescriptors` is empty
- AND `VoiceTone` is null
- AND `AverageSlidesPerCarousel` is null
- AND `AnalyzedAtUtc` is null

#### Scenario: Phase 2 image URL fields absent without breaking existing consumers (CAG-CG-05-c)

- GIVEN the completed Phase 1 implementation
- WHEN a Phase 2 nullable `BackgroundImageUrls: IReadOnlyList<string>?` field is added
  to `ClientAestheticProfile`
- THEN all existing Phase 1 consumers compile and behave identically
- AND no existing mapping, constructor, or consumer requires modification

---

### Requirement: CAG-CG-06 — ClientAestheticProfile Threaded into Both Background Designer and Copy Generators

The `ClientAestheticProfile` resolved during the pre-loop orchestration step MUST be
passed to:
- The **AI background designer** (`AiVisualDesigner` / `CarouselBackgroundPlan`) via the
  existing `BackgroundSelection.ClientAesthetic` path — `BackgroundDescriptors` used for
  aesthetic context (existing behavior, now richer).
- **All copy generators** (`CarouselGenerator`, `StoryGenerator`, `FeedPostGenerator`)
  via `GenerationContext` / `GenerationPromptContext` — `VoiceTone` and
  `AverageSlidesPerCarousel` included as advisory hints.

Neither path MAY receive the profile through an Infrastructure or EF type. The profile
flows through Application types only.

#### Scenario: Background designer receives BackgroundDescriptors (CAG-CG-06-a)

- GIVEN a `CaseC` generation job with a `ClientAestheticProfile` carrying
  `BackgroundDescriptors = ["moody dark gradients", "minimal white space"]`
- WHEN the carousel background plan is computed
- THEN the AI visual designer prompt includes those descriptor strings as aesthetic context
- AND no Infrastructure or EF Core type is present in the data path from profile to prompt

#### Scenario: Copy generators receive VoiceTone and AverageSlidesPerCarousel hints (CAG-CG-06-b)

- GIVEN a `CaseC` generation job with `VoiceTone = "Playful and energetic"` and
  `AverageSlidesPerCarousel = 6.0`
- WHEN a `CarouselGenerator` builds its copy prompt
- THEN the prompt carries a copy-style section referencing `VoiceTone` as an advisory
  style hint
- AND the prompt carries a carousel-structure section referencing the avg-slides hint
- AND neither hint is absent from the assembled prompt

---

### Requirement: CAG-CG-07 — VoiceTone Is Advisory; Read Directly Into Generation Context; Not Imputed Into BrandManual

`VoiceTone` from the `ClientAestheticProfile` MUST be read directly into
`GenerationContext` / `GenerationPromptContext` at generation time. It MUST NOT be
written to or merged with `BrandManual.VoiceAndTone` or any persisted brand field.
It operates as a transient, advisory signal that layers over (not replaces) the
brand-manual voice guidance already present in the generation context.

#### Scenario: VoiceTone from aesthetic profile does not mutate BrandManual (CAG-CG-07-a)

- GIVEN a `CaseC` client with `BrandManual.VoiceAndTone = "Professional and concise"`
  and `ClientAestheticProfile.VoiceTone = "Energetic"`
- WHEN generation runs
- THEN `BrandManual.VoiceAndTone` remains `"Professional and concise"` after the job
- AND no write or update to any `BrandManual` or `BrandManualSnapshot` field occurs
  as a result of this generation job
- AND both guidance values are present in `GenerationPromptContext` (brand manual tone
  AND aesthetic profile tone)

#### Scenario: VoiceTone null — no advisory section in prompt (CAG-CG-07-b)

- GIVEN a `CaseC` client with `ClientAestheticProfile.VoiceTone = null`
- WHEN generation runs
- THEN the copy generators receive no advisory VoiceTone section
- AND no null reference exception is thrown
- AND generation completes normally

---

### Requirement: CAG-CG-08 — AverageSlidesPerCarousel Is Advisory; Clamped 3–10; Never Hard-Rejects AI Output

`AverageSlidesPerCarousel` from the `ClientAestheticProfile` MUST be threaded into
carousel copy-generator prompts as a **soft advisory hint only**. The hint MUST be
clamped to the range `[3, 10]` before inclusion in the prompt. The system MUST NOT
reject, retry, or fail AI-generated copy solely because the slide count deviates from
this hint. The hint coexists with and MUST NOT replace the existing advisory
`ConcisionConstraint`.

#### Scenario: Avg-slides hint present in carousel prompt (CAG-CG-08-a)

- GIVEN a `CaseC` carousel job with `AverageSlidesPerCarousel = 7.0`
- WHEN `CarouselGenerator` assembles its copy prompt
- THEN the prompt contains an advisory sentence communicating that the client typically
  uses approximately 7 slides per carousel
- AND the hint is labeled as advisory (e.g., "typically uses around N slides")

#### Scenario: Avg-slides clamped to 10 when raw value exceeds range (CAG-CG-08-b)

- GIVEN `AverageSlidesPerCarousel = 12.5` (above the 10 ceiling)
- WHEN the hint is computed for the prompt
- THEN the value included in the prompt is 10 (clamped)
- AND no exception is thrown

#### Scenario: Avg-slides clamped to 3 when raw value is below range (CAG-CG-08-c)

- GIVEN `AverageSlidesPerCarousel = 1.5` (below the 3 floor)
- WHEN the hint is computed for the prompt
- THEN the value included in the prompt is 3 (clamped)

#### Scenario: Avg-slides null — hint section absent from prompt (CAG-CG-08-d)

- GIVEN `AverageSlidesPerCarousel = null` (childrenCount unavailable)
- WHEN `CarouselGenerator` assembles its copy prompt
- THEN no avg-slides advisory section appears in the prompt
- AND generation proceeds normally without the hint

#### Scenario: AI output with slide count deviating from hint is NOT rejected (CAG-CG-08-e)

- GIVEN a carousel prompt with avg-slides hint of 6
- AND the AI returns copy structured for 8 slides
- WHEN the copy guardrail runs
- THEN the AI output is accepted
- AND no retry or failure is triggered solely because the AI chose a different slide count

---

### Requirement: CAG-CG-09 — Copy-Style and ConcisionConstraint Coexist; Concision Is Not Replaced

The `VoiceTone` advisory hint and the `AverageSlidesPerCarousel` advisory hint MUST
be **additive** to the existing advisory `ConcisionConstraint` already defined in the
`content-generation` spec (requirements `CG-CON-01` through `CG-CON-04` from the
predecessor change). Neither aesthetic hint replaces, overrides, or removes the
concision directive. Both coexist in the same prompt. When in conflict, no system
rule adjudicates between them; the AI receives both and decides. Neither is treated
as a hard constraint.

#### Scenario: Carousel prompt carries both concision directive and aesthetic hints (CAG-CG-09-a)

- GIVEN a `CaseC` carousel job with `VoiceTone = "Bold"` and `AverageSlidesPerCarousel = 5`
- WHEN `CarouselGenerator` assembles its copy prompt
- THEN the prompt contains the existing concision directive (one concept across slides,
  per-slot length guidance)
- AND the prompt ALSO contains the VoiceTone advisory section
- AND the prompt ALSO contains the avg-slides advisory section
- AND no section replaces or suppresses the others

#### Scenario: Non-CaseC jobs still carry concision directives without aesthetic hints (CAG-CG-09-b)

- GIVEN a generation job for a client with policy `CaseA` (not `CaseC`)
- WHEN copy generators assemble their prompts
- THEN the existing concision directive is present
- AND no VoiceTone or AverageSlidesPerCarousel hint section is added
- AND no null reference exception is thrown

---

### Requirement: CAG-CG-10 — Refresh Debounce: At Most One In-Flight Refresh Per Client

When a ClientAesthetic generation job triggers a background refresh, the system MUST
check whether a refresh for the same `clientId` is already in-flight (queued or
running). If one is already in-flight, the new enqueue call MUST be skipped silently
(no duplicate enqueue, no error). The debounce window is per-client; different clients
are independent.

#### Scenario: First generation triggers refresh; second within debounce window skips enqueue (CAG-CG-10-a)

- GIVEN a `CaseC` client with a stale analysis
- AND a first generation job enqueues a background refresh for that client
- WHEN a SECOND generation job runs for the same client before the refresh completes
- THEN the second job detects the in-flight refresh and does NOT enqueue another refresh
- AND only ONE refresh job exists in the queue for this client
- AND both generation jobs complete normally

#### Scenario: Different clients are debounced independently (CAG-CG-10-b)

- GIVEN two `CaseC` clients `C1` and `C2`, both with stale analyses
- WHEN generation jobs run for `C1` and `C2` simultaneously
- THEN a refresh is enqueued for `C1` AND a refresh is enqueued for `C2`
- AND neither client's debounce state affects the other

---

### Requirement: CAG-CG-11 — Prompt Version Bumped for All Modified Copy Generators

Every copy-generator whose prompt is modified by this change (to carry VoiceTone
and/or AverageSlidesPerCarousel advisory hints) MUST have its entry in
`ContentGenerationPromptVersions` incremented. No previously-versioned prompt that
receives changes in this change may retain its old version constant.

#### Scenario: Prompt versions incremented for all modified generators (CAG-CG-11-a)

- GIVEN the completed implementation
- WHEN `ContentGenerationPromptVersions` is inspected
- THEN every generator whose prompt was modified by this change has a version value
  higher than its pre-change value
- AND no modified generator retains its pre-change version constant

---

## MODIFIED Requirements

### Requirement Delta: BSP-04 — CASE C Degrade Path Adds Refresh Fire-and-Forget

`BSP-04` from the predecessor change (`content-background-sourcing-policy`) specified
that CASE C degrades to CASE B behavior and logs a warning when no completed
`ProfileAiAnalysis` exists. This change **adds** a second obligation to that path:

> When CASE C degrades due to no-analysis, a background refresh job MUST ALSO be
> enqueued (fire-and-forget) so that the NEXT generation can use a fresh analysis.

The existing `BSP-04-b` scenario (degrade + log) remains valid; it is supplemented
by the new `CAG-CG-01-c` scenario (degrade + log + enqueue refresh). BSP-04-b is
NOT superseded; both obligations apply.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative, for test authoring)

- **CAG-CG-01** MUST be covered by unit tests on the orchestration service with a mocked
  repository: (a) fresh analysis → profile returned, no enqueue; (b) stale analysis →
  profile returned, enqueue called once; (c) no analysis → Creative fallback, enqueue
  called once, warning logged.
- **CAG-CG-02** MUST be covered by a unit test where `IClientOwnAccountRepository` returns
  null → no enqueue call, no exception, generation proceeds.
- **CAG-CG-03** parameterized tests: (a) `AestheticRefreshThresholdDays = null`, analysis at `UtcNow - 30d` → fresh; (b) `null` threshold, analysis at `UtcNow - 30d - 1s` → stale (enqueued); (c) custom threshold 7 days, analysis at `UtcNow - 10d` → stale (custom threshold wins over system default); (d) custom threshold 60 days, analysis at `UtcNow - 60d` → fresh. Assert enqueue behavior for each.
- **CAG-CG-04** MUST be covered by a test where the queue mock throws → exception caught,
  generation continues, warning logged.
- **CAG-CG-05** is a pure value-object unit test: verify all four fields, verify null
  profile shape, verify forward-compat shape.
- **CAG-CG-06** MUST be verified by unit tests on `ContentGenerationJobProcessor` that
  (a) assert `BackgroundDescriptors` reach `AiVisualDesigner` prompt, and (b) assert
  `VoiceTone` and `AverageSlidesPerCarousel` reach copy-generator prompts.
- **CAG-CG-07** MUST be verified by asserting no mutation occurs on any brand-manual
  or snapshot object during the generation run.
- **CAG-CG-08** MUST be verified by parameterized tests: raw value 12.5 → prompt receives
  10; raw value 1.5 → prompt receives 3; null → no section in prompt; AI output with
  deviant slide count → accepted (no retry triggered).
- **CAG-CG-09** prompt-content test: assert both concision directive text AND aesthetic
  hint text are present in the same assembled prompt string for CaseC; assert non-CaseC
  prompt has concision directive but NO aesthetic hint text.
- **CAG-CG-10** MUST be verified by a unit test that calls the debounce check twice for
  the same client — assert `IProfileAnalysisJobQueue.Enqueue` is called exactly ONCE.
- **CAG-CG-11**: compile-time / snapshot test on `ContentGenerationPromptVersions` — CI
  fails if a modified generator's constant matches its pre-change value.
- Architecture gate: no EF Core or Npgsql type may appear in `ClientAestheticProfile`
  or the orchestration service interface. The `pwsh -File scripts/gates/Run-Gates.ps1`
  gate enforces this.

*Merged from `openspec/changes/content-aesthetic-generation/specs/content-generation/spec-delta.md` — change applied 2026-06-21.*

---

<!-- Merged from openspec/changes/content-image-generation/specs/content-generation/spec-delta.md — change applied 2026-06-24 (WU-10) -->

## Added Requirements: ClientAesthetic Creative-vs-Flat Routing (content-image-generation)

### Requirement: CIG-CG-01 — ClientAesthetic Carousels Route Slides as Creative or Flat

For `ClientAesthetic` carousel jobs, each slide MUST be classified as either **creative**
(from the client's configured creative-slide set) or **flat** before compositing begins.
Flat slides MUST continue to use the existing CSS designer path unchanged.

#### Scenario: Creative slides identified by position; flat slides use CSS (CIG-CG-01-a)

- GIVEN a `ClientAesthetic` carousel with 5 slides and a creative-slide set = `{0, 4}` (first + last)
- WHEN the carousel generator prepares slide assignments
- THEN slides at positions 0 and 4 are marked as creative (image-gen path)
- AND slides at positions 1, 2, 3 are marked as flat (CSS designer path)
- AND the CSS designer is invoked for flat slides exactly as before this change

#### Scenario: No creative-slide set configured — all slides use CSS designer (CIG-CG-01-b)

- GIVEN a `ClientAesthetic` client with no creative-slide set on `ClientAestheticProfile`
- WHEN the carousel generator runs
- THEN all slides take the CSS designer path (no image-gen attempted)
- AND the job completes normally

#### Scenario: Non-ClientAesthetic clients are unaffected (CIG-CG-01-c)

- GIVEN a client with `BackgroundSourcingPolicy` other than `ClientAesthetic`
- WHEN a carousel job runs
- THEN no creative-vs-flat routing is applied
- AND the existing carousel path executes unchanged

---

### Requirement: CIG-CG-02 — Generated Background Produces an Editable-Text Slide

For creative slides, the generated raster image URL MUST be injected as
`BackgroundSelection.ForcedDefault(url, "ai-generated-background")` so the existing
per-slide compositing path places the image as `data-layer=background` with
`data-layer=content` text remaining fully editable in the Content Studio.

#### Scenario: Text on a generated-background slide is editable in the studio (CIG-CG-02-a)

- GIVEN a creative slide whose background was generated and stored in Supabase
- WHEN the composition envelope is built using `BackgroundSelection.ForcedDefault(url, ...)`
- THEN the slide's background layer carries the raster image URL
- AND the slide's content layer carries text fields rendered as editable in the Content Studio
- AND the studio can modify text without touching the background layer

#### Scenario: Composition envelope carries a URL, not inline bytes (CIG-CG-02-b)

- GIVEN a successful image render and storage upload
- WHEN the `BackgroundSelection.ForcedDefault` value is built
- THEN the background reference is a durable URL or storage path
- AND `composition_spec_json` contains no base-64 or data-URI representation of the image

---

### Requirement: CIG-CG-03 — CSS Designer Fallback on Any Image-Gen Failure; Job Always Completes

If any step of the image-gen pipeline fails for a creative slide (prompt write, render,
or storage upload), the CSS designer background MUST be used for that slide. The failure
MUST be logged. The job MUST NOT be marked failed on image-gen error.

#### Scenario: Image-gen failure for one slide — other slides and job unaffected (CIG-CG-03-a)

- GIVEN a `ClientAesthetic` carousel with creative slides at positions 0 and 4
- AND image-gen fails (any reason) for position 0
- WHEN the carousel completes
- THEN slide 0 uses the CSS designer background
- AND slide 4 proceeds through image-gen normally (independent of slide 0's failure)
- AND the job completes with a success status
- AND the failure is logged with job id, slide index, and reason

#### Scenario: All image-gen steps fail — job completes with all-CSS output (CIG-CG-03-b)

- GIVEN image-gen fails for every creative slide in a job
- WHEN the carousel completes
- THEN all slides use the CSS designer background
- AND the job completes normally (no job-level failure raised)

---

### Requirement: CIG-CG-04 — Two-Step Flow Drives Each Creative Slide Independently

Each creative slide MUST execute the two-step flow (prompt write → pixel render)
independently. A failure in one slide's two-step sequence MUST NOT abort the sequence
for another slide.

#### Scenario: Per-slide two-step flow; each slide's failure is isolated (CIG-CG-04-a)

- GIVEN a carousel with two creative slides
- AND the render step fails for slide 0 (provider error)
- WHEN the carousel generator processes slide 4 next
- THEN slide 4's two-step flow executes independently
- AND slide 4 can succeed even though slide 0 fell back to CSS
- AND both outcomes are logged

*Merged from `openspec/changes/content-image-generation/specs/content-generation/spec-delta.md` — change applied 2026-06-24.*

---

<!-- Merged from openspec/changes/client-aligned-carousel-generation/specs/content-generation/spec-delta.md — change applied 2026-06-28 -->

## Added Requirements: Slide Count Enforcement, Color-Pairing Palette, and Handle Slot (client-aligned-carousel-generation)

### Requirement: CAC-CG-01 — `CarouselGenerator` Enforces `TypicalSlideCount` When Captured

When `ClientAestheticProfile.TypicalSlideCount` is non-null, `CarouselGenerator` MUST
use that value as the target slide count, overriding the hardcoded `~3-5` range. When
`TypicalSlideCount` is null, the existing `~3-5` range applies unchanged. This requirement
is independent of `CAG-CG-08` (the advisory avg-slides hint in copy prompts), which
remains valid and unchanged.

#### Scenario: Captured count enforced — carousel generated at client's typical length (CAC-CG-01-a)

- GIVEN a `ClientAesthetic` carousel job with `TypicalSlideCount = 7`
- WHEN `CarouselGenerator` runs
- THEN the carousel is generated with 7 slides
- AND the hardcoded `~3-5` range does not apply

#### Scenario: `TypicalSlideCount` null — existing range applies unchanged (CAC-CG-01-b)

- GIVEN a `ClientAesthetic` carousel job with `TypicalSlideCount = null`
- WHEN `CarouselGenerator` runs
- THEN the carousel is generated within the current `~3-5` slide range
- AND no error is raised for the absent count

---

### Requirement: CAC-CG-02 — `CompositionDefaults.ResolvePalette()` Applies Captured Color Pairings; Positional Fallback When Absent

When `ClientAestheticProfile.ColorPairings` is non-null and non-empty,
`CompositionDefaults.ResolvePalette()` MUST assign background and text colors from
the captured `{BackgroundHex, TextHex}` pairings according to the client's observed
criterion. When `ColorPairings` is null or empty, positional array-position palette
assignment MUST be used unchanged.

#### Scenario: Captured pairings applied — colors follow client criterion (CAC-CG-02-a)

- GIVEN a `ClientAesthetic` carousel job with `ColorPairings = [{BackgroundHex: "#1A1A2E", TextHex: "#E0E0E0"}]`
- WHEN `CompositionDefaults.ResolvePalette()` is called for a slide
- THEN the slide background color is `#1A1A2E` and text color is `#E0E0E0`
- AND array-position palette assignment is NOT used

#### Scenario: Absent pairings — positional palette assignment runs unchanged (CAC-CG-02-b)

- GIVEN a `ClientAesthetic` carousel job with `ColorPairings = null`
- WHEN `CompositionDefaults.ResolvePalette()` is called
- THEN colors are assigned by existing positional palette order
- AND the job completes normally with no error

---

### Requirement: CAC-CG-03 — `CarouselVisualComposer` Gains `@handle`/Signature Slot

`CarouselVisualComposer` MUST include a dedicated composition slot for the client's
`@handle` and signature at parity with `StoryVisualComposer` and `FeedPostVisualComposer`.
The slot MUST be rendered when `BrandSnapshot.Handle` is non-null. When `Handle` is null
or empty, the slot MUST be empty or omitted with no error.

#### Scenario: Handle present — slot rendered in carousel composition (CAC-CG-03-a)

- GIVEN a `ClientAesthetic` carousel job where `BrandSnapshot.Handle = "@clientbrand"`
- WHEN `CarouselVisualComposer` composes a slide
- THEN the `@handle` slot carries `"@clientbrand"` in the composition envelope
- AND the slot position is consistent with the story/feed-post format parity

#### Scenario: Handle absent — slot empty; composition succeeds (CAC-CG-03-b)

- GIVEN `BrandSnapshot.Handle = null`
- WHEN `CarouselVisualComposer` composes a slide
- THEN the `@handle` slot is empty or omitted
- AND no exception is raised and the slide renders normally

---

### Requirement: CAC-CG-04 — `BrandSnapshot` Carries `Handle` from `ClientOwnAccount` via Application Port

The `BrandSnapshot` loader MUST load `ClientOwnAccount.Handle` into the snapshot via
an Application-layer port (no Infrastructure type in the snapshot or loader port signature),
making it available as `BrandSnapshot.Handle` at composition time. When no own account
exists for the client, `Handle` MUST be null. This field is additive and MUST NOT break
any existing consumer of `BrandSnapshot` per BS-01.

#### Scenario: Own account found — `Handle` populated in snapshot (CAC-CG-04-a)

- GIVEN a client with a registered own account whose handle is `"@brand"`
- WHEN the `BrandSnapshot` is assembled
- THEN `BrandSnapshot.Handle = "@brand"`
- AND no Infrastructure or provider type appears in the snapshot or the loader port

#### Scenario: No own account — `Handle` null; snapshot assembles normally (CAC-CG-04-b)

- GIVEN a client with no registered own account
- WHEN the `BrandSnapshot` is assembled
- THEN `BrandSnapshot.Handle = null`
- AND snapshot assembly succeeds and all existing consumers are unaffected

*Merged from `openspec/changes/client-aligned-carousel-generation/specs/content-generation/spec-delta.md` — change applied 2026-06-28.*

---

<!-- Merged from openspec/changes/client-aesthetic-conceptual-detection/specs/content-generation/spec-delta.md — change applied 2026-07-01 -->

## Added Requirements: Concept Ideation in ImagePromptWriter, Ratio-Aware Prompt (client-aesthetic-conceptual-detection)

### Purpose

Upgrade `ImagePromptWriter` to perform concept ideation when the client's
`ConceptualMethod` is known, and to produce ratio-aware instructions when
`ImageTypeRatio` is known. When both fields are null (cold-start), the output
MUST be identical to the pre-change prompt.

### Client Exemplars (Anchors for Acceptance Tests)

| Client | Method | Expected Ideation Output |
|---|---|---|
| Atlas (`@atlas.editorial`) | Metaphorical-editorial: idea-driven visual metaphors via mono-color still-life and staged photography | A concrete staged still-life or editorial composition that metaphorically represents the slide's topic; saturated mono-color background; no text in the image |
| Comet Comedy | Literal-illustration: tender watercolor illustrations of children | A specific tender watercolor scene literally depicting the slide's topic with children characters; warm cream background; no text in the image |

---

### Requirement: CACD-CG-01 — `ImagePromptWriter` Performs Concept Ideation When `ConceptualMethod` Is Non-Null

When `ClientAestheticProfile.ConceptualMethod` is non-null, `ImagePromptWriter.BuildInput`
(or equivalent prompt-assembly method) MUST invent a method-faithful visual concept for
the slide's specific topic — a concrete visual metaphor or literal scene decided in the
client's detected art-direction method. The invented concept MUST appear in the image
generation instruction. The background MUST remain TEXT-FREE (consistent with CAC-IMG-02).
The resulting instruction MUST be discernibly richer than a thin style-descriptor prompt.

Atlas-type prompts MUST contain a specific metaphorical or editorial composition concept.
Comet-type prompts MUST contain a specific tender illustrative scene concept.
Both MUST be distinguishably different for the same input topic.

#### Scenario: Metaphorical-editorial method → specific metaphor invented (CACD-CG-01-a)

- GIVEN a `ClientAesthetic` carousel job with `ConceptualMethod` identifying
  Atlas's metaphorical-editorial method (idea-driven visual metaphors,
  mono-color still-life, staged photography)
- AND a slide topic of "overcoming fear of failure"
- WHEN `ImagePromptWriter` builds the image instruction
- THEN the instruction contains a specific invented visual concept that metaphorically
  represents "overcoming fear of failure" (e.g., a staged object composition or
  editorial scene, NOT a generic "representing fear" background descriptor)
- AND the instruction does NOT include text, labels, handles, or signatures to be
  embedded in the generated image

#### Scenario: Literal-illustration method → specific scene invented (CACD-CG-01-b)

- GIVEN a `ClientAesthetic` carousel job with `ConceptualMethod` identifying
  Comet's literal tender illustration method (watercolor illustrations of children)
- AND a slide topic of "children learning to share"
- WHEN `ImagePromptWriter` builds the image instruction
- THEN the instruction contains a specific tender watercolor scene literally depicting
  children sharing (NOT a generic "illustration style" background descriptor)
- AND the instruction does NOT include text, labels, handles, or signatures to be
  embedded in the generated image

#### Scenario: Same topic, different methods → distinguishably different instructions (CACD-CG-01-c)

- GIVEN both an Atlas-type client (CACD-CG-01-a) and a Comet-type client (CACD-CG-01-b)
  with the same slide topic "growth through challenge"
- WHEN `ImagePromptWriter` builds the instruction for each
- THEN the Atlas instruction contains a metaphorical editorial concept
- AND the Comet instruction contains a literal tender illustrative scene
- AND the two instructions are discernibly different from each other

#### Scenario: `ConceptualMethod` null → no ideation section added (CACD-CG-01-d)

- GIVEN `ClientAestheticProfile.ConceptualMethod = null`
- WHEN `ImagePromptWriter.BuildInput` runs
- THEN no concept ideation section appears in the instruction
- AND the output is the same as the pre-change system's output for the same inputs

---

### Requirement: CACD-CG-02 — `ImagePromptWriter` Is Ratio-Aware When `ImageTypeRatio` Is Non-Null

When `ClientAestheticProfile.ImageTypeRatio` is non-null, `ImagePromptWriter` MUST
include a background-type cue in the image instruction that is consistent with the
detected ratio for the slide's position. The cue MUST guide the model toward a
photographic/staged-photography background OR a flat/mono-color background as
appropriate for the slide type.

#### Scenario: Photo-type slide → photographic background cue included (CACD-CG-02-a)

- GIVEN `ImageTypeRatio` and a slide position that calls for a photographic background
- WHEN `ImagePromptWriter` builds the instruction
- THEN the instruction includes a cue directing the model toward a photographic or
  staged-photography background
- AND the instruction does NOT direct the model toward a flat or mono-color background
  for this position

#### Scenario: Flat-type slide → flat/mono-color background cue included (CACD-CG-02-b)

- GIVEN `ImageTypeRatio` and a slide position that calls for a flat-color background
- WHEN `ImagePromptWriter` builds the instruction
- THEN the instruction includes a cue directing the model toward a flat or mono-color
  background
- AND the instruction does NOT direct the model toward a photographic background
  for this position

#### Scenario: `ImageTypeRatio` null → no ratio cue added; existing instruction unchanged (CACD-CG-02-c)

- GIVEN `ClientAestheticProfile.ImageTypeRatio = null`
- WHEN `ImagePromptWriter` builds the instruction
- THEN no ratio-specific background-type section is added to the instruction
- AND the existing style-descriptor-driven instruction runs unchanged

---

### Requirement: CACD-CG-03 — Cold-Start Parity: Null Conceptual Fields → Instruction Identical to Pre-Change Output

When both `ConceptualMethod` and `ImageTypeRatio` are null on `ClientAestheticProfile`,
`ImagePromptWriter` MUST produce an image instruction identical to the one produced
by the pre-change system for the same inputs. No ideation section and no ratio section
MUST be added.

#### Scenario: Both fields null → legacy-identical instruction (CACD-CG-03-a)

- GIVEN `ClientAestheticProfile.ConceptualMethod = null`
- AND `ClientAestheticProfile.ImageTypeRatio = null`
- WHEN `ImagePromptWriter.BuildInput` runs
- THEN the resulting instruction is identical to the pre-change output for the
  same `ClientAestheticProfile` and slide inputs
- AND no ideation or ratio section appears in the output

---

## Modified Requirements (client-aesthetic-conceptual-detection)

None. `CAG-CG-06` (profile threaded into background designer and copy generators)
remains valid; CACD-CG-01 and CACD-CG-02 extend `ImagePromptWriter` behavior
when the new profile fields are non-null, additive to the existing path.
`CAC-IMG-02` (background-only contract) is reinforced, not modified: the concept-ideated
instruction MUST still be text-free.

*Merged from `openspec/changes/client-aesthetic-conceptual-detection/specs/content-generation/spec-delta.md` — change applied 2026-07-01.*
## Added Requirements: Single-Analyzer Generation History UX (#46)

### Requirement: Preserve successful generation state after a failed regeneration

The saved-analysis history MUST preserve the existence and downloadable job id of the newest
successful package independently from the status of the latest generation attempt. When a later
regeneration fails, the UI MUST show both `Generated` and `Regeneration failed`, and package
download MUST target the successful package job rather than the failed attempt.

#### Scenario: Regeneration fails after a successful package

- GIVEN an analysis has a previously generated package
- WHEN its latest regeneration attempt fails
- THEN the history row shows a green `Generated` badge and a `Regeneration failed` badge
- AND downloading uses the job that owns the existing package

### Requirement: Historical hydration does not navigate tabs

Opening a saved analysis MUST preserve the user's current analyzer tab while the historical job is
reattached. Automatic navigation to Package MAY occur only when a generation initiated by the user
in the current view completes.

#### Scenario: User opens an analysis with generated content

- GIVEN a saved analysis has a completed generation job
- WHEN the user opens that saved analysis and its job status is hydrated
- THEN the active tab does not change without a new user-initiated generation

## Added Requirements: Faithful Flat-Slide Rendering + Brand-Mark Placement (faithful-flat-slide-rendering, #50)

### Requirement: FFS-CG-01 — FlatColor Slides Render Deterministic Flat Background, Bypassing the AI Designer

For a `ClientAesthetic` carousel, when the per-slide analyzed kind (mapped via `CreativeSlideIndexResolver`
from `ClientAestheticProfile.BackgroundPattern`) is `SlideBackgroundKind.FlatColor` AND `ColorPairings`
resolves a pairing, the slide MUST render a flat background+text pairing from
`CompositionDefaults.ResolvePalette()` with NO gradient, blob, glow, grain, or baked decoration, and
MUST NOT call `AiVisualDesigner.ComposeAsync`.

#### Scenario: FlatColor with resolved pairing renders flat, designer skipped (FFS-CG-01-a)

- GIVEN a ClientAesthetic slide mapped to FlatColor with a resolved `ColorPairings` entry
- WHEN the slide renders
- THEN background/text colors match the pairing, rank/rotation preserved
- AND no gradient/blob/glow/grain is present and `ComposeAsync` is not invoked

#### Scenario: Photo slide unaffected (FFS-CG-01-b)

- GIVEN a slide mapped to a Photo kind
- WHEN it renders
- THEN it still uses the AI-generated background path unchanged

#### Scenario: Non-ClientAesthetic format unaffected (FFS-CG-01-c)

- GIVEN a carousel job whose policy is not ClientAesthetic
- WHEN it renders
- THEN output is byte-identical to pre-change behavior

### Requirement: FFS-CG-02 — Accent and Brand-Mark Elements Are Editable Content-Layer Slots, Never Baked

Any accent element or brand-mark element on a rendered slide MUST be emitted as an editable
content-layer element (`data-slot="accent"` / `data-slot="brand-mark"`) and MUST NOT be rasterized
into the background image. The brand-mark injection is identical whichever markup path produced the
slide (the FlatColor short-circuit or the AI/photo designer path) — the AI designer MUST NEVER draw,
trace, or place a brand mark itself.

#### Scenario: Brand mark rendered as editable slot (FFS-CG-02-a)

- GIVEN `BrandMark.Present = true` with a position hint and the pairing's text color
- WHEN the slide renders (flat or AI/photo path)
- THEN a `data-slot="brand-mark"` element appears at the detected position in the text color, conservative size
- AND it is not part of the rasterized background

#### Scenario: No brand mark detected — no slot rendered (FFS-CG-02-b)

- GIVEN `BrandMark` is null or `BrandMark.Present = false`
- WHEN the slide renders
- THEN no brand-mark element is emitted and the slide is otherwise unaffected

#### Scenario: Ambiguous/unknown mark fields treated as absent (FFS-CG-02-c)

- GIVEN extraction produced an unknown/ambiguous position or color
- WHEN the persisted `BrandMark` is evaluated at render
- THEN it is treated as not present; no mark is rendered

### Requirement: FFS-CG-03 — Cold-Start Parity for Flat Rendering and Brand Mark

When `ColorPairings`/`BackgroundPattern` do not resolve a FlatColor decision, OR `BrandMark` is
absent, rendering MUST be byte-identical to pre-change behavior. No palette or mark MAY be
fabricated.

#### Scenario: Cold-start client renders unchanged (FFS-CG-03-a)

- GIVEN a profile with no `ColorPairings`/`BackgroundPattern`/`BrandMark` (Phase-1-only or unanalyzed)
- WHEN the carousel renders
- THEN output matches pre-change output exactly, including AI-designer usage where it previously applied

*Merged from SDD change `faithful-flat-slide-rendering` (issue #50) — applied 2026-07-06.*

### Requirement: BGREF-01 — New Generations Store a Storage Path in BackgroundImageRef

For every NEW content generation job that captures a background-only PNG, the
`ContentGenerationJobProcessor` MUST upload that PNG via `IGeneratedAssetStorage`
and set `CompositionSpecEnvelope.BackgroundImageRef` to the returned storage
path. The processor MUST NOT embed a base64 `data:` URI into the persisted
envelope for new generations.

#### Scenario: New generation persists a storage path, not base64

- GIVEN a content generation job captures a background-only PNG for a slide
- WHEN the job persists `CompositionSpecEnvelope` to `composition_spec_json`
- THEN `BackgroundImageRef` is a storage path (not a `data:` URI)
- AND the persisted `composition_spec_json` size does not grow with the PNG's base64 payload

#### Scenario: MaterializeRunCommand copies the storage path, not base64, into draft markup

- GIVEN a `ContentGenerationFormatResult` whose `CompositionSpecJson` has a storage-path `BackgroundImageRef`
- WHEN `MaterializeRunCommand` copies it into `content_studio_items.draft_visual_spec_markup`
- THEN the materialized markup contains the same storage path
- AND no base64 payload is introduced during materialization

> **Deferred to Slice 2 (tracked as issue #58):** making the background-only PNG
> addressable/servable to the frontend (e.g. via `/items/{itemId}/assets/{assetIndex}`).
> Slice 2 MUST NOT register the background as a `FormatAssetRow` in
> `content_generation_format_assets` — that table is enumerated by package
> assembly (ZIP), package listing, Canva export, and delete/regenerate handlers
> with no asset-kind filter, so an extra row would double ZIP deliverables and
> corrupt asset counts. Serving mechanism is decided in Slice 2's own design.

### Requirement: BGREF-03 — PPTX Export Resolves Both Legacy and New BackgroundImageRef Forms

`PptxExportAdapter` MUST resolve `BackgroundImageRef` by detecting its form at
render time: a `data:` URI prefix MUST be decoded inline (legacy behavior); a
storage path MUST be opened via `IGeneratedAssetStorage`. Both forms MUST
produce a correctly rendered background Picture layer in the exported PPTX.
This requirement is back-compat only — no backfill of existing rows occurs.

#### Scenario: Legacy envelope with a base64 data-URI still exports correctly

- GIVEN a persisted `CompositionSpecEnvelope` whose `BackgroundImageRef` is a `data:image/png;base64,...` URI (pre-existing row)
- WHEN the PPTX export renders that slide
- THEN the background Picture layer is decoded inline from the data-URI
- AND the exported PPTX contains the correct background image

#### Scenario: New envelope with a storage path exports correctly

- GIVEN a persisted `CompositionSpecEnvelope` whose `BackgroundImageRef` is a storage path (new-form row)
- WHEN the PPTX export renders that slide
- THEN the background Picture layer is resolved by opening the asset via `IGeneratedAssetStorage`
- AND the exported PPTX contains the correct background image

### Requirement: BGREF-04 — Missing Background Reference Degrades Gracefully

If `CompositionSpecEnvelope.BackgroundImageRef` is null or empty (background
capture failed upstream), PPTX export MUST NOT throw and MUST NOT abort the
whole export. The affected slide MUST export without a background Picture
layer while all other layers (text/content components) remain present.

#### Scenario: Null BackgroundImageRef exports without a background layer

- GIVEN a persisted `CompositionSpecEnvelope` whose `BackgroundImageRef` is null
- WHEN the PPTX export renders that slide
- THEN the export completes successfully
- AND the slide has no background Picture layer
- AND text/content components for that slide are still present in the export

### Requirement: BGREF-05 — Markdown/Text Formats Are Unaffected

Formats that produce `CompositionOutputKind.Markdown` output (e.g. reel-script)
have no `CompositionSpecEnvelope` and MUST NOT be affected by this change.

#### Scenario: Reel-script generation and export behavior is unchanged

- GIVEN a reel-script generation job (Markdown output, no composition envelope)
- WHEN the job runs end to end and is packaged
- THEN the output is `reel-script.md` as before
- AND no `BackgroundImageRef` resolution logic is invoked

*Merged from SDD change `studio-background-asset-ref` (issue #56, Slice 1) — applied 2026-07-09. Slice 2 (frontend serving) tracked as #58.*

---

<!-- Merged from openspec/changes/client-aesthetic-fidelity-fixes/specs/content-generation/spec-delta.md — change applied 2026-08-24 -->

# Spec Delta: Content Generation — Faithful ClientAesthetic Degrade, Typography Wiring, Per-Slide Transparency Render

<!-- Change: client-aesthetic-fidelity-fixes -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Specify the **additive** content-generation behavior this change introduces:

1. **Two deterministic-degrade triggers**, scoped to `ClientAesthetic` carousel slides only:
   trigger A (Photo-classified slide whose image generation failed) and trigger B (AI
   designer returned no usable background). Both route to the same deterministic
   palette-backed flat treatment already proven correct for FlatColor slides, replacing the
   unconditional `rgba(0,0,0,0.52)` scrim card **for ClientAesthetic only**. Every other
   policy keeps the existing scrim-card fallback unchanged.
2. A **degrade marker** recorded on the per-slide `CompositionSpecEnvelope` whenever either
   trigger fires.
3. **Per-slide transparency treatment** applied at render time, resolved per `specIndex`.
4. **`ClientAestheticDesignContext`** carrying client-derived typography and the per-slide
   transparency signal.
5. **`AiVisualDesigner.AllowedFonts`** sourced from client-derived font classes.

The pre-existing `mappedFlatKind == FlatColor` short-circuit is untouched and remains first
in evaluation order (settled decision 6).

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CAFF-CG-01 | Settled decision 1 & 6, Scope item 1 — trigger A |
| CAFF-CG-02 | Settled decision 6, Scope item 1 — trigger B, scoped to ClientAesthetic |
| CAFF-CG-03 | Settled decision 6 — FlatColor short-circuit untouched |
| CAFF-CG-04 | Scope item 2 — degrade marker on envelope |
| CAFF-CG-05 | Scope item 5 — per-slide transparency render treatment |
| CAFF-CG-06 | Scope item 4 — design context carries typography + transparency |
| CAFF-CG-07 | Scope item 4 — AllowedFonts from client-derived classes |

---

## ADDED Requirements

### Requirement: CAFF-CG-01 — Trigger A: Failed Photo-Slide Image Generation Degrades to Deterministic Flat Treatment

For a `ClientAesthetic` carousel slide whose per-slide `BackgroundPattern` is `Photo` but
whose image generation failed for that slide (the slide never enters
`creativeMap`/`slidesWithGeneratedImage`), `ContentGenerationJobProcessor` MUST route that
slide through the deterministic palette-backed flat treatment (`BuildFlatColorMarkup` /
`CompositionDefaults.ResolvePalette`). `AiVisualDesigner.ComposeAsync` MUST NOT be called for
that slide.

#### Scenario: Photo slide with forced image-generation failure renders faithfully

- GIVEN a `ClientAesthetic` carousel slide whose `BackgroundPattern` is `Photo`
- AND that slide's image generation is forced to fail (fixture)
- WHEN the slide is composed
- THEN the slide renders through the deterministic flat treatment using the client's extracted `ColorPairings`/palette
- AND `AiVisualDesigner.ComposeAsync` is NOT invoked for that slide
- AND no dark scrim, card overlay, gradient, blob, or glow appears in the slide's markup

---

### Requirement: CAFF-CG-02 — Trigger B: AI Designer Returns No Usable Background Degrades to Deterministic Flat Treatment (ClientAesthetic Only)

For a `ClientAesthetic` carousel slide, when `AiVisualDesigner.ComposeAsync` returns no
usable background (`background is null`), the slide MUST route through the same
deterministic flat treatment used by trigger A. The `rgba(0,0,0,0.52)` scrim card MUST NOT
be used for this slide. This trigger MUST be scoped to `ClientAesthetic` carousel slides
only — every other `BackgroundSourcingPolicy` MUST keep its existing scrim-card fallback
unchanged.

#### Scenario: ClientAesthetic slide with a failed AI designer renders faithfully, no scrim card

- GIVEN a `ClientAesthetic` carousel slide
- AND the AI designer returns no usable background for that slide
- WHEN the slide is composed
- THEN the slide renders through the deterministic flat treatment
- AND no `rgba(0,0,0,0.52)` scrim card (or any dark card overlay) appears anywhere in the slide's markup

#### Scenario: Non-ClientAesthetic job still gets the legacy scrim card (regression)

- GIVEN a carousel slide whose `BackgroundSourcingPolicy` is NOT `ClientAesthetic`
- AND the AI designer returns no usable background for that slide
- WHEN the slide is composed
- THEN the slide renders the pre-existing `rgba(0,0,0,0.52)` scrim card, unchanged from before this change
- AND trigger B's flat-treatment routing is NOT applied

---

### Requirement: CAFF-CG-03 — FlatColor Short-Circuit Remains Untouched and Takes Priority Over Both New Triggers

The pre-existing `mappedFlatKind == SlideBackgroundKind.FlatColor` short-circuit MUST
continue to run first, unmodified, and MUST resolve before either trigger A (CAFF-CG-01) or
trigger B (CAFF-CG-02) is evaluated.

#### Scenario: FlatColor slide never reaches the new triggers (regression)

- GIVEN a `ClientAesthetic` carousel slide whose `BackgroundPattern` already maps to `FlatColor`
- WHEN the slide is composed
- THEN the slide takes the pre-existing `FlatColor` short-circuit path
- AND neither trigger A's failed-image-generation check nor trigger B's designer-null check is evaluated for that slide
- AND the rendered output is identical to pre-change `FlatColor` behavior

---

### Requirement: CAFF-CG-04 — Degrade Marker Recorded on Per-Slide CompositionSpecEnvelope

Whenever trigger A (CAFF-CG-01) or trigger B (CAFF-CG-02) fires for a slide, a trailing
additive-nullable degrade marker MUST be written onto that slide's `CompositionSpecEnvelope`
(mirroring the existing `Deleted` member pattern). A non-degraded slide's envelope MUST be
byte-identical to its pre-change serialized form (marker absent).

#### Scenario: Trigger A fires — envelope carries the marker

- GIVEN a slide that degrades via trigger A
- WHEN its `CompositionSpecEnvelope` is serialized
- THEN the degrade marker is present on the envelope

#### Scenario: Trigger B fires — envelope carries the marker

- GIVEN a `ClientAesthetic` slide that degrades via trigger B
- WHEN its `CompositionSpecEnvelope` is serialized
- THEN the degrade marker is present on the envelope

#### Scenario: Non-degraded slide — envelope unchanged (regression)

- GIVEN a slide that does not trigger either degrade path
- WHEN its `CompositionSpecEnvelope` is serialized
- THEN the marker is absent
- AND the serialized envelope is byte-identical to the pre-change output for the same slide

---

### Requirement: CAFF-CG-05 — Per-Slide Transparency Treatment Applied at Render Time

`BuildTemplateBackedMarkup`'s `<img>` element, and the AI path's own real-photo background,
MUST apply the per-slide photo-transparency signal (sourced from
`ClientAestheticDesignContext`, resolved per `specIndex`). When the signal is present and
true for a slide, an opacity/blend treatment consistent with the extracted qualitative
descriptor MUST be applied. When the signal is absent or false for a slide, the current
unconditional `object-fit:cover` (no alpha/blend treatment) MUST be preserved unchanged.

#### Scenario: Signal present on one slide only — treatment applied per specIndex

- GIVEN a client whose transparency signal is `true` on slide index 2 only, and absent/false on every other slide
- WHEN the carousel is rendered
- THEN slide index 2's `<img>` carries the opacity/blend treatment
- AND every other slide renders the unconditional `object-fit:cover` with no alpha/blend treatment

#### Scenario: No transparency signal anywhere — cold start preserved (regression)

- GIVEN a client whose profile carries no transparency signal on any slide
- WHEN the carousel is rendered
- THEN every slide's `<img>` renders `object-fit:cover` with no opacity/blend treatment, exactly as before this change

---

### Requirement: CAFF-CG-06 — ClientAestheticDesignContext Carries Client-Derived Typography and Per-Slide Transparency

`ClientAestheticDesignContext` MUST expose a `Typography` field (client-derived, resolved
from `profile.Typography`) and the per-slide transparency signal, both additive-nullable.
When either is absent from the source profile, the corresponding context field MUST be
absent/null and downstream behavior MUST be unchanged from before this change.

#### Scenario: Context built from a profile with both signals populated

- GIVEN a `ClientAestheticProfile` with populated `Typography` (3 roles) and a per-slide transparency array
- WHEN `ClientAestheticDesignContext` is constructed
- THEN `Typography` and the per-slide transparency signal are both present on the context

#### Scenario: Context built from a profile with neither signal — cold start (regression)

- GIVEN a `ClientAestheticProfile` with `Typography = null` and no transparency signal
- WHEN `ClientAestheticDesignContext` is constructed
- THEN `Typography` is absent/null and the transparency signal is absent
- AND designer/render behavior for that client is identical to pre-change behavior

---

### Requirement: CAFF-CG-07 — AiVisualDesigner.AllowedFonts Sourced From Client-Derived Font Classes

For a `ClientAesthetic` job whose `ClientAestheticDesignContext.Typography` is populated,
`AiVisualDesigner.BuildInputJson` MUST source `AllowedFonts` from the client-derived font
classes (resolved through `FontFamilyResolution`) instead of the org Brand Kit. When the
client has no typography signal, `AllowedFonts` MUST fall back to the current org-Brand-Kit
behavior, unchanged.

#### Scenario: Client with detected font classes — AllowedFonts reflects them

- GIVEN a `ClientAesthetic` job whose context carries client-derived typography classes
- WHEN `AiVisualDesigner.BuildInputJson` builds its input
- THEN `AllowedFonts` reflects the client-derived classes resolved through `FontFamilyResolution`

#### Scenario: Client with no typography signal — legacy brand-kit behavior (regression)

- GIVEN a `ClientAesthetic` job whose context carries no typography signal
- WHEN `AiVisualDesigner.BuildInputJson` builds its input
- THEN `AllowedFonts` is sourced from the org Brand Kit exactly as before this change

---

## MODIFIED Requirements

None. All requirements above are additive. Trigger B narrows the scrim-card fallback's
applicability to non-ClientAesthetic policies only (CAFF-CG-02); the scrim-card behavior
itself, for those policies, is unchanged and is asserted as a regression scenario rather
than restated as a MODIFIED requirement.

## REMOVED Requirements

None.

*Merged from `openspec/changes/client-aesthetic-fidelity-fixes/specs/content-generation/spec-delta.md` — change applied 2026-08-24.*

## Added Requirements: Typography Sizes, Photo-Path Text Colour, Brand-Mark Text/Icon Rendering (client-aesthetic-generation-hardening)

### Requirement: CAGH-CG-01 — Markup Builders Consume Resolved Typography Sizes, Hardcoded Literals Become Fallback

`BuildStoryTemplateSlots`, `BuildCarouselTemplateSlots`, and `BuildFlatCarouselSlots` MUST read
`spec.Typography.HeadingSize` / `.BodySize` (and `.CaptionSize` for the signature role) for their
headline/subtitle/body/signature font sizes. The pre-existing hardcoded pixel literals (`88px`,
`74px`, `38px`, `36px`, `30px`, `25px`) MUST remain reachable only as the fallback when the resolver
yields no value for that role.

#### Scenario: Client with a measured SizeRatio renders at that size (CAGH-CG-01-a)

- GIVEN a `ClientAesthetic` client whose `Typography.HeadingSize` and `.BodySize` are resolved from
  a measured `SizeRatio`
- WHEN a story or carousel slide is composed
- THEN the rendered headline/subtitle font-size matches `HeadingSize`, and body matches `BodySize`
- AND neither hardcoded literal (`88px`/`74px`/`38px`/`36px`/`30px`) appears as the effective value

#### Scenario: Client with no typography data — pre-existing hardcoded fallback unchanged (CAGH-CG-01-b, regression/cold-start)

- GIVEN a client whose `Typography` is null or whose `HeadingSize`/`.BodySize` did not resolve
- WHEN a story or carousel slide is composed
- THEN the rendered font sizes equal the pre-change hardcoded literals exactly
- AND no exception is thrown

#### Scenario: Two clients with materially different SizeRatio render different heading sizes (CAGH-CG-01-c)

- GIVEN two client profiles with materially different `SizeRatio` values, both resolving `HeadingSize`
- WHEN each client's carousel is composed
- THEN the two rendered headline font-sizes differ, asserted on markup

---

### Requirement: CAGH-CG-02 — Designer System Prompt's Size Claim Is Derived From the Client's Resolved Sizes

`AiVisualDesigner.BuildSystemPrompt` MUST NOT assert a fixed headline-size percentage range
independent of the client. When the client's typography sizes are resolved, the prompt's size
guidance line MUST be derived from those resolved values. When no typography is resolved, the
prompt MUST fall back to the pre-existing fixed guidance line, unchanged.

#### Scenario: Resolved typography drives the prompt's size line (CAGH-CG-02-a)

- GIVEN a client with resolved `HeadingSize`/`.BodySize`
- WHEN `BuildSystemPrompt` is constructed for that client's job
- THEN the size-guidance line reflects the client's resolved sizes, not the fixed percentage range

#### Scenario: No resolved typography — fixed guidance line preserved (CAGH-CG-02-b, regression)

- GIVEN a client with no resolved typography
- WHEN `BuildSystemPrompt` is constructed
- THEN the prompt contains the pre-existing fixed percentage-range guidance line, unchanged

---

### Requirement: CAGH-CG-03 — Enforced-Photo Text Colour Resolves From ColorPairings, White+Shadow Is the Fallback

Story/carousel template slots on the enforced-photo path MUST resolve text colour from
`aestheticProfile.ColorPairings` using the same resolution pattern `BuildFlatCarouselSlots`'s
`textColor` parameter already proves. When no pairing resolves, the pre-existing
`color:#FFFFFF` + `text-shadow` treatment MUST be applied unchanged. Whichever colour is applied,
the resulting text-over-background contrast MUST meet a minimum legibility guard; a resolved
pairing colour that would produce insufficient contrast against the slide's photo background MUST
NOT be applied without the compensating shadow/scrim treatment that preserves legibility.

#### Scenario: Photo-background slide uses the client's real text colour (CAGH-CG-03-a)

- GIVEN a `ClientAesthetic` slide on the enforced-photo path whose `ColorPairings` resolves a
  pairing with `TextHex = "#E0E0E0"`
- WHEN the slide's template slots are built
- THEN the rendered text colour is `#E0E0E0`
- AND `color:#FFFFFF` + `text-shadow` is NOT applied

#### Scenario: No resolvable pairing — white+shadow fallback unchanged (CAGH-CG-03-b, regression)

- GIVEN a slide on the enforced-photo path whose `ColorPairings` is null or does not resolve
- WHEN the slide's template slots are built
- THEN the rendered text is `color:#FFFFFF` with `text-shadow`, exactly as before this change

#### Scenario: Legibility guard preserved when a resolved pairing colour is low-contrast (CAGH-CG-03-c)

- GIVEN a resolved pairing whose `TextHex` would be low-contrast against a photo background
- WHEN the slide's template slots are built
- THEN a shadow/scrim compensation is retained so the rendered text remains legible
- AND the pairing colour is still the one rendered (not silently replaced by white)

---

### Requirement: CAGH-CG-04 — Text-Mark Rendering Uses the Client's Resolved Caption Typography

When `BrandMarkDetection.MarkType` is `"text"`, `BuildBrandMarkSlot` MUST render the extracted
`Text` literal through the client's already-resolved caption typography (family/weight/size),
in the previously-empty brand-mark slot. This extends `FFS-CG-02`'s brand-mark slot without
changing its editable-content-layer contract.

#### Scenario: Text mark renders the captured literal text (CAGH-CG-04-a)

- GIVEN a client whose `BrandMarkDetection.MarkType = "text"` and `.Text = "Studio Marisol"`
- WHEN the brand-mark slot is rendered
- THEN the slot renders the literal text `"Studio Marisol"`
- AND the text uses the client's resolved caption typography (family/weight/size)

#### Scenario: MarkType is text but Text is null — no fabricated text (CAGH-CG-04-b)

- GIVEN `MarkType = "text"` and `Text = null`
- WHEN the brand-mark slot is rendered
- THEN no text-mark content is rendered and no exception is thrown

---

### Requirement: CAGH-CG-05 — Icon-Mark Rendering Emits a Stored Crop `<img>`

When `BrandMarkDetection.MarkType` is `"icon"` and a usable bounding box produced a stored crop
asset, `BuildBrandMarkSlot` MUST emit an `<img>` referencing the stored crop asset in the
brand-mark slot, replacing the empty coloured `<div>`.

#### Scenario: Icon mark with a usable bounding box renders as an image (CAGH-CG-05-a)

- GIVEN a client whose `MarkType = "icon"` and whose bounding box produced a stored crop asset
- WHEN the brand-mark slot is rendered
- THEN an `<img>` referencing the stored crop asset appears in the brand-mark slot
- AND no empty coloured `<div>` is rendered for that slide

---

### Requirement: CAGH-CG-06 — Icon-Mark Degrade to PositionHint+ColorHex Chip Is Explicit and Non-Throwing

When `MarkType` is `"icon"` but the bounding box is missing, invalid, implausibly small, or the crop
could not be produced or stored, `BuildBrandMarkSlot` MUST fall back to the pre-existing
`PositionHint` + `ColorHex` coloured-chip treatment. This degrade MUST NOT throw and MUST be logged.

#### Scenario: Missing/unusable bounding box degrades to the coloured chip (CAGH-CG-06-a)

- GIVEN `MarkType = "icon"` and the bounding box is missing, invalid, or below the minimum-area
  guard (or the crop failed to store)
- WHEN the brand-mark slot is rendered
- THEN the slot renders the `PositionHint` + `ColorHex` coloured chip, unchanged from pre-change
  icon-mark handling
- AND no exception is thrown
- AND a degrade log entry is emitted

---

### Requirement: CAGH-CG-07 — Unrecognised MarkType Renders Exactly As Today

An unrecognised `MarkType` value (mapped to `"unknown"` at parse time) MUST render identically to
pre-change `BuildBrandMarkSlot` behaviour — the existing `PositionHint` + `ColorHex` chip when
`BrandMark.Present` is true, or no slot when absent.

#### Scenario: Unknown MarkType renders the pre-existing chip treatment (CAGH-CG-07-a)

- GIVEN `BrandMarkDetection.MarkType = "unknown"` with `Present = true`
- WHEN the brand-mark slot is rendered
- THEN it renders the pre-existing `PositionHint` + `ColorHex` chip, with no text or `<img>` content
- AND no exception is thrown

---

## Modified Requirements (client-aesthetic-generation-hardening)

### Requirement: FFS-CG-02 — Accent and Brand-Mark Elements Are Editable Content-Layer Slots, Never Baked

Any accent element or brand-mark element on a rendered slide MUST be emitted as an editable
content-layer element (`data-slot="accent"` / `data-slot="brand-mark"`) and MUST NOT be rasterized
into the background image. The brand-mark injection is identical whichever markup path produced the
slide (the FlatColor short-circuit or the AI/photo designer path) — the AI designer MUST NEVER draw,
trace, or place a brand mark itself. When `MarkType` is `"text"`, the slot renders the extracted
`Text` through the client's resolved caption typography (`CAGH-CG-04`). When `MarkType` is `"icon"`
with a usable bounding box, the slot renders a stored crop `<img>` (`CAGH-CG-05`); otherwise it
degrades to the `PositionHint` + `ColorHex` coloured chip (`CAGH-CG-06`), the same chip this
requirement originally specified as the only treatment.
(Previously: the slot always rendered the `PositionHint` + `ColorHex` coloured chip regardless of
mark content or type — no text or icon content was ever rendered.)

#### Scenario: Text mark renders real text, icon mark renders a crop image (FFS-CG-02-d, new)

- GIVEN `MarkType = "text"` on one client and `MarkType = "icon"` with a usable bounding box on
  another
- WHEN each client's brand-mark slot renders
- THEN the text-mark client's slot renders its literal `Text`
- AND the icon-mark client's slot renders an `<img>` of the stored crop
- AND neither renders the coloured-chip fallback

The pre-existing FFS-CG-02-a/b/c scenarios (chip rendering, no-mark absence, ambiguous-field
absence) are unchanged regression guards and remain in force.

## Removed Requirements (client-aesthetic-generation-hardening)

None.

*Merged from `openspec/changes/client-aesthetic-generation-hardening/specs/content-generation/spec-delta.md` — change applied 2026-08-25.*

---

<!-- Merged from openspec/changes/client-aesthetic-visual-sophistication/specs/content-generation/spec-delta.md — change applied 2026-08-26 (CAVS-CG-01..03 only; CAVS-CG-04/05 NOT shipped, see note below) -->

## Added Requirements: Divider Rule and Composition Archetype Presets — client-aesthetic-visual-sophistication

### Requirement: CAVS-CG-01 — Divider Rule Renders From the Client's Real Accent or Text Colour

`BuildFlatCarouselSlots` MUST render a `data-slot="divider"` element when
`DecorationMotifs.DividerRule` is non-null, coloured from the client's resolved `Accent` palette
role, falling back to the slide's resolved text colour when no `Accent` role resolves. For
`LineWithDiamond` and `LineWithDot`, an additional glyph element (rotated square / circle
respectively) MUST render centred on the rule. A slide whose `DecorationMotifs` is null, or whose
`DividerRule` is null, MUST render no divider element.

#### Scenario: LineWithDiamond renders a divider using the client's accent colour (CAVS-CG-01-a)

- GIVEN a profile signalling `DecorationMotifs.DividerRule = LineWithDiamond` and a resolved
  `Accent` palette colour
- WHEN a FlatColor slide is composed
- THEN a `data-slot="divider"` rule element renders in the client's real accent colour
- AND a rotated-square glyph element renders centred on the rule

#### Scenario: No Accent role resolves — divider falls back to text colour (CAVS-CG-01-b)

- GIVEN a profile signalling `DividerRule = Line` with no resolvable `Accent` palette role
- WHEN a FlatColor slide is composed
- THEN the `data-slot="divider"` element renders in the slide's resolved text colour

#### Scenario: DividerRule is null — no divider element (CAVS-CG-01-c, regression)

- GIVEN a profile with `DecorationMotifs.DividerRule = null` (or `DecorationMotifs = null`)
- WHEN a FlatColor slide is composed
- THEN no `data-slot="divider"` element is rendered

### Requirement: CAVS-CG-02 — Composition Archetype Drives Geometry Presets Across Accent, Divider, Title, Subtitle, CTA

The hardcoded `left:104px` / `left:108px` / `text-align:left` literals MUST become a preset resolved
by token lookup from `DecorationMotifs.CompositionArchetype`, applied consistently across the
accent, divider, title, subtitle, and cta elements. `"left-aligned"` MUST reproduce today's literal
offsets exactly. A null, blank, or unrecognised archetype token MUST resolve to the
`"left-aligned"` preset — the sole fallback path.

#### Scenario: left-aligned reproduces today's literal geometry exactly (CAVS-CG-02-a, regression)

- GIVEN a profile with `CompositionArchetype = "left-aligned"`
- WHEN a FlatColor slide is composed
- THEN the rendered title/subtitle/cta offsets equal the pre-change hardcoded literals
  (`left:104px` / `left:108px`, `text-align:left`) exactly

#### Scenario: Two archetypes produce measurably different geometry (CAVS-CG-02-b)

- GIVEN two profiles identical except for `CompositionArchetype` (e.g. `"left-aligned"` vs.
  `"centered"`)
- WHEN each client's carousel is composed from the same input copy
- THEN the rendered slot geometry for title, subtitle, and cta measurably differs between the two,
  asserted on markup

#### Scenario: Unrecognised archetype token falls back to left-aligned geometry (CAVS-CG-02-c)

- GIVEN a profile with `CompositionArchetype` set to a deliberately-invented, unrecognised token
  (e.g. `"diagonal-split"`)
- WHEN a FlatColor slide is composed
- THEN the rendered geometry equals the `"left-aligned"` preset exactly
- AND no exception is thrown

#### Scenario: Null archetype falls back to left-aligned geometry (CAVS-CG-02-d, regression)

- GIVEN a profile with `DecorationMotifs = null` or `CompositionArchetype = null`
- WHEN a FlatColor slide is composed
- THEN the rendered geometry equals the `"left-aligned"` preset exactly

### Requirement: CAVS-CG-03 — DecorationMotifs Null Renders Byte-Identical Markup to Pre-Change Output

A profile with `DecorationMotifs = null` MUST render markup byte-identical to the pre-change
`BuildFlatColorMarkup` / `BuildFlatCarouselSlots` output for the same `CompositionSpec`. This is a
blocking regression guarantee: every pre-existing flat-path test MUST continue to pass unchanged,
and the accent pill's rendered markup MUST be unchanged from pre-change output in every case.

#### Scenario: Null DecorationMotifs produces byte-identical markup (CAVS-CG-03-a, regression, blocking)

- GIVEN a `CompositionSpec` and client profile identical to one used in a pre-change flat-path test,
  except `DecorationMotifs = null`
- WHEN the flat slide markup is built
- THEN the produced markup string is byte-identical to the markup produced by the pre-change
  builder for the same input
- AND no divider, archetype-preset offset change, or ornament element appears anywhere in the output

#### Scenario: Accent pill is unaffected regardless of DecorationMotifs (CAVS-CG-03-b, regression)

- GIVEN any profile, with or without `DecorationMotifs` populated
- WHEN a FlatColor slide is composed
- THEN the accent pill's rendered markup is identical to pre-change output in every case

## Not Implemented in This Delivery: Ornament Crop Render (CAVS-CG-04) and Ornament Degrade Precedence (CAVS-CG-05)

The proposal's `content-generation` spec delta also defined `CAVS-CG-04` (ornament crop renders as
a stored `data-slot="ornament"` `<img>`) and `CAVS-CG-05` (ornament failure sets the
`"ornament-unavailable"` degrade token with whole-slide-token precedence). Both were **gated** on
`profile-analysis` `CAVS-PA-04-c`'s reliability measurement, which did **not pass** during
`sdd-apply` (see `openspec/changes/client-aesthetic-visual-sophistication/wu6-reliability-gate.md`).
Per the change's own stated fallback, WU-7 (crop port extension) and WU-8 (render + degrade + badge)
were **cut** from this delivery. No `data-slot="ornament"` element, `SlideDegradeReason.OrnamentUnavailable`
token, or ornament-related `ResolveDegradeReason` precedence logic exists in the codebase as of this
merge. The full requirement text for `CAVS-CG-04`/`CAVS-CG-05` remains available, unmerged, in
`openspec/changes/client-aesthetic-visual-sophistication/specs/content-generation/spec-delta.md` for
reference if/when the reliability gate is re-attempted and passes (re-entry condition recorded in
`wu6-reliability-gate.md`).

## Modified Requirements (client-aesthetic-visual-sophistication)

None. `CAVS-CG-01`..`03` are additive to the deterministic flat-carousel builder; the cold-start
guarantee (`CAVS-CG-03`) constrains the addition rather than changing any existing requirement's
stated behavior.

## Removed Requirements (client-aesthetic-visual-sophistication)

None.

---

## Cross-Slot Overlap Detection, Guardrail Coverage on the Template-Backed Path, Two-Pass Reflow, and Auto-Fit Typography — Change: content-layout-and-image-fidelity

Every stacked text slot's `top` offset in `BuildTemplateBackedMarkup` (and its
`BuildCarouselTemplateSlots`/`BuildStoryTemplateSlots` dispatch targets) was a fixed pixel
literal; no slot's position was ever computed from another slot's measured extent, and no
overlap check ever ran on this path's output. This section adds cross-slot overlap detection, a
non-blocking declared-box check on the template-backed path, and a bounded two-pass
measure-then-render reflow (ADR 0016) so a legitimately large headline pushes the subtitle down
instead of colliding with it, plus a floor-bounded typography auto-fit for unusually long copy.
Absolute positioning is preserved throughout; the markup contract does not change. Reflow is
scoped to the template-backed builders only — the deterministic flat path
(`BuildFlatColorMarkup`/`BuildFlatCarouselSlots`) and its byte-identity golden are an explicit
non-goal.

### Requirement: CLIF-CG-01 — Cross-Slot Vertical-Overlap Detection

The pipeline MUST gain a pure cross-slot geometry check (`SlotOverlapDetector`) able to detect
when two text slots' vertical extents intersect on both axes. This check is independent of, and
additional to, the existing single-slot `data-max-lines` char-count heuristic
(`VisualGuardrailValidator.ValidateOverflow`), which this change does not modify.

#### Scenario: Real defect fixture is flagged as a violation (CLIF-CG-01-a)

- GIVEN a headline slot rendered at `font-size:149px`, `top:255px`, wrapping to 2–3 lines
- AND a subtitle slot at `top:575px`
- WHEN the cross-slot overlap check runs
- THEN the headline's rendered bottom edge is found to exceed the subtitle's top edge
- AND a cross-slot vertical-overlap violation is reported

#### Scenario: Non-overlapping slots pass (CLIF-CG-01-b)

- GIVEN a headline and a subtitle slot whose rendered vertical extents do not intersect
- WHEN the cross-slot overlap check runs
- THEN no violation is reported for that slide

### Requirement: CLIF-CG-02 — A Non-Blocking Cross-Slot Overlap Check Runs on the Template-Backed Path's Final Markup

Whenever the enforced-background priority resolution selects `BuildTemplateBackedMarkup`'s (or
the legacy backstop's) output as the slide's final markup, the pipeline MUST run
`SlotOverlapDetector` against that markup's declared slot boxes (parsed pre-render — no measured
Chromium geometry exists at this point in the method) and log a structured, non-blocking
diagnostic on a hit. This declared-box check can only ever catch a builder regression, since the
literals were hand-tuned not to overlap; it is deliberately separate from `VisualGuardrailValidator`
(whose signature and existing four steps are unmodified) and from CLIF-CG-03 through CLIF-CG-05's
measured-geometry reflow, which is the only mechanism that can catch a real growth-driven overlap.

#### Scenario: Template-backed markup is actually checked (CLIF-CG-02-a, regression)

- GIVEN a `ClientAesthetic` job whose enforced-background priority resolution selects
  `BuildTemplateBackedMarkup`'s output over the AI designer's markup
- WHEN the slide's final markup is produced
- THEN `SlotOverlapDetector.Detect` is invoked against that final template-backed markup's
  declared slot boxes
- AND this is asserted directly (not inferred), guarding against the previously-existing gap —
  where no overlap check ever ran once the enforced-background priority resolution picked
  template markup over AI-designer markup — being silently reintroduced by a future refactor

#### Scenario: A violation detected on the template-backed path is recorded, not retried (CLIF-CG-02-b)

- GIVEN the declared-box check reports a cross-slot violation on `BuildTemplateBackedMarkup`'s
  output
- WHEN the pipeline handles the violation
- THEN the violation is logged as a non-blocking diagnostic — it does not gate markup selection
  and is a separate budget from `CAC-IMG-03`, which governs image-generation retries and has
  already completed by this point
- AND the job is not marked failed

### Requirement: CLIF-CG-03 — Two-Pass Reflow: Fast Path Renders Once When No Overlap Is Measured

If the first render's measurement (via the existing `SlotGeometryScript`/`RenderWithGeometryAsync`
mechanism) shows no two slots intersect and no recomputed `top` differs from the rendered `top` by
more than a stated epsilon, the pipeline MUST stop at one render. The fast path applies to the
template-backed carousel and story builders only.

#### Scenario: Short-copy slide renders exactly once (CLIF-CG-03-a)

- GIVEN a template-backed slide whose first (Pass 1) render shows no cross-slot intersection and
  no material `top` delta
- WHEN the reflow mechanism runs
- THEN rendering stops after exactly one render (no Pass 2, no re-measurement)
- AND this is asserted directly against a render-count assertion

### Requirement: CLIF-CG-04 — Two-Pass Reflow: Overlap Triggers Recompute and Re-Render, Verified by Re-Measurement

If Pass 1's measurement detects an intersection, the pipeline MUST recompute subsequent slots'
`top` offsets from the preceding slot's measured extent, re-render, and re-measure to verify the
overlap is resolved. If the recompute changes a slot's own wrapping and overlap survives, the
pipeline MUST recompute and re-render once more (Pass 3), subject to the hard cap in CLIF-CG-05.

#### Scenario: Long headline at the real measured SizeRatio converges without intersection (CLIF-CG-04-a)

- GIVEN a headline whose `top` is derived from the client's real measured `SizeRatio` and wraps
  to 2–3 lines, initially overlapping the subtitle
- WHEN the reflow mechanism runs on the carousel or the story template builder
- THEN the subtitle's `top` is recomputed from the headline's measured extent
- AND the re-rendered, re-measured layout shows no intersection between the two slots

#### Scenario: Markup remains absolutely positioned after reflow (CLIF-CG-04-b, regression)

- GIVEN a slide that converges via CLIF-CG-04-a
- WHEN the final markup is inspected
- THEN every text slot remains absolutely positioned (no document-flow/flex conversion)
- AND `PptxExportAdapter` output and `StudioVisualMarkupResolver` behavior are unaffected

### Requirement: CLIF-CG-05 — Hard Cap of 3 Renders; Non-Convergence Produces a Best-Effort Layout and a Structured Diagnostic

The reflow loop (`MeasuredLayoutReflow.MaxRenderPasses = 3`) MUST NEVER exceed 3 renders per
slide (1 initial + at most 2 re-renders), and MUST be structurally incapable of iterating further.
If overlap still survives at the cap, the pipeline MUST accept the best-effort pass (the measured
layout with the smallest total intersection area, ties broken by lowest pass index), emit a
structured non-convergence diagnostic (`ContentGeneration.LayoutReflow`: job id, format key, spec
index, render passes, selected pass index, residual overlap count/area, overlap pairs, selected
tops, clamped-at-canvas-bottom flag), and ship that best-effort layout rather than failing the
slide. This budget is deliberately disjoint from `CAC-IMG-03` (`ClientAestheticImageBackgroundService`'s
image-generation retry budget, exhausted earlier in the pipeline before markup exists) — the two
never compose, and non-convergence at the render stage MUST NOT re-trigger image generation or
any other retry.

#### Scenario: Render loop never exceeds 3 renders (CLIF-CG-05-a)

- GIVEN a deliberately non-converging fixture (recompute changes wrapping in a way that
  re-introduces overlap each pass)
- WHEN the reflow mechanism runs
- THEN no more than 3 renders occur for that slide
- AND this is asserted directly against a render-count assertion, not inferred

#### Scenario: Non-convergence yields a best-effort layout and a diagnostic, never a retry (CLIF-CG-05-b)

- GIVEN the same non-converging fixture reaches the 3-render cap without resolving overlap
- WHEN the cap is reached
- THEN the pipeline selects the pass with the smallest total intersection area as the
  best-effort layout
- AND a structured non-convergence diagnostic is emitted (`ContentGeneration.LayoutReflow`)
- AND that best-effort layout ships as the slide's final markup — no `CAC-IMG-03` route, no
  image-regeneration retry, no additional render pass
- AND the job is not marked failed and does not hang

### Requirement: CLIF-CG-06 — Scope Boundary Regression: The Deterministic Flat Path Is Untouched

The reflow mechanism (CLIF-CG-03 through CLIF-CG-05) MUST apply only to the template-backed path.
`BuildFlatColorMarkup`/`BuildFlatCarouselSlots` MUST NOT be reflowed, measured, or re-rendered by
this change.

#### Scenario: Flat-path byte-identity golden renders unchanged (CLIF-CG-06-a, regression)

- GIVEN a flat-color carousel/story job that previously produced a byte-identical golden output
- WHEN the same job runs after this change
- THEN `BuildFlatColorMarkup`/`BuildFlatCarouselSlots` output remains byte-identical to the
  pre-change golden
- AND no measurement pass, re-render, or overlap check runs against this path

### Requirement: CLIF-CG-07 — Copy-Length-Aware Typography Auto-Fit Is Floor-Bounded and Reduction-Only

`CompositionDefaults.ResolveTypography` MUST gain a bounded, opt-in auto-fit (an optional
`headlineCopy` parameter defaulting to `null`, preserving every pre-existing call site
byte-identically): when a legitimate `SizeRatio` applied to an unusually long copy string would
produce a heading size beyond the auto-fit's stated limits, the resolved size MUST be reduced
within those stated limits. Auto-fit MUST NEVER increase a resolved size, and MUST NEVER reduce a
size below the auto-fit's stated floor. `MaxSizeRatio` continues to guard against a bad
measurement; auto-fit separately guards against a long copy string at an otherwise legitimate
ratio.

#### Scenario: Legitimately large SizeRatio with unusually long copy is reduced within stated limits (CLIF-CG-07-a)

- GIVEN a client's real measured `SizeRatio` and a copy string materially longer than typical
- WHEN typography is resolved for that headline
- THEN the resolved size is reduced from the rigid `SizeRatio`-derived value
- AND the reduced size does not fall below the auto-fit's stated floor

#### Scenario: Auto-fit never increases a resolved size (CLIF-CG-07-b)

- GIVEN any copy string and `SizeRatio` combination
- WHEN typography auto-fit runs
- THEN the resolved size after auto-fit is never greater than the size before auto-fit

### Requirement: CLIF-CG-08 — Cold-Start / No-Op Regression: A Profile and Copy That Already Fit Render Byte-Identically

A profile and copy string that already fit today (no long headline, no cross-slot overlap) MUST
render byte-identically to the pre-change output. Neither the reflow mechanism nor the typography
auto-fit MUST alter output for a slide unaffected by either fix.

#### Scenario: Unaffected client/job renders identically to before (CLIF-CG-08-a, regression)

- GIVEN a client and copy string that produce no long headline and no cross-slot overlap under
  today's fixed-literal layout
- WHEN the same job runs after this change
- THEN the rendered markup, typography sizes, and slot positions are byte-identical to the
  pre-change output
- AND the reflow mechanism takes the fast path (CLIF-CG-03) with exactly one render

## Modified Requirements (content-layout-and-image-fidelity)

None. `CLIF-CG-01`..`08` are additive to the template-backed rendering pipeline; the existing
`VisualGuardrailValidator` signature and its four existing steps are unmodified, and `ValidateOverflow`
is untouched.

## Removed Requirements (content-layout-and-image-fidelity)

None.

*Merged from `openspec/changes/content-layout-and-image-fidelity/specs/content-generation/spec-delta.md`
— change applied 2026-08-27. Corrected during merge: the delta's original CLIF-CG-01/CLIF-CG-02
wording described the cross-slot check as living inside `VisualGuardrailValidator`; the shipped
implementation (WU-3, tasks.md deviation) instead calls `SlotOverlapDetector.Detect` directly from
`ContentGenerationJobProcessor` against declared slot boxes, since the call site runs before any
Chromium measurement exists. `VisualGuardrailValidator` itself was not modified. Requirement IDs,
scenario IDs, and observable behavior are unchanged from the delta; only the mechanism description
was corrected to match shipped code.*

*Merged from `openspec/changes/client-aesthetic-visual-sophistication/specs/content-generation/spec-delta.md` — change applied 2026-08-26. CAVS-CG-01/02/03 shipped (WU-4/WU-5); CAVS-CG-04/05 NOT merged as shipped — gated on WU-6.4's reliability measurement, which did not pass; see `wu6-reliability-gate.md`.*

---

## Client Style Fidelity Requirements

### Requirement: CSF-CG-01 — Carousel Template Is Chosen by Layout, Not by Format Key

`BuildTemplateBackedMarkup` MUST use the carousel template for every format whose catalog layout is
Carousel on a non-Story surface (`feed-carousel` and the legacy `carousel` alias). Story surfaces
keep the Story template.

#### Scenario: feed-carousel uses the carousel template (CSF-CG-01-a)

- GIVEN a `feed-carousel` slide with an enforced background
- WHEN the markup is built
- THEN the title renders in `data-slot="title"` from the carousel template
- AND the Story `headline` slot and its accent pill are not emitted

### Requirement: CSF-CG-02 — No Hardcoded Decoration for ClientAesthetic Slides

When a ClientAesthetic design context is present, the deterministic builders MUST NOT emit the
accent pill, the full-width accent bar, or an empty brand-mark chip. A divider renders only from a
measured `DecorationMotifs.DividerRule`. An icon brand mark without an extracted crop renders
nothing. Without a design context (cold start) the markup stays byte-identical to the previous
behaviour. Supersedes the unconditional accent pill of `client-aesthetic-visual-sophistication`
decision #4 and the empty-chip fallback of `CAGH-CG-06-a` for ClientAesthetic slides.

#### Scenario: client without a divider gets no bar (CSF-CG-02-a)

- GIVEN a ClientAesthetic photo slide whose profile has no divider rule
- WHEN the markup is built
- THEN no `data-slot="accent"` element and no decorative bar element is present

#### Scenario: icon mark without crop renders nothing (CSF-CG-02-b)

- GIVEN a detected icon brand mark and no extracted crop
- WHEN the brand-mark slot is built
- THEN no `data-slot="brand-mark"` element is emitted

### Requirement: CSF-CG-03 — Colour Pairings Follow Observed Frequency

When the profile's pairings carry an observed `SlideCount`, flat slides MUST take flat pairings
allocated in proportion to their slide counts (largest remainder, dominant pairing first), and photo
slides MUST take the observed photo pairing when one exists. Pairings without `SlideCount` keep the
legacy rotation.

#### Scenario: dominant pairing fills a short carousel (CSF-CG-03-a)

- GIVEN observed flat pairings with slide counts 10 and 1
- WHEN a 3-slide carousel is composed
- THEN every slide uses the dominant pairing

### Requirement: CSF-CG-04 — Rendered Type Uses the Client's Catalog Font

Typography resolved from a client profile MUST name a bundled catalog family, clamp each weight to
the weights that family ships, and render with `letter-spacing:0`. The renderer MUST embed every
catalog family referenced by the markup, so no host font substitutes it.

#### Scenario: condensed client font renders with its own face (CSF-CG-04-a)

- GIVEN a heading role resolved to `League Gothic` at observed weight 800
- WHEN the slide renders
- THEN the markup requests `League Gothic` at weight 400 (the only weight it ships)
- AND the render document contains an `@font-face` rule for `League Gothic`

### Requirement: CSF-CG-05 — Measured Headline Width

When the heading role carries `BlockWidthRatio`, the title slot width MUST be that ratio of the
canvas width, clamped to 30–90 %, with `text-wrap:balance`.

### Requirement: CSF-CG-06 — Keyword Highlight

When an observed pairing carries a highlight colour, the carousel generator MUST request a
`titleHighlight` per slide (and `coverTitleHighlight`) holding an exact substring of the title, and
the builders MUST wrap its first case-insensitive occurrence in
`<span data-highlight="true">` coloured with the pairing's highlight colour. A highlight that is not
a substring of the title is ignored. Titles persist as plain text.

#### Scenario: highlighted words render in the client's accent (CSF-CG-06-a)

- GIVEN title "¿Sabés qué toman tus clientes en tu local?" and highlight "tus clientes"
- AND an observed pairing with highlight `#A6CE3C`
- WHEN the title slot is built
- THEN "tus clientes" is wrapped in a span coloured `#A6CE3C`

### Requirement: CSF-CG-07 — Flat Slides Reflow Around the Measured Headline

The deterministic flat path MUST run the bounded measure-then-render reflow (ADR 0016) over
`title → divider → subtitle`, like the template-backed path. A client's measured headline (larger
size, condensed face, narrow block) has no fixed height, so the subtitle's literal top can land on
it. Supersedes `CLIF-CG-06` for the flat path; AI-designer markup is still never reflowed. The
reflow slot order is chosen by layout (CSF-CG-01), so `feed-carousel` reflows its carousel slots.

#### Scenario: tall measured headline pushes the subtitle down (CSF-CG-07-a)

- GIVEN a flat slide whose rendered title is taller than the gap above the subtitle
- WHEN the slide renders
- THEN the subtitle is moved below the title's measured extent before the final render

## Known Follow-up (not in scope)

`DecorationMotifs` (divider rule, composition archetype) is never threaded into
`ClientAestheticDesignContext`, so `CAVS-CG-01/02` never reach production markup. Wiring it is
deferred: the extractor still reports a `Line` divider for clients that do not use one, so enabling it
today would reintroduce stray bars.

*Merged from `openspec/changes/client-style-fidelity/specs/content-generation/spec-delta.md` — change applied 2026-10-10.*

---

## Client Style Fidelity — Follow-up (2026-10-11)

### Requirement: CSF-CG-08 — Generated Images Reproduce the Client's Slide Treatment

Each generated image prompt MUST carry the client's treatment observed at that slide position
(e.g. "product cut-out on seamless black backdrop"), the backdrop tone behind their text when an
observed pairing provides it, and the headline's reserved area. Product shots MUST ask for plain,
unbranded packaging, and the image negative prompt MUST suppress printed labels.

### Requirement: CSF-CG-09 — A Failed Prompt Writer Does Not Flatten the Slide

When the image prompt writer fails, the slide's own image description (the carousel generator's
`imagePromptHint`) plus the treatment cue MUST be used as the prompt. Only a slide with no
description degrades to the flat treatment.
