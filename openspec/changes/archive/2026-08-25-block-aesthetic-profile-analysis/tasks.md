# Tasks: Block First-Time ClientAesthetic Generation on Profile Analysis (issue #67)

Delivery strategy: `ask-on-risk`. Chain strategy: TBD if chaining is needed (see Review Workload
Forecast). Work units follow `work-unit-commits` — each unit has a clear start/finish/verify
boundary and produces one focused commit. **Strict TDD is ACTIVE**: every work unit that touches
production code MUST start by writing/adjusting the failing test, then implementing until green.

Test runner: `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release`. NOTE:
`dotnet test` is SAC-blocked locally on this machine — write tests first per Strict TDD, verify
locally where possible, and confirm final GREEN via CI dispatch for every backend unit.

Legend: `[P]` = can run in parallel with sibling `[P]` tasks. Units are ordered top to bottom
unless marked `[P]`.

---

## Backend

### WU1 — Contract: `EnsureAvailableOutcome` enum + `EnsureAvailableBlockingAsync`/`LatestUsableAestheticPresentAsync` signatures [P]
**Satisfies:** Design Decision 1 (contract shape) — foundation for BAPA-CG-01, BAPA-CG-02.

- [x] Modify `SocialStudio.Application/Abstractions/Social/ContentGeneration/IClientAestheticEnsureFreshService.cs`:
      add `EnsureAvailableOutcome { Ready, NoOwnAccount, TimedOut, AnalysisFailed }`, add
      `Task<EnsureAvailableOutcome> EnsureAvailableBlockingAsync(Guid orgId, Guid clientId,
      TimeSpan maxWait, Func<CancellationToken, Task>? onWaitStarted, CancellationToken ct)`, add
      `Task<bool> LatestUsableAestheticPresentAsync(Guid orgId, Guid clientId, CancellationToken ct)`.
      Leave `EnsureFreshAsync`/`EnsureFreshOutcome` byte-for-byte unchanged.
- **Verify:** solution compiles; interface-only change has no runtime behavior to test in
  isolation (exercised by WU2 tests).
- **Depends on:** nothing.

### WU2 — Service implementation: blocking wait + usable-aesthetic predicate
**Satisfies:** BAPA-CG-01 (bounded blocking wait: success + timeout scenarios), BAPA-CG-02
(no-own-account immediate failure), Design Decisions 2, 3, 6 (poll mechanism, predicate,
cancellation safety).

- [x] TDD: add new test file `SocialStudio.Tests/.../Background/EnsureAvailableBlockingTests.cs`
      (or sibling suite in `EnsureFreshServiceIsolationTests.cs`) with failing tests for all five
      scenarios from design's Reconciled Test Contract:
      own-account-absent → `NoOwnAccount` (no wait, no enqueue); fake repo flips
      `LatestAnalyzedHasAestheticAsync` true after N polls → `Ready`, `onWaitStarted` fired
      exactly once; repo stays no-aesthetic and `HasNonTerminalAnalysisForClientAsync` goes
      `false` → `AnalysisFailed`; repo stays no-aesthetic + in-flight forever with tiny `maxWait`
      → `TimedOut` (no throw); job `ct` cancelled mid-wait → `OperationCanceledException`
      propagates.
- [x] Implement `LatestUsableAestheticPresentAsync` in
      `SocialStudio.Application/Features/Social/ContentGeneration/Background/ClientAestheticEnsureFreshService.cs`
      delegating to `IProfileAnalysisRepository.LatestAnalyzedHasAestheticAsync`.
- [x] Implement `EnsureAvailableBlockingAsync`: resolve own account (reuse existing resolution) →
      `NoOwnAccount` if absent; ensure an in-flight/enqueued analysis exists (reuse existing
      enqueue path, debounce via `HasNonTerminalAnalysisForClientAsync`); invoke `onWaitStarted`
      exactly once when a real wait begins; poll on `PollInterval` under a linked CTS
      (`CreateLinkedTokenSource(ct)` + `CancelAfter(maxWait)`) evaluating
      `LatestAnalyzedHasAestheticAsync` → `Ready`, non-terminal-gone-with-no-aesthetic →
      `AnalysisFailed`, `maxWait` token fired → `TimedOut` (never throw), job `ct` fired → let
      `OperationCanceledException` propagate. Repo/probe exceptions inside the loop are caught and
      mapped to continued polling or `AnalysisFailed`, never propagate uncaught.
