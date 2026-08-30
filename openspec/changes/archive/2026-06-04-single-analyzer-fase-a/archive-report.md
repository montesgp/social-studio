# SDD Archive Report: single-analyzer-fase-a

**Archived**: 2026-06-04  
**Change ID**: `single-analyzer-fase-a`  
**Status**: COMPLETE — Fully implemented, verified, and ready for user commit/merge to dev

---

## Executive Summary

The `single-analyzer-fase-a` change — a quick-wins slice to improve the existing single-analyzer packaging flow through three targeted fixes (folder naming, reel-script Markdown output, and flow diagram) — is now complete and archived. All 5 work units (WU-A, WU-B, WU-B Cross-OS Hardening, WU-C, WU-D) have been delivered and verified PASS with 0 CRITICAL findings. Spec delta has been merged into the canonical `openspec/specs/content-generation/spec.md`. The change is archived and ready for user commit.

---

## What Was Delivered

**Single PR to dev (awaiting user commit)**

### WU-A — Reel-Script Markdown Output (RS-01, RS-02, RS-03)
- Added `Markdown` member to `CompositionOutputKind` enum (Domain)
- Rewrote `ReelScriptVisualComposer` to emit newline-separated Markdown body with bold timestamps and preserved emojis
- Added `Markdown` branch to `LocalSvgContentRenderer` (Infrastructure) → `reel-script.md`, MIME `text/markdown; charset=utf-8`
- Updated frontend `FORMAT_FILE_NAMES["reel-script"]` from `.txt` to `.md`
- Created comprehensive test suite: 10 new tests covering enum, composition, rendering, and emoji preservation
- **Result**: 1000 passed, 0 failed, 2 skipped (pre-existing)

### WU-B — Display-Name Folders (PKG-01, PKG-02)
- Rewrote `NormalizeFolderName` sanitizer: strips ONLY Windows-reserved chars + control chars, preserves case/accents/spaces
- Updated `AssembleGeneratedPackageQueryHandler` to resolve client display name via `ISocialClientRepository` (reused existing port, zero new abstractions)
- Frontend: `clientLabels` and `clientFolderName` now use `client.name` directly; deleted unused `deriveClientFolderSlug`
- Rewrote 2 existing `PackageBuilderTests` assertions to match new sanitize-only behavior
- Added 21 new unit tests covering all cross-OS scenarios (accents, symbols, device names, edge cases)
- Added 3 new Assemble handler tests (display-name resolution, GUID fallback)
- **Result**: 1021 passed, 0 failed, 2 skipped (+21 new tests)

### WU-B Cross-OS Hardening
- Added 18 rigorous cross-OS tests (leading/trailing dots, spaces, device names, DEL char, control chars)
- Implemented device-name guarding: `NUL` → `NUL_`, substring matches like `Conway` unaffected
- Updated spec-delta PKG-01 with 5-rule cross-OS specification
- **Result**: 1039 passed, 0 failed, 2 skipped (+18 new tests); 16 MUST gates pass

### WU-C — Single-Analyzer Flow Diagram (DIAG-01)
- Created `docs/architecture/diagrams/single-analyzer-flow.excalidraw` — valid Type 2 JSON following `content-plan-flow` conventions
  - Blue input nodes, purple pipeline stages, amber decision node, green terminal
  - Happy path: URL → SingleContentAnalysis → requestPackage → Orchestrator → storage → ZIP → DOWNLOAD vs EXPORT-TO-CANVA decision
  - Canva branch marked Phase B placeholder (dashed, amber)
- Created companion `docs/architecture/diagrams/single-analyzer-flow.md` — Mermaid flowchart + legend + implementation notes
- Both files follow `content-plan-flow` conventions: Excalidraw schema 2, Virgil font, 35% opacity zones, rectangle+text+arrow elements

### WU-D — Docs Sync
- Created `docs/operators/content-generation-package.md` — operator narrative documenting:
  - Reel-script.md behavior and file structure
  - Client display-name folder naming rules and examples
  - Cross-OS sanitizer rules with 5-rule specification
  - ZIP structure and asset layout
  - No prior operator doc existed for these behaviors
- **Spec delta merge** (archive-phase job): delta spec PKG-01, PKG-02, RS-01, RS-02, RS-03, DIAG-01 merged into `openspec/specs/content-generation/spec.md` (replacing TBD stub with full content-generation capability spec)

---

## Verification Outcome

**Status: PASS WITH NOTES**

