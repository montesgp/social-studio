# Proposal: ComfyUI Detailer Preflight

## Change ID
`comfyui-detailer-preflight`

## Status
Proposal — revision 1. Builds on `exploration.md` (findings (a)–(f), carried forward from the
investigation-only feasibility report delivered by `client-aesthetic-generation-hardening`'s WU-5,
archived 2026-08-25). This is the **gated follow-up** that report recommended.

Three decisions remain genuinely open and are surfaced in `## Proposal question round`. None of them
blocks the spec or design phases — each has a stated recommendation and a stated default. D1 changes
the shape of one work unit; D2 and D3 change constants and dispositions only.

## Intent

**Business problem.** Real generations still ship slides with deformed anatomy — duplicated faces,
malformed hands, extra limbs. That defect was observed again this session, after
`client-aesthetic-generation-hardening` added anatomy terms to `DefaultNegativePrompt`
(`CAGH-CUI-02`). A negative prompt is soft steering; it reduces the frequency of the defect and
cannot eliminate it. The ComfyUI-native remedy is a `FaceDetailer` pass, and the prior feasibility
report established exactly what it costs and exactly why it could not be added unconditionally.

**Why now, and why not "later".** The owner's verbatim direction is production readiness:
*"tengamos ese nodo de reparación. Esto nos va a servir cuando migremos a envs productivos."* The
local dev ComfyUI backend almost certainly cannot exercise the positive path today — no Impact-Pack
install, no detection-model weights. Building the capability now means that when a production backend
does have them, the improvement is a **backend install**, not a code change and a release cycle.

**Current-state gap (verified in the exploration, not re-derived).**

| # | Fact | Source |
|---|---|---|
| 1 | The workflow graph is ONE flat JSON submitted whole to `POST /prompt`. A referenced-but-unregistered node fails the **entire** prompt — non-2xx and 2xx-with-`node_errors` both map to `ExternalProviderUnavailable`. There is no partial-success path. | finding (d) |
| 2 | `ComfyUiImageModelClient` does **not** call `/object_info` at all. Only `ComfyUiModelCatalogClient` does. Wiring a preflight into the generation path is new code, not a reused call site. | finding (c) |
| 3 | `SubstituteTemplate` / `BuildWorkflowGraph` fill placeholder tokens into **one** graph shape. There is no branching on which nodes exist. | finding (e) |
| 4 | `FaceDetailer` needs **two independent** installs: `ComfyUI-Impact-Pack` (+ `-Subpack`) *and* separately-downloaded detection weights under `models/`. Either missing breaks the node. | finding (b) |
| 5 | `/object_info` reveals registered node **classes** only — never whether the weight files exist on disk. A "probe says yes" can still fail at generation time. | findings (b)+(d) |

**Outcome.** A ComfyUI backend that genuinely supports detailing produces anatomically repaired
faces and hands; a backend that does not — which includes every environment we can test in today —
produces exactly what it produces now, byte-for-byte, with no new failure mode. The choice between
those two is made by the code, not by an operator remembering to configure something.

## Scope

### In Scope

1. **A node-presence preflight probe.** A `GET {baseUrl}/object_info` call checking that **both**
   `FaceDetailer` and `UltralyticsDetectorProvider` are registered, mirroring
   `ComfyUiModelCatalogClient`'s existing `class_type`-presence parsing (`AppendEnumModels` pattern)
   rather than duplicating it. Partial presence (one class but not the other) is **not** support.
2. **A detailer-augmented workflow template.** A new embedded resource sibling to
   `DefaultComfyUiWorkflowTemplate.json`, carrying the `UltralyticsDetectorProvider` +
   `FaceDetailer` subgraph spliced between the existing sampler output and the save/output node.
   Placeholder-token contract identical to the plain template so `SubstituteTemplate` is unchanged.
3. **Template selection in `ComfyUiImageModelClient`.** The generation path consults the probe and
   selects the augmented template only on an unambiguous positive result.
4. **A probe-result cache keyed by `baseUrl`.** ComfyUI is already the slowest pipeline stage; an
   extra round trip per generation is not viable. The cache stores the resolved answer, not the raw
   `/object_info` payload.
