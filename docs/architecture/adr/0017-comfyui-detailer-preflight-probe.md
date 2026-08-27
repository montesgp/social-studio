# ADR 0017 - Backend-Capability-Gated Workflow-Template Selection (ComfyUI Detailer Preflight)

## Status
Accepted

## Context

`ComfyUiImageModelClient` (ADR 0010) submits one of two possible workflow graphs to a
ComfyUI server: the bundled plain SD1.5 + IP-Adapter template, or a candidate
`FaceDetailer`-augmented template that runs a face/hand repair pass (`UltralyticsDetectorProvider`
→ `FaceDetailer`) after the primary sampler, spliced between `KSampler`/`VAEDecode` and
`SaveImage`. The augmented graph depends on two ComfyUI custom-node classes —
`UltralyticsDetectorProvider` (ships in the Impact-Subpack) and `FaceDetailer` (ships in
the Impact-Pack) — that are **not** part of a stock ComfyUI install and are not part of
`ComfyUiImageModelClient`'s already-required `ComfyUI_IPAdapter_plus` custom-node pack
(ADR 0010, `docs/infrastructure/comfyui/setup.md`).

**The all-or-nothing workflow-graph failure mode.** ComfyUI's `/prompt` endpoint validates
the *entire* submitted graph before executing any of it. If a single referenced
`class_type` is not registered on that server, the whole submission is rejected —
including the primary sampler/IP-Adapter portion that would otherwise have produced a
perfectly good background image. Unlike a degraded feature that fails independently
(e.g. a missing optional LoRA), submitting the augmented template to a backend that
lacks the Impact-Pack/Subpack does not silently skip the detailer step: it fails the
*entire* generation, which then falls through to `CUI-02`'s CSS-designer-background
degrade path — trading a perfectly generatable AI image for a flat-color fallback, solely
because of an optional quality enhancement neither requested by config nor detectable at
config time. This asymmetry — the augmented template can only ever be as good as the plain
template, never worse, unless it triggers this all-or-nothing failure — is the entire
reason this ADR's design is conservative by construction rather than "try augmented,
fall back to plain on rejection": a fallback retry would double `/prompt` round-trip
latency and cost on every miss, and would still race a resource-constrained backend
against a second submission under the same `CAC-IMG-03` retry budget (ADR 0010 OQ-2) that
governs the *whole* generation, not per-template attempts.

Because ComfyUI custom-node installation is per-server, per-operator, and never expressed
in `ComfyUiSettings` today (no "installed custom nodes" credential field exists, nor
should one — it would require the operator to manually track and update a list that
`/object_info` already answers authoritatively), the client cannot know in advance,
from settings alone, whether the target server can execute the augmented graph.

## Decision

### Tri-state capability probe, not a boolean

`ComfyUiDetailerProbe.ProbeAsync` answers the node-presence question by calling `GET
{baseUrl}/object_info` — the same endpoint and the same `class_type`-presence parsing
`ComfyUiModelCatalogClient.ListModelsAsync` (CUI-04) already uses for checkpoint sync —
and returns `ComfyUiDetailerSupport { Unknown = 0, Supported, NotSupported }`, never a
`bool`/`bool?`. `Unknown` is the zero/default value deliberately: a `bool` collapses
"definitively absent" and "couldn't determine" into the same `false`, and a forgotten or
newly added code branch that returns `default` on a `bool?` is indistinguishable from an
explicit negative. Making the *undetermined* state a distinct, non-`Supported` enum member
means every call site — present or future — that does not explicitly handle `Supported`
falls through to plain by construction, not by convention. `Supported` requires **both**
`FaceDetailer` and `UltralyticsDetectorProvider` present as top-level `class_type` keys;
partial presence (Impact-Pack without Subpack, or vice versa) resolves to `NotSupported`,
because `FaceDetailer` has a required `bbox_detector` input that only
`UltralyticsDetectorProvider` supplies — a partially-capable backend is not a usably
capable one for this graph.

### Conservative-fallback invariant: every non-`Supported` path selects plain

`ComfyUiImageModelClient.GenerateAsync` consults the probe once, before
`BuildWorkflowGraph`, and selects the augmented template resource **only** when the
result is `Supported` **and** `ComfyUiSettings.DetailerEnabled` is not explicitly `false`.
`NotSupported`, `Unknown`, and opt-out all select the exact same plain template that
shipped before this change — byte-identical graph, byte-identical behavior, zero added
latency beyond one cached probe call. This is the direct mitigation for the all-or-nothing
failure mode above: the probe's job is not to *maximize* augmented-template usage, it is
to *guarantee* that an unsupporting or indeterminate backend never sees the augmented
graph in the first place, so the `/prompt` all-or-nothing rejection this ADR exists to
avoid simply cannot occur on the capability axis. The only residual way augmented-graph
rejection can still occur is the accepted residual risk below (node class registered,
weights/models absent) — a narrower failure surface than "custom node pack not installed
at all," and one the probe cannot observe without submitting a prompt.

