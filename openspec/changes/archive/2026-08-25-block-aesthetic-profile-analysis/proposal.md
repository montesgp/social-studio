# Proposal: Block Content Generation on First-Time Aesthetic Profile Analysis

Tracking issue: #67

## Intent

For a `ClientAesthetic` client with **no usable completed profile analysis** (first-time / empty aesthetic), content generation today produces degraded output that ignores the client's aesthetic. In `ContentGenerationJobProcessor.ProcessInternalAsync` (design §5.1 ORDERING, ~lines 242-295), when policy == `ClientAesthetic` the processor calls `_aestheticEnsureFreshService.EnsureFreshAsync(...)` **fire-and-forget** (exception swallowed, never blocks — CAG-CG-04), then reads the profile "best-available" and generates anyway. On a first run the aesthetic is empty, so the generation silently ignores it.

This change makes generation **block on the profile analysis** for the first-time case: the job waits for the just-enqueued `ProfileAnalysisJob` to complete (bounded by the analysis job budget) before generating, surfaces a new `AnalyzingProfile` phase in the progress bar, and — when the client is `ClientAesthetic` but has **no own account configured** — fails with an actionable error instead of degrading. Success looks like: a first-time `ClientAesthetic` client either generates *with* its analyzed aesthetic or gets a clear, actionable failure — never silent degradation.

## Scope

### In Scope
- Block generation ONLY when there is **no usable aesthetic** (first-time / empty): after enqueuing/confirming the `ProfileAnalysisJob`, wait for it to reach a usable analyzed state before generating.
- Bound the blocking wait by the **profile-analysis job budget** (decision 1); on timeout, fail the generation job (do not degrade).
- New job phase / status **`AnalyzingProfile`** (decision 3) surfaced through the job status response so the frontend shows an "Analyzing profile" phase with subsequent steps pending/blocked until it clears.
- `ClientAesthetic` + **no own account in ABM** → **actionable blocking failure** telling the user to configure an own account to generate in this modality (decision 2); do NOT silently degrade.
- Reconcile the reversed isolation contract: keep the existing non-blocking background refresh for the **aesthetic-present-but-stale** case; block only for the **no-usable-aesthetic** case.
- Frontend: render the `AnalyzingProfile` phase in the generation overlay and keep later steps blocked/pending until it resolves.

### Out of Scope (Non-goals)
- Changing behavior for the **aesthetic-present-but-stale** case — it keeps today's fire-and-forget background refresh (CAG-CG-04) and generates best-available.
- Changing behavior for non-`ClientAesthetic` policies.
- Redesigning the `ProfileAnalysisJob` itself, its scheduling, or its freshness heuristics.
- Adding retry/backoff or user-driven "retry analysis" UX beyond the single bounded wait.
- Changing own-account configuration (ABM) flows; we only detect its absence and fail actionably.

## Capabilities

### Modified Capabilities
- `content-generation-aesthetic-gating`: for `ClientAesthetic`, generation is gated on a usable analyzed aesthetic in the first-time case — blocking wait + timeout + no-own-account actionable failure — instead of always generating best-available.
- `content-generation-job-status`: job status vocabulary gains `AnalyzingProfile` alongside the existing Pending, Running, Completed, Partial, Failed, surfaced to the polling status endpoint and the frontend overlay.

## Approach

Inside `ContentGenerationJobProcessor.ProcessInternalAsync`, branch on the `EnsureFreshAsync` outcome instead of firing it and forgetting. `ClientAestheticEnsureFreshService.EnsureFreshAsync` already returns an `EnsureFreshOutcome` (Fresh, SkippedInFlight, SkippedNoOwnAccount, RefreshEnqueued) and already resolves the own account and enqueues the `ProfileAnalysisJob`:

- **Usable aesthetic already present** (Fresh, or present-but-stale that resolves to best-available): generate as today; stale-refresh stays non-blocking.
- **No usable aesthetic + own account exists** (RefreshEnqueued / SkippedInFlight with empty aesthetic): transition the job to `AnalyzingProfile`, **wait** for the enqueued/in-flight `ProfileAnalysisJob` to reach a usable analyzed state, bounded by the profile-analysis job budget, then generate. On timeout → `Failed` with an actionable message.
- **No own account** (SkippedNoOwnAccount): fail immediately with an actionable error instructing the user to configure an own account for this modality.

