# ADR 0011 - Block First-Time ClientAesthetic Generation on Profile Analysis

## Status
Accepted

## Context

ADR 0006 introduced `IClientAestheticEnsureFreshService.EnsureFreshAsync` as a **non-blocking**
fire-and-forget refresh: `ContentGenerationJobProcessor` calls it, swallows any exception, then
reads the "best-available" `ClientAestheticProfile` and generates regardless. This is correct for
the fresh/stale-but-present case, but on a **first-time** `ClientAesthetic` client (no analysis
has ever completed, or the latest analysis carries no aesthetic payload) the profile read returns
empty and generation silently proceeds without any aesthetic guidance — the exact bug tracked by
issue #67.

## Decision

### 1. New blocking entry point, `EnsureFreshAsync` unchanged

`IClientAestheticEnsureFreshService` gains two members; `EnsureFreshAsync` and
`EnsureFreshOutcome` are untouched (byte-for-byte):

```
Task<bool> LatestUsableAestheticPresentAsync(orgId, clientId, ct)

Task<EnsureAvailableOutcome> EnsureAvailableBlockingAsync(
    orgId, clientId, maxWait, onWaitStarted, ct)
```

`EnsureAvailableOutcome` is a new, distinct enum: `Ready`, `NoOwnAccount`, `TimedOut`,
`AnalysisFailed`. Unlike `EnsureFreshOutcome` (logging-only, callers MUST NOT branch on it), the
processor DOES branch on `EnsureAvailableOutcome`.

`ContentGenerationJobProcessor` branches on `LatestUsableAestheticPresentAsync` (delegates to
`IProfileAnalysisRepository.LatestAnalyzedHasAestheticAsync` — presence, not the debounce enum,
because `EnsureFreshOutcome.SkippedInFlight` also covers a first-ever in-flight analysis with no
aesthetic yet):

- **Present** (fresh or stale-but-present): unchanged `EnsureFreshAsync` fire-and-forget branch.
- **Absent** (first-time/empty): the new blocking branch. The job transitions to a new non-terminal
  status, `AnalyzingProfile`, and `EnsureAvailableBlockingAsync` polls
  `IProfileAnalysisRepository.LatestAnalyzedHasAestheticAsync` until the just-enqueued/in-flight
  `ProfileAnalysisJob` yields a usable aesthetic, fails terminally, or the bounded wait elapses.

### 2. Config: `ContentGeneration:AestheticGating`

New options class `ContentGenerationAestheticGatingOptions` (Application layer, bound via
`IOptions<T>` in Infrastructure DI):

| Key | Default | Purpose |
|---|---|---|
| `ContentGeneration:AestheticGating:BlockOnFirstTime` | `true` | Feature toggle. `false` restores legacy fire-and-forget everywhere — a **temporary rollback safety valve**, not a permanent mode. It leaves the pre-#67 silent-degradation bug latent, so treat it as an emergency escape hatch only. |
| `ContentGeneration:AestheticGating:PollIntervalSeconds` | `2` | Interval between polls of the analysis repository while waiting. Matches the frontend's existing poll cadence (`use-content-generation-job.ts`). |
| `ContentGeneration:AestheticGating:MaxWaitSeconds` | `600` (10 min) | Bounded ceiling for the blocking wait. |

**Budget invariant (MUST hold):**

```
MaxWaitSeconds (600) < ContentGenerationBackgroundWorker.JobTimeout (900s / 15min)
                     < ContentGenerationJobWatchdog.StuckJobThreshold (960s / 16min)
```

The profile-analysis worker (`ProfileAnalysisBackgroundWorker`) has **no per-job timeout** of its
own — it only stops on host shutdown. The binding ceiling for the gating wait is therefore the
**content-generation job budget**, not the analysis worker. `MaxWaitSeconds` MUST stay strictly
below `JobTimeout` so the gating wait always resolves to a clean, actionable `Failed` write before
the outer per-job `CancelAfter` fires — a future bump to `MaxWaitSeconds` MUST NOT cross the
15-minute line, or the timeout path degrades into an unhandled cancellation instead of an
actionable failure.