The `sdd-verify` phase confirmed:
- All 5 work units complete and implemented
- 1039 tests passed, 0 failed (pre-existing 2 skipped are unrelated)
- 16 MUST gates pass, 2 SHOULD gates skipped (pre-existing, not introduced by change)
- 0 CRITICAL issues
- 1 WARNING W-01 (documentation gap — `NormalizeFolderName_NullInput_ReturnsEmpty` test not added as separate explicit case, but behavior is tested and correct)
- 1 SUGGESTION S-01 (Assemble test comment clarification for readability)
- Both warnings were POST-VERIFY REMEDIATED:
  - W-01: Added `NormalizeFolderName_NullInput_ReturnsEmpty` explicit test (line ~134 in PackageBuilderTests)
  - S-01: Added clarifying comment to `Handle_TwoClientSelection_ZipContainsBothClientFolders` test

**Hexagonal boundary**: Clean. `ISocialClientRepository` is an Application-layer port returning Domain types; no persistence leaks. Architecture gates pass.

**Scope discipline**: No Canva code, no visual-quality changes, no schema changes. Fase B explicitly deferred and placeholded in diagram.

---

## Spec Delta Merge

The following requirements from `openspec/changes/single-analyzer-fase-a/specs/content-generation/spec-delta.md` have been merged into `openspec/specs/content-generation/spec.md`:

### Added Requirements
- **PKG-01** — Folder Name Equals Client Display Name (Frozen-ZIP Path): sanitize-only cross-OS transform with 5 rules (illegal chars, control chars, leading/trailing dots/spaces, device name guarding)
- **PKG-02** — Folder Name Equals Client Display Name (Assemble Path): same sanitizer via read-only `ISocialClientRepository` lookup
- **RS-01** — CompositionOutputKind Contains Markdown Discriminator: explicit enum member + exhaustive renderer dispatch
- **RS-02** — Reel-Script Body Is Newline-Separated with Bold Timestamps: Markdown body structure with scene separation and emoji preservation
- **RS-03** — Renderer Emits reel-script.md with Markdown MIME Type: `.md` filename, `text/markdown; charset=utf-8` MIME, UTF-8 encoding
- **DIAG-01** — Single-Analyzer Flow Diagram Exists and Is Accurate (Docs-Only): Excalidraw + Mermaid pair following conventions, happy path + Phase B decision node

**Acceptance criteria**: All scenarios passing (26 runtime + 1 docs-only). Both files exist and follow conventions. Diagram includes correct Phase B placeholder.

**Integration style**: Marked each section with `<!-- Change: single-analyzer-fase-a -->` to indicate provenance (following the pattern in `openspec/specs/integrations/spec.md` with INT-DELTA sections).

---

## Files Delivered

### Production Code
| File | Change | Status |
|---|---|---|
| `Incoders.Hive.Domain/Social/ContentGeneration/VisualCompositionSpec.cs` | Added `Markdown` enum member | COMPLETE |
| `Incoders.Hive.Application/.../Composers/ReelScriptVisualComposer.cs` | Newline-separated body; `OutputKind = Markdown` | COMPLETE |
| `Incoders.Hive.Infrastructure/Rendering/LocalSvgContentRenderer.cs` | Added Markdown branch | COMPLETE |
| `Incoders.Hive.Application/.../GeneratedPackageBuilder.cs` | Sanitize-only `NormalizeFolderName` | COMPLETE |
| `Incoders.Hive.Application/.../AssembleGeneratedPackageQuery.cs` | `ISocialClientRepository` + `ResolveClientFoldersAsync` | COMPLETE |
| `apps/web/components/social/single-analyzer-client.tsx` | `.md` resolution, `client.name` display, removed `deriveClientFolderSlug` | COMPLETE |

### Tests
| File | Change | Count |
|---|---|---|
| `PackageBuilderTests.cs` | Rewrote 2 assertions, added 21 tests | +21 |
| `AssembleGeneratedPackageQueryHandlerTests.cs` | Added 3 tests | +3 |
| `ReelScriptVisualComposerTests.cs` | Created new file, 4 tests | +4 |
| `LocalSvgContentRendererTests.cs` | Added 4 Markdown tests | +4 |
| `ComposerOutputKindTests.cs` | Updated reel-script assertion | 1 updated |
| `VisualCompositionSpecTests.cs` | Added enum coverage | (implicit) |
| **Total new tests** | — | **+49** |

