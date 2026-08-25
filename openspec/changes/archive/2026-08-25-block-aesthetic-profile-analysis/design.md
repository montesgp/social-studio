# Design: Block Content Generation on First-Time Aesthetic Profile Analysis (issue #67)

## Technical Approach

Today, for a `ClientAesthetic` client, `ContentGenerationJobProcessor.ProcessInternalAsync`
fire-and-forgets `IClientAestheticEnsureFreshService.EnsureFreshAsync(...)` (wrapped in a
try/catch that swallows everything — CAG-CG-04, `ContentGenerationJobProcessor.cs:257-277`), then
reads the "best-available" profile (`:280-287`) and generates regardless. On a first run the
aesthetic is empty, so generation silently ignores it.

The change introduces a **second, explicitly-blocking entry point** on the same service and keeps
`EnsureFreshAsync` byte-for-byte unchanged. The processor stays the orchestrator: it owns every
`_jobs.UpdateStatusAsync(...)` write (the service is client-scoped and never touches a job id), it
decides which branch to run, and it maps the blocking outcome to job success/failure.

Two branches, chosen by a single predicate — **is a usable analyzed aesthetic present right now?**
(`IProfileAnalysisRepository.LatestAnalyzedHasAestheticAsync`, `IProfileAnalysisRepository.cs:72`):

- **Usable aesthetic present (fresh OR present-but-stale):** unchanged behavior. Call
  `EnsureFreshAsync` fire-and-forget (stale → non-blocking background refresh; fresh → no-op),
  read the profile, generate best-available. This is the CAG-CG-04 isolation contract, now
  scoped precisely to this branch.
- **No usable aesthetic (first-time / empty):** call the new blocking method. If it decides a wait
  is actually needed, the processor transitions the job to `AnalyzingProfile`, the method polls the
  profile-analysis repository until the just-enqueued/in-flight `ProfileAnalysisJob` yields a usable
  aesthetic (or fails, or times out), then generation proceeds *with* the analyzed aesthetic. No own
  account → immediate actionable failure. Timeout / analysis failure → actionable `Failed`, never
  silent degradation.

No new infrastructure, no new queue, no change to `ProfileAnalysisJob` or its worker. The blocking
wait is a bounded poll over repository predicates the service already depends on.

## Resolved Decisions

### Decision 1 — Where the gating + blocking wait lives

**Choice:** Add ONE new method to `IClientAestheticEnsureFreshService`:

```csharp
Task<EnsureAvailableOutcome> EnsureAvailableBlockingAsync(
    Guid orgId,
    Guid clientId,
    TimeSpan maxWait,
    Func<CancellationToken, Task>? onWaitStarted,   // invoked once, iff a real wait begins
    CancellationToken ct);
```

returning a new, distinct enum `EnsureAvailableOutcome { Ready, NoOwnAccount, TimedOut,
AnalysisFailed }`. `EnsureFreshAsync` and `EnsureFreshOutcome` are untouched.

The service is the right home: it already owns every collaborator the wait needs — the freshness
probes (`GetLatestAnalyzedCompletedAtAsync`, `LatestAnalyzedHasAestheticAsync`), the in-flight
debounce (`HasNonTerminalAnalysisForClientAsync`), own-account resolution
(`IClientOwnAccountRepository`), and the enqueue path (`IProfileAnalysisJobQueue`). Putting the poll
loop next to them keeps cohesion high and the processor thin.

The `onWaitStarted` callback is how the service (which must stay job-agnostic) lets the
**processor** write the `AnalyzingProfile` status **exactly when** a blocking wait begins — and
never when the aesthetic was already usable. This avoids status churn for the common case with no
extra pre-probe round-trip.

