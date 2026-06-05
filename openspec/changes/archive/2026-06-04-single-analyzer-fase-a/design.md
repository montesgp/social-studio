# Design: single-analyzer-fase-a — Packaging & Reel-Script Quick Wins

## Overview

Fase A is a quick-wins slice for the existing **content-generation** capability, delivered as a single PR to `dev` (~370 lines, no chaining). It fixes three reported pain points without touching the generation pipeline shape:

1. **#2 — Folders use the client DISPLAY NAME on BOTH download paths** (frozen-ZIP and assemble-on-demand), sanitizing only filesystem-illegal characters instead of slugifying.
2. **#3 — Reel-script is delivered as Markdown** (`reel-script.md`, `text/markdown`) with newline-separated scenes and bold timestamps, preserving emojis.
3. **#7 — A committed Excalidraw + Mermaid diagram** of the single-analyzer happy path with a Download / Export-to-Canva decision node (Canva = Fase B placeholder).

Canva integration and visual-quality items (#1/#4/#5/#6) are EXPLICITLY out of scope (Fase B). Strict TDD applies to all backend changes.

This doc is the HOW (architecture/seams). The spec (`specs/content-generation/spec.md`) is the WHAT. Authoritative exploration: engram obs #555.

## Architectural Boundaries

Strict hexagonal — dependencies point inward (Domain ← Application ← Infrastructure/Api). No EF/Npgsql/Supabase/AspNetCore types may appear in Domain or Application. `TreatWarningsAsErrors` is on for Domain/Application/Infrastructure, so any non-exhaustive `switch` warning is a build break.

| Layer | Change in this slice |
|---|---|
| Domain | Add `Markdown` to `CompositionOutputKind` (`VisualCompositionSpec.cs`). Pure enum addition. |
| Application | Rewrite `NormalizeFolderName` (sanitize-only); resolve client display name in `AssembleGeneratedPackageQueryHandler` via the EXISTING `ISocialClientRepository` port; rewrite `ReelScriptVisualComposer` body + set `OutputKind = Markdown`. |
| Infrastructure | Add a `Markdown` branch to `LocalSvgContentRenderer` → `.md` + `text/markdown; charset=utf-8`. |
| Api | No contract change. `AssemblePackageAsync` keeps sending GUIDs; name resolution stays inside the handler. |
| Frontend | `clientLabels`/`clientFolderName` use `client.name` directly; `FORMAT_FILE_NAMES["reel-script"]` → `reel-script.md` + `resolveFormatFilename` `.md` handling. |
| Docs | New `single-analyzer-flow.{excalidraw,md}` pair. |

## Architecture Decisions

### Decision 1 — Client-name seam on the Assemble path: resolve via the EXISTING `ISocialClientRepository` port (Option B)

**Context.** `AssembleGeneratedPackageQueryHandler` (`Assemble/AssembleGeneratedPackageQuery.cs:163`) builds each ZIP entry as `$"{parsed.ClientId}/{parsed.RelativeAssetPath}"` using the raw `clientId` GUID. The handler receives only GUIDs (the Api `AssemblePackageAsync` facade forwards `ClientSelection(ClientId, FormatKeys)` from the request body — the frontend posts GUIDs, never names). So neither the handler nor its caller currently has the display name.

**Choice.** Resolve the display name INSIDE the handler using the existing Application port `ISocialClientRepository.GetByIdAsync(ClientId id, Guid organizationId, CancellationToken)`, which returns a `Domain.Social.Clients.Client` exposing `.Name` (`Client.cs:36`). The handler already holds `query.OrganizationId` and every per-asset `clientId`. Build a `Dictionary<Guid, string>` of clientId → sanitized folder name once (deduplicated over the latest rows), then compose the entry path as `$"{folderName}/{parsed.RelativeAssetPath}"`, falling back to the GUID string when the client cannot be resolved (deleted client, cross-tenant miss).

**Signature/flow changes.**
- Constructor gains one dependency: `ISocialClientRepository clientRepository` (alongside the existing three). Register is already in DI; no new registration needed.
- New private helper:
  ```csharp
  private async Task<IReadOnlyDictionary<Guid, string>> ResolveClientFoldersAsync(
      IEnumerable<Guid> clientIds, Guid organizationId, CancellationToken ct)
  ```
  For each distinct clientId: `await _clientRepository.GetByIdAsync(new ClientId(id), organizationId, ct)`; map to `GeneratedPackageBuilder.NormalizeFolderName(client?.Name)`; if null/empty → fall back to `id.ToString()`.
- In `Handle`, after `latestRows` is computed, call the helper with `latestRows.Select(r => r.ClientId).Distinct()`. Replace line 163:
  ```csharp
  var folder = clientFolders.TryGetValue(parsed.ClientId, out var f) && !string.IsNullOrEmpty(f)
      ? f : parsed.ClientId.ToString();
  var entryName = $"{folder}/{parsed.RelativeAssetPath}";
  ```
- `NormalizeFolderName` is made `internal static` already — reuse it directly so BOTH paths share ONE sanitizer (single source of truth). No public-surface change.

**Why Option B over A and C.**
- **Option A (pass name through the query DTO):** REJECTED. The caller does not have the name — it would force the Api layer to do a client lookup first (moving persistence concerns up the stack) or force the frontend to post display names (untrusted, spoofable, breaks tenant authority). The handler is the correct authority boundary because it already owns the org-scoped tenant check.
- **Option C (persist display name in asset/package metadata at generation time):** REJECTED for this slice. It would require a schema/migration change on `content_generation_format_assets` and a backfill for already-stored assets; existing packages would still show GUIDs. Out of proportion for a quick win and it duplicates data that the client row already owns (stale-name risk).
- **Option B is the cleanest hexagonal seam:** zero new abstractions, zero schema change, no provider leak (the port returns a Domain `Client`), and it reuses the org-scoped repository that already enforces tenancy. The frozen-ZIP path already receives the name (`ContentGenerationJobProcessor` passes `brand.Client?.Name`), so after this change both paths share the same sanitizer and the same authority for the name.

**Cost/risk.** N extra repository reads per assemble (N = distinct clients in the selection, typically 1–5). Acceptable; the assemble path is an on-demand user action, not hot. Cache within the single call via the dictionary so each client is read at most once.

### Decision 2 — `NormalizeFolderName` is a cross-OS sanitizer (strip illegal chars, trim edge chars, guard device names)

**Choice.** Replace the aggressive slug regex (`[^a-z0-9]+` → `-`, lowercased, hyphen-trimmed) with a cross-OS sanitizer that removes filesystem-illegal characters while preserving case, accents, spaces, ampersands, internal dots, and emoji. Method name and `internal static string NormalizeFolderName(string?)` signature stay stable so both callers (`GeneratedPackageBuilder.BuildAsync` and the Assemble helper) are unaffected.

**Cross-OS rules applied in order:**
1. Strip the 9 cross-OS-illegal chars (`< > : " / \ | ? *`), DEL (0x7F), and C0 control chars (0x00–0x1F).
2. `TrimStart('.', ' ')` — leading dots (Unix hidden-file/directory) and leading spaces.
3. `TrimEnd('.', ' ')` — trailing dots and spaces (Windows disallows these as folder leaf names).
4. If the entire result is a Windows reserved device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, `CLOCK$`) matched case-insensitively as a whole, append `_` to neutralize (e.g. `NUL` → `NUL_`). Substring matches (e.g. `Conway`) are NOT affected.

