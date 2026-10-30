# Tasks: single-analyzer-fase-a — Packaging & Reel-Script Quick Wins

<!-- Change: single-analyzer-fase-a -->
<!-- Spec: openspec/changes/single-analyzer-fase-a/specs/content-generation/spec-delta.md -->
<!-- Design: openspec/changes/single-analyzer-fase-a/design.md -->

---

## Review Workload Forecast

| Metric | Value |
|---|---|
| **Estimated total changed lines** | ~340 (backend ~220, frontend ~80, docs ~40) |
| **Chained PRs recommended** | **No** |
| **400-line budget risk** | **Low** |
| **Decision needed before apply** | **No** — all design decisions are resolved; single PR to `dev` |

> Estimated breakdown: WU-A ~130 lines (2 test files + enum + composer + renderer branch), WU-B ~170 lines (test rewrites + builder + handler + FE), WU-C ~40 lines (docs-only). Total ~340 — well inside the 400-line single-PR budget.

---

## Work Units

---

### WU-A — Reel-Script Markdown output

**Spec IDs: RS-01, RS-02, RS-03**
**TDD order: failing tests FIRST, then implementation**
**Sequential: must complete before WU-B (shared renderer path)**

#### A-1 [TEST — FAILING] — xUnit: CompositionOutputKind enum has Markdown member

- [x] **[TEST]** Add test to `SocialStudio.Tests\Domain\Social\ContentGeneration\VisualCompositionSpecTests.cs`:
  - Assert `Enum.IsDefined(typeof(CompositionOutputKind), "Markdown")` is true.
  - _Spec: RS-01 scenario "Markdown member exists"_

#### A-2 [TEST — FAILING] — xUnit: ReelScriptVisualComposer produces Markdown output

- [x] **[TEST]** Add tests to `SocialStudio.Tests\Application\Social\ContentGeneration\Visual\ComposerOutputKindTests.cs`:
  - `ReelScript_Compose_OutputKindIsMarkdown`: compose with null payload → `OutputKind == CompositionOutputKind.Markdown`.
  - Also removed `"reel-script"` from `TextFormatComposer_Compose_SetsOutputKindText` theory (was expecting Text, now expects Markdown).
  - _Spec: RS-01 scenario "Renderer handles Markdown case explicitly", RS-02 scenario "OutputKind is Markdown"_

#### A-3 [TEST — FAILING] — xUnit: ReelScriptVisualComposer body structure

- [x] **[TEST]** Created new file `SocialStudio.Tests\Application\Social\ContentGeneration\Visual\ReelScriptVisualComposerTests.cs`:
  - `MultiScene_Body_ContainsNewlines`: body with two scenes contains `\n` and is NOT a single space-joined line.
  - `Scene_Body_StartsWithBoldTimestamp`: scene line starts with `**0:00**`.
  - `EmojiInVoiceover_Preserved`: body contains `🎬` unchanged.
  - `CTA_AppearsAfterLastScene`: CTA text appears after the last scene block.
  - _Spec: RS-02 scenarios (multi-scene newlines, bold timestamp, emoji, CTA)_

#### A-4 [TEST — FAILING] — xUnit: LocalSvgContentRenderer Markdown branch

- [x] **[TEST]** Added tests to `SocialStudio.Tests\Infrastructure\Rendering\LocalSvgContentRendererTests.cs`:
  - `RenderAsync_MarkdownOutputKind_FilenameIsReelScriptMd`: spec with `OutputKind=Markdown`, `FormatKey="reel-script"` → filename is `reel-script.md`.
  - `RenderAsync_MarkdownOutputKind_MimeIsTextMarkdown`: same spec → MIME is `text/markdown; charset=utf-8`.
  - `RenderAsync_MarkdownOutputKind_BodyBytesAreUtf8`: body bytes decode to expected text (emoji-safe).
  - `RenderAsync_MarkdownOutputKind_DoesNotFallThroughToRaster`: result is NOT `image/png`.
  - Added `BuildMarkdownSpec` helper.
  - _Spec: RS-03 scenarios (renderer writes reel-script.md, correct MIME)_

#### A-5 [IMPL] — Add `CompositionOutputKind.Markdown` to domain enum

- [x] Edit `SocialStudio.Domain\Social\ContentGeneration\VisualCompositionSpec.cs`:
  - Added `Markdown` member to `CompositionOutputKind` enum with XML doc comment.
  - _Spec: RS-01_

#### A-6 [IMPL] — Rewrite `ReelScriptVisualComposer` body