- [x] Confirm `EnsureFreshAsync` body is untouched (diff review, not just tests).
- **Verify:** all 5 new tests GREEN locally if possible, else CI dispatch;
  `EnsureFreshServiceIsolationTests` (existing) still pass unmodified.
- **Depends on:** WU1.

### WU3 — Processor: branch on usable-aesthetic presence, `AnalyzingProfile` transition, outcome→Failed mapping
**Satisfies:** BAPA-CG-01 (status transition + resume-on-Ready), BAPA-CG-02 (immediate Failed,
never enqueues/never reaches AnalyzingProfile), MODIFIED CAG-CG-01 (branch selection + scoped
fire-and-forget), MODIFIED CAG-CG-02 (own-account degrade only when usable-present), MODIFIED
CAG-CG-04 (enqueue stays fire-and-forget; wait is a distinct, scoped concern), REMOVED
CAG-CG-01-c (no more silent Creative degradation on empty aesthetic).

- [x] TDD: reconcile `SocialStudio.Tests/.../Pipeline/ProcessorEnsureFreshIsolationTests.cs` —
      re-scope the existing "throwing `EnsureFreshAsync` service → job not Failed" tests to the
      stale/present branch (fake `LatestUsableAestheticPresentAsync` returns `true`), extend the
      `StatusTracker` fake from last-status-only to a status LIST so `AnalyzingProfile → Running`
      sequences can be asserted, then write new FAILING tests for the blocking branch: usable
      absent + own account present + aesthetic becomes usable → status list contains
      `AnalyzingProfile` then completes; `NoOwnAccount` → `Failed` +
      `aesthetic-own-account-required`; `TimedOut` → `Failed` + `aesthetic-analysis-timeout`;
      `AnalysisFailed` → `Failed` + `aesthetic-analysis-failed`.
- [x] Modify `SocialStudio.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`
      (~253-295): replace the unconditional `EnsureFreshAsync` fire-and-forget with the branch —
      `LatestUsableAestheticPresentAsync` true → unchanged fire-and-forget `EnsureFreshAsync` +
      read profile (existing CAG-CG-04 isolation, now scoped to this branch); false → call
      `EnsureAvailableBlockingAsync(org, client, maxWait, onWaitStarted: c =>
      _jobs.UpdateStatusAsync(job.Id, "AnalyzingProfile", null, null, c), ct)`, switch on outcome:
      `NoOwnAccount`/`TimedOut`/`AnalysisFailed` → terminal `Failed` write with
      `CancellationToken.None` and the matching error code/message from Design Decision 5, return;
      `Ready` → write `Running` and resume, then read profile + build design context exactly as
      today.
- [x] Read `maxWait` from the new config key (see WU4) instead of a hardcoded value.
- **Verify:** all reconciled + new `ProcessorEnsureFreshIsolationTests` GREEN locally if possible,
  else CI dispatch. No regression on non-`ClientAesthetic` policies.
- **Depends on:** WU2.

### WU4 — Config: `ContentGeneration:AestheticGating` options [P]
**Satisfies:** Design Decision 2 config table; Timeout/Budget Alignment invariant
(`MaxWaitSeconds < JobTimeout`).

- [x] Add/modify the content-generation options binding class with
      `BlockOnFirstTime` (default `true`), `PollIntervalSeconds` (default `2`),
      `MaxWaitSeconds` (default `600`).
- [x] Modify `appsettings*.json` to register the new `ContentGeneration:AestheticGating` section
      with the documented defaults.
- [x] Add a code comment or config-doc note (see WU9) that `MaxWaitSeconds` MUST stay strictly
      below `ContentGenerationBackgroundWorker.JobTimeout` (900s).