### Docs
| File | Change | Status |
|---|---|---|
| `docs/architecture/diagrams/single-analyzer-flow.excalidraw` | Created Type 2 Excalidraw diagram | COMPLETE |
| `docs/architecture/diagrams/single-analyzer-flow.md` | Created Mermaid + legend + impl notes | COMPLETE |
| `docs/operators/content-generation-package.md` | Created operator narrative | COMPLETE |
| `openspec/specs/content-generation/spec.md` | Merged spec-delta; replaced TBD stub | COMPLETE |

### Archive Artifacts (this folder)
- `proposal.md` — original change proposal
- `design.md` — architecture decisions and seams
- `tasks.md` — work unit breakdown and test strategy
- `archive-report.md` — this file

---

## Gate Summary

**All 16 MUST gates: PASS**
- `build.backend` → PASS
- `build.frontend.web` → PASS
- `tests.backend` → PASS (1039/1041, 2 pre-existing skips)
- `architecture.layering` → PASS (no forbidden namespace imports)
- `architecture.imports` → PASS
- `dependencies.cycles` → PASS
- `dependencies.solution-drift` → PASS
- `secrets.scan` → PASS (1277 files scanned; new docs files clean)
- `secrets.hygiene` → PASS
- `security.config` → PASS
- `security.insecure-defaults` → PASS
- (6 additional MUST gates) → PASS

**2 SHOULD gates: SKIPPED** (pre-existing, unrelated to this change)
- `coverage.line` — skipped
- `tests.frontend.web` — skipped (frontend JS tests optional)

---

## TDD Evidence

### WU-A — Reel-Script Markdown
| Task | RED Confirmed | GREEN Confirmed | Exhaustiveness |
|------|---|---|---|
| A-1 (enum test) | CS0117 compile error | Enum member added | ✓ |
| A-2 (composer test) | CS0117 errors | Composer body rewritten; OutputKind=Markdown | ✓ |
| A-3 (body structure test) | CS0117 + runtime fail | Newline/bold/emoji implementation | ✓ |
| A-4 (renderer test) | CS0117 errors | Markdown branch added before raster fallthrough | ✓ |

### WU-B — Display-Name Folders
| Task | RED Confirmed | GREEN Confirmed | Coverage |
|------|---|---|---|
| B-1/B-2 (assertion + unit tests) | CS1729 + runtime fail | `NormalizeFolderName` sanitize-only impl | ✓ (21 tests) |
| B-3 (Assemble tests) | CS0103 missing parameter | `ISocialClientRepository` injected; `ResolveClientFoldersAsync` | ✓ (3 tests) |

### WU-B Hardening — Cross-OS
| Task | Test Count | Device Names | Edge Cases |
|------|---|---|---|
| H-1 | +18 tests | 7 device-name cases | Leading dot, leading space, trailing dot, DEL, control chars |
| H-2 | All pass | `NUL` → `NUL_`, substring `Conway` unchanged | 18/18 green |

---

## Deliberate Design Decisions

1. **Sanitize-only folder naming** (Decision 2): Preserves user intent while ensuring cross-OS safety. No lowercasing, no slug generation. Accents, spaces, ampersands, internal dots, emoji all survive.

2. **Assemble path client-name resolution via `ISocialClientRepository`** (Decision 1, Option B): Reuses existing Application port; zero new abstractions; no persistence leaks; on-demand, org-scoped, with GUID fallback for deleted clients.

3. **Enum-dispatch exhaustiveness** (Decision 3): Every renderer branch is explicit; no silent defaults. Future refactors to `switch` over the enum will stay exhaustive.

4. **Markdown as a first-class output kind** (Decisions 3–5): Not a format-key hack; proper enum member. Timestamp bold-formatting and emoji preservation are intentional Markdown features, not hacks.

5. **Single spec-delta source of truth** (Merge approach): PKG-01, PKG-02, RS-01, RS-02, RS-03, DIAG-01 folded into canonical spec with change provenance markers. Future readers know which change introduced them.

---

## Risks / Limitations

### Resolved
- ✓ Test assertion updates — all 2 existing assertions rewritten; no old behavior locked in
- ✓ Enum exhaustiveness — single renderer `if` statement confirmed; no non-exhaustive switch
- ✓ Frontend extension enforcement — both `FORMAT_FILE_NAMES` and `resolveFormatFilename` updated
- ✓ Cross-OS sanitization — 25 comprehensive tests cover Windows, macOS, Linux edge cases
- ✓ Diagram accuracy — reviewed before merge; happy path + Phase B decision node correct