- [x] Edit `SocialStudio.Application\Features\Social\ContentGeneration\Visual\Composers\ReelScriptVisualComposer.cs`:
  - Replaced `string.Join(" ", ...)` with newline-separated scene blocks.
  - Scene format: `**{timestamp}**: {voiceover}\n{onScreenText}` (omit on-screen line when empty).
  - Scenes joined with `\n\n`.
  - Full body: `{Hook}\n\n{scenes}\n\nCTA: {ctaText}` (null payload → fallback + Markdown).
  - Changed `OutputKind = CompositionOutputKind.Text` → `CompositionOutputKind.Markdown`.
  - _Spec: RS-02_

#### A-7 [IMPL] — Add Markdown branch to `LocalSvgContentRenderer`

- [x] Edit `SocialStudio.Infrastructure\Rendering\LocalSvgContentRenderer.cs` line 52:
  - Inserted Markdown branch BEFORE the raster fallthrough (order: Text → Markdown → raster).
  - MIME: `text/markdown; charset=utf-8`, filename: `{FormatKey}.md`.
  - _Spec: RS-03_

#### A-8 [TEST UPDATE] — Fix existing reel-script test assertions

- [x] Updated `SocialStudio.Tests\Application\Social\ContentGeneration\Visual\ComposerOutputKindTests.cs`:
  - Removed `"reel-script"` from `TextFormatComposer_Compose_SetsOutputKindText` theory.
  - Added separate `ReelScript_Compose_OutputKindIsMarkdown` test (done as part of A-2).
  - `LocalSvgContentRendererTests.RenderAsync_SpecWithOutputKindText_ReturnsTxtAsset`: already uses explicit `OutputKind=Text` via `BuildTextSpec` — no change needed; test correctly validates Text branch with any format key.
  - _Spec: RS-01 / RS-03 — updated to expect Markdown for reel-script_

- [x] Frontend: Updated `apps\web\components\social\single-analyzer-client.tsx`:
  - `FORMAT_FILE_NAMES["reel-script"]`: `"reel-script.txt"` → `"reel-script.md"`.
  - Updated doc comment: ext example updated from `.txt` to `.md`.
  - _Spec: RS-03 scenario "Frontend resolves .md extension"_

#### A-9 — Gates + commit

- [x] `dotnet test` — 1000 passed, 0 failed, 2 skipped (pre-existing). All WU-A tests green.
- [x] `pwsh -File scripts/gates/Run-Gates.ps1` — 16 pass, 2 skipped (SHOULD), 0 MUST failures. PASS.
- [ ] Commit: awaiting user

---

### WU-B — Display-name folders

**Spec IDs: PKG-01, PKG-02**
**TDD order: failing tests FIRST, then implementation**
**Sequential: after WU-A (WU-A must be green first to avoid noisy test runs)**

#### B-1 [TEST — UPDATE] — Rewrite existing `PackageBuilderTests` slug assertions

- [x] Edit `SocialStudio.Tests\Application\Social\ContentGeneration\Packaging\PackageBuilderTests.cs`:
  - `BuildAsync_WithClientName_FilesPlacedUnderClientFolder` (line 128–133):
    - Change `"acme-corp/flyer.png"` → `"Acme Corp/flyer.png"`.
    - Change `"acme-corp/carousel/slide_1.png"` → `"Acme Corp/carousel/slide_1.png"`.
    - Change `Assert.Equal("acme-corp", result.Manifest.ClientFolderName)` → `"Acme Corp"`.
  - `BuildAsync_ClientNameNormalisation_CollapsesSpecialChars` (line 153–154):
    - Rename test to `BuildAsync_ClientName_SanitizesIllegalWindowsChars`.
    - Input `"  My Client & Co.! "` → expected folder `"My Client & Co.!"` (trim spaces; `&`, `.`, `!` are legal — NOT stripped).
    - Update `Assert.Contains` and `Assert.Equal` accordingly.
  - _Spec: PKG-01 scenarios (passes through unchanged, accents/symbols preserved)_

#### B-2 [TEST — NEW] — xUnit: `NormalizeFolderName` unit tests

- [x] Add tests to `SocialStudio.Tests\Application\Social\ContentGeneration\Packaging\PackageBuilderTests.cs` (or a dedicated `NormalizeFolderNameTests.cs` in same namespace):
  - `NormalizeFolderName_NoReservedChars_PassesThroughUnchanged`: `"Acme Corp"` → `"Acme Corp"`.
  - `NormalizeFolderName_AccentsAndSymbolsPreserved`: `"Café & Co."` → `"Café & Co."`.
  - `NormalizeFolderName_ReservedCharsStripped_NoSlugification`: `"Client:Name/Test?"` → `"ClientNameTest"`.
  - `NormalizeFolderName_AllReservedReturnsEmpty`: `"<>:\""` → `""`.
  - `NormalizeFolderName_NullInput_ReturnsEmpty`: `null` → `""`.
  - `NormalizeFolderName_TrailingDotTrimmed`: `"Client."` → `"Client"` (Windows trailing-dot rule).
  - _Spec: PKG-01 all four scenarios_

