# Proposal: Single-Analyzer Fase A — Packaging & Reel-Script Quick Wins

## Change ID
`single-analyzer-fase-a`

## Summary
Three low-risk, high-value fixes to the existing single-analyzer content-generation/packaging flow, committed directly to `dev` as a single PR:

1. **#2 — Download folders use the client DISPLAY NAME**, not a slug or a raw GUID, so the on-disk folder is identical to the label the user sees in the front end.
2. **#3 — The reel-script asset is delivered as Markdown** (`reel-script.md`) with newline-separated scenes and bold timestamps, instead of a single flattened `.txt` line.
3. **#7 — A committed Excalidraw + Mermaid diagram** of the current single-analyzer happy path through packaging, ending in a DOWNLOAD vs EXPORT-TO-CANVA decision node (Canva marked as a Phase B placeholder).

This is **Fase A** — the quick-wins slice. Canva integration and broader visual-quality work (the items numbered #1, #4, #5, #6 in the original review) are **explicitly OUT of scope** and reserved for **Fase B**.

## Problem
Three concrete pain points reported against the live single-analyzer packaging output:

1. **Folder names don't match what the user sees.** The user selects a client labelled e.g. `Acme Corp` in the front end, but the downloaded ZIP contains a folder named `acme-corp` (slugified) on the frozen-ZIP path, and a raw GUID (`3f2a…`) on the newer Assemble path. Neither matches the label, which breaks the user's mental model and makes downloaded packages hard to organise.
2. **The reel-script is unreadable.** Every scene is joined with a single space onto ONE line and delivered as `reel-script.txt`. There are no line breaks, no visual separation of timestamps, and the file extension doesn't signal that it is structured script content. Marketers cannot scan or hand off the script.
3. **No shared mental model of the flow.** The single-analyzer happy path (analyze → request package → orchestrate → render → store → download) is undocumented as a diagram. There is also no agreed visual touchpoint for where the upcoming Canva export will plug in, which makes Fase B planning ambiguous.

## Goals
- Make the download folder name IDENTICAL to the front-end client label on BOTH packaging paths (frozen-ZIP build and Assemble).
- Sanitize folder names by removing ONLY filesystem-unsafe characters, preserving case, spaces, and accents.
- Deliver the reel-script as well-formed Markdown with one scene per block and bold timestamps, preserving any emojis the AI produced.
- Add a committed, reviewable diagram of the current flow plus the Phase B Canva decision point, matching existing diagram conventions.
- Keep changes within strict hexagonal boundaries (Domain ← Application ← Infrastructure/Api) and pass all MUST gates.

## Out of Scope
- **Canva integration (Fase B).** This change only DEPICTS the Canva decision node in the diagram as a placeholder; no Canva backend, API, or export code is added.
- **Broader visual-quality / template work (Fase B, items #1, #4, #5, #6).** No changes to raster composers, brand templates, fonts, or layout.
- **Mundo 1 vs Mundo 2 structural changes.** No changes to the analysis pipeline, scrape contracts, or AI flows — only the packaging output and the reel-script text body.
- **New write commands or new persisted artifacts.** No schema changes, no migrations.

## Scope Boundaries
- Backend changes stay layer-correct: the enum value is a Domain change; the composer is Application; the renderer and folder sanitizer that touch I/O stay in their existing layers.
- The Assemble path will load the client display name via the existing read path rather than leaking persistence types upward.
- Item #7 is docs-only (no production code, no automated test).

## Approach (per item)

### Item #2 — Folder uses client display name (BOTH paths)
USER DECISION: fix both packaging paths.

- **Frozen-ZIP path** — `GeneratedPackageBuilder.NormalizeFolderName()` currently slugifies (lowercase + collapse non-alphanumeric runs to `-`). Replace it with a minimal sanitizer that strips ONLY the 9 Windows-reserved characters (`< > : " / \ | ? *`) plus control characters, preserving case, accents, and spaces. Result: the folder equals the display name with only illegal characters removed (e.g. `Café & Co.` stays `Café & Co.`).
- **Assemble path** — `AssembleGeneratedPackageQueryHandler` currently uses the raw client GUID as the folder segment. Change it to use the client display name, loading that name (passed through the query input or resolved via a repo/read lookup) and applying the same sanitizer.
- **Frontend** — `single-analyzer-client.tsx`: `clientLabels` must map to `c.name` directly (not `deriveClientFolderSlug(c.name)`), so the Package tab tree shows the same display name.
- **Tests** — update the existing `PackageBuilderTests` slug assertions to the new sanitize-only behavior and add new coverage.

**Acceptance criteria**
- [ ] `NormalizeFolderName("Café & Co.")` returns `Café & Co.` (only illegal chars removed; case/accents/spaces preserved).
- [ ] `NormalizeFolderName("Client/Name:Test")` removes `/` and `:` (no slugification, no lowercasing).
- [ ] `NormalizeFolderName` on empty/null still returns empty (existing behavior preserved).
- [ ] A frozen-ZIP built for a client named `Acme Corp` contains `Acme Corp/<asset>`, NOT `acme-corp/<asset>`.
- [ ] The Assemble path produces a folder using the client display name, NOT the raw GUID.
- [ ] Front end `clientLabels` renders `c.name` (verified by inspection / existing component test if present).

### Item #3 — Reel-script as Markdown
- **Domain** — add a `Markdown` value to `CompositionOutputKind` (in `VisualCompositionSpec.cs`).
- **Application** — rewrite `ReelScriptVisualComposer` to emit a newline-separated body: hook, then one block per scene with `**{timestamp}**` bold per line, preserving emojis present in `Voiceover` / `OnScreenText`, then the CTA. Set `OutputKind = CompositionOutputKind.Markdown`.
- **Infrastructure** — `LocalSvgContentRenderer` adds a `Markdown` branch: filename `reel-script.md`, mime `text/markdown; charset=utf-8`.
- **Frontend** — `single-analyzer-client.tsx`: `FORMAT_FILE_NAMES["reel-script"]` → `reel-script.md` and `resolveFormatFilename` extension enforcement updated from `.txt` to `.md`.

**Acceptance criteria**
- [ ] `CompositionOutputKind` contains a `Markdown` member.
- [ ] The reel-script spec has `OutputKind == CompositionOutputKind.Markdown`.
- [ ] For a multi-scene payload, the text body contains newlines between scenes (not a single space-joined line).
- [ ] Each scene line begins with `**{timestamp}**`.
- [ ] Emojis present in `Voiceover` / `OnScreenText` survive into the output body unchanged.
- [ ] The renderer emits filename `reel-script.md` with mime `text/markdown; charset=utf-8`.
- [ ] Front end resolves the reel-script filename with a `.md` extension.

### Item #7 — Single-analyzer flow diagram (docs-only)
- New `docs/architecture/diagrams/single-analyzer-flow.excalidraw` (Type 2 Excalidraw JSON) and a companion `single-analyzer-flow.md` (Mermaid flowchart + legend), matching the conventions of the existing `content-plan-flow` pair (same color palette, rectangle+text+arrow schema, low-opacity grouping zones, Virgil font).
- Depict the current happy path: submit URL → analyze → request package → orchestrate → render → store per client → frozen-ZIP build, ending in a **DOWNLOAD vs EXPORT-TO-CANVA decision node**. Mark the Canva node as a **Phase B placeholder** and note that BOTH paths must yield Canva-compatible formats.

**Acceptance criteria**
- [ ] Both files exist and follow the `content-plan-flow` conventions (palette, element schema, companion Mermaid).
- [ ] The diagram shows the current happy path through packaging.
- [ ] A DOWNLOAD vs EXPORT-TO-CANVA decision node is present, with Canva flagged as Phase B.
- [ ] A note states both branches must produce Canva-compatible output.

## Work-Unit Commit Order (recommendation)
Three cohesive work units, each independently reviewable and revertible:

1. **Unit A — Item #3 (reel-script Markdown).** Domain enum + composer + renderer + frontend filename + tests, in one commit. Self-contained Domain→Infra→FE slice.
2. **Unit B — Item #2 (display-name folders).** `GeneratedPackageBuilder` sanitizer + `AssembleGeneratedPackageQueryHandler` name lookup + frontend `clientLabels` + updated/new `PackageBuilderTests`, in one commit.
3. **Unit C — Item #7 (diagram).** The two docs files, in one commit. No code, no tests.

Recommended sequence: A → B → C (smallest blast radius first; docs last). All three land in a single PR to `dev` (~320 changed lines, no chaining).

## Affected Capabilities / Specs

| Spec (`openspec/specs/`) | Effect | Why |
|---|---|---|
| `content-generation/spec.md` | Touched (delta) | Packaging folder-naming contract (#2) and reel-script Markdown output contract (#3) both belong to the content-generation/packaging capability. Add the new acceptance criteria here. |
| `integrations/spec.md` | Referenced only | Canva is the future integration touchpoint depicted in #7; no behavior added now. |

## Docs-Sync Note
- `openspec/specs/content-generation/spec.md` — record the folder-naming and reel-script Markdown acceptance criteria (currently a TBD stub; this change can seed the relevant capability sections).
- `docs/architecture/diagrams/single-analyzer-flow.{excalidraw,md}` — NEW diagram pair (the #7 deliverable itself).
- No PRD/SDD structural change; this is a refinement of existing packaging behavior. If a behavior note is warranted, reference PRD content-generation/packaging acceptance ("the frontend shows workflow state and downloadable output clearly").

## PRD Traceability
- **PRD `docs/PRD.md` — content-generation / packaging acceptance** (downloadable content package output): items #2 and #3 improve the fidelity and usability of that downloadable output (folder labels match the UI; reel-script is readable structured Markdown).
- **SDD `docs/SDD.md` — Content Generation bounded context / Persistence-free packaging**: the folder sanitizer and the `CompositionOutputKind` change stay within the existing content-generation packaging seam; no new persistence.
- **Capability**: `content-generation` (packaging + visual composition).

## Risks
- **Test assertion updates (#2).** Existing `PackageBuilderTests` hardcode slug expectations (`acme-corp`, `my-client-co`). ALL of these must be rewritten to the sanitize-only behavior; missing one will fail the gate or, worse, lock in the old behavior. Review every assertion.
- **Enum exhaustiveness (#3).** Adding `CompositionOutputKind.Markdown` is a Domain change; any `switch` over the enum that is not exhaustive may silently fall through. Verify the renderer and any test doubles handle the new case explicitly.
- **Frontend extension enforcement (#3).** `resolveFormatFilename` enforces `.txt` for reel-script; if it is not updated, the file downloads with the wrong extension regardless of the backend change. Update both `FORMAT_FILE_NAMES` and the extension check.
- **Assemble path client-name lookup (#2).** The Assemble path currently has only the GUID; sourcing the display name requires passing it through the query input or adding a read lookup WITHOUT leaking persistence types into Application. Confirm the cleanest seam in the design phase.
- **Docs accuracy (#7).** No TDD gate covers the diagram; review it for accuracy (current happy path + correct Phase B placeholder) before merge.

## Review Workload Note
Estimated ~320 changed lines (~200 production + ~120 test) across three small work units. **Budget risk: LOW. Single PR to `dev`, no chaining.**

## Traceability
- Exploration: engram `sdd/single-analyzer-fase-a/explore` (obs #555).
- Related (Fase B planning): `single-analyzer-profile-analysis` change set.
- Capability spec: `openspec/specs/content-generation/spec.md`.