**Reference implementation.**
```csharp
private static readonly Regex ReservedAndControl =
    new(@"[<>:""/\\|?*\x00-\x1F\x7F]", RegexOptions.Compiled);

private static readonly Regex DeviceName =
    new(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9]|CLOCK\$)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

internal static string NormalizeFolderName(string? name)
{
    if (string.IsNullOrWhiteSpace(name)) return string.Empty;
    var sanitized = ReservedAndControl
        .Replace(name, string.Empty)
        .TrimStart('.', ' ')
        .TrimEnd('.', ' ');
    if (sanitized.Length == 0) return string.Empty;
    if (DeviceName.IsMatch(sanitized)) sanitized += "_";
    return sanitized;
}
```

**Acceptance mapping.**
- `Café & Co.` → `Café & Co` (trailing `.` trimmed; accent/ampersand preserved).
- `Co. Ltd` → `Co. Ltd` (middle dot preserved).
- `.config` → `config` (leading dot trimmed — Unix hidden-file prevention).
- `Client/Name:Test` → `ClientNameTest` (strips `/` and `:`).
- `NUL` → `NUL_` (device-name guard). `Conway` → `Conway` (substring — unchanged).
- Empty/whitespace → `""` (legacy flat layout, unchanged).

### Decision 3 — `CompositionOutputKind.Markdown` is an explicit new arm everywhere the enum is dispatched