The status transition adds `AnalyzingProfile` between `Running` and the terminal states, threaded through the status DTO the frontend polls. Frontend renders it as a distinct phase with downstream steps blocked. The precise wait mechanism (poll analysis job status vs. await a completion signal) and the exact timeout value tied to the analysis budget are design decisions.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Incoders.Hive.Application/.../ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` (~242-295) | Modified | Branch on `EnsureFreshAsync` outcome; blocking wait + `AnalyzingProfile` transition + timeout/no-own-account failures |
| `Incoders.Hive.Application/.../ContentGeneration/Background/ClientAestheticEnsureFreshService.cs` | Modified (likely) | Expose whether the enqueued analysis is awaitable / usable-aesthetic-present signal for the gating decision |
| `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IClientAestheticEnsureFreshService.cs` | Modified (possible) | Contract surface for the awaitable / gating signal |
| Job status DTO + status endpoint (`GET /api/social/content-generation-jobs/{jobId}/status`) | Modified | Add `AnalyzingProfile` to the status vocabulary |
| `Incoders.Hive.Tests/.../ContentGeneration/Pipeline/ProcessorEnsureFreshIsolationTests.cs` | Modified | Reconcile: keep non-blocking for stale; assert blocking for no-usable-aesthetic |
| `Incoders.Hive.Tests/.../ContentGeneration/Background/EnsureFreshServiceIsolationTests.cs` | Modified | Same reconciliation of the reversed isolation contract |
| `apps/web/lib/hooks/use-content-generation-job.ts` | Modified | Recognize `AnalyzingProfile` in the polled status |
| `apps/web/components/social/single-analyzer-client.tsx` | Modified | Render "Analyzing profile" phase; block later steps until cleared |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Reverses a **tested isolation contract** (`ProcessorEnsureFreshIsolationTests`, `EnsureFreshServiceIsolationTests` assert never-block / fire-and-forget) | High | Scope the reversal precisely to the no-usable-aesthetic case; keep stale non-blocking; update tests to encode the new two-branch contract, not delete coverage |
| **Waiting on an async job inside the generation job** (analysis job may stall / never signal) | High | Hard timeout bounded by the profile-analysis job budget (decision 1); on timeout → actionable `Failed`, never hang or silently degrade |
| Timeout too tight → false failures; too loose → poor UX | Med | Align ceiling to the analysis job budget (analysis normally runs fast); make it configurable at design |
| Status-vocabulary expansion breaks status consumers expecting the closed set | Med | Additive value only; frontend hook + overlay explicitly handle `AnalyzingProfile`; audit other status consumers at design |
| `SkippedInFlight` with empty aesthetic must still block (in-flight analysis, no usable result yet) | Med | Gate on *usable aesthetic present*, not merely on outcome enum; verify empty-in-flight path at design |

## Success Criteria

- [ ] A first-time `ClientAesthetic` client (no usable aesthetic, own account present) blocks on `AnalyzingProfile`, waits for the analysis, then generates **with** the analyzed aesthetic.
- [ ] The blocking wait is bounded by the profile-analysis job budget; on timeout the generation job ends `Failed` with an actionable message (no silent degradation).
- [ ] A `ClientAesthetic` client with **no own account** fails immediately with an actionable error to configure an own account for this modality.
- [ ] The **aesthetic-present-but-stale** case still generates best-available with a non-blocking background refresh (existing behavior preserved).
- [ ] Job status exposes `AnalyzingProfile`; the frontend overlay shows an "Analyzing profile" phase with later steps blocked/pending until it clears.
- [ ] Isolation tests are reconciled to assert: non-blocking for stale, blocking for no-usable-aesthetic.

## Open Questions for Design/Spec

1. Exact wait mechanism: poll `ProfileAnalysisJob` status vs. await a completion signal/handle.
2. Concrete timeout value and config key tied to the profile-analysis job budget.
3. Precise "usable aesthetic present" predicate for the `SkippedInFlight`/empty edge cases (reuse `LatestAnalyzedHasAestheticAsync` / freshness checks).
4. Whether the gating signal belongs on `EnsureFreshOutcome` or a richer return from `EnsureFreshAsync`.
5. Actionable-error copy and error code for the no-own-account and timeout cases.
