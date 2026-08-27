# Design: ComfyUI Detailer Preflight

## Technical Approach

A new Infrastructure-only type, `ComfyUiDetailerProbe`, answers one question — does this base URL register
**both** `FaceDetailer` and `UltralyticsDetectorProvider`? — via the same `GET {baseUrl}/object_info` call
`ComfyUiModelCatalogClient.ListModelsAsync` already makes. `ComfyUiImageModelClient.GenerateAsync` consults it
once, before `BuildWorkflowGraph`, to pick between the bundled plain template and a new
`DefaultComfyUiWorkflowTemplateWithDetailer.json`. Everything else — `SubstituteTemplate`,
`BuildWorkflowGraph`, `PollHistoryAsync`, the `CAC-IMG-03` retry contract — is untouched.

## Architecture Decisions

| # | Decision | Choice | Rejected | Rationale |
|---|---|---|---|---|
| A1 | Probe result shape | `enum ComfyUiDetailerSupport { Unknown = 0, Supported, NotSupported }`; `Unknown` is the zero value | `bool` / `bool?` | The augmented template is selected on `Supported` only. `Unknown` as default(T) means a forgotten branch falls to plain, per proposal §Approach. |
| A2 | Presence rule | Both class keys must be top-level properties of the `/object_info` object | `FaceDetailer` alone | Finding (b): `UltralyticsDetectorProvider` ships in the Subpack; without it `FaceDetailer` has no `bbox_detector`. Partial presence ⇒ `NotSupported`. |
| A3 | Cache | `private static readonly ConcurrentDictionary<string, CacheEntry> Cache`, key = `settings.BaseUrl.TrimEnd('/')`, `record CacheEntry(ComfyUiDetailerSupport Support, DateTimeOffset ResolvedAt)`, TTL 10 min (D2) | `IMemoryCache` | Client is per-request via `ExternalProviderClientFactory:61`, so instance fields cache nothing. Mirrors `AiCompletionGateway.cs:190` `SchemaCache`; no new DI dependency in `Ai/Providers/`. Key is `BaseUrl` because node registration is a property of the backend process, not the credential. |
| A4 | Transport failures | Not written to the cache at all (D3) | Cache as negative | A 30 s blip must not pin a capable backend to plain for the full TTL. |
| A5 | Invalidation trigger | Only when the augmented graph was submitted **and** the `/prompt` rejection is detailer-attributable | Evict on any `ExternalProviderUnavailable` | Unrelated ComfyUI outages must not thrash the cache. |
| A6 | Splice point | `KSampler → VAEDecode → FaceDetailer → SaveImage` | Splice before `VAEDecode` | `FaceDetailer`'s primary input is `IMAGE`, not `LATENT` — it takes a `vae` input and runs its own encode/decode internally. **Live-verify (see Testing d).** |
| A7 | Opt-out (D1) | `ComfyUiSettings.DetailerEnabled` (bool, credential key `detailer_enabled`, default `true`). `false` ⇒ plain template, probe never called | Opt-in default-off | Config-only kill switch for the undetectable weights-missing case; can only force plain, never force augmented. |

## Data Flow

    GenerateAsync
      │ settings.DetailerEnabled == false ──────────────► plain template
      ▼ (true)
    ComfyUiDetailerProbe.ProbeAsync(http, baseUrl, ct)
      │  cache hit & !expired ──► cached tri-state
      │  else GET /object_info ──► parse ──► store (unless transport failure)
      ▼
    Supported ──► augmented template     Unknown/NotSupported ──► plain template
      ▼
    BuildWorkflowGraph (unchanged) ──► QueuePromptAsync ──► rejected?
                                             │ yes & augmented & detailer-attributable
                                             ▼
                                    ComfyUiDetailerProbe.Invalidate(baseUrl)

## File Changes

| File | Action | Description |
|---|---|---|
| `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiDetailerProbe.cs` | Create | `internal static` — enum, `ProbeAsync`, `Invalidate`, `IsDetailerAttributable`, `ResetCacheForTests`. |
| `Incoders.Hive.Infrastructure/Ai/Providers/DefaultComfyUiWorkflowTemplateWithDetailer.json` | Create | Plain graph + nodes `"10"`/`"11"`; `"9"` `SaveImage.images` ← `["11", 0]`. |
| `Incoders.Hive.Infrastructure/Incoders.Hive.Infrastructure.csproj` | Modify | `<EmbeddedResource Include="Ai\Providers\DefaultComfyUiWorkflowTemplateWithDetailer.json" />` beside line 89. |
| `.../ComfyUiImageModelClient.cs` | Modify | Second resource name const; template selection at L178; `QueuePromptAsync` returns a rejection payload; invalidation call. |
| `.../ComfyUiSettings.cs` | Modify | `bool DetailerEnabled` record member + `Read(credential, "detailer_enabled")` parse (D1/WU-5). |
| `openspec/changes/.../specs/comfyui-image-provider/spec.md`, `docs/architecture/adr/` | Create | Spec delta + the one required ADR. |