| Option | Tradeoff | Verdict |
|---|---|---|
| New `EnsureAvailableBlockingAsync` + `onWaitStarted` callback | Single method, service owns all probes, status written exactly when the wait starts, `EnsureFreshAsync` contract preserved | **Chosen** |
| Poll loop inline in the processor | Leaks repo-probe details + own-account resolution into the pipeline orchestrator, duplicates logic, far harder to unit-test | Rejected |
| Overload `EnsureFreshAsync` with a `block` flag | Muddies the documented "NEVER awaits / NEVER blocks" contract that other callers and `EnsureFreshServiceIsolationTests` rely on | Rejected |
| Return a richer `ReadyImmediately` vs `ReadyAfterWait` outcome, processor sets status after | Status can only be written AFTER the wait — the `AnalyzingProfile` window would never be visible to the poller | Rejected |

Processor control flow (replaces `ContentGenerationJobProcessor.cs:256-277`):

```
if policy == ClientAesthetic and _aestheticEnsureFreshService is not null:
    if await svc.LatestUsableAestheticPresentAsync(org, client, ct):   // present → stale branch
        try { await svc.EnsureFreshAsync(org, client, ct); }           // fire-and-forget (CAG-CG-04)
        catch (Exception ex) { log; }                                  // never fails the job
    else:                                                              // no usable aesthetic → block
        var outcome = await svc.EnsureAvailableBlockingAsync(
            org, client, maxWait,
            onWaitStarted: c => _jobs.UpdateStatusAsync(job.Id, "AnalyzingProfile", null, null, c),
            ct);
        switch (outcome):
            NoOwnAccount   -> UpdateStatus(Failed, ownAccountMsg,  "aesthetic-own-account-required"); return
            TimedOut       -> UpdateStatus(Failed, timeoutMsg,     "aesthetic-analysis-timeout");     return
            AnalysisFailed -> UpdateStatus(Failed, analysisMsg,    "aesthetic-analysis-failed");      return
            Ready          -> await _jobs.UpdateStatusAsync(job.Id, "Running", null, null, ct);       // resume
    // then read profile + build design context exactly as today (:280-315)
```

The predicate `LatestUsableAestheticPresentAsync` is a thin service method delegating to
`_analysisRepository.LatestAnalyzedHasAestheticAsync` (keeps the predicate owned by the service, not
the pipeline — see Decision 3). It replaces the raw repo call so the service stays the single
authority on "usable".

### Decision 2 — Wait mechanism: bounded poll over repository predicates

**Choice:** Poll, no completion-signal handle. `EnsureAvailableBlockingAsync`:

1. Resolve own account (reuse the existing resolution in `EnsureFreshService.cs:116-126`). None →
   return `NoOwnAccount` (no wait, `onWaitStarted` never fired).
2. Ensure an analysis exists to wait on: if `HasNonTerminalAnalysisForClientAsync` is false, run the
   enqueue path (`EnsureFreshService.cs:128-158`) to create + enqueue a `ProfileAnalysisJob`. If it
   is already in flight (debounce), wait on that one.
3. Invoke `onWaitStarted` once (processor writes `AnalyzingProfile`).
4. Poll every `PollInterval` under a linked CTS (`CreateLinkedTokenSource(ct)` + `CancelAfter(maxWait)`):
   - `LatestAnalyzedHasAestheticAsync == true` → `Ready`.
   - else if `HasNonTerminalAnalysisForClientAsync == false` (nothing in flight) AND still no
     aesthetic → the analysis reached a terminal state without producing a usable aesthetic →
     `AnalysisFailed`.
   - else keep polling.
   - linked token fired by `maxWait` (not by job `ct`) → `TimedOut`.

| Parameter | Value | Config key |
|---|---|---|
| Poll interval | 2s default | `ContentGeneration:AestheticGating:PollIntervalSeconds` |
| Max wait ceiling | 600s (10 min) default | `ContentGeneration:AestheticGating:MaxWaitSeconds` |
| Feature toggle | on | `ContentGeneration:AestheticGating:BlockOnFirstTime` (off → legacy fire-and-forget everywhere, rollback-safe) |