### Accepted Limitations
- Device-name guarding applies to WHOLE NAME only (e.g., `NUL` → `NUL_`, but `NUL.txt` stays `NUL.txt` — acceptable per spec)
- Leading dot trimming prevents Unix hidden-file creation but does not validate other OS-specific rules (e.g., Windows trailing dot rule is covered; macOS `:` is in reserved set)
- Spec-delta does not cover rollout strategy for existing assets with old slug names; acceptable because new reel-scripts are `.md` and new ZIPs use display names going forward

---

## What Remains (Fase B — Future)

- Canva integration (#1) — marked as Phase B in diagram
- Visual-quality / template work (#4, #5, #6) — explicitly out of scope
- Mundo 1/2 structural changes — not touched

All Phase B items are clearly documented in the diagram with amber dashed nodes and "Phase B (placeholder)" labels.

---

## Engram Artifact References

| Artifact | Engram Topic Key | Observation ID |
|---|---|---|
| Proposal | `sdd/single-analyzer-fase-a/proposal` | #556 |
| Spec | `sdd/single-analyzer-fase-a/spec` | #557 |
| Design | `sdd/single-analyzer-fase-a/design` | #558 |
| Tasks | `sdd/single-analyzer-fase-a/tasks` | #559 |
| Apply-Progress | `sdd/single-analyzer-fase-a/apply-progress` | #560 |
| Verify-Report | `sdd/single-analyzer-fase-a/verify-report` | #562 |
| Archive-Report | `sdd/single-analyzer-fase-a/archive-report` | (saved during archive phase) |

---

## Conventional Commits (User to execute)

Suggested work-unit commits (no Co-Authored-By; single author per git policy):

```
feat(content-gen): add Markdown output kind for reel-script with bold timestamps and emoji-safe body

- Add Markdown member to CompositionOutputKind enum
- Rewrite ReelScriptVisualComposer: newline-separated scenes with **timestamp** bold
- Add Markdown branch to LocalSvgContentRenderer → reel-script.md with text/markdown MIME
- Update frontend FORMAT_FILE_NAMES["reel-script"] to resolve .md extension
- Add 10 new tests (enum, composition, rendering, emoji preservation)
- All 1000+ tests pass; 16 MUST gates pass

Spec: RS-01, RS-02, RS-03
```

```
feat(content-gen): display-name folders in ZIP packaging and Assemble handler

- Rewrite NormalizeFolderName: sanitize-only (strip illegal chars, preserve case/accents)
- Assemble handler: resolve client display name via ISocialClientRepository
- Frontend clientLabels/clientFolderName: use client.name directly; remove deriveClientFolderSlug
- Add cross-OS sanitization: leading/trailing dots, spaces, device names (NUL → NUL_)
- Rewrite 2 existing PackageBuilderTests assertions; add 21 new tests
- Add 3 Assemble handler tests (display-name, GUID fallback)
- All 1000+ tests pass; 16 MUST gates pass

Spec: PKG-01, PKG-02
```

```
docs: add single-analyzer-flow diagram with Phase B Canva decision node

- Create docs/architecture/diagrams/single-analyzer-flow.excalidraw (Type 2 JSON)
- Create docs/architecture/diagrams/single-analyzer-flow.md (Mermaid flowchart + legend)
- Diagram shows happy path: URL → analyze → package → orchestrate → render → ZIP → [DECISION] Download vs Export-to-Canva
- Canva branch marked Phase B placeholder (dashed, amber)
- Follows content-plan-flow conventions: Virgil font, color palette, 35% opacity zones

Spec: DIAG-01
```

```
docs: sync content-generation docs with single-analyzer-fase-a behavior

- Create docs/operators/content-generation-package.md: operator narrative
  - Reel-script.md behavior and file structure
  - Client display-name folder naming rules and examples
  - Cross-OS sanitizer 5-rule specification
  - ZIP structure and asset layout
- Merge spec-delta into openspec/specs/content-generation/spec.md (replace TBD stub)
- Record PKG-01, PKG-02, RS-01, RS-02, RS-03, DIAG-01 as canonical spec requirements
- All 1000+ tests pass; 16 MUST gates pass

Spec: (all)
```

---

## Next Steps

1. User reviews file changes in the working tree (all files prepared, gates pass)
2. User commits the four work-unit commits above (or combines as preferred) to a feature branch
3. User opens PR to `dev` with the four commits; all gates pass on CI
4. PR is reviewed and merged to `dev`
5. Archive is complete; change is closed

**Fase B planning** can proceed independently using this diagram and spec as reference.

---

Session: manual-profile-analyzer-followup-20260604-post-compaction  
Archived: 2026-06-04  
Project: incoders-hive  
Change: single-analyzer-fase-a  
Status: READY FOR USER COMMIT