### Per-base-URL cache, 10-minute TTL, asymmetric invalidation

The probe result is cached in a `static ConcurrentDictionary<string, CacheEntry>` keyed by
`BaseUrl.TrimEnd('/')` (not per-adapter-instance state — `ComfyUiImageModelClient` is
resolved per-request through `ExternalProviderClientFactory`, so instance fields cache
nothing across jobs) with a 10-minute TTL, mirroring the existing
`AiCompletionGateway.cs` `SchemaCache` pattern already used elsewhere in Infrastructure.
The cache is deliberately **asymmetric**:

- `Unknown` is never cached. A transient 30-second network blip or timeout must not pin a
  genuinely capable backend to the plain template for the full TTL window.
- A cached `NotSupported` rides out the full TTL untouched by any signal other than
  expiry — a backend that lacks the custom-node pack does not suddenly gain it
  mid-generation-run, and re-probing on every job would defeat the point of caching.
- A cached `Supported` result is invalidated **immediately**, without waiting for TTL,
  the moment a submission using the augmented template is rejected with a `node_errors`
  payload attributable to the detailer subgraph (`ComfyUiDetailerProbe.IsDetailerAttributable`
  matches node ids `"10"`/`"11"` or the `FaceDetailer`/`UltralyticsDetectorProvider` class
  names in the rejection). This is the accepted-residual-risk safety net: node **class**
  registration (what `/object_info` reports) does not guarantee the underlying **model
  weights** (`bbox/face_yolov8m.pt` etc.) are actually installed and loadable — that
  failure mode is invisible to a capability probe and can only surface at submission time.
  Rather than pin the whole `dev`/prod job stream to the CSS-fallback degrade for 10
  minutes after one job's `/prompt` rejection, the very next job for that base URL
  self-heals to plain immediately. Invalidation is deliberately scoped to
  detailer-attributable rejections only — an unrelated `ExternalProviderUnavailable`
  outage (server down, VRAM exhaustion on the primary sampler, etc.) must not thrash a
  perfectly valid `Supported` cache entry.

### Opt-out is opt-out, not opt-in: default `true`, config-only kill switch, plain-only

`ComfyUiSettings.DetailerEnabled` defaults to `true` — the probe governs actual selection
by default, no per-org action required to benefit from the augmented template once a
backend genuinely supports it. `false` short-circuits selection ahead of the probe call
entirely (zero `/object_info` calls when opted out), and — critically — `false` can
**only ever force plain**; there is no corresponding value that force-selects augmented on
an unsupporting backend. An opt-in-default-off flag was rejected: it would require every
org to take an explicit action to receive a strictly-better-or-equal quality improvement
that the probe already gates safely, defeating the purpose of having a safe automatic
gate at all. The flag exists purely as an escape hatch for the one failure mode the probe
structurally cannot see — the weights-missing residual risk above — letting an operator
who observes repeated detailer-attributable rejections on a given backend force plain
without a deploy, while the automatic probe+cache mechanism keeps working correctly for
every other backend.

## Data Flow

    GenerateAsync
      │ settings.DetailerEnabled == false ──────────────► plain template
      ▼ (true)
    ComfyUiDetailerProbe.ProbeAsync(http, baseUrl, ct)
      │  cache hit & !expired ──► cached tri-state
      │  else GET /object_info ──► parse ──► store (unless transport failure/Unknown)
      ▼
    Supported ──► augmented template     Unknown/NotSupported ──► plain template
      ▼
    BuildWorkflowGraph (unchanged) ──► QueuePromptAsync ──► rejected?
                                             │ yes & augmented & detailer-attributable
                                             ▼
                                    ComfyUiDetailerProbe.Invalidate(baseUrl)

## Rejected Alternatives

- **`bool`/`bool?` probe result.** Collapses "definitively unsupported" and "couldn't
  determine" into the same falsy value; a `bool?` default(T) on a forgotten branch is
  indistinguishable from an explicit negative. The tri-state enum makes "fall through to
  plain" the structural default, not a convention every call site must remember.
- **"Try augmented, fall back to plain on `/prompt` rejection."** Doubles `/prompt`
  round-trips on every backend that lacks the custom-node pack, and would need to share
  (or duplicate) the `CAC-IMG-03` retry budget that already governs the whole generation —
  the exact all-or-nothing failure mode this ADR exists to avoid, just deferred one HTTP
  round-trip later and now paid on every miss instead of never.
- **`IMemoryCache` for the probe result.** `ComfyUiImageModelClient` is built via
  `ActivatorUtilities.CreateInstance` per request (`ExternalProviderClientFactory:61`);
  instance-scoped caching caches nothing across jobs. A new DI dependency in
  `Ai/Providers/` was also unnecessary — the existing static-dictionary pattern
  (`AiCompletionGateway.cs` `SchemaCache`) already solves the identical per-base-URL
  cross-request caching problem with zero new wiring.