5. **Conservative fallback as a hard invariant.** Probe failure, probe timeout, malformed or
   unparseable response, partial node presence, an explicitly-configured opt-out, or any unhandled
   exception in the selection path ⇒ **the plain template**. The augmented template is selected only
   by an affirmative, fully-parsed, both-classes-present result. There is no path in which
   uncertainty produces the augmented template.
6. **The residual-failure catch.** Because of fact 5 above, a `node_errors` failure attributable to
   the detailer subgraph invalidates the cached positive for that `baseUrl`, so the backend is not
   re-probed into the same failure on every subsequent job.
7. **Spec deltas and doc sync** for `comfyui-image-provider`, plus the ADR determined below.

### Out of Scope — explicit non-goals

| Deferred | Why now is wrong |
|---|---|
| **Installing `ComfyUI-Impact-Pack`, `-Subpack`, or any detection-model weights on any backend** | Infra/ops concern, not a code change. Same exclusion pattern as `client-aesthetic-visual-sophistication`'s checkpoint-curation carve-out: this change makes the code *react correctly* to what a backend has; provisioning what it has is an operator action with its own runbook. |
| **Any deeper capability probe than node-class presence** | `/object_info` is the only surface ComfyUI exposes for this, and it does not report weight files. A filesystem or model-listing probe would be a new provider capability with its own credential and error posture. Item 6's cache-invalidation catch is the accepted mitigation instead. |
| **Automatically retrying a failed generation with the plain template inside the same job** | The existing bounded-retry / CSS-fallback contract (`CAC-IMG-03`) already governs a failed image; adding a second, detailer-specific retry layer inside the adapter duplicates a disposition that already exists one level up. Item 6 makes the *next* job correct, which is the cheaper half of the same outcome. |
| **Tuning detailer parameters** (denoise, guide size, per-format thresholds, hand-vs-face weighting) | Quality tuning requires a backend that can actually run the node. Ship one conservative parameter set; tune when a real positive path exists to tune against. |
| **Applying the detailer to any path other than the ComfyUI generation path** | The flat deterministic path and AI-designer markup produce no photographic anatomy and have nothing to detail. |
| **Anatomy-defect detection or scoring** | This repository still has no way to score generated-image quality. Effectiveness stays owner-verified by real generation, consistent with the prior three changes. |
| **Removing or weakening the anatomy negative-prompt terms** (`CAGH-CUI-02`) | They remain the sole defense on every non-supporting backend, which is every backend we can test today. Untouched. |
| **Per-organization detailer *quality* settings surfaced in the UI** | No settings UI work in this change regardless of how D1 resolves; D1's flag, if adopted, is a `ComfyUiSettings` field like every other per-credential option. |

## Capabilities

### New Capabilities
None. The probe, the second template and the selection rule are all behaviour of the existing
ComfyUI adapter; none of them is a new port or a new provider category.

### Modified Capabilities (additive deltas)

- `comfyui-image-provider`: the adapter MUST determine, before submitting a prompt, whether the
  target backend registers both `FaceDetailer` and `UltralyticsDetectorProvider`; MUST select the
  detailer-augmented workflow template **only** on an unambiguous positive determination; MUST select
  the plain template on absence, partial presence, probe failure, probe timeout, unparseable
  response, or opt-out; MUST cache the determination per base URL so the probe is not on the
  per-generation hot path; and MUST invalidate a cached positive when a submitted prompt fails with
  node errors attributable to the detailer subgraph. Amends the `CUI-03` settings surface if D1
  resolves toward a flag, and sits beside `CUI-04`'s existing `/object_info` usage.
- `image-generation`: **untouched.** Prompt construction, negative prompts and the retry contract are
  unchanged; this change alters which graph the adapter submits, not what it is asked to depict.
- `integrations`: **untouched.** No new credential type, no new provider category, no change to
  `IntegrationExecutionPolicy`.

## Approach

One track, four sequential pieces, each independently revertible. The design principle is stated
once and holds everywhere: **the augmented template requires proof; the plain template is what
happens otherwise.** Every branch that is not an affirmative both-classes-present parse falls to
plain, including branches an author forgot to write.

