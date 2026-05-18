# Design: Generate Social Content Workflow

## Overview
This feature implements a comprehensive content generation workflow using a provider-agnostic, hexagonal architecture. The workflow supports:
1. **Client Selection**: User selects client from organization context.
2. **Search Criteria Configuration**: User chooses idea search criterion (niche, references, viral, competitors).
3. **Content Strategy Selection**: User selects content strategy mode (mix or specific: educational, informative, entertainment).
4. **Multi-Network Source Selection**: User selects one or more source networks (TikTok, Instagram, etc.) simultaneously.
5. **Content Retrieval with Progress Tracking**: System retrieves content from selected networks via provider adapters (TikTok API, Apify for Instagram scraping, Data365 where applicable); workflow steps expose real-time/polling-friendly status updates visible to frontend.
6. **Results Display**: Search results presented in paginated card list with video + metadata.
7. **Preview Panel with Analysis**: Selected content displays in right-side preview panel with tabs: "Video Analysis" and "Prebuilt Script to Replicate Style".
8. **Multi-Content Selection & Package Generation**: User selects multiple items; system generates simulated ZIP monthly content package (actual video generation deferred to future).
9. **History/Resume Capability**: Community managers can revisit workflow, replace selections after rejections, and regenerate packages.
10. **Wizard/Stepper UX**: Intuitive, modern, flow-oriented UI using full screen width; step-by-step navigation with progress visibility.
11. **Per-Content Actions**: Selected content items expose operational actions such as audio download, transcription/script generation, and carousel generation.
12. **Adaptive Device Preview**: Preview supports mutation mode selection (reel/story/carousel/feed), iPad-style Instagram-first emulator rendering, and network tabs (Instagram/TikTok/YouTube) that adapt the preview chrome per network.

## Architectural Boundaries
- **Domain**: workflow state transitions, business invariants, search criteria and strategy mode value objects, content selection aggregates.
- **Application**: orchestration use cases and ports:
  - `IContentResearchProvider` (abstracts TikTok API, Apify, Data365)
  - `IMultiNetworkContentAggregator` (combines results from multiple network providers)
  - `IAiContentAnalyzer` (video analysis and script generation)
  - `IAiIdeaGenerator` and `IAiIdeaEvaluator` (existing)
  - `IContentPackageGenerator` (simulated ZIP package for MVP)
  - `IWorkflowStateRepository` (persistence with history/resume support)
- **Infrastructure**: provider-specific adapters (TikTok, Instagram/Apify, Data365, AI providers, package generation).
- **API**: workflow endpoints with async/polling status contracts for frontend; step-by-step progress updates; selection and regeneration endpoints.

## Workflow State Model
- **WorkflowSession**: Aggregate root tracking client, search criteria, strategy mode, selected networks, current step, history.
- **WorkflowStep**: Value object with states: Pending, InProgress, Completed, Failed; includes provider activity metadata (e.g., "TikTok API retrieving...").
- **ContentSearchCriteria**: Enum/value object (Niche, References, Viral, Competitors).
- **ContentStrategyMode**: Enum/value object (Mixed, Educational, Informative, Entertainment).
- **NetworkSource**: Enum/value object (TikTok, Instagram, YouTube, etc.).
- **ContentResult**: Retrieved content with video URL, metadata, provider source tag.
- **ContentSelection**: User-selected content items for package generation.
- **ContentActionRequest**: Requested action on selected content item(s), including action type and execution status.
- **ContentPackage**: Simulated monthly package (ZIP structure placeholder).

