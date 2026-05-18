# Tasks: Generate Social Content Workflow

## 1. Explore and validate scope
- Confirm bounded contexts and impacted modules for the content generation workflow.
- Identify provider abstractions needed per workflow stage: content retrieval (multi-network), AI analysis, package generation.
- Validate multi-network aggregation strategy (parallel vs sequential retrieval).
- Review wizard/stepper UX requirements and full-width layout constraints.

## 2. Define domain model for workflow session and state
- Define `WorkflowSession` aggregate with client, search criteria, strategy mode, selected networks, current step, history.
- Define `WorkflowStep` value object with states: Pending, InProgress, Completed, Failed, PartiallyCompleted.
- Define `ContentSearchCriteria` enum/value object: Niche, References, Viral, Competitors.
- Define `ContentStrategyMode` enum/value object: Mixed, Educational, Informative, Entertainment.
- Define `NetworkSource` enum/value object: TikTok, Instagram, YouTube, etc.
- Define `ContentResult`, `ContentSelection`, `ContentPackage` entities with metadata.
- Add domain events: `WorkflowStepProgressed`, `WorkflowStepCompleted`, `WorkflowStepFailed`, `ContentSelected`, `PackageGenerated`.

## 3. Define application ports and use cases
- Define `IContentResearchProvider` port for single-network content retrieval (abstraction over TikTok API, Apify, Data365).
- Define `IMultiNetworkContentAggregator` port for orchestrating multi-network retrieval and result merging.
- Define `IAiContentAnalyzer` port for video analysis and script generation (preview tabs).
- Define `IContentPackageGenerator` port for simulated ZIP package generation (MVP).
- Define `IWorkflowStateRepository` port for session persistence with history/audit trail.
- Implement use cases:
  - `InitializeWorkflowSessionUseCase`: Create session with client selection.
  - `ConfigureSearchCriteriaUseCase`: Set criteria, strategy, networks.
  - `TriggerContentGenerationUseCase`: Start multi-network retrieval with progress tracking.
  - `GetWorkflowStatusUseCase`: Poll current step, progress, provider activity messages.
  - `GetContentResultsUseCase`: Retrieve search results for card list display.
  - `GetPaginatedContentResultsUseCase`: Retrieve paginated results with metadata.
  - `GetContentPreviewUseCase`: Get video analysis + script for selected content.
  - `SelectContentUseCase`: Record user content selections.
  - `ExecuteContentActionUseCase`: Trigger content-level action (`download_audio`, `transcribe_and_build_script`, `build_carousel`).
  - `ExecuteBulkContentActionUseCase`: Trigger an action over multiple selected content items.
  - `GenerateContentPackageUseCase`: Create simulated ZIP package.
  - `GetWorkflowHistoryUseCase`: Retrieve past sessions for resume.
  - `ReplaceSelectionAndRegenerateUseCase`: Update selections and regenerate package.

## 4. Define API contracts for workflow endpoints
- `POST /api/workflows/content-generation`: Initialize session with client, criteria, strategy, networks.
- `GET /api/workflows/{id}/status`: Poll current step, progress percentage, provider activity messages.
- `GET /api/workflows/{id}/results?page={n}&pageSize={m}`: Retrieve paginated search results (cards list + pagination metadata).
- `GET /api/workflows/{id}/results/{resultId}/preview`: Get preview data (video analysis + script tabs).
- `POST /api/workflows/{id}/results/{resultId}/actions`: Trigger action for selected content item.
- `POST /api/workflows/{id}/actions/bulk`: Trigger action over selected content items.
- `POST /api/workflows/{id}/select`: Submit content selection.
- `POST /api/workflows/{id}/generate-package`: Trigger simulated package generation.
- `GET /api/workflows/{id}/history`: Retrieve workflow history for resume.
- `PUT /api/workflows/{id}/replace-selection`: Replace rejected items and regenerate.
- Define request/response DTOs with validation rules.
- Include pagination response envelope (`items`, `page`, `pageSize`, `totalItems`, `totalPages`, `hasNextPage`, `hasPreviousPage`).
- Include action contracts with extensible action enum and execution status (`queued`, `running`, `completed`, `failed`).
- Add OpenAPI/Swagger annotations for frontend contract sync.