### The selection rule, stated precisely

| Probe outcome | Template | Cached? |
|---|---|---|
| Both classes present, response fully parsed | **Augmented** | Yes, positive |
| Neither class present | Plain | Yes, negative |
| Exactly one class present | Plain | Yes, negative — partial presence is not support |
| HTTP non-2xx, timeout, transport exception | Plain | **No** — a transient outage must not pin a backend to plain for the whole TTL |
| 2xx with malformed / unparseable body | Plain | Yes, negative — a backend that answers unintelligibly is not a backend we trust with an all-or-nothing graph |
| Opt-out configured (if D1 adopts a flag) | Plain | Probe not performed at all |
| Generation later fails with detailer-attributable `node_errors` | Plain, next job | Cached positive **invalidated** |

The probe never throws into the generation path. It returns a tri-state (`Supported`,
`NotSupported`, `Unknown`), and both `NotSupported` and `Unknown` route to plain — so the caller has
no branch in which "unknown" could be mistaken for support.

### Cache mechanism

The adapter is resolved per request through `ExternalProviderClientFactory`
(`SocialStudio.Infrastructure/Integrations/ExternalProviderClientFactory.cs:61`), so an instance
field would cache nothing. This repository already uses a static `ConcurrentDictionary` for exactly
this shape of per-process memoization: `AiCompletionGateway.cs:190`
(`ConcurrentDictionary<Type, string> SchemaCache`). The probe cache follows that precedent — a
`ConcurrentDictionary` keyed by normalized `baseUrl`, holding the tri-state plus a timestamp for the
TTL D2 settles. No `IMemoryCache` dependency is introduced; there is none in
`Infrastructure/Ai/Providers/` today.

### File / layer map

| Piece | Layer | Path |
|---|---|---|
| Node-presence probe (`/object_info`, tri-state result) | Infrastructure | new type beside `Ai/Providers/ComfyUiModelCatalogClient.cs`; class-presence parsing reused, not duplicated |
| Probe cache (static `ConcurrentDictionary` keyed by base URL, TTL, invalidation) | Infrastructure | same new type; precedent `Ai/AiCompletionGateway.cs:190` |
| Template selection + fallback invariant | Infrastructure | `Ai/Providers/ComfyUiImageModelClient.cs` (template resolution ~L742, submission path ~L156/L522) |
| Detailer-augmented graph | Infrastructure | new embedded resource beside `DefaultComfyUiWorkflowTemplate.json` |
| Opt-out / opt-in flag (**conditional on D1**) | Infrastructure | `Ai/Providers/ComfyUiSettings.cs` |
| Cache invalidation on detailer-attributable `node_errors` | Infrastructure | `Ai/Providers/ComfyUiImageModelClient.cs` (`QueuePromptAsync` error mapping) |
| Spec delta + ADR | Docs | `openspec/changes/comfyui-detailer-preflight/specs/comfyui-image-provider/spec.md`, `docs/architecture/adr/` |

### Reused vs. new

| Reused unchanged | New |
|---|---|
| `ComfyUiModelCatalogClient`'s `/object_info` call and `class_type`-presence parsing (`CUI-04`) | A second consumer of that parsing, on the generation path |
| `SubstituteTemplate` / `BuildWorkflowGraph` placeholder-token contract | A second template honouring the identical token contract |
| `IntegrationErrors.ExternalProviderUnavailable` mapping and the `CAC-IMG-03` retry/degrade contract | Reused as the disposition for a detailer-attributable failure |
| `AiCompletionGateway`'s static-`ConcurrentDictionary` memoization shape | Applied to the probe result |
| `DefaultNegativePrompt` anatomy terms (`CAGH-CUI-02`), `BackgroundOnly` handling (`CAGH-CUI-01`), the plain template, every non-ComfyUI path | **Untouched** |

### Ordering rationale

Probe before template, template before selection. WU-1 ships a probe nothing calls — inert and fully
unit-testable against mocked `/object_info` bodies, including every malformed shape. WU-2 ships a
template nothing selects. Only WU-3 connects them, which means the risky wiring lands against two
already-proven pieces rather than being debugged simultaneously with them.