#### B-3 [TEST — NEW] — xUnit: Assemble handler uses display name not GUID

- [x] Add tests to `SocialStudio.Tests\Application\Social\ContentGeneration\Assemble\AssembleGeneratedPackageQueryHandlerTests.cs`:
  - `Handle_ClientWithDisplayName_ZipEntryUsesDisplayNameNotGuid`: mock `ISocialClientRepository.GetByIdAsync` returns client with `.Name = "Acme Corp"` → ZIP entry path is `"Acme Corp/flyer.png"`, NOT `"{guid}/flyer.png"`.
  - `Handle_ClientWithReservedCharsInName_SanitizesFolder`: client `.Name = "Client:Name/Test?"` → ZIP entry path `"ClientNameTest/flyer.png"`.
  - `Handle_UnknownClient_FallsBackToGuid`: `GetByIdAsync` returns null → ZIP entry path is `"{clientId}/…"`.
  - _Spec: PKG-02 scenarios (assemble path uses display name, applies same sanitizer, unknown-client fallback)_

#### B-4 [IMPL] — Rewrite `NormalizeFolderName` sanitize-only

- [x] Edit `SocialStudio.Application\Features\Social\ContentGeneration\Packaging\GeneratedPackageBuilder.cs`:
  - Replace `SlugUnsafe` regex and the `ToLowerInvariant()` + `Trim('-')` logic with:
    - Strip only the 9 Windows-reserved chars `< > : " / \ | ? *` plus C0 control chars (0x00–0x1F, 0x7F) via a targeted regex.
    - `TrimEnd('.', ' ')` for Windows trailing-dot/space rule.
    - Preserve case, accents, spaces, `&`, `.`, `!`, emoji — no slug generation.
  - Remove `SlugUnsafe` static field.
  - _Spec: PKG-01 (sanitize-only, no slugification)_

#### B-5 [IMPL] — `AssembleGeneratedPackageQueryHandler` resolves client display name

- [x] Edit `SocialStudio.Application\Features\Social\ContentGeneration\Assemble\AssembleGeneratedPackageQuery.cs`:
  - Add constructor parameter `ISocialClientRepository clientRepository` (4th dependency).
  - Add private `async Task<IReadOnlyDictionary<Guid, string>> ResolveClientFoldersAsync(IEnumerable<Guid> clientIds, Guid organizationId, CancellationToken ct)`:
    - For each distinct clientId: `GetByIdAsync(ClientId, organizationId, ct)` → `GeneratedPackageBuilder.NormalizeFolderName(client?.Name)`.
    - Null/empty result → fallback to `id.ToString()`.
    - Cache per call via `Dictionary<Guid, string>`.
  - After `latestRows` is computed, call `ResolveClientFoldersAsync` with distinct client IDs from those rows.
  - Replace line 163 `$"{parsed.ClientId}/{parsed.RelativeAssetPath}"` with `$"{folder}/{parsed.RelativeAssetPath}"` where `folder` comes from the resolved dictionary.
  - _Spec: PKG-02_

#### B-6 [IMPL] — Frontend: update `FORMAT_FILE_NAMES`, `clientLabels`, and `clientFolderName`

- [x] Edit `apps\web\components\social\single-analyzer-client.tsx`:
  - Line 514: change `"reel-script": "reel-script.txt"` → `"reel-script": "reel-script.md"`.
  - Line 2836: change `clients.map((c) => [c.id, deriveClientFolderSlug(c.name)])` → `clients.map((c) => [c.id, c.name])`.
  - Line 2831: change `deriveClientFolderSlug(selectedClient?.name ?? "")` → `selectedClient?.name ?? ""`.
  - Line 1063: change `description: deriveClientFolderSlug(client.name) || client.id` → `description: client.name || client.id`.
  - _Spec: RS-03 scenario "Frontend resolves .md extension", PKG-02 scenario "Assemble path uses display name"_

#### B-7 [IMPL] — Remove `deriveClientFolderSlug` if caller-free

- [x] Verify no remaining callers of `deriveClientFolderSlug` in `apps\web\components\social\single-analyzer-client.tsx` after B-6.
  - If no callers remain: delete the `deriveClientFolderSlug` function (lines 591–596 approx).
  - If callers remain: leave in place and add a `// TODO: remove after all callers migrated` comment.
  - _Design: Decision 6 — remove if no caller remains after switch_

#### B-8 — Gates + commit

- [x] Run `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj --filter "FullyQualifiedName~PackageBuilder|FullyQualifiedName~AssembleGeneratedPackage|FullyQualifiedName~NormalizeFolderName"` — 1021 passed, 0 failed, 2 skipped. All WU-B tests green.
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — 16 pass (all MUST), 2 skipped (SHOULD). PASS.
- [ ] Commit: awaiting user