**Choice.** Add `Markdown` to the Domain enum. The renderer dispatches on the enum (never on format-key names — enforced by the type's own doc comment). Every dispatch site gets an explicit `Markdown` arm so the change is exhaustive and warning-clean.

**Full switch/dispatch audit (every `CompositionOutputKind` consumer in production code):**

| Site | File:line | Current shape | Needs Markdown arm? |
|---|---|---|---|
| Enum definition | `Domain/.../VisualCompositionSpec.cs:8` | `Raster`, `Text` | YES — add `Markdown` member + xmldoc |
| Renderer dispatch | `Infrastructure/Rendering/LocalSvgContentRenderer.cs:52` | `if (OutputKind == Text) {...} else raster` | YES — add `Markdown` branch BEFORE the raster fallthrough → `.md` + `text/markdown; charset=utf-8` |
| Reel-script composer | `Application/.../Composers/ReelScriptVisualComposer.cs:38` | sets `OutputKind = Text` | YES — change to `Markdown` |
| Text-card base composer | `Application/.../Composers/TextCardVisualComposerBase.cs:34` | sets `OutputKind = Text` | NO — hook/caption/cta stay `.txt` |
| Content-idea composer | `Application/.../Composers/ContentIdeaVisualComposer.cs:35` | sets `OutputKind = Text` | NO — stays `.txt` |

There is NO `switch` statement over the enum in production code; the only dispatch is the single `if` in `LocalSvgContentRenderer`. The renderer branch ordering must be: `Text` → `.txt`; `Markdown` → `.md`; default → raster PNG. Because `RenderAsync` returns via `if/else`, no exhaustiveness warning fires, but we ADD the explicit `Markdown` `if` so behavior is intentional and a future `switch` refactor stays correct.

**Test doubles to update** (these construct specs with `OutputKind`, not dispatch on it — they compile as-is but their assertions change):
- `Tests/.../ComposerOutputKindTests.cs:31` — reel-script assertion flips from `Text` to `Markdown`.
- `Tests/.../VisualCompositionSpecTests.cs`, `LocalSvgContentRendererTests.cs`, `ContentGenerationJobProcessorStorageTests.cs` — keep using `Text` for their generic fixtures; add NEW cases for `Markdown` where renderer behavior is asserted.

### Decision 4 — `ReelScriptVisualComposer` builds a structured Markdown body

**Choice.** Stop joining scenes with a single space. Build a newline-structured Markdown document; bold each timestamp; preserve any emoji already present in `Voiceover`/`OnScreenText` (no emoji field exists — they live inside the AI strings, so simply not flattening preserves them). Set `OutputKind = CompositionOutputKind.Markdown`.

**Body shape.**
```
{Hook}

**{timestamp}**: {voiceover}
{onScreenText}

**{timestamp}**: {voiceover}
{onScreenText}

CTA: {ctaText}
```
- Scenes joined by `\n\n` (blank line between blocks).
- Each scene line starts with `**{scene.Timestamp}**`.
- `OnScreenText` rendered on its own line under the voiceover (omit the line when empty).
- Null/empty payload falls back to the existing `"Reel script content"` literal with `OutputKind = Markdown`.
- The same `scriptBody` continues to feed `CopySlots["script"]` and `TextBody` (the PNG text-card path is unused for this format now, but keeping `CopySlots` consistent avoids a divergent code path).

### Decision 5 — `LocalSvgContentRenderer` Markdown branch

**Choice.** Add, immediately after the existing `Text` branch:
```csharp
if (spec.OutputKind == CompositionOutputKind.Markdown)
{
    var body = spec.TextBody ?? string.Empty;
    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
    var asset = RenderedAsset.Create(
        spec.FormatKey, "text/markdown; charset=utf-8", $"{spec.FormatKey}.md", bytes);
    return Task.FromResult(Result.Success(asset));
}
```
Filename is `{FormatKey}.md` (→ `reel-script.md`), mime `text/markdown; charset=utf-8`. UTF-8 bytes guarantee emoji survive end to end.

### Decision 6 — Frontend uses the display name directly and resolves `.md`

**Choice.**
- `FORMAT_FILE_NAMES["reel-script"]` (`single-analyzer-client.tsx:514`): `"reel-script.txt"` → `"reel-script.md"`. `resolveFormatFilename` (line 528) already corrects extensions against the canonical map, so a stored `reel-script.txt` manifest entry is rewritten to `.md` automatically once the map changes — no extra branch needed.
- `clientLabels` (line 2836) and `clientFolderName` (line 2828) switch from `deriveClientFolderSlug(c.name)` to `c.name` directly so the Package tab tree shows the same display name the ZIP now uses.
- `deriveClientFolderSlug` (line 591) is retained as an EXPORTED function only if still referenced elsewhere; line 1063 (`OptionCardSelector` description) currently uses it — change that to `client.name` as well, then DELETE `deriveClientFolderSlug` if it has no remaining callers (verify at apply; removing dead slug logic keeps the FE aligned with the backend's sanitize-only contract). If any non-display caller remains, leave the function but stop using it for folder display.

### Decision 7 — Diagram authored in apply (docs-only, no test gate)

Mirror `docs/architecture/diagrams/content-plan-flow.{excalidraw,md}`: a committed `.excalidraw` (Excalidraw schema `type: "excalidraw"`, `version: 2`, `source: ...pomdtr.excalidraw-editor`) plus a companion Mermaid `.md` for GitHub rendering with a top note linking the `.excalidraw`.

**Element conventions (from `content-plan-flow.excalidraw`):** `rectangle` nodes (rounded via `roundness.type:3`), `text` labels bound via `containerId`, `arrow` elements with `startBinding`/`endBinding`, low-opacity (35%) colored grouping rectangles as zones, `fontFamily: 1` (Virgil), `fontSize: 18` for node labels. Palette: blue (`#4a9eed`/`#a5d8ff`) inputs, purple (`#8b5cf6`/`#d0bfff`) pipeline stages, amber (`#f59e0b`/`#fff3bf`) decisions, green (`#22c55e`/`#b2f2bb`) terminal, red (`#ef4444`/`#ffd8d8`) failure.

**Happy-path node list:**
```
Submit URL (blue)
  → SingleContentAnalysis: Requested→ResolvingContent→ContentResolved→Analyzing→Analyzed (purple)
  → requestPackage → ContentGenerationJob queued (purple)
  → ContentPackageOrchestrator.RunAsync: BrandSnapshot → format generators
        → VisualCompositionService → IContentRenderer (PNG | .md | .txt) (purple)
  → assets stored per-client via IGeneratedAssetStorage (purple)
  → job completes; frozen ZIP built (purple)
  → [DECISION] Download vs Export to Canva (amber)
        → Download: assemblePackage (preferred) | downloadPackageFile (legacy) → green terminal
        → Export to Canva: Phase B placeholder, dashed/amber, "both paths must yield Canva-compatible formats"
```

## Data Flow

```
DOWNLOAD (Assemble path, INC-3+):
[Web] --POST /api/social/single-analyzer/{analysisId}/package/assemble {clients:[guid]}-->
  AssembleGeneratedPackageQueryHandler
    1. tenant-scope analysis (ISingleContentAnalysisRepository)
    2. load asset rows, keep latest per (client, format)
    3. ResolveClientFoldersAsync: per distinct clientId ->
         ISocialClientRepository.GetByIdAsync -> Client.Name -> NormalizeFolderName
    4. ZIP entry = "{displayNameFolder}/{relativeAssetPath}"   (was "{guid}/...")
    5. return ZIP stream

REEL-SCRIPT (generation path, unchanged shape):
ReelScriptVisualComposer.Compose -> VisualCompositionSpec{OutputKind=Markdown, TextBody=md}
  -> LocalSvgContentRenderer.RenderAsync -> RenderedAsset{ "reel-script.md", text/markdown }
  -> stored -> appears in package tree as reel-script.md (FE resolveFormatFilename forces .md)
```

## File Changes

| File | Action | Description |
|---|---|---|
| `Domain/Social/ContentGeneration/VisualCompositionSpec.cs` | Modify | Add `Markdown` enum member + xmldoc; update `Text` doc cross-refs |
| `Application/.../Packaging/GeneratedPackageBuilder.cs` | Modify | Replace `SlugUnsafe` regex with sanitize-only `NormalizeFolderName` |
| `Application/.../Assemble/AssembleGeneratedPackageQuery.cs` | Modify | Inject `ISocialClientRepository`; add `ResolveClientFoldersAsync`; entry path uses display-name folder + GUID fallback |
| `Application/.../Visual/Composers/ReelScriptVisualComposer.cs` | Modify | Newline/bold-timestamp Markdown body; `OutputKind = Markdown` |
| `Infrastructure/Rendering/LocalSvgContentRenderer.cs` | Modify | Add `Markdown` branch → `.md` + `text/markdown; charset=utf-8` |
| `apps/web/components/social/single-analyzer-client.tsx` | Modify | `FORMAT_FILE_NAMES["reel-script"]`→`.md`; `clientLabels`/`clientFolderName`/OptionCard use `client.name`; retire `deriveClientFolderSlug` if unused |
| `Tests/.../Packaging/PackageBuilderTests.cs` | Modify | Rewrite slug assertions (see below) + add accent/illegal-char/empty cases |
| `Tests/.../Assemble/AssembleGeneratedPackageQueryHandlerTests.cs` | Modify | Add display-name folder + GUID-fallback cases; wire `ISocialClientRepository` mock |
| `Tests/.../Visual/ComposerOutputKindTests.cs` | Modify | Reel-script asserts `Markdown` not `Text` |
| `Tests/.../Visual/ReelScriptComposerTests.cs` | Create | Newlines, bold timestamps, emoji-preserved, Markdown output kind |
| `Tests/.../Rendering/LocalSvgContentRendererTests.cs` | Modify | Add `Markdown` → `.md` + `text/markdown` case |
| `docs/architecture/diagrams/single-analyzer-flow.excalidraw` | Create | Diagram source |
| `docs/architecture/diagrams/single-analyzer-flow.md` | Create | Mermaid + legend, links `.excalidraw` |
| `openspec/specs/content-generation/spec.md` | Modify/Create | Record folder-naming + reel-script Markdown acceptance criteria |

### Existing `PackageBuilderTests` assertions that MUST change

| Test | Line | Old assertion | New assertion |
|---|---|---|---|
| `BuildAsync_WithClientName_FilesPlacedUnderClientFolder` | 128–129 | `acme-corp/flyer.png`, `acme-corp/carousel/slide_1.png` | `Acme Corp/flyer.png`, `Acme Corp/carousel/slide_1.png` |
| same | 133 | `ClientFolderName == "acme-corp"` | `ClientFolderName == "Acme Corp"` |
| `BuildAsync_ClientNameNormalisation_CollapsesSpecialChars` | 153–154 | `my-client-co/hook.png`, `"my-client-co"` (input `"  My Client & Co.! "`) | rename to `..._SanitizesIllegalChars`; input keeps `& . !` (all legal) → `My Client & Co.!` trimmed → assert `My Client & Co.!/hook.png` and `ClientFolderName == "My Client & Co.!"` (trailing space trimmed; `.`/`!` kept since neither is illegal and `!` is not trailing) |

## Interfaces / Contracts

```csharp
// Domain — additive enum member (no signature change elsewhere)
public enum CompositionOutputKind { Raster, Text, Markdown }

// Application — Assemble handler gains one existing port; no public DTO change
public AssembleGeneratedPackageQueryHandler(
    IGeneratedAssetStorage assetStorage,
    IContentGenerationFormatAssetRepository assetRepository,
    ISingleContentAnalysisRepository analysisRepository,
    ISocialClientRepository clientRepository);   // NEW (already DI-registered)

// Shared sanitizer — signature unchanged, semantics changed
internal static string NormalizeFolderName(string? name);
```

## Testing Strategy (TDD — test-first ordering aligned to spec scenarios)

Strict TDD: write the failing test, then the minimum code. Order respects the dependency graph (Domain → Application → Infrastructure → Frontend), and follows the proposal's work-unit order A (#3) → B (#2) → C (#7).

**Work-unit A — Reel-script Markdown (#3):**
1. `VisualCompositionSpecTests` / enum: `CompositionOutputKind` HAS `Markdown` (Domain).
2. `ReelScriptComposerTests.ReelScriptComposer_OutputKind_IsMarkdown`.
3. `ReelScriptComposerTests.TextBody_ContainsNewlinesBetweenScenes` (multi-scene → `\n`).
4. `ReelScriptComposerTests.TextBody_BoldTimestamps` (each scene line starts `**{timestamp}**`).
5. `ReelScriptComposerTests.TextBody_PreservesEmojis` (emoji in voiceover survives).
6. `ComposerOutputKindTests` — reel-script flips `Text` → `Markdown`.
7. `LocalSvgContentRendererTests.Markdown_EmitsMdExtensionAndMime` (`reel-script.md`, `text/markdown; charset=utf-8`).

**Work-unit B — Display-name folders (#2):**
8. `PackageBuilderTests.NormalizeFolderName_PreservesAccentsAndCase` (`Café & Co.` → `Café & Co`).
9. `PackageBuilderTests.NormalizeFolderName_StripsIllegalChars` (`Client/Name:Test` → `ClientNameTest`).
10. `PackageBuilderTests.NormalizeFolderName_EmptyOrWhitespace_ReturnsEmpty`.
11. `PackageBuilderTests` — rewrite the two existing folder-layout assertions (table above).
12. `AssembleGeneratedPackageQueryHandlerTests.Assemble_UsesClientDisplayName_NotGuid` (mock `ISocialClientRepository` → `"Acme Corp"`; entry is `Acme Corp/<asset>`).
13. `AssembleGeneratedPackageQueryHandlerTests.Assemble_UnknownClient_FallsBackToGuid` (repo returns null → GUID folder).
14. Frontend: manual/Jest check that `resolveFormatFilename("reel-script")` → `reel-script.md` and tree label is `client.name`.

**Work-unit C — Diagram (#7):** no automated test; review for accuracy before merge.

## Migration / Rollout

Additive and behavior-only — no schema change, no migration, no data migration. New reel-scripts are `.md`; already-stored `reel-script.txt` assets keep their stored extension but the FE rewrites the displayed/download name to `.md` via `resolveFormatFilename`. New assembled ZIPs use display-name folders; previously frozen ZIPs are unaffected. Single PR to `dev`.

## Open Questions

- [ ] Confirm at apply whether `deriveClientFolderSlug` has any remaining caller after the display-name switch; delete if dead, otherwise keep and stop using for folder display.
- [ ] Confirm the desired handling of `OnScreenText` empty lines in the Markdown body (omit line vs. blank) — assume omit when empty.