## ADR determination

`openspec/config.yaml` sets `require_adr_for_architecture_changes: true`.

**Exactly one ADR is required.**

- **Backend-capability-gated workflow-template selection** — **ADR required.** This introduces a new
  architectural pattern to the provider layer: a runtime capability probe against an external
  backend, a cached tri-state capability determination, and behaviour that varies by what the remote
  installation happens to have. It also records the conservative-default invariant and the accepted
  residual risk that node-class registration does not imply usable weights. Both the pattern and the
  residual-risk acceptance must be written down — a future author who does not know why the default
  is asymmetric could "simplify" it into an outage.
- **The second template, the probe cache TTL, D2 and D3** — no ADR. They are parameters and a data
  file inside the pattern the ADR records.
- **D1, whichever way it resolves** — no separate ADR; it is a subsection of the same one.

## PRD Traceability

`openspec/config.yaml` sets `require_prd_traceability: true`.

| PRD anchor | This change | Honesty note |
|---|---|---|
| §1 Product Summary / §3 Goals — content the client can publish as-is | A slide with a duplicated face or a malformed hand is a manual redo, which is the exact cost this capability exists to remove | The fix only lands on a backend that supports it. On today's backends the observable outcome is unchanged. |
| §9 Acceptance Criteria — fidelity of generated assets | Adds anatomical correctness to the colour/type/composition/layout fidelity already shipped | Not measurable in-repo; no anatomy scoring exists. Owner-verified by real generation on a supporting backend. |
| §13 Integrations / provider configuration | Backend capability now influences adapter behaviour; `ComfyUiSettings` may gain one field (D1) | Spec phase confirms whether §13 needs an amendment — provisionally **no** if D1 resolves automatic, **yes, minor** if it resolves to a flag. |

No `profile-analysis` / §11 involvement and no `content-generation` / §10 taxonomy change: nothing
here touches extraction, layout, or format.

## Delivery forecast — direct sequential commits to `dev`

Delivery strategy `single-pr`: direct sequential commits to `dev`, no PR chain, no stacked PRs,
tracked via one GitHub issue. Budget **800 changed lines per work unit**; estimates are
`additions + deletions` with Strict TDD included (tests ≈ 1:1).

| # | Work unit | Est. | Over budget? |
|---|---|---|---|
| WU-1 | Node-presence probe: tri-state result, `/object_info` parsing reused from the catalog client, static `ConcurrentDictionary` cache with TTL, plus exhaustive mocked-response tests (both present, neither, each one alone, non-2xx, timeout, malformed JSON, empty body, cache hit/miss/expiry) | ~520 | No |
| WU-2 | Detailer-augmented workflow template as an embedded resource + a test asserting its placeholder-token set is **identical** to the plain template's, so `SubstituteTemplate` needs no change | ~260 | No |
| WU-3 | Template selection in `ComfyUiImageModelClient` + the conservative-fallback invariant, asserted branch by branch against the selection table above | ~440 | No |
| WU-4 | Cache invalidation on detailer-attributable `node_errors` + a regression test that a second job after such a failure submits the plain template | ~300 | No |
| WU-5 | **Conditional on D1** — `ComfyUiSettings` flag, its default, its plumbing and its precedence over a positive probe. **Omitted entirely if D1 resolves automatic.** | ~280 | No |
| WU-6 | Spec delta for `comfyui-image-provider` + ADR + doc sync | ~360 | No |

**Total: ~1,880 changed lines across 5 commits, or ~2,160 across 6 if D1 adopts the flag.**

`Decision needed before apply: Yes` — D1 determines whether WU-5 exists at all. D2 and D3 set
constants and dispositions inside WU-1/WU-3 and do not change any unit's shape.
`Chained PRs recommended: No`
`800-line budget risk: Low` — the largest unit (WU-1 at ~520) has ~280 lines of headroom, and it is
a self-contained pure-Infrastructure type with no cross-layer surface.

