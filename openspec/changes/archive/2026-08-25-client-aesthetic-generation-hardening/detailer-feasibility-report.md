# Detailer (Face/Hand Restoration) Feasibility Report

<!-- Change: client-aesthetic-generation-hardening -->
<!-- WU-5 — investigative deliverable, documentation only, no code or workflow-graph change -->

## Status

Investigative — final. This report satisfies design decision D7 and WU-5's task 5.1. **No
workflow-graph or production-code change ships as part of this change.**

## Question

Should `DefaultComfyUiWorkflowTemplate.json` (the fixed API-format graph
`ComfyUiImageModelClient` submits to `POST /prompt`) gain a face/hand-restoration
("detailer") node, to correct the anatomy defects (malformed hands, distorted faces) the
IP-Adapter pipeline occasionally produces?

## (a) Node family that would be needed

Two ecosystems solve this; only one is native to ComfyUI's JSON-graph API:

- **`FaceDetailer` + `UltralyticsDetectorProvider`** (ComfyUI-native nodes). `FaceDetailer`
  is a compound node: it runs a YOLO-family detector (via `UltralyticsDetectorProvider` or
  `SAMLoader`/`BBOX Detector`) to locate faces/hands, crops each detection, re-samples the
  crop through a second, smaller KSampler pass at higher denoise, and composites the result
  back into the full image. It is the direct ComfyUI-graph equivalent of Automatic1111's
  "ADetailer" behavior.
- **`ADetailer`** is an Automatic1111/Forge WebUI **extension**, not a ComfyUI node. It has
  no native ComfyUI node — it is referenced here only because it is the term most operators
  search for; the ComfyUI-side implementation is `FaceDetailer`, not a port of ADetailer.

**Conclusion**: the node type needed is `FaceDetailer`, backed by an
`UltralyticsDetectorProvider` (or `BboxDetectorLoader`/`SAMLoader`) detection stage feeding
it, plus a second KSampler pass the `FaceDetailer` compound node drives internally.

## (b) Upstream custom-node package and model downloads

`FaceDetailer` and `UltralyticsDetectorProvider` ship in **ComfyUI-Impact-Pack**
(`ltdrdata/ComfyUI-Impact-Pack`), the most widely installed ComfyUI custom-node pack for
this purpose. Detection-model loading for the Ultralytics-based nodes is split into a
companion package, **ComfyUI-Impact-Subpack** (`ltdrdata/ComfyUI-Impact-Subpack`), which the
main pack depends on for `UltralyticsDetectorProvider`.

Beyond installing the custom-node packages, the workflow additionally requires **separately
downloaded detection-model weights** placed under the ComfyUI `models/` tree (e.g. a
face-bbox YOLO model such as `bbox/face_yolov8m.pt` and, for hands, a
person/hand-segmentation model such as `segm/hand_yolov8s-seg.pt` or a hand-specific bbox
model), which are **not** part of a base ComfyUI or Impact-Pack install and must be fetched
and placed manually or via ComfyUI-Manager. This is a second installation dependency beyond
the node package itself.

## (c) Can this repo detect a target ComfyUI instance's installed custom nodes?

**Yes — via `GET /object_info`, and this repo already exercises exactly that endpoint.**

`ComfyUiModelCatalogClient.ListModelsAsync` (`Incoders.Hive.Infrastructure/Ai/Providers/
ComfyUiModelCatalogClient.cs`) already calls `GET {baseUrl}/object_info` and parses the
response for specific node `class_type` keys (`CheckpointLoaderSimple`,
`IPAdapterModelLoader`, `LoraLoader`) via `AppendEnumModels`, reading each node's declared
`input.required.<field>` enum. `/object_info` returns **every node class the backend has
registered**, keyed by class name — so a presence probe for `FaceDetailer` and
`UltralyticsDetectorProvider` is the same shape of call already in production: a
`root.TryGetProperty("FaceDetailer", out _)` check needs no new HTTP surface, only a new
probe function alongside the existing `AppendEnumModels` pattern. `ComfyUiImageModelClient`
itself does not currently call `/object_info` — only the catalog client does — so wiring a
preflight into the generation path would be new code, not a new capability.

## (d) Failure mode if the node is absent and referenced anyway

The workflow graph is one flat JSON document submitted whole via `POST /prompt`
(`QueuePromptAsync`). If `DefaultComfyUiWorkflowTemplate.json` referenced a `FaceDetailer`
node on a backend where Impact-Pack (or the detection-model weights) is not installed,
ComfyUI rejects the entire prompt: either the HTTP response is non-2xx, or a 2xx response
carries a populated `node_errors` object naming the unresolvable class/input.
`QueuePromptAsync` already maps **both** of those shapes to
`IntegrationErrors.ExternalProviderUnavailable` and fails the call outright — there is no
partial-success path. A missing detailer node does not degrade the image quietly; it fails
the **entire generation**, including the background image that today ships fine without it.

## (e) Is a graceful-degrade graph (probe → conditional node injection) viable?

In principle yes, but it does not fit the current template mechanism without new
architecture:

- `ComfyUiSettings.WorkflowTemplateJson` is a **single fixed template string** per
  credential (or the bundled default). There is no per-request template selection today —
  `SubstituteTemplate`/`BuildWorkflowGraph` only fill placeholder tokens into one graph
  shape; they do not branch which nodes exist.
- A graceful-degrade design would need: (1) a preflight `GET /object_info` probe (new call
  site, ideally cached per `baseUrl` rather than per-request, since it adds a full round
  trip to every generation), (2) two template variants — with and without the detailer
  subgraph — or a graph-mutation step that splices detailer nodes in/out of the parsed JSON
  before submission, and (3) cache invalidation/staleness handling if an operator's backend
  configuration changes between probes.
- None of this exists today. It is a real, buildable feature, but it is new surface area —
  not a drop-in template edit — and the org-supplied `WorkflowTemplateJson` override path
  (`ComfyUiSettings.FromCredential`) would need the same conditional logic to stay correct
  for self-hosted templates.

## (f) Recommendation

**Recommend-with-preflight — do not ship a `FaceDetailer` node into
`DefaultComfyUiWorkflowTemplate.json` unconditionally.**

Rationale:
- The single-flat-graph failure mode (d) means an unconditional addition turns "this
  backend doesn't have Impact-Pack installed" into "background generation for this client
  stops working entirely," which is strictly worse than today's occasional anatomy defects
  — anatomy is already partially mitigated by WU-4's negative-prompt terms in this same
  change, at zero backend-dependency cost.
- The detection-model weights (b) are a second, easy-to-miss installation dependency beyond
  the node package itself; a backend can have Impact-Pack installed and still lack the bbox
  model the template names, hitting the same `node_errors` failure.
- This repo already has the exact mechanism needed to de-risk a future rollout — the
  `/object_info` probe pattern in `ComfyUiModelCatalogClient` (c) — so a preflight-gated
  rollout is low-effort relative to building probing from scratch, but it is still new work
  (a probe call site, caching, and template branching) that is out of scope for this change.
- If/when this ships, it should default OFF, require an explicit
  `ComfyUiSettings`-level opt-in per organization (mirroring how `WorkflowTemplateJson` is
  already an org-level override), and use the probe result to select between the
  detailer-augmented and current template rather than assuming universal availability.

## Source

This report answers design decision D7 in `openspec/changes/
client-aesthetic-generation-hardening/design.md` and delivers WU-5 task 5.1 in the same
change's `tasks.md`. Settled decision: investigation only, no workflow-graph change in this
change.
