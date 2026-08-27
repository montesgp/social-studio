# Exploration: comfyui-detailer-preflight

Change name: `comfyui-detailer-preflight`

Follow-up to `client-aesthetic-generation-hardening` (2026-08-25), whose WU-5 delivered an
investigation-only feasibility report (`detailer-feasibility-report.md`, archived) on adding a
face/hand-restoration node to the ComfyUI workflow to fix deformed anatomy (duplicated
faces/hands/limbs) — a defect still observed in real generations this session. The owner now wants
this implemented, explicitly for production-environment readiness (the local dev ComfyUI backend
almost certainly lacks the required custom-node package and model weights today; production may
have them).

## Current State (from the prior feasibility report — verified findings, not re-derived)

### (a) Node family needed
`FaceDetailer` (compound node: detects faces/hands via `UltralyticsDetectorProvider`, crops,
re-samples at higher denoise through a second KSampler pass, composites back). This is the
ComfyUI-native equivalent of Automatic1111's "ADetailer" extension — there is no ComfyUI node
literally named "ADetailer."

### (b) Upstream dependency
Ships in `ComfyUI-Impact-Pack` (+ `ComfyUI-Impact-Subpack` for `UltralyticsDetectorProvider`'s
detection loading). **Also requires separately-downloaded detection-model weights** (e.g.
`bbox/face_yolov8m.pt`, `segm/hand_yolov8s-seg.pt`) placed under the backend's `models/` tree — not
bundled with the node package itself. Two independent installation dependencies, either of which
missing breaks the node.

### (c) This repo can already detect installed custom nodes
`ComfyUiModelCatalogClient.ListModelsAsync` (`Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiModelCatalogClient.cs`)
already calls `GET {baseUrl}/object_info` and parses per-node `class_type` presence
(`AppendEnumModels` pattern for `CheckpointLoaderSimple`/`IPAdapterModelLoader`/`LoraLoader`).
`/object_info` returns every registered node class — a presence probe for `FaceDetailer` +
`UltralyticsDetectorProvider` is the same HTTP surface, not a new capability class.
`ComfyUiImageModelClient` (the actual generation client) does NOT currently call `/object_info` —
only the catalog client does — so wiring a preflight into the generation path is new code.

### (d) Failure mode if referenced but absent — critical constraint
The workflow graph is ONE flat JSON document submitted whole (`POST /prompt`,
`QueuePromptAsync`). If the template references `FaceDetailer` on a backend without Impact-Pack (or
the model weights), ComfyUI rejects the ENTIRE prompt — `QueuePromptAsync` already maps both a
non-2xx response and a 2xx-with-populated-`node_errors` response to
`IntegrationErrors.ExternalProviderUnavailable`. **There is no partial-success path.** An
unconditional addition would turn "backend lacks Impact-Pack" into "background generation stops
working entirely" — strictly worse than today's occasional anatomy defects.

### (e) Graceful-degrade requires new architecture, not a template edit
`ComfyUiSettings.WorkflowTemplateJson` is a single fixed template string per credential (or the
bundled default). `SubstituteTemplate`/`BuildWorkflowGraph` only fill placeholder tokens into one
graph shape — no branching on which nodes exist. A conditional design needs: (1) a preflight
`/object_info` probe (new call site, should be cached per `baseUrl` rather than per-request — a full
round trip on every generation is not viable given ComfyUI is already the slowest pipeline stage),
(2) two template variants (with/without the detailer subgraph) or a graph-mutation step, (3) cache
invalidation if an operator's backend configuration changes between probes. None of this exists
today.

### (f) Prior recommendation
Preflight-gated, default OFF, per-organization opt-in (mirroring the existing
`ComfyUiSettings`/`WorkflowTemplateJson` per-credential override pattern), using the probe result to
select between the detailer-augmented and current template rather than assuming universal backend
support.

## Affected Areas

- `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` — the generation client;
  needs the new preflight probe call site and template-selection logic.
- `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiModelCatalogClient.cs` — existing
  `/object_info` probe pattern to mirror/reuse, not duplicate.
- `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiSettings.cs` — per-credential settings; needs
  the new opt-in flag and (likely) a second template string/path for the detailer-augmented graph.
- A new `DefaultComfyUiWorkflowTemplateWithDetailer.json` (or equivalent) sibling to
  `DefaultComfyUiWorkflowTemplate.json` — the augmented graph with `FaceDetailer` +
  `UltralyticsDetectorProvider` nodes spliced in.
- Caching for the probe result — needs a decision on mechanism (in-memory per-process, with what
  TTL/invalidation trigger; this repo's existing caching patterns should be checked first, e.g.
  `IMemoryCache` usage elsewhere in `Infrastructure/Ai/Providers/`).

## Open Questions for Proposal

1. **Per-org opt-in vs. always-probe-and-select automatically.** The prior report recommended an
   explicit opt-in flag as an extra safety valve on top of the probe. An alternative: always probe
   (cached) and automatically select the augmented template when the backend supports it, no
   separate settings flag needed — simpler config surface, but removes an operator's ability to
   force the safe template even when the backend nominally advertises the nodes (e.g. if the model
   weights are missing despite the node classes being registered — the probe only sees registered
   node CLASSES, not whether their required model files are present on disk, so a "probe says yes"
   could still fail at generation time per finding (b)/(d)). This needs an explicit owner decision —
   the failure mode in (d) means a wrong default here is costly.
2. **Probe cache TTL and invalidation.** How long to trust a cached "backend supports detailer"
   result before re-probing — an operator could add/remove the custom-node package or download the
   model weights between probes.
3. **What happens on a probe failure/timeout itself** (not "nodes absent" but "couldn't reach
   `/object_info` at all") — degrade to the current template (safe) or treat as blocking?

## Risks

- The failure mode in (d) is the central risk of this whole change: any bug in the probe/selection
  logic that causes the augmented template to be selected when the backend can't actually run it
  breaks 100% of image generation for that job, not just anatomy quality. This needs the most
  conservative possible fallback-to-safe-template default on ANY uncertainty (probe failure, probe
  timeout, partial node presence, ambiguous response shape).
- Detection-model weights are invisible to a `/object_info` node-class probe (per finding (b)) — a
  probe that only checks `FaceDetailer`/`UltralyticsDetectorProvider` class registration could still
  false-positive if the classes are registered but the model files aren't present, hitting the
  exact all-or-nothing failure in (d) anyway. The proposal should consider whether this residual risk
  is acceptable (with the safe fallback catching the resulting `node_errors` failure and retrying
  with the plain template) or whether a deeper capability check is warranted.
- This change cannot be validated end-to-end in the current local dev environment (no Impact-Pack
  install, no model weights) — testing will necessarily be limited to the probe-negative path
  (confirm the safe template is selected/used when nodes are absent) plus unit-level mocked-probe
  coverage for the positive path. Real production validation is deferred to actual production
  deployment.

## Ready for Proposal

Yes. The central proposal-phase decision is item 1 above (opt-in flag vs. automatic-probe-only) and
how conservatively to handle the residual "registered but unusable" risk from missing model weights.