**Ordering constraints**: WU-1 → WU-3 and WU-2 → WU-3 (selection cannot be wired before a probe and
a template exist). WU-3 → WU-4 (invalidation needs something to invalidate). WU-5, if it exists,
after WU-3. WU-6 last.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **The augmented template is selected on a backend that cannot run it, breaking 100% of image generation for that job** — the all-or-nothing failure of finding (d), and the central risk of this entire change. | Low, catastrophic | Asymmetric-by-construction defaults: the augmented template requires an affirmative both-classes-present parse; every other branch, including unwritten ones, falls to plain. The probe returns a tri-state so `Unknown` cannot be silently coerced to supported. WU-3 asserts every row of the selection table individually, not the happy path plus a catch-all. |
| **Node classes registered but detection weights absent** (finding (b)) — the probe false-positives and the job hits the all-or-nothing failure anyway. `/object_info` cannot see weight files; **no probe design closes this**. | Medium, **accepted** | Stated openly rather than engineered around. The failure degrades through the existing `ExternalProviderUnavailable` → `CAC-IMG-03` retry/degrade contract, exactly as any other ComfyUI failure does today — it is not a new failure *class*, only a new trigger. WU-4's cache invalidation bounds the blast radius to **one** job per backend-configuration change rather than every job. This is the specific risk D1 asks the owner to weigh. |
| **This change cannot be validated end-to-end in the local dev environment** — no Impact-Pack, no weights, so the positive path is unexercisable outside mocks. | High, **accepted and explicit** | Split honestly in `## Success Criteria` into what is asserted in-repo and what is deferred to production. The mocked-probe coverage is exhaustive precisely *because* the live positive path cannot be exercised — the tests are the only evidence available before deployment, so their breadth is not optional. |
| **A stale cached positive survives an operator uninstalling Impact-Pack**, so every job fails until the TTL expires. | Low | D2's TTL bounds it, and WU-4's failure-driven invalidation collapses it to one job in practice. The reverse direction (a stale *negative* after an operator installs the package) costs only continued current behaviour, which is why the two directions do not need symmetric handling. |
| **Detailing changes the image in ways the client's aesthetic profile did not ask for** — a repaired face is a re-sampled region, and the second KSampler pass can drift palette or style locally. | Medium | Conservative default parameters, low denoise. Parameter tuning is an explicit non-goal until a backend exists to tune against; the ADR records that the shipped values are a safe starting point, not a tuned result. |
| **Latency**: `FaceDetailer` adds a detection pass plus a second sampling pass on ComfyUI, already the pipeline's slowest stage. | Medium | Only ever paid on a backend that supports it, i.e. never in the environments we run today. The existing per-client overall timeout (`ComfyUiImageModelClient`, default 600s, `DependencyInjection.cs:365`) already bounds it; the ADR records that this bound was reviewed rather than assumed sufficient. |
| **The augmented template drifts from the plain one** as future changes edit the plain graph and forget the sibling. | Medium | WU-2 ships a test asserting the two templates share an identical placeholder-token set — a drift in the token contract fails the build. It does **not** catch every graph-level divergence; that residual is recorded in the ADR. |
| **The probe adds an HTTP call to a path that previously made none**, on a provider whose outages already exist. | Low | Cached per base URL, so it is not on the per-generation hot path. Probe failure never fails a job — it selects plain, which is the pre-change behaviour exactly. |

## Rollback Plan

- **WU-1** — a probe type nothing calls. Inert; reverts freely.
- **WU-2** — an unreferenced embedded resource. Inert; reverts freely.
- **WU-3** — **the only unit with behavioural risk, and it is a single-direction revert.** Reverting
  restores unconditional plain-template selection, which is today's behaviour exactly. No persisted
  state, no migration, no downstream consumer is affected — the submitted graph shape returns to the
  one every existing test already covers.
- **WU-4** — cache-invalidation logic only; reverting restores TTL-only expiry. Degrades to a
  slightly staler cache, never to a broken one.
- **WU-5** (if it exists) — a settings field with a safe default. Reverting drops the field; because
  the probe still gates selection, removing the flag cannot enable the augmented template on an
  unsupporting backend.
