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