**Timeout source (grounded correction):** the proposal assumed a "profile-analysis job budget". In
the real code the `ProfileAnalysisBackgroundWorker` has **no per-job timeout** — it runs under
`stoppingToken` only (`ProfileAnalysisBackgroundWorker.cs:40-79`). The binding ceiling is therefore
the **content-generation job budget**: `ContentGenerationBackgroundWorker.JobTimeout =
TimeSpan.FromMinutes(15)` (`ContentGenerationBackgroundWorker.cs:31`, enforced via
`jobCts.CancelAfter(JobTimeout)` at `:102`) plus the `StuckJobThreshold = 16 min` watchdog
(`ContentGenerationJobWatchdog.cs:19`). `MaxWaitSeconds` MUST stay **strictly below 15 min** so the
gating wait always resolves to a clean actionable `Failed` write BEFORE the outer `CancelAfter`
fires. 10 min default leaves ~5 min headroom for the rest of the pipeline. See Timeout/Budget
Alignment below.

### Decision 3 — "Usable aesthetic" predicate

**Choice:** Key on **aesthetic presence**, not the outcome enum. Usable ==
`IProfileAnalysisRepository.LatestAnalyzedHasAestheticAsync(org, client) == true` (the latest
`Analyzed` analysis carries a non-null `aesthetic_profile_json` — `IProfileAnalysisRepository.cs:66-75`).

This is exactly why the outcome enum is insufficient: `EnsureFreshOutcome.SkippedInFlight`
(`EnsureFreshService.cs:107-113`) is returned whenever ANY non-terminal analysis exists — including
a first-ever analysis that has produced no aesthetic yet. Branching on the enum would let an
empty-in-flight client skip the block. Branching on `LatestAnalyzedHasAestheticAsync` makes the
empty-in-flight case correctly resolve to "not usable → wait". The service exposes this through the
new `LatestUsableAestheticPresentAsync` wrapper so the processor never touches the repo directly.

### Decision 4 — Status contract: `AnalyzingProfile`

**Choice:** Add `AnalyzingProfile` as a **non-terminal** status between `Running` and the terminal
states. Job statuses are free-form strings on the wire (`ContentGenerationJobStatusQuery.cs:20-33`
passes `job.Status` straight through), so the backend needs no enum change — the processor simply
writes the new string.

Frontend:
- Add `"AnalyzingProfile"` to the `ContentGenerationJobStatus` union (`apps/web/lib/api/types.ts:880-886`)
  and to `ContentGenerationJobStatusLabel` (`:737-743`).
- It is deliberately **absent** from `TERMINAL_STATUSES` (`use-content-generation-job.ts:18-23`) so
  the poller keeps polling through the analysis phase — no hook change needed beyond the type.
- Overlay `packageProgressState` (`single-analyzer-client.tsx:169-254`) gets an `AnalyzingProfile`
  branch placed BEFORE the `Running` branch: `{ percent: 14, label: "Analyzing profile aesthetic",
  currentStep: 1, completedFormats: 0 }`, with all later `PACKAGE_STEPS` (`:141-147`) rendered
  pending/blocked. Optionally insert a dedicated step `{ key: "analyze", label: "Analyzing profile" }`
  before `"prepare"`; if steps are added, bump the `currentStep` indices in the existing branches by
  one.

**Watchdog reconciliation (grounded gotcha):** `ContentGenerationJobWatchdog` filters strictly on
`j.Status == "Running"` in BOTH `MarkOrphanedRunningJobsAsync` (startup recovery,
`ContentGenerationJobWatchdog.cs:32`) and `FailStuckRunningJobsAsync` (periodic sweep, `:54`). A job
parked in `AnalyzingProfile` would be invisible to both. In-process this self-heals (the outer
`CancelAfter(15min)` fires → `OperationCanceledException` → terminal `Failed` via the `:164-169`
handler), but a hard crash mid-`AnalyzingProfile` would strand the job. **Extend both filters to
`Status == "Running" || Status == "AnalyzingProfile"`** so orphan recovery and the stuck-sweep cover
the new state. Additive, low-risk.

### Decision 5 — Failure messages and error codes

Follow the existing `code`/`reason` shape (e.g. `"client-required"` at
`ContentGenerationJobProcessor.cs:213`, `"analysis-not-completed"` at `:191`,
`"no-renderable-assets"` at `:1050`). All three are terminal `Failed` writes:

| Outcome | Error code | Status reason (actionable) |
|---|---|---|
| `NoOwnAccount` | `aesthetic-own-account-required` | "This client uses the Client Aesthetic modality but has no own account configured. Add the client's own Instagram account to generate in this modality." |
| `TimedOut` | `aesthetic-analysis-timeout` | "Profile aesthetic analysis did not finish in time. Please try again in a few minutes." |
| `AnalysisFailed` | `aesthetic-analysis-failed` | "Profile aesthetic analysis could not be completed for this client. Check the client's own account and try again." |

These flow through `StatusReason` on `ContentGenerationJobStatusResponse` (`:29`) to the overlay's
existing `statusReason` rendering path. Add matching entries to
`apps/web/lib/support/user-facing-errors.ts` keyed by the three codes.

### Decision 6 — Cancellation / timeout safety

Mirror the terminal-write-with-`CancellationToken.None` pattern
(`ContentGenerationJobProcessor.cs:164-179`) so the job can never get stuck in `AnalyzingProfile`
or `Running`:

- The poll uses a linked CTS: `CancellationTokenSource.CreateLinkedTokenSource(ct)` then
  `CancelAfter(maxWait)`. The method distinguishes the two cancel sources:
  - job `ct` cancelled (real per-job timeout / shutdown) → let `OperationCanceledException`
    propagate; the processor's outer `catch (OperationCanceledException) when (ct.IsCancellationRequested)`
    writes `Failed` with `CancellationToken.None` (`:164-169`).
  - only the `maxWait` linked token fired → **do not throw**; return `TimedOut`.
- The three gating-failure `UpdateStatusAsync(..., "Failed", ...)` writes in the processor use
  `CancellationToken.None`, matching the incident-hardened pattern, so the `AnalyzingProfile →
  Failed` transition always lands even if `ct` is later faulted.
- `EnsureAvailableBlockingAsync` NEVER throws for non-cancellation reasons — repo/probe exceptions
  inside the loop are caught and mapped (best-effort) to continued polling or `AnalysisFailed`,
  keeping the "service never throws into the pipeline" spirit while allowing a bounded, decidable
  wait.

## Architecture / Component Design

```
ContentGenerationBackgroundWorker (JobTimeout 15m, CancelAfter)   ← unchanged, outer ceiling
        │
        ▼
ContentGenerationJobProcessor.ProcessInternalAsync                ← orchestrator; owns status writes
   policy == ClientAesthetic?
     ├─ usable present  → EnsureFreshAsync (fire-and-forget, CAG-CG-04)   → generate best-available
     └─ no usable       → onWaitStarted:UpdateStatus("AnalyzingProfile")
                          EnsureAvailableBlockingAsync(maxWait) ──────────┐
                            Ready→resume | NoOwnAccount/TimedOut/AnalysisFailed→Failed
        │                                                                 │
        ▼                                                                 ▼
IClientAestheticEnsureFreshService                        IProfileAnalysisRepository (probes)
   EnsureFreshAsync           (unchanged)                    LatestAnalyzedHasAestheticAsync
   LatestUsableAestheticPresentAsync (new, thin)            HasNonTerminalAnalysisForClientAsync
   EnsureAvailableBlockingAsync (new)                     IProfileAnalysisJobQueue (enqueue)
                                                          IClientOwnAccountRepository
                                                                 │ enqueue
                                                                 ▼
                                                ProfileAnalysisBackgroundWorker (no per-job timeout)
```

Dependency direction is preserved: the new method lives in Application, depends only on existing
Application ports, no Infrastructure/provider types leak (matches the file's header contract,
`ClientAestheticEnsureFreshService.cs:27-29`).

## Reconciled Test Contract

The reversal is scoped precisely; coverage is re-encoded, not deleted.

**`EnsureFreshServiceIsolationTests`** (`.../Background/EnsureFreshServiceIsolationTests.cs`): keep
as-is. It asserts `EnsureFreshAsync` never propagates (`:29-62`) — that contract is unchanged. ADD a
sibling suite (or new file `EnsureAvailableBlockingTests`) for the new method:
- own account absent → `NoOwnAccount`, no wait, no enqueue.
- fake repo whose `LatestAnalyzedHasAestheticAsync` flips `true` after N polls → `Ready`,
  `onWaitStarted` fired exactly once.