---

### WU-C — Single-analyzer flow diagram (docs-only, no test)

**Spec IDs: DIAG-01**
**Parallel: can run alongside WU-A or WU-B**

#### C-1 — Excalidraw diagram

- [x] Create `docs\architecture\diagrams\single-analyzer-flow.excalidraw`:
  - Valid Excalidraw type 2 JSON (`type: "excalidraw"`, `version: 2`, `source: "pomdtr.excalidraw-editor"`).
  - Follows `content-plan-flow` conventions (rectangle + `roundness.type:3`, text `containerId`, arrows with `start/endBinding`, 35% opacity zones).
  - Color palette: blue (`#4a9eed`/`#a5d8ff`) input nodes, purple (`#8b5cf6`/`#d0bfff`) pipeline stages, amber (`#f59e0b`/`#fff3bf`) decision nodes, green terminal, red failure.
  - Happy path flow: `Submit URL → SingleContentAnalysis (Requested→…→Analyzed) → requestPackage → ContentGenerationJob → Orchestrator (BrandSnapshot → generators → VisualCompositionService → IContentRenderer [PNG | .md | .txt]) → per-client storage → frozen ZIP → [DECISION amber] Download (Assemble | legacy) = green vs Export-to-Canva (Phase B dashed placeholder)`.
  - Decision node labelled: `Download vs Export to Canva` with Canva branch marked `Phase B`.
  - _Spec: DIAG-01_

#### C-2 — Mermaid companion file

- [x] Create `docs\architecture\diagrams\single-analyzer-flow.md`:
  - Header + link to `.excalidraw` source.
  - Mermaid `flowchart TD` rendering the same happy path as the Excalidraw diagram.
  - Legend section: color meanings (input, pipeline, decision, terminal).
  - Download vs Export-to-Canva decision node with Canva branch labelled `Phase B (placeholder)`.
  - _Spec: DIAG-01_

#### C-3 — Docs sync (no separate commit needed — bundle with C-2)

- [x] Commit: awaiting user

---

### WU-D — Docs sync (spec delta + openspec update)

**Spec IDs: all (documentation sync rule)**
**Parallel: can run after WU-A and WU-B are merged**

#### D-1 — Update narrative/operator docs

- [x] Created `docs\operators\content-generation-package.md` — documents reel-script.md download behavior, client display-name folder naming, cross-OS sanitizer rules, and ZIP structure.
  - No existing narrative doc described these behaviors before this change. `openspec/specs/content-generation/spec.md` is a TBD stub; merging the spec-delta into it is the **sdd-archive** phase's job. Only narrative + operator docs updated here.
  - _AGENTS.md §Documentation Sync — updated docs/ in the same change_

#### D-2 — Gates + commit

- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — final gate run (all layers). Results below.
- [ ] Commit: awaiting user

---

## Task Dependency Map

```
WU-A (Reel-Script Markdown)
  A-1..A-4 [tests: RED]
      ↓
  A-5 [enum Markdown]
      ↓
  A-6 [ReelScriptComposer body]
      ↓
  A-7 [LocalSvgContentRenderer Markdown branch]
      ↓
  A-8 [fix existing test assertions]
      ↓
  A-9 [gates + commit]
      ↓
WU-B (Display-name folders)              WU-C (Diagram) ← parallel
  B-1..B-3 [tests: RED/UPDATE]           C-1 [excalidraw]
      ↓                                  C-2 [mermaid + legend]
  B-4 [NormalizeFolderName rewrite]      C-3 [commit]
      ↓
  B-5 [Assemble handler + ISocialClientRepository]
      ↓
  B-6 [Frontend .md + client.name]
      ↓
  B-7 [remove deriveClientFolderSlug if caller-free]
      ↓
  B-8 [gates + commit]
      ↓
WU-D (Docs sync)
  D-1 [merge spec-delta into canonical spec]
  D-2 [gates + commit]
```

**Parallel opportunities:**
- WU-C (diagram) is entirely independent — can be authored any time.
- WU-D requires WU-A and WU-B to be committed first so the docs reflect final behavior.
- WU-B tasks B-1 through B-3 (test RED) can be written immediately without waiting for A to complete (no compile dependency).

---

## Spec Coverage Matrix

| Spec ID | Work Units |
|---|---|
| PKG-01 | WU-B (B-1, B-2, B-4) |
| PKG-02 | WU-B (B-3, B-5, B-6) |
| RS-01 | WU-A (A-1, A-2, A-5) |
| RS-02 | WU-A (A-3, A-6) |
| RS-03 | WU-A (A-4, A-7, A-8) + WU-B (B-6) |
| DIAG-01 | WU-C (C-1, C-2) |