## Interfaces / Contracts

```csharp
internal enum ComfyUiDetailerSupport { Unknown = 0, Supported, NotSupported }

internal static class ComfyUiDetailerProbe
{
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    internal const string FaceDetailerClass = "FaceDetailer";
    internal const string DetectorProviderClass = "UltralyticsDetectorProvider";
    internal static readonly IReadOnlySet<string> DetailerNodeIds = new HashSet<string> { "10", "11" };

    // Never throws. Returns Unknown on transport failure/timeout (uncached).
    internal static Task<ComfyUiDetailerSupport> ProbeAsync(HttpClient http, string baseUrl, CancellationToken ct);
    internal static void Invalidate(string baseUrl);
    // True when node_errors keys intersect DetailerNodeIds, or the payload names either class.
    internal static bool IsDetailerAttributable(string? promptRejectionPayload);
}
```

Augmented-template node fragment (conservative parameters, D-scope non-goal to tune):

```json
"10": { "class_type": "UltralyticsDetectorProvider",
        "inputs": { "model_name": "bbox/face_yolov8m.pt" } },
"11": { "class_type": "FaceDetailer",
        "inputs": { "image": ["vae", 0], "model": ["6", 0], "clip": ["1", 1], "vae": ["1", 2],
                    "positive": ["2", 0], "negative": ["3", 0], "bbox_detector": ["10", 0],
                    "seed": {{SEED}}, "steps": 20, "cfg": 7.0, "denoise": 0.4,
                    "sampler_name": "euler", "scheduler": "normal",
                    "guide_size": 512, "guide_size_for": true, "max_size": 1024,
                    "feather": 5, "noise_mask": true, "force_inpaint": true,
                    "bbox_threshold": 0.5, "bbox_dilation": 10, "bbox_crop_factor": 3.0,
                    "sam_detection_hint": "center-1", "sam_dilation": 0, "sam_threshold": 0.93,
                    "sam_bbox_expansion": 0, "sam_mask_hint_threshold": 0.7,
                    "sam_mask_hint_use_negative": "False", "drop_size": 10,
                    "wildcard": "", "cycle": 1 } }
```

**Token-contract invariants.** The augmented file uses the *same* placeholder set as the plain one
(`CHECKPOINT_NAME`, `POSITIVE_PROMPT`, `NEGATIVE_PROMPT`, `REFERENCE_IMAGE`, `WIDTH`, `HEIGHT`, `SEED`,
`STEPS`, `CFG`), so `SubstituteTemplate` is unchanged and credential overrides for checkpoint/steps/CFG behave
identically. `FaceDetailer.steps`/`cfg` are **literals**, not tokens — the detailer pass is a fixed conservative
repair, not the primary sampler, and adding tokens would break the identity test. Critically,
`FaceDetailer.model` is wired to `["6", 0]` so the no-reference bypass — a plain `Replace` of
`ConditionedModelEdge` with `PassThroughModelEdge` (L785) — rewrites **both** edges; a different wiring would
leave a dangling IP-Adapter reference in the no-reference path.

## Testing Strategy

| Layer | What | Approach |
|---|---|---|
| Unit (a) | Tri-state probe | Mocked `/object_info`: both classes, neither, each alone, 500, timeout, malformed JSON, empty body, non-object root. |
| Unit (b) | Cache | Second call within TTL issues no HTTP; expiry re-probes; transport failure leaves cache empty; `Invalidate` drops a `Supported`; `IsDetailerAttributable` true for `node_errors` on `"10"`/`"11"` and class-name payloads, false for a VRAM/`ckpt_name` error. |
| Unit (c) | Selection | One assertion per row of the proposal's selection table; `DetailerEnabled=false` short-circuits with zero `/object_info` calls; token-set identity between the two templates; byte-identical graph vs. pre-change on `NotSupported`. |
| Integration | Post-failure regression | Job 1 augmented → detailer `node_errors`; job 2 same base URL submits plain. |
| **E2E (d) — owner-run, manual** | **Now newly possible**: Impact-Pack + Subpack + weights are installed on the local dev ComfyUI. | Real generation against the local backend, confirming (i) the probe answers `Supported`, (ii) `POST /prompt` is accepted (proves A6's splice point and the full `FaceDetailer` input contract are well-formed — no unit test can), (iii) the output image shows repaired faces/hands versus the same seed on the plain template. Not automatable in this suite; same category as the alignment-guides manual check. Record accept/reject in writing. |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration
boundary. The one new outbound surface is an HTTP GET to an already-configured provider base URL, on the same
`IHttpClientFactory` path as the existing catalog client.

## Migration / Rollout

No migration. No new dependency, credential type or provider category. Per-work-unit revert per proposal
§Rollback Plan; `DetailerEnabled=false` is a config-only kill switch requiring no deploy.

## Open Questions

- [ ] A6 splice point and the exact `FaceDetailer` required-input list are asserted from the node's published
      contract; the owner's now-installed local backend can confirm both in one real submission. A rejection
      there is a template fix, not a design change.