- repo stays no-aesthetic, `HasNonTerminalAnalysisForClientAsync` goes `false` → `AnalysisFailed`.
- repo stays no-aesthetic and in-flight forever with `maxWait` tiny → `TimedOut` (no throw).
- job `ct` cancelled mid-wait → `OperationCanceledException` propagates.

**`ProcessorEnsureFreshIsolationTests`** (`.../Pipeline/ProcessorEnsureFreshIsolationTests.cs`):
today its brand is `ClientAesthetic` with a profile reader returning `null` (`:39`, `:91`) — under
the new contract that is the *blocking* branch, so the existing "throwing service → job not Failed"
tests (`:35-84`) must be re-scoped to the **stale/present branch**. Reconcile by giving those tests
a fake service where `LatestUsableAestheticPresentAsync` returns `true` (usable present) so
`EnsureFreshAsync` is the code path exercised — a throwing `EnsureFreshAsync` there must STILL not
fail the job, and `GetProfileAsync` must still be called. ADD new processor tests for the blocking
branch (usable absent):
- own account present, aesthetic becomes usable → job transitions `AnalyzingProfile` then completes
  (assert `AnalyzingProfile` was written via the `StatusTracker` seam, `:216-220`).
- `NoOwnAccount` → `Failed` + `aesthetic-own-account-required`.
- `TimedOut` → `Failed` + `aesthetic-analysis-timeout`.
- `AnalysisFailed` → `Failed` + `aesthetic-analysis-failed`.

The `StatusTracker` fake (`:216-271`) only records the LAST status; extend it to a status LIST to
assert the `AnalyzingProfile → Running/Failed` sequence.

**New**: `ContentGenerationJobWatchdog` test asserting an `AnalyzingProfile` job older than
`StuckJobThreshold` is force-failed, and orphan recovery fails a stranded `AnalyzingProfile` job.

Strict TDD: write each of the above as a failing test first, then implement.

## Timeout / Budget Alignment

```
0 ─────────────── AnalyzingProfile (poll @2s) ─────────── 10m ─────────── 15m ─────── 16m
                  MaxWaitSeconds=600 → TimedOut→Failed      │               │           │
                                                            └ headroom      └ JobTimeout └ Watchdog
                                                              for gen          CancelAfter  StuckJobThreshold
```

- `MaxWaitSeconds` (600) < `JobTimeout` (900) < `StuckJobThreshold` (960). The gating wait can never
  outlive the job budget; a timeout always produces an actionable `Failed`, never a hang.
- Poll interval 2s matches the existing frontend poll cadence (`use-content-generation-job.ts:11`),
  so overlay updates stay responsive during the wait.
- All three values are config-overridable; the ceiling MUST remain `< JobTimeout` — call this out in
  the config docs so a future bump to `MaxWaitSeconds` doesn't cross the 15-min line.

## Failure Modes

| Failure | Detection | Outcome |
|---|---|---|
| No own account for `ClientAesthetic` | own-account resolution empty (`EnsureFreshService.cs:119-126`) | Immediate `Failed` / `aesthetic-own-account-required`, no wait |
| Analysis stalls / never yields aesthetic | `maxWait` linked token fires | `TimedOut` → `Failed` / `aesthetic-analysis-timeout` |
| Analysis reaches terminal state with no aesthetic | `LatestAnalyzedHasAestheticAsync` false AND `HasNonTerminalAnalysisForClientAsync` false | `AnalysisFailed` → `Failed` / `aesthetic-analysis-failed` |
| Enqueue throws | caught inside the enqueue path (existing `:151-158`) | falls through to poll; resolves to `AnalysisFailed`/`TimedOut` |
| Real per-job timeout / host shutdown | job `ct` cancelled | `OperationCanceledException` → outer handler `Failed` w/ `CancellationToken.None` |
| Process crash mid-`AnalyzingProfile` | startup orphan sweep (extended filter) | `Failed` / `restart-detected` |
| Feature toggle off | `BlockOnFirstTime=false` | legacy fire-and-forget everywhere (rollback) |

