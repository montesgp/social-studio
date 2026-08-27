# Tasks: ComfyUI Detailer Preflight

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1,880 (D1 default: opt-out flag ships) → ~2,160 across 6 units |
| 800-line budget risk | Low (largest unit ~520, headroom ~280) |
| Chained PRs recommended | No |
| Suggested split | Single PR per work unit, sequential commits to `dev` |
| Delivery strategy | single-pr |
| Chain strategy | pending (no chaining needed; single-pr per commit) |

Decision needed before apply: Yes
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

Note: proposal's own budget is 800 lines/unit (not the skill's default 400); all units are Low risk either way.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | `ComfyUiDetailerProbe`: tri-state result + cache (A1-A4) | Commit 1 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~ComfyUiDetailerProbe` | N/A — pure unit, mocked HTTP, no live backend needed | Delete `ComfyUiDetailerProbe.cs` + its test file; nothing references it yet |
| 2 | Detailer-augmented workflow template JSON (A6) | Commit 2 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~ComfyUiDetailerTemplate` | N/A — file + token-identity test only, no adapter wiring yet | Remove new `.json` + csproj `<EmbeddedResource>` entry |
| 3 | Template selection + fallback invariant in `GenerateAsync` (A1,A6) | Commit 3 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~ComfyUiImageModelClientTests\|ComfyUiDetailerSelection` | Owner-run manual E2E (task 7.1) once WU-5/6 land | Revert `GenerateAsync` template-selection diff; probe/template stay inert |
| 4 | Cache invalidation on detailer-attributable `node_errors` (A5) | Commit 4 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~ComfyUiDetailerInvalidation` | N/A — mocked rejection payload | Revert `QueuePromptAsync` invalidation call; TTL-only expiry resumes |
| 5 | `ComfyUiSettings.DetailerEnabled` opt-out flag (A7, D1) | Commit 5 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~ComfyUiSettingsTests` | N/A — settings parse only | Drop the field/parse line; probe still gates selection, so removal cannot enable augmented on an unsupporting backend |
| 6 | Spec/ADR/doc sync | Commit 6 | N/A — docs only | N/A | Revert doc files |
| 7 | Owner-run manual E2E verification | N/A (manual) | N/A | Real generation against local ComfyUI with Impact-Pack/Subpack/weights installed | N/A — no code change |

## Phase 1: Foundation — Probe & Cache (A1-A4)

- [x] 1.1 RED: `Incoders.Hive.Tests/Infrastructure/Ai/ComfyUiDetailerProbeTests.cs` — mocked `/object_info`: both classes present → `Supported`; neither present → `NotSupported`; each class alone → `NotSupported` (CUI-07-a, CUI-07-b)
- [x] 1.2 RED: same file — non-2xx, timeout, transport exception, malformed JSON, empty body, non-object root → `Unknown`, no exception thrown (CUI-07-c)
- [x] 1.3 RED: same file — cache hit within TTL issues no second `/object_info` call; expiry after 10 min re-probes; transport failure leaves cache empty (`Unknown` never cached) (CUI-09-a)
- [x] 1.4 RED: same file — `Invalidate(baseUrl)` drops a cached `Supported`; parsed `NotSupported` is cached and rides out TTL unaffected by non-TTL signals (CUI-09-b, CUI-09-c)
- [x] 1.5 RED: same file — `IsDetailerAttributable` true for `node_errors` keyed on `"10"`/`"11"` or naming `FaceDetailer`/`UltralyticsDetectorProvider`; false for an unrelated error (e.g. `ckpt_name`/VRAM)
- [x] 1.6 GREEN: create `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiDetailerProbe.cs` — `internal enum ComfyUiDetailerSupport { Unknown = 0, Supported, NotSupported }`; `internal static class ComfyUiDetailerProbe` with `CacheTtl`, `FaceDetailerClass`, `DetectorProviderClass`, `DetailerNodeIds`, `ProbeAsync`, `Invalidate`, `IsDetailerAttributable`, `ResetCacheForTests` (A1, A2, A3, A4)
- [x] 1.7 GREEN: implement `ProbeAsync` reusing `ComfyUiModelCatalogClient`'s `class_type`-presence parsing pattern (`AppendEnumModels`-style traversal) against `GET {baseUrl}/object_info`; static `ConcurrentDictionary<string, CacheEntry>` keyed by `BaseUrl.TrimEnd('/')`, `record CacheEntry(ComfyUiDetailerSupport Support, DateTimeOffset ResolvedAt)` (A3, precedent `AiCompletionGateway.cs:190`)
- [x] 1.8 REFACTOR: confirm no throw paths escape `ProbeAsync`; confirm `Unknown` results are never written to `Cache`

## Phase 2: Detailer-Augmented Template (A6)

- [x] 2.1 RED: `Incoders.Hive.Tests/Infrastructure/Ai/ComfyUiDetailerTemplateTests.cs` — asserts the placeholder-token set of the new template (`CHECKPOINT_NAME`, `POSITIVE_PROMPT`, `NEGATIVE_PROMPT`, `REFERENCE_IMAGE`, `WIDTH`, `HEIGHT`, `SEED`, `STEPS`, `CFG`) is identical to `DefaultComfyUiWorkflowTemplate.json`'s
- [x] 2.2 RED: same file — asserts node `"9"` (`SaveImage`) input `images` resolves to `["11", 0]` in the augmented graph, and nodes `"10"`/`"11"` exist with `class_type` `UltralyticsDetectorProvider`/`FaceDetailer`
- [x] 2.3 GREEN: create `Incoders.Hive.Infrastructure/Ai/Providers/DefaultComfyUiWorkflowTemplateWithDetailer.json` — plain graph plus splice `KSampler → VAEDecode → FaceDetailer(11) → SaveImage(9)`, node `"10"` = `UltralyticsDetectorProvider`; `FaceDetailer.model` wired to `["6", 0]`; `steps`/`cfg` on the detailer node are literals, not tokens (A6)
- [x] 2.4 GREEN: modify `Incoders.Hive.Infrastructure/Incoders.Hive.Infrastructure.csproj` — add `<EmbeddedResource Include="Ai\Providers\DefaultComfyUiWorkflowTemplateWithDetailer.json" />` beside line 89

## Phase 3: Template Selection + Fallback Invariant (A1, A6)

- [ ] 3.1 RED: `Incoders.Hive.Tests/Infrastructure/Ai/ComfyUiDetailerSelectionTests.cs` — one assertion per selection-table row: `Supported`+opt-in → augmented; `NotSupported`/`Unknown` → plain; `Supported`+opt-out → plain with zero `/object_info` calls (CUI-08-a, CUI-08-b)
- [ ] 3.2 RED: same file — with probe forced `Supported` (mocked), submitted graph contains `FaceDetailer`/`UltralyticsDetectorProvider` wired between sampler and output (CUI-08-a)
- [ ] 3.3 RED: same file — on `NotSupported`/`Unknown`, submitted graph is byte-identical to the pre-change plain-template graph (CUI-08-b, regression)
- [ ] 3.4 RED: confirm all pre-existing `comfyui-image-provider` tests still pass unmodified, including `CAGH-CUI-01` (`BackgroundOnly`) and `CAGH-CUI-02` (anatomy negative prompt)
- [ ] 3.5 GREEN: modify `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` `GenerateAsync` (~L156/L742) — consult `ComfyUiDetailerProbe.ProbeAsync` before `BuildWorkflowGraph`; select augmented resource name only on `Supported` and `DetailerEnabled != false`; select plain otherwise (A1, A6)
- [ ] 3.6 REFACTOR: verify every non-`Supported` branch — including any unwritten/default branch — resolves to plain, per the proposal's asymmetric-default invariant

## Phase 4: Cache Invalidation on Detailer-Attributable Failure (A5)

- [ ] 4.1 RED: `Incoders.Hive.Tests/Infrastructure/Ai/ComfyUiDetailerInvalidationTests.cs` — job 1 submits augmented template, `QueuePromptAsync` rejects with detailer-attributable `node_errors`; job 2 against the same base URL submits plain without waiting for TTL (CUI-09-b)
- [ ] 4.2 RED: same file — a non-detailer `ExternalProviderUnavailable` failure does NOT invalidate the cached `Supported` entry (regression, A5)
- [ ] 4.3 GREEN: modify `ComfyUiImageModelClient.cs` `QueuePromptAsync` (~L522) — on rejection, call `ComfyUiDetailerProbe.IsDetailerAttributable` against the rejection payload; if true and the augmented template was submitted, call `ComfyUiDetailerProbe.Invalidate(baseUrl)` (A5)

## Phase 5: Opt-Out Flag (A7, D1 default)

- [x] 5.1 RED: `Incoders.Hive.Tests/Infrastructure/Ai/ComfyUiSettingsTests.cs` — `DetailerEnabled` defaults to `true` when `detailer_enabled` credential key is absent; parses `false` explicitly; `false` short-circuits selection with zero `/object_info` calls (CUI-03)
- [x] 5.2 GREEN: modify `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiSettings.cs` — add `bool DetailerEnabled` record member, `Read(credential, "detailer_enabled")` parse, default `true`
- [ ] 5.3 GREEN: wire `DetailerEnabled == false` short-circuit in `GenerateAsync` ahead of the probe call (Phase 3 selection logic)

## Phase 6: Spec, ADR, Doc Sync

- [ ] 6.1 Confirm `openspec/changes/comfyui-detailer-preflight/specs/comfyui-image-provider/spec-delta.md` (CUI-07/08/09 added, CUI-03 modified) is merge-ready against the landed implementation
- [ ] 6.2 Write the required ADR under `docs/architecture/adr/` — "Backend-capability-gated workflow-template selection" per proposal's ADR determination: records the probe/cache/select pattern, the conservative-default asymmetric invariant, and the accepted residual risk that node-class registration does not imply usable weights (finding (b))
- [ ] 6.3 Update `docs/` provider documentation if `comfyui-image-provider` capability docs reference the settings surface or workflow template selection (Documentation Sync, per project CLAUDE.md)
- [ ] 6.4 Run `pwsh -File scripts/gates/Run-Gates.ps1`; confirm architecture gate green (no ComfyUI/HTTP type crossing into Domain/Application, `CUI-05`)

## Phase 7: OWNER-RUN MANUAL VERIFICATION (not automatable — pending human action)

- [ ] 7.1 **MANUAL, OWNER-RUN.** Real generation against the local ComfyUI instance (Impact-Pack/Subpack/weights now installed). Confirm: (i) the probe answers `Supported` against the live `/object_info`; (ii) `POST /prompt` with the augmented graph is accepted — validating A6's splice point and the full `FaceDetailer` input contract, which no mock can verify; (iii) the output image shows visible anatomical repair versus the same seed on the plain template. Record accept/reject in writing per the proposal's `## Success Criteria` deferred block. This task stays open until the owner performs and records the run — same category as the prior change's alignment-guides manual verification.

## Key Learnings

1. The proposal's own review budget for this change is 800 changed lines per work unit, not the skill-default 400, and every unit stays well under it.
2. `ComfyUiDetailerProbe`'s cache is a static `ConcurrentDictionary` because the adapter is resolved per-request through `ExternalProviderClientFactory`, so instance fields cache nothing.
3. The augmented template reuses the plain template's exact placeholder-token set, so no change to `SubstituteTemplate` or `BuildWorkflowGraph` is required.
4. `FaceDetailer.model` must wire to the literal `["6", 0]` edge so the no-reference `PassThroughModelEdge` rewrite at L785 rewrites both sampler and detailer edges.
5. The owner-run manual E2E task is the only verification that can confirm the A6 splice point and full `FaceDetailer` input contract, since no unit test can submit to a real ComfyUI backend.
