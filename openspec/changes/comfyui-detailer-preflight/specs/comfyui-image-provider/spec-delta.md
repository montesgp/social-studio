# Spec Delta: ComfyUI Image Provider — Detailer Node-Presence Preflight

<!-- Change: comfyui-detailer-preflight -->
<!-- Applies to: openspec/specs/comfyui-image-provider/spec.md (additive delta; amends CUI-03) -->

## Purpose

`ComfyUiImageModelClient` selects a `FaceDetailer`-augmented workflow template only when the target
backend unambiguously registers both `FaceDetailer` and `UltralyticsDetectorProvider`, reusing
`CUI-04`'s `/object_info` parsing. Every other outcome — absence, partial presence, probe failure,
malformed response, or opt-out — selects today's plain template unchanged.

---

## PRD Traceability

| Requirement ID | Proposal Section |
|---|---|
| CUI-07 | Scope items 1, 5 — tri-state node-presence probe |
| CUI-08 | Scope items 2, 3, 5 — template selection, conservative-fallback invariant |
| CUI-09 | Scope items 4, 6 — probe-result cache, TTL, asymmetric invalidation |
| CUI-03 (MODIFIED) | Scope item 5, D1 — opt-out settings field |

**Testability note**: local `Impact-Pack` is now installed, so `CUI-07-a`/`CUI-08-a` MAY be exercised
against a real response, not only mocks; weight-file presence stays unconfirmed, so `CUI-09-b`'s
deferred false-positive criterion is unchanged.

---

## ADDED Requirements

### Requirement: CUI-07 — Node-Presence Probe Returns a Tri-State Result; Unknown Is Never Coerced to Supported

Before submitting a prompt, the adapter MUST determine detailer support via `GET
{baseUrl}/object_info`, reusing `CUI-04`'s `class_type`-presence parsing. The probe MUST return
exactly one of `Supported`, `NotSupported`, `Unknown`, and MUST NOT throw into the generation path.
`Supported` MUST require both node classes present in a well-formed response; every other shape
(neither/one present, non-2xx, timeout, transport exception, malformed body) MUST resolve to
`NotSupported` or `Unknown`, never `Supported`.

#### Scenario: Both classes present — Supported (CUI-07-a)
- GIVEN a well-formed response listing `FaceDetailer` and `UltralyticsDetectorProvider`
- WHEN the probe runs
- THEN it returns `Supported`

#### Scenario: Partial presence — NotSupported (CUI-07-b)
- GIVEN a response listing only one of the two classes
- WHEN the probe runs
- THEN it returns `NotSupported`, not `Supported`

#### Scenario: Transport failure or malformed body — Unknown, no exception (CUI-07-c)
- GIVEN a non-2xx response, timeout, transport exception, or unparseable body
- WHEN the probe runs
- THEN it returns `Unknown` and no exception propagates

---

### Requirement: CUI-08 — Template Selection Requires an Affirmative Probe; Every Other Path Selects Plain

The adapter MUST select the augmented template only when the probe (fresh or cached) is `Supported`
AND the opt-out flag is unset. `NotSupported`, `Unknown`, and opt-out MUST all select plain. On any
backend where the probe is never `Supported` — including local dev today — the submitted graph and
generation outcome MUST match pre-change behaviour byte-for-byte, with no added latency beyond one
cached probe call.

#### Scenario: Positive probe selects the augmented template (CUI-08-a)
- GIVEN a `Supported` probe result and the opt-out flag unset
- WHEN a generation is submitted
- THEN the graph contains `FaceDetailer` and `UltralyticsDetectorProvider`, wired between the sampler
  and the output node

#### Scenario: Negative, unknown, or opted-out probe selects plain (CUI-08-b, regression)
- GIVEN a `NotSupported`/`Unknown` result, or a `Supported` result with the opt-out flag set
- WHEN a generation is submitted
- THEN the plain template is submitted byte-identical to pre-change behaviour, and opt-out makes no
  probe call

---

### Requirement: CUI-09 — Probe Result Cached Per Base URL, 10-Minute TTL, Asymmetric Invalidation

The adapter MUST cache a resolved probe result (not the raw payload) keyed by normalized `baseUrl`
for 10 minutes. `Unknown` results MUST NOT be cached. A cached `Supported` result MUST be invalidated
immediately when a submission using the augmented template fails with a node-registration error
attributable to the detailer subgraph, so the next job for that `baseUrl` submits plain. A cached
`NotSupported` result MUST NOT be proactively invalidated and MUST simply expire at TTL.

#### Scenario: Cache hit avoids re-probing within TTL (CUI-09-a)
- GIVEN a cached result under 10 minutes old
- WHEN a second generation targets the same `baseUrl`
- THEN no second `/object_info` call is made

#### Scenario: Positive invalidated by a detailer node_errors failure (CUI-09-b)
- GIVEN a cached `Supported` result and a submission that fails with detailer-attributable
  `node_errors` (accepted residual: node registration does not guarantee model weights)
- WHEN the next generation targets the same `baseUrl`
- THEN plain is submitted without waiting for TTL expiry

#### Scenario: Negative result rides out the full TTL (CUI-09-c)
- GIVEN a cached `NotSupported` result
- WHEN any signal other than TTL expiry occurs
- THEN the cached result is unchanged until 10 minutes elapse

---

## MODIFIED Requirements

### Requirement: CUI-03 — ComfyUiSettings Carries Base URL, Checkpoint, Workflow Template, Timeout, Detailer Opt-Out; Per-Org Configurable

`ComfyUiSettings` MUST expose at minimum: base URL (default `http://127.0.0.1:8188`), checkpoint
identifier, workflow template reference, polling timeout, and a detailer opt-out flag (default
unset — probe governs). Settings MUST be per-org configurable without a code change. The opt-out
flag MUST only ever force the plain template; it MUST NOT force the augmented template on an
unsupporting backend.
(Previously: no detailer opt-out flag; only base URL/checkpoint/template/timeout.)

#### Scenario: Custom base URL in settings — adapter uses configured endpoint (CUI-03-a)
- GIVEN `ComfyUiSettings.BaseUrl` set to a custom endpoint for an org
- WHEN the adapter makes HTTP calls
- THEN all calls target the configured endpoint, not the default

#### Scenario: Base URL unset — adapter defaults to `http://127.0.0.1:8188` (CUI-03-b)
- GIVEN `ComfyUiSettings.BaseUrl` is not set
- WHEN the adapter makes HTTP calls
- THEN all calls target `http://127.0.0.1:8188`

## REMOVED Requirements

None.