- **Verify:** options bind correctly (unit test or manual `IOptions<T>` resolution check); no
  runtime behavior change until WU3 consumes the values.
- **Depends on:** nothing.

### WU5 — Watchdog: extend orphan-recovery + stuck-sweep filters to `AnalyzingProfile` [P]
**Satisfies:** Design Decision 4 grounded gotcha (watchdog reconciliation); prevents stranded jobs
in `AnalyzingProfile` on crash/orphan.

- [x] TDD: add a new watchdog test asserting an `AnalyzingProfile` job older than
      `StuckJobThreshold` is force-failed by `FailStuckRunningJobsAsync`, and a stranded
      `AnalyzingProfile` job is caught by `MarkOrphanedRunningJobsAsync` startup recovery.
- [x] Modify `SocialStudio.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobWatchdog.cs`:
      change the `j.Status == "Running"` filter to `j.Status == "Running" || j.Status ==
      "AnalyzingProfile"` in BOTH `MarkOrphanedRunningJobsAsync` and
      `FailStuckRunningJobsAsync`.
- **Verify:** new watchdog test GREEN locally if possible, else CI dispatch; existing watchdog
  tests for `Running` unaffected.
- **Depends on:** nothing (uses the literal status string directly; does not require WU1-WU3 to
  compile or land first).

---

## Frontend

### WU6 — `AnalyzingProfile` status type + label [P]
**Satisfies:** BAPA-STATUS-01 (vocabulary gains `AnalyzingProfile`), BAPA-STATUS-02 (status DTO
round-trips without serialization errors / no fallback-to-unknown).

- [x] Modify `apps/web/lib/api/types.ts`: add `"AnalyzingProfile"` to the
      `ContentGenerationJobStatus` union (~`:880-886`) and to `ContentGenerationJobStatusLabel`
      (~`:737-743`).
- [x] Confirm `TERMINAL_STATUSES` in `apps/web/lib/hooks/use-content-generation-job.ts`
      (`:18-23`) deliberately does NOT include `AnalyzingProfile` (no change needed — poller keeps
      polling through the phase); add a code comment noting this is intentional so a future editor
      doesn't "fix" it.
- **Verify:** TypeScript compiles with the new union member; any exhaustive switch over
  `ContentGenerationJobStatus` elsewhere in the codebase either handles the new member explicitly
  or is grepped and confirmed non-exhaustive/default-safe per BAPA-STATUS-02.
- **Depends on:** nothing.

### WU7 — Overlay: "Analyzing profile" phase with later steps pending
**Satisfies:** BAPA-STATUS-03 (overlay renders distinct phase, later steps blocked, resumes on
status change).

- [x] Modify `apps/web/components/social/single-analyzer-client.tsx`
      (`packageProgressState`, `:169-254`): add an `AnalyzingProfile` branch placed BEFORE the
      `Running` branch — `{ percent: 14, label: "Analyzing profile aesthetic", currentStep: 1,
      completedFormats: 0 }` — with all later `PACKAGE_STEPS` rendered pending/blocked.
- [x] Resolve the design's Open Question: decide whether to add a dedicated
      `PACKAGE_STEPS` entry (`{ key: "analyze", label: "Analyzing profile" }`) before `"prepare"`
      or fold the phase into the existing "Preparing context" step. If a step is added, bump
      `currentStep` indices in the existing branches by one.
- [x] Add a component/unit test asserting the overlay shows the "Analyzing profile" phase when
      status is `AnalyzingProfile`, and transitions out (later steps unblock) when status changes
      to `Running`/`Completed`/`Partial`/`Failed`.
- **Verify:** new overlay/hook unit test GREEN; manual overlay check against a mocked
  `AnalyzingProfile` status.
- **Depends on:** WU6.

### WU8 — User-facing error copy for the three new failure codes [P]
**Satisfies:** Design Decision 5 (actionable failure messages surfaced via `StatusReason`).