### 3. Status contract: `AnalyzingProfile`

Job statuses are free-form strings on the wire (no backend enum). `AnalyzingProfile` is added as a
non-terminal status between `Running` and the terminal states:

- `ContentGenerationJobWatchdog` — both `MarkOrphanedRunningJobsAsync` (startup orphan recovery)
  and `FailStuckRunningJobsAsync` (periodic stuck-sweep) filter on
  `Status == "Running" || Status == "AnalyzingProfile"` so a crash mid-wait cannot strand a job.
  **Any future non-terminal status MUST be added to both filters or jobs can strand** — this is
  the single spot to check when adding a new in-progress status.
- Frontend: `AnalyzingProfile` added to both `ContentGenerationJobStatus` /
  `ContentGenerationJobStatusLabel` unions (`apps/web/lib/api/types.ts`). It is deliberately
  **absent** from `TERMINAL_STATUSES` (`use-content-generation-job.ts`) so the poller keeps
  polling through it. The overlay (`packageProgressState`, extracted to
  `apps/web/lib/single-analyzer-ui-state.ts`) renders a distinct "Analyzing profile aesthetic"
  phase, folded into the existing "Preparing context" step index so no other step numbering shifts.

### 4. Failure codes

Three terminal `Failed` outcomes, each with a code and an actionable message (mapped in
`apps/web/lib/support/user-facing-errors.ts`):

| Outcome | Code | Message |
|---|---|---|
| `NoOwnAccount` | `aesthetic-own-account-required` | This client uses the Client Aesthetic modality but has no own account configured. Add the client's own Instagram account to generate in this modality. |
| `TimedOut` | `aesthetic-analysis-timeout` | Profile aesthetic analysis did not finish in time. Please try again in a few minutes. |
| `AnalysisFailed` | `aesthetic-analysis-failed` | Profile aesthetic analysis could not be completed for this client. Check the client's own account and try again. |

`ContentGenerationJobStatusResponse` does not expose a separate `ErrorCode` field to the frontend
— only `StatusReason` (free text) round-trips. The processor therefore prefixes the `StatusReason`
text with the code (`"aesthetic-own-account-required: <message>"`), mirroring the existing
`render_failed[...]` embedded-code convention, so `resolveGenerationFailureError` can match it by
substring.

### 5. Cancellation safety

`EnsureAvailableBlockingAsync` polls under a linked `CancellationTokenSource`
(`CreateLinkedTokenSource(ct)` + `CancelAfter(maxWait)`) and distinguishes the two cancel sources:
job `ct` cancelled (real per-job timeout / shutdown) → `OperationCanceledException` propagates, so
the processor's existing outer handler records the cancellation-specific terminal status; only the
`maxWait` ceiling firing → returns `TimedOut`, never throws. The method never throws for
non-cancellation reasons — repo/probe exceptions inside the poll loop are caught, logged, and
resolve to continued polling or `AnalysisFailed`.

## Consequences

- First-time `ClientAesthetic` clients get their analyzed aesthetic or an actionable failure —
  never a silent Creative degradation or generation with an empty aesthetic.
- The pre-existing stale-but-present fire-and-forget contract (ADR 0006) is fully preserved;
  `EnsureFreshAsync` is untouched.
- A crash mid-`AnalyzingProfile` self-heals via the watchdog, same as a crash mid-`Running`.
- `BlockOnFirstTime=false` is available as a rollback valve but is NOT a supported permanent mode.
- Any future non-terminal content-generation status must be added to both
  `ContentGenerationJobWatchdog` filters, or jobs in that status can strand on a crash.

## Related

- Proposal / Spec / Design: `openspec/changes/block-aesthetic-profile-analysis/`
- Builds on: ADR 0006 (Client Aesthetic Ensure-Fresh Port)
- Issue: #67