- **Evict the cache on any `ExternalProviderUnavailable`.** Unrelated ComfyUI outages
  (server restart, VRAM exhaustion on the primary sampler, network blip) would thrash a
  perfectly valid `Supported` cache entry for a failure that has nothing to do with
  detailer node registration.
- **Opt-in-default-off flag.** Every org would need to take an explicit action to receive
  a strictly-better-or-equal-quality improvement that the probe already gates safely for
  free — inverts the cost/benefit of having an automatic capability gate at all.
- **A settings-declared "installed custom nodes" list.** Requires the operator to
  manually track and keep in sync exactly what `/object_info` already reports
  authoritatively and in real time; strictly worse than probing.

## Accepted Residual Risk

Node **class** registration (`/object_info` reporting `FaceDetailer` and
`UltralyticsDetectorProvider` as installed) does not guarantee the underlying **model
weights** the detailer nodes reference (e.g. `bbox/face_yolov8m.pt`) are actually present
and loadable on that server. This is invisible to a capability probe by construction — it
can only surface as a `/prompt` rejection at submission time. The asymmetric
`Supported`-cache invalidation above is the accepted mitigation: the very next job for
that base URL self-heals to plain without waiting out the 10-minute TTL, bounding the
blast radius to at most one failed submission per weights-missing backend per TTL window,
rather than eliminating the risk outright. `ComfyUiSettings.DetailerEnabled = false`
remains available as a manual, config-only kill switch if an operator observes repeated
rejections on a specific backend.

## Consequences

- Adding the detailer preflight is entirely additive and probe-gated: a backend that has
  never installed the Impact-Pack/Subpack (including every local dev environment as of
  this change) sees byte-identical graphs and behavior to pre-change
  `ComfyUiImageModelClient`, with the sole overhead of one cached `/object_info` probe
  call per 10-minute window.
- `BuildWorkflowGraph`, `SubstituteTemplate`, `PollHistoryAsync`, and the `CAC-IMG-03`
  retry contract are all untouched — the augmented template reuses the exact same
  placeholder-token set as the plain template (`CHECKPOINT_NAME`, `POSITIVE_PROMPT`,
  `NEGATIVE_PROMPT`, `REFERENCE_IMAGE`, `WIDTH`, `HEIGHT`, `SEED`, `STEPS`, `CFG`), so no
  substitution or graph-building logic required a change.
- A future third workflow variant (or a second detector/repair node pair) can reuse the
  same probe/cache/select pattern with a new tri-state check and a new template resource;
  no change to `GenerateAsync`'s control flow shape is required.
- One verification step remains open and is NOT satisfied by this ADR alone: real
  submission against a live ComfyUI backend with Impact-Pack/Subpack/weights installed,
  confirming the `KSampler → VAEDecode → FaceDetailer → SaveImage` splice point and the
  full `FaceDetailer` required-input contract are well-formed. No unit test can submit to
  a real ComfyUI server; this is an owner-run, manual, pending action (see Operational
  prerequisites), same category as ADR 0010's WU-7.4 template-validation checklist.

## Operational prerequisites

### 1. Owner-run manual E2E against the live ComfyUI backend

Now that Impact-Pack, Impact-Subpack, and their weights are installed on the local dev
ComfyUI instance, the owner must run a real generation and confirm, in writing: (i) the
probe answers `Supported` against the live `/object_info`; (ii) `POST /prompt` with the
augmented graph is accepted, validating this ADR's splice point and the full
`FaceDetailer` input contract, which no mock can verify; (iii) the output image shows
visible anatomical repair versus the same seed on the plain template. This is tracked as
`tasks.md` Phase 7 / task 7.1 and does not block this ADR's acceptance, but the change it
documents is not considered fully verified until it is recorded.

## Related

- Proposal / Spec delta / Design: `openspec/changes/comfyui-detailer-preflight/`
- Spec requirements realized: `openspec/specs/comfyui-image-provider/spec.md`
  (CUI-07, CUI-08, CUI-09 added; CUI-03 modified)
- Builds on: ADR 0010 (ComfyUI IP-Adapter Local Image Provider) — reuses its
  `/object_info` parsing pattern (OQ-3), its `IntegrationErrors` failure vocabulary
  (OQ-2), and its `CAC-IMG-03` retry contract (untouched by this change)
- Architecture gate: `pwsh -File scripts/gates/Run-Gates.ps1`
- Test suites: `ComfyUiDetailerProbeTests.cs`, `ComfyUiDetailerTemplateTests.cs`,
  `ComfyUiDetailerSelectionTests.cs`, `ComfyUiDetailerInvalidationTests.cs`,
  `ComfyUiSettingsTests.cs` (`Incoders.Hive.Tests/Infrastructure/Ai/`)
- Commits: `d173966`, `928de6b`, `f02ed4a`, `cba29fe`, `c0091a4` (Phases 1-5, `dev`)