- **WU-6** — documentation and spec deltas only.
- **No database migration exists in this change. No credential, provider category or runtime
  dependency is added.** A full revert of every unit returns the adapter to byte-identical
  pre-change behaviour.
- **Operational rollback without a code revert**: uninstalling Impact-Pack on the backend causes the
  next probe to answer negative and the adapter to select plain. If D1 adopts the flag, flipping it
  off is an immediate config-only kill switch — that is D1's principal argument.

## Success Criteria

### Verifiable in-repo, on any environment (blocking)

- [ ] The probe returns `Supported` **only** when both `FaceDetailer` and
      `UltralyticsDetectorProvider` appear in a well-formed `/object_info` response.
- [ ] Every non-affirmative probe outcome — neither class, exactly one class, non-2xx, timeout,
      transport exception, malformed JSON, empty body — selects the **plain** template. Asserted
      **individually per row** of the selection table, not by a single catch-all test.
- [ ] Transport failures and timeouts are **not** cached; parsed negatives and partial presence
      **are**. Asserted directly.
- [ ] A second generation against the same base URL within the TTL performs **no** second
      `/object_info` call.
- [ ] A generation failing with detailer-attributable `node_errors` invalidates the cached positive,
      and the next job for that base URL submits the **plain** template.
- [ ] The augmented template's placeholder-token set is identical to the plain template's, so
      `SubstituteTemplate` and `BuildWorkflowGraph` are unchanged.
- [ ] With a probe forced to `Supported` (mocked), the submitted graph contains the `FaceDetailer`
      and `UltralyticsDetectorProvider` nodes, correctly wired between the sampler and the output.
- [ ] On an environment where the probe answers negative — **this includes the local dev backend** —
      the submitted graph is **byte-identical** to the pre-change graph.
- [ ] Every pre-existing `comfyui-image-provider` test passes unchanged, including `CAGH-CUI-01`
      (`BackgroundOnly`) and `CAGH-CUI-02` (anatomy negative prompt).
- [ ] Spec delta merged for `comfyui-image-provider`; the capability-gated-template-selection ADR
      merged; PRD §13 amendment merged if the spec phase confirms it is required.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD followed;
      architecture gate green (no ComfyUI or HTTP type crossing into Domain/Application, per
      `CUI-05`).

### NOT verifiable before production deployment (explicitly deferred, not waived)

- [ ] *(deferred)* A real generation on a backend with Impact-Pack **and** detection weights
      installed produces visibly repaired faces and hands versus the same prompt without the
      detailer. Requires a provisioned backend; the provisioning itself is out of scope.
- [ ] *(deferred)* The added latency of the detailer pass on a real supporting backend is measured
      against the existing 600s timeout, and the ADR's latency section is updated with the real
      figure rather than the reviewed bound.
- [ ] *(deferred)* The false-positive case from finding (b) — classes registered, weights absent — is
      observed at least once against a real backend, confirming it degrades through
      `CAC-IMG-03` as designed rather than surfacing as a hard job failure.
- [ ] *(deferred)* The owner reviews real generations on a supporting backend and records
      accept/reject in writing, including whether the detailer's re-sampling drifts the client's
      aesthetic.

**This split is the honest posture of the change and must not be quietly collapsed.** Marking the
first block complete does **not** mean the feature is proven; it means the feature is proven to be
*safe on a non-supporting backend*, which is a different and smaller claim.

## Dependencies

- Existing `ComfyUiModelCatalogClient` `/object_info` parsing (`CUI-04`), the `SubstituteTemplate` /
  `BuildWorkflowGraph` token contract, `IntegrationErrors.ExternalProviderUnavailable` and the
  `CAC-IMG-03` retry/degrade contract — all reused read-only.
- **Operator/infra prerequisite for the deferred criteria only**: a ComfyUI backend with
  `ComfyUI-Impact-Pack`, `ComfyUI-Impact-Subpack`, and detection weights (`bbox/face_yolov8m.pt`,
  `segm/hand_yolov8s-seg.pt`) under `models/`. Provisioning it is **out of scope for this change**;
  none of the in-repo success criteria depends on it.