## 5. Implement infrastructure adapters for content providers
- Implement `TikTokContentAdapter` (adapter for TikTok API) implementing `IContentResearchProvider`.
- Implement `ApifyInstagramAdapter` (adapter for Instagram scraping via Apify) implementing `IContentResearchProvider`.
- Implement `Data365Adapter` (adapter for Data365 trends) implementing `IContentResearchProvider` (if applicable).
- Implement `MultiNetworkContentAggregator` orchestrating parallel/sequential provider calls and merging results.
- Ensure adapters emit progress events (`ProviderStarted`, `ProviderInProgress`, `ProviderCompleted`, `ProviderFailed`).
- Map provider-specific status to generic workflow step messages in application layer.
- Store provider metadata (model, prompt version, provider name) without coupling domain to SDKs.

## 6. Implement AI analysis and package generation adapters
- Implement `AiContentAnalyzerAdapter` calling configured AI provider for video analysis and script generation.
- Implement `SimulatedPackageGeneratorAdapter` creating ZIP package structure with metadata and content references (no actual video files in MVP).
- Ensure AI analysis outputs include tabs data: "Video Analysis" (hook, format, topic, angle, CTA, visual pattern) and "Prebuilt Script to Replicate Style".

## 7. Implement backend workflow orchestration and persistence
- Implement workflow session persistence in Supabase with `organization_id` scope.
- Add audit trail for selections, replacements, and package generations with timestamps and user tracking.
- Implement workflow step state machine with explicit failure handling and retry support.
- Add workflow history queries for resume functionality.
- Support partial success scenarios (e.g., one network fails, others succeed).

## 8. Implement frontend wizard/stepper UI
- Build Next.js wizard/stepper component with steps: Client → Criteria → Strategy → Networks → Generate → Results → Preview → Select → Package.
- Use shadcn/ui and Radix primitives for accessible stepper, progress bar, card list, and preview panel.
- Implement full-width layout with card list (left/center) + preview panel (right).
- Reduce shell/sidebar horizontal spacing on large screens to increase usable workflow area.
- Add progress bar/timeline during generation with real-time/polled provider activity messages.
- Add pagination controls to results list (page navigation, page size selector, total results).
- Implement preview panel with tabs: "Video Analysis" and "Prebuilt Script to Replicate Style" (email-like side pane behavior).
- Add network tabs (Instagram/TikTok/YouTube) and mutation selector (reel/story/carousel/feed) to preview.
- Implement iPad-style social emulator surface that updates from selected result and chosen mutation/network.
- Add multi-content selection UI with checkboxes and selection summary.
- Add source-network checkbox icons for recognizable social platforms.
- Add visible per-content actions: download audio, transcribe + script, build carousel, with action status feedback.
- Integrate workflow history/resume dashboard with replace/regenerate actions.
- Support a frontend mock-data mode (videos, details, preview, actions, history, pagination) for UI iteration before backend integration is finalized.

## 9. Implement real-time/polling progress updates
- Add polling mechanism in frontend to call `GET /api/workflows/{id}/status` at regular intervals during generation.
- OR implement SignalR/websocket for live progress updates if polling is insufficient.
- Display provider activity messages and progress percentage in UI.
- Handle step failures with user-visible error messages and retry options.

## 10. Testing and quality gates
- Add unit tests for domain state transitions, invariants, and criteria/strategy validation.
- Add unit tests for use cases with mocked ports.
- Add integration tests for provider adapters (TikTok, Apify, Data365) with test fixtures.
- Add integration tests for multi-network aggregation with partial failure scenarios.
- Add integration tests for workflow persistence and audit trail.
- Add API contract tests for all workflow endpoints.
- Add API contract tests for paginated results and action endpoints.
- Add UI integration tests for wizard/stepper navigation, card list rendering, and preview panel behavior.
- Add UI integration tests for emulator mutation modes, cross-network preview tabs, and iconized network selectors.
- Add UI integration tests for pagination behavior and per-content action execution feedback.
- Run quality gates and resolve MUST failures before completion.

## 11. Documentation and spec sync
- Update `docs/PRD.md` if workflow behavior or scope changes during implementation.
- Update `docs/SDD.md` with workflow session model, ports, and adapters.
- Update OpenSpec specs if requirements evolve.
- Document simulated vs real functionality (ZIP package simulation, video generation deferral) clearly in README or feature docs.
- Add inline code comments for complex orchestration and aggregation logic.

## 12. Deployment and configuration
- Add configuration settings for provider API keys/credentials (TikTok API, Apify, Data365) via `platform.organization_integration_credentials`.
- Add feature flags for multi-network support and simulated package generation if needed.
- Deploy to staging environment and validate end-to-end workflow with test data.
- Document deployment steps and configuration requirements.