## Affected Files

| File | Action | Purpose |
|---|---|---|
| `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IClientAestheticEnsureFreshService.cs` | Modify | Add `EnsureAvailableBlockingAsync` + `LatestUsableAestheticPresentAsync`; add `EnsureAvailableOutcome` enum |
| `Incoders.Hive.Application/Features/Social/ContentGeneration/Background/ClientAestheticEnsureFreshService.cs` | Modify | Implement blocking poll + presence predicate; reuse own-account + enqueue paths; `EnsureFreshAsync` unchanged |
| `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` (~253-295) | Modify | Branch on presence; `AnalyzingProfile` transition via `onWaitStarted`; map outcomes to terminal `Failed`; read config |
| `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobWatchdog.cs` | Modify | Include `AnalyzingProfile` in orphan + stuck-sweep filters |
| Content-generation options/config binding + `appsettings*.json` | Modify | `ContentGeneration:AestheticGating:{BlockOnFirstTime,PollIntervalSeconds,MaxWaitSeconds}` |
| `apps/web/lib/api/types.ts` (`:737`, `:880`) | Modify | Add `AnalyzingProfile` to both status unions |
| `apps/web/components/social/single-analyzer-client.tsx` (`:141-254`) | Modify | `AnalyzingProfile` branch in `packageProgressState`; optional new `PACKAGE_STEPS` entry |
| `apps/web/lib/support/user-facing-errors.ts` | Modify | Map the three new error codes to user copy |
| `Incoders.Hive.Tests/.../Background/EnsureFreshServiceIsolationTests.cs` (+ new blocking suite) | Modify/Add | Blocking-outcome tests; keep `EnsureFreshAsync` isolation |
| `Incoders.Hive.Tests/.../Pipeline/ProcessorEnsureFreshIsolationTests.cs` | Modify | Re-scope throwing tests to stale branch; add blocking-branch tests; `StatusTracker` → status list |
| `Incoders.Hive.Tests/.../Pipeline/` watchdog test | Add | `AnalyzingProfile` stuck + orphan recovery |

## Verification Strategy

Note: `dotnet test` is SAC-blocked locally — verify via CI dispatch (per project memory).

| Layer | What | How |
|---|---|---|
| Service | four outcomes + `onWaitStarted`-once + no-throw-on-timeout | new xUnit suite with fake analysis repo (aesthetic flips after N polls / never) |
| Processor | branch selection; `AnalyzingProfile` written; outcome→status/code mapping | `ProcessorEnsureFreshIsolationTests` with status-list tracker |
| Watchdog | `AnalyzingProfile` stuck + orphan recovery force-fail | watchdog unit test |
| Frontend | `AnalyzingProfile` renders as its own phase; later steps blocked; polling continues | overlay/hook unit test + manual overlay check |
| Budget | `MaxWaitSeconds < JobTimeout` invariant | review + config doc assertion |

## Residual Risks

- **No native analysis budget.** The blocking ceiling is derived from the content-generation job
  budget, not the analysis worker (which has none). If analysis latency regresses past ~10 min,
  first-time users hit `TimedOut` more often. Mitigated by config override + fast-analysis
  expectation; a future per-job analysis budget would let the two align natively.
- **Watchdog filter must be kept in sync.** Any new non-terminal status added later must be added to
  the watchdog filters too, or jobs can strand. Documented at the filter site.
- **`AnalyzingProfile` consumers.** Additive status; the poller already treats unknown non-terminal
  statuses as "keep polling". Audit any other status consumer (grid summaries, history) before ship.
- **Toggle drift.** `BlockOnFirstTime=false` restores legacy degradation — safe rollback, but leaves
  the silent-degradation bug latent; treat the toggle as a temporary safety valve, not a permanent
  mode.

## Open Questions

- [ ] Confirm whether to add a dedicated `PACKAGE_STEPS` entry for the analysis phase (extra visible
  step) or fold it into the existing "Preparing context" step — UX call, not a design blocker.
</content>
</invoke>