- **No new runtime dependency, NuGet package, migration, credential or provider category.**

## Proposal question round

These questions exist to sharpen the product decision, not the implementation. The exploration
flagged all three as genuinely open; each carries a recommendation and a stated default, so you may
answer, skip, correct the framing, or ask for a second round without blocking spec or design.

### D1 — Per-organization opt-in/opt-out flag, or fully automatic probe-and-select?

The prior feasibility report recommended an explicit flag on top of the probe. The alternative is no
flag at all: probe, cache, select.

**The question framed concretely.** Scope item 5 already guarantees safe fallback on every form of
uncertainty the probe can *detect*. A flag adds nothing to those cases. The only case where a flag
changes an outcome is the **undetectable** one: node classes registered, detection weights absent
(finding (b)). There, the probe says yes, the job fails, and a flag would have let an operator force
the safe template *without a deploy*.

So the real question is: **is a config-only kill switch for a failure mode we cannot detect worth one
settings field?**

| | With a flag | Without a flag |
|---|---|---|
| Undetectable false-positive | Operator flips one setting; fixed immediately, per org | WU-4's invalidation absorbs it after **one** failed job; if the classes stay registered, the next probe re-enables it and the failure recurs each TTL |
| Config surface | One more `ComfyUiSettings` field with a default someone must get right | Zero |
| Cost | WU-5, ~280 lines | — |
| Risk of the flag itself | An org left opted-out never gets the fix, silently | — |

**Recommendation: adopt the flag, as an opt-OUT with a safe-on default** — i.e. the probe governs by
default, and the flag can only ever force the *plain* template, never force the augmented one. This
keeps the automatic path's zero-configuration benefit for the normal case while preserving a
config-only escape hatch for the one failure we provably cannot detect. An opt-*in* flag (default
off) is rejected: it would mean the fix silently never applies on a fully-capable production backend
until someone remembers to enable it, which defeats the production-readiness intent of the change.

*Default if unanswered: opt-out flag, default "probe governs", flag can only force plain. WU-5
ships.*

### D2 — Probe cache TTL and invalidation strategy

Backend node availability changes only when an operator installs or removes a custom-node package or
downloads weights — a rare, deliberate, ops-driven event, not a runtime condition. Re-probing costs
one HTTP call, and because it is cached it never sits on the per-generation hot path.

- **Per-process lifetime (no TTL)** — cheapest, but an operator installing Impact-Pack sees no effect
  until the next deploy or restart, which is opaque and will generate a support question.
- **Bounded TTL** — bounds staleness in both directions with a negligible cost: at a 10-minute TTL, a
  backend running a job every 30 seconds pays roughly one extra HTTP call per 20 generations.

**Recommendation: a 10-minute TTL**, combined with WU-4's immediate failure-driven invalidation of a
positive. The asymmetry is deliberate — a stale *positive* is expensive (failed jobs) and is
therefore invalidated on evidence rather than waiting for the TTL; a stale *negative* costs only
continued current behaviour and can safely wait out the full 10 minutes.

*Default if unanswered: 10-minute TTL, plus failure-driven invalidation of positives (WU-4).*

### D3 — Probe-failure disposition: confirm it equals "nodes absent"

Confirming rather than asking: this proposal treats **"could not reach `/object_info` at all"**
(non-2xx, timeout, transport exception) **identically to "the nodes are absent"** — select the plain
template, proceed with the job, never block or fail.

The reasoning: an unreachable `/object_info` on a backend we are about to submit a prompt to means
either the backend is down (the job will fail on `POST /prompt` regardless, through the existing
`CAC-IMG-03` path) or the endpoint is transiently unavailable (in which case degrading to today's
behaviour costs nothing). Treating it as blocking would convert a transient probe hiccup into a
failed generation — inventing a new outage in the name of a quality improvement, which is precisely
the outcome finding (d) warns against.

The one refinement worth naming: transport failures are **not cached** (see the selection table), so
a 30-second network blip does not pin a capable backend to the plain template for the whole TTL.

*Default if unanswered: probe failure ⇒ plain template, uncached, job proceeds normally.*