- [x] Modify `apps/web/lib/support/user-facing-errors.ts`: add entries for
      `aesthetic-own-account-required`, `aesthetic-analysis-timeout`, `aesthetic-analysis-failed`
      with the copy from the Design Decision 5 table.
- **Verify:** mapping unit test (if the file has existing coverage) or manual check that the three
  codes resolve to non-generic copy.
- **Depends on:** nothing (codes are fixed by design, independent of WU3 landing first).

---

## Docs

### WU9 — Config docs: budget invariant + rollback toggle
**Satisfies:** cross-cutting documentation for BAPA-CG-01/02, Timeout/Budget Alignment.

- [x] Document `ContentGeneration:AestheticGating:{BlockOnFirstTime,PollIntervalSeconds,
      MaxWaitSeconds}` in the relevant config docs, calling out the
      `MaxWaitSeconds < JobTimeout (900s) < StuckJobThreshold (960s)` invariant so a future bump to
      `MaxWaitSeconds` doesn't cross the 15-minute line.
- [x] Note `BlockOnFirstTime=false` as a temporary rollback safety valve (legacy fire-and-forget
      everywhere), not a permanent mode, per design's Residual Risks.
- **Verify:** doc reviewed against the actually-shipped config keys and defaults from WU4.
- **Depends on:** WU4, WU3 (needs final config keys + error codes to document correctly).

---

## Review Workload Forecast

| Metric | Estimate |
|---|---|
| Files touched (backend) | 6 (`IClientAestheticEnsureFreshService.cs`, `ClientAestheticEnsureFreshService.cs`, `ContentGenerationJobProcessor.cs`, `ContentGenerationJobWatchdog.cs`, options binding class, `appsettings*.json`) |
| Files touched (backend tests) | 3 (`EnsureFreshServiceIsolationTests.cs` + new blocking suite, `ProcessorEnsureFreshIsolationTests.cs`, new watchdog test file) |
| Files touched (frontend) | 3 (`types.ts`, `single-analyzer-client.tsx`, `user-facing-errors.ts`) + 1 test file |
| Estimated changed lines, backend (WU1-5) | ~350-450 (new poll loop + predicate ~100-150 lines, new test suite ~150-200 lines, processor branch rewrite ~60-90 lines, watchdog 2-line filter change + ~40 line test, config ~20-30 lines) |
| Estimated changed lines, frontend (WU6-8) | ~90-140 (type union + label ~10 lines, overlay branch + optional step ~40-60 lines, overlay test ~30-40 lines, error copy ~15-20 lines) |
| Estimated changed lines, docs (WU9) | ~20-40 |
| Total estimated changed lines | ~460-630 |
| 400-line budget risk | **High** if delivered as a single PR — backend service+processor+watchdog+tests alone clusters near/above 400 lines before frontend is even counted |
| Chained PRs recommended | **Yes**: PR1 = backend contract + service (WU1, WU2) — self-contained, testable in isolation; PR2 = processor branching + config + watchdog (WU3, WU4, WU5) — depends on PR1; PR3 = frontend status/overlay/errors (WU6, WU7, WU8) — can start in parallel with PR1/PR2 since it only depends on the fixed status string/error codes from the design, not on backend code landing; PR4 (or folded into PR2/PR3) = docs (WU9) |
| Decision needed before apply | **Yes** — confirm PR-splitting/chain strategy (backend-first sequential vs backend/frontend in parallel tracks) before `sdd-apply` starts, per `delivery_strategy = ask-on-risk` |

## Cross-Cutting Notes

- `dotnet test` is SAC-blocked locally on this machine — write every backend test first per
  Strict TDD, run locally where the harness allows, and confirm final GREEN via CI dispatch.
- WU5 (watchdog) and WU4 (config) have no code dependency on WU2/WU3 and can be implemented in
  parallel with the service/processor work; they were marked `[P]` accordingly.
- WU6 and WU8 (frontend type + error copy) have no dependency on backend code landing — they only
  need the fixed status string / error codes already pinned in the design — so the frontend track
  can start immediately in parallel with the backend track.
</content>