## Data and Contracts
- Workflow sessions are organization-scoped (`organization_id`).
- Workflow steps store provider-specific activity messages without coupling domain to provider details (infrastructure adapters map provider status to generic workflow step messages).
- API contracts expose:
  - `POST /workflows/content-generation`: Initialize session with client, criteria, strategy, networks.
  - `GET /workflows/{id}/status`: Poll current step, progress percentage, provider activity messages.
  - `GET /workflows/{id}/results?page={n}&pageSize={m}`: Retrieve paginated search results (cards list) with pagination metadata (`totalItems`, `totalPages`, `hasNextPage`, `hasPreviousPage`).
  - `GET /workflows/{id}/results/{resultId}/preview`: Get preview data (video analysis + script tabs).
  - `POST /workflows/{id}/results/{resultId}/actions`: Trigger action for one content result (`download_audio`, `transcribe_and_build_script`, `build_carousel`, extensible).
  - `POST /workflows/{id}/actions/bulk`: Trigger action over multiple selected content results.
  - `POST /workflows/{id}/select`: Submit content selection.
  - `POST /workflows/{id}/generate-package`: Trigger simulated package generation.
  - `GET /workflows/{id}/history`: Retrieve workflow history for resume.
  - `PUT /workflows/{id}/replace-selection`: Replace rejected items and regenerate.
- Multi-network aggregation is handled by `IMultiNetworkContentAggregator` which orchestrates parallel/sequential provider calls and merges results.

## Progress Tracking and Provider Visibility
- Frontend polls `/workflows/{id}/status` or uses SignalR/websocket for live updates.
- Backend workflow orchestrator updates step status with human-readable activity messages mapped from provider adapters:
  - Infrastructure adapters emit progress events: `ProviderStarted`, `ProviderInProgress`, `ProviderCompleted`, `ProviderFailed`.
  - Application orchestrator translates these to workflow step messages: "Retrieving from TikTok API...", "Apify scraping Instagram...", "Data365 analyzing trends...".
- Domain events: `WorkflowStepProgressed`, `WorkflowStepCompleted`, `WorkflowStepFailed`.
- Action execution status is exposed as queued/running/completed/failed so users can track long-running action outcomes.

## Error and Resilience Strategy
- Use explicit step-level failure states; do not hide provider failures.
- Preserve partial workflow progress with recoverable retry points.
- Support provider timeout and fallback strategies (e.g., if one network fails, continue with others).
- Require idempotency support for retrieval, analysis, and package generation operations.
- History/resume allows user to retry from last successful step or replace failed selections.

## MVP Simulation vs Future Implementation
- **Simulated in MVP**:
  - ZIP package generation (placeholder structure with metadata; no actual video files).
  - Video generation (deferred; package includes video references/URLs, not generated videos).
- **Real in MVP**:
  - Multi-network content retrieval via provider adapters.
  - AI video analysis and script generation.
  - Workflow state persistence and history.
  - Progress tracking and step-by-step UI.
  - Selection and regeneration flows.

## Testing Strategy
- Unit tests for domain state transitions, invariants, and search criteria/strategy validation.
- Integration tests for provider adapters (TikTok, Apify, Data365), multi-network aggregation, and persistence boundaries.
- API contract tests for workflow endpoints, status polling, and selection flows.
- UI integration tests for wizard/stepper navigation and preview panel behavior.

## UX/UI Design Principles
- **Wizard/Stepper Flow**: Step-by-step navigation (Client → Criteria → Strategy → Networks → Generate → Results → Preview → Select → Package).
- **Full Width Layout**: Dense information display requires full screen width; cards list + right-side preview panel.
- **Layout Density Tuning**: Reduce excessive left/right shell spacing and sidebar footprint on large screens to maximize content workspace.
- **Paginated Results Navigation**: UI must provide explicit page controls and total result context for month-scale datasets.
- **Real-time Progress**: Progress bar/timeline with provider activity messages during retrieval.
- **Preview Panel**: Right-side panel (similar to email preview pane) with tabs for video analysis and script.
- **Adaptive Network Preview Tabs**: Preview panel includes network tabs and an iPad-style emulator that updates as selected result or mutation mode changes.
- **Network Selectors with Icons**: Source-network checkboxes include recognizable social platform icons.
- **Action Bar**: Each selected content item exposes clear actions (download audio, transcribe + script, carousel) with visible status feedback.
- **History/Resume**: Accessible from workflow dashboard; shows past sessions with replace/regenerate actions.

## Documentation Sync
If behavior changes during implementation, update `docs/` and relevant `openspec/specs/` artifacts in the same change.
