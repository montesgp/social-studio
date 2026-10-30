# ADR 0018 - Content Generator: Real Pipeline

## Status
Accepted — implemented (2026-08-30).

## Context

The Generator screen (`apps/web/components/social/workflow/workflow-client.tsx`) and its backing
`IContentWorkflowService` (`InMemoryContentWorkflowService`) were built as a UI/API scaffold ahead of the
real content-generation pipeline (`CreateContentGenerationBatchCommand` /
`SocialStudio.Application/Features/Social/ContentGeneration`). Three problems followed from shipping the
scaffold unchanged once the real pipeline existed:

1. **Session state lived in a `ConcurrentDictionary`.** It did not survive a process restart, could not be
   filtered by client at the query layer, and had no row-level write isolation for concurrent
   per-candidate status updates.
2. **The screen exposed content that was never generated.** A Preview tab, an "Analysis & Conversion" tab,
   per-card `generateScript`/`generateCarousel`/`generateStory` functions, and a `generate-package` action
   all fabricated plausible-looking output client-side or server-side from static templates — with no
   actor call and no AI call behind them. An operator had no way to tell fabricated content from real
   content.
3. **No path connected discovery to the real generation pipeline.** `AnalyzeSingleContentCommand` and
   `CreateContentGenerationBatchCommand` already existed and worked; nothing on the Generator screen called
   them.

## Decision

### 1. Normalized Postgres persistence replaces the in-memory dictionary
`content_workflow_sessions` / `content_workflow_candidates` / `content_workflow_events` (schema `social`)
behind a new `IContentWorkflowSessionRepository` port. Candidate status is a column, not a value inside a
larger JSON document, because N concurrent per-candidate workers write status independently — a JSON
document would need read-modify-write and risk lost updates under `Parallel.ForEachAsync`.

### 2. Discovery stays a stateless facade; enqueue is a new independent flow
`IContentWorkflowService` (`EfContentWorkflowService` now, `InMemoryContentWorkflowService` deleted) keeps
discovery/idea-draw orchestration unchanged in behavior — same progress-ramp math, same start+poll model —
now writing through the repository. `GetPreviewAsync`, `RunActionAsync`, `GeneratePackageAsync` are
deleted from the interface: nothing calls them once the fabricated UI paths are gone.

### 3. One candidate = one independent analysis-then-batch sequence
`POST /api/social/workflows/{workflowId}/enqueue` accepts N candidates with their per-candidate composed
formats in one call. `IContentWorkflowEnqueueQueue` + `ContentWorkflowEnqueueBackgroundWorker`
(`Parallel.ForEachAsync(MaxConcurrency=3)`, mirroring `SingleAnalysisBackgroundWorker`) fan this out: each
candidate independently runs `AnalyzeSingleContentCommand` → bounded wait → `CreateContentGenerationBatchCommand`.
A per-candidate try/catch means one candidate's failure never blocks or unwinds a sibling's sequence.

### 4. The UI never polls for analysis/generation completion
The enqueue endpoint returns 202 with a per-candidate accepted/rejected result, an `EstimatedMinutes`
heuristic (`ContentWorkflowEtaOptions`: analysis minutes + per-piece minutes × total pieces), and a
`JobsUrl`. The frontend renders exactly that response once and links to
`/content-generation-jobs` for ongoing progress — it does not hold a live connection or poll a new
per-candidate status endpoint. This was the available, already-implemented backend contract; adding a
live-polling status surface was explicitly out of scope for this change (spec: "MUST NOT block the UI with
a synchronous poll").

### 5. Frontend decomposition
`workflow-client.tsx` (~2050 lines, single file) is now a thin container composing:
`panels/configuration-panel.tsx`, `panels/generation-progress-panel.tsx` (both wrapped in the new
`components/ui/collapsible.tsx` primitive — this repo had no `@radix-ui/react-collapsible` dependency, so
it is a small dependency-free controlled/uncontrolled primitive, not a Radix wrapper),
`candidates/candidate-grid.tsx` + `candidate-card.tsx` (discovery-only, no generation actions),
`composition/format-composition-editor.tsx` + `enqueue-summary.tsx` (per-candidate format×count from the
real `ALL_FORMAT_KEYS` catalog), and `history/workflow-history-panel.tsx` (now client-filterable). Pure
logic — the composition reducer and the enqueue-response formatter — lives in
`lib/social/workflow-composition.ts` and `lib/social/workflow-enqueue-summary.ts` specifically so it is
unit-testable with the repo's existing `tsx --test` runner; this repo has no component/DOM test runner, so
the components themselves are verified via `tsc --noEmit`, `next lint`, and `next build` rather than RTL.

## Consequences

- Live in-memory sessions at deploy time are discarded by design (spec: "Cutover discards in-memory
  sessions"). No migration path exists for them.
- `IContentWorkflowService`'s DI registration moved from singleton to scoped: it now depends directly on
  the scoped, EF-`DbContext`-bound repository for its synchronous request-path calls. The background
  worker still resolves its own scope via `IServiceScopeFactory`, unchanged from the existing
  queue+worker pattern used elsewhere in this codebase.
- ~~The Generator screen no longer has a way to show ongoing per-candidate `analyzing` → `batch_started`
  progress inline~~ — **closed**: `GET /api/social/workflows/{workflowId}/history` now returns
  `Candidates: ContentWorkflowCandidateStatusResponse[]` (title, status, failure code) sourced directly from
  `IContentWorkflowSessionRepository.GetDetailAsync`, and `WorkflowHistoryPanel` renders it per candidate
  when a history entry is expanded — durable across reload, not the ephemeral post-enqueue React state.
  Ongoing generation-batch progress itself still lives in `/content-generation-jobs`; this closes only the
  "what is this candidate's enqueue-sequence status" gap.
- The `ai-ideas` discovery origin has no analyzable URL and is rejected at enqueue with an explicit error;
  the Generator screen has no UI control for that origin today, so there was nothing to hide or disable.

## References
- `openspec/changes/generate-social-content-workflow/` describes the original (mocked) scaffold this ADR
  supersedes.
- Domain: `SocialStudio.Domain/Social/Workflow/`. Application: `Features/Social/Workflow/`,
  `Features/Social/Workflow/Enqueue/`, `Features/Social/Workflow/Sequencing/`. Infrastructure:
  `Infrastructure/Social/Workflow/EfContentWorkflowService.cs`,
  `Infrastructure/Persistence/Postgres/Repositories/EfContentWorkflowSessionRepository.cs`. ApiService:
  `BackgroundServices/ContentWorkflowEnqueueBackgroundWorker.cs`,
  `Endpoints/Social/MapSocialContentWorkflowEndpoints.cs`. Frontend:
  `apps/web/components/social/workflow/`, `apps/web/lib/api/content-workflow.ts`,
  `apps/web/lib/social/workflow-composition.ts`, `apps/web/lib/social/workflow-enqueue-summary.ts`,
  `apps/web/lib/social/workflow-candidate-status.ts`.
