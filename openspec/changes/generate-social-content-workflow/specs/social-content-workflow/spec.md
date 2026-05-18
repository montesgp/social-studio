# Spec Delta: Generate Social Content Workflow

## ADDED Requirements

### Requirement: Client selection and workflow initialization
The system MUST allow an authenticated organization user to initialize a content generation workflow by selecting a client.

#### Scenario: Start workflow with selected client
- **GIVEN** an authenticated user with organization access
- **AND** a valid client selection in that organization
- **WHEN** the user starts a content generation workflow
- **THEN** the system creates a workflow session instance scoped to that organization/client
- **AND** returns the initial workflow state with available next steps to the caller

---

### Requirement: Multi-criteria idea search configuration
The system MUST allow users to select an idea search criterion from: niche, references, viral, competitors.

#### Scenario: Configure search criteria
- **GIVEN** an initialized workflow session
- **WHEN** the user selects a search criterion (e.g., "viral")
- **THEN** the system records the criterion in the workflow session
- **AND** the criterion influences content retrieval strategy in subsequent steps

---

### Requirement: Content strategy mode selection
The system MUST allow users to select a content strategy mode: mixed or specific type (educational, informative, entertainment).

#### Scenario: Select content strategy mode
- **GIVEN** a workflow session with search criteria configured
- **WHEN** the user selects a strategy mode (e.g., "educational")
- **THEN** the system records the strategy mode in the workflow session
- **AND** the mode filters or influences content retrieval and AI analysis

---

### Requirement: Multi-network source selection
The system MUST allow users to select one or more source social networks (e.g., TikTok, Instagram) simultaneously for content retrieval.

#### Scenario: Select multiple source networks
- **GIVEN** a workflow session with search criteria and strategy mode configured
- **WHEN** the user selects multiple networks (e.g., TikTok + Instagram)
- **THEN** the system records all selected networks in the workflow session
- **AND** retrieval steps target all selected networks in parallel or sequence

---

### Requirement: Generate trigger and real-time progress tracking
The system MUST provide a generate trigger that initiates content retrieval and MUST expose step-by-step progress updates with provider activity messages visible to the user.

#### Scenario: Trigger generation and track progress
- **GIVEN** a workflow session with client, criteria, strategy, and networks configured
- **WHEN** the user triggers content generation
- **THEN** the system starts retrieval steps for all selected networks
- **AND** the system exposes step status and provider activity messages (e.g., "TikTok API retrieving...", "Apify scraping Instagram...", "Data365 analyzing trends...")
- **AND** the frontend can poll or subscribe to live progress updates

#### Scenario: Provider activity messages preserve abstraction
- **GIVEN** a content retrieval step is in progress
- **WHEN** a provider adapter (e.g., TikTok API) updates its status
- **THEN** the infrastructure adapter emits a progress event
- **AND** the application orchestrator maps it to a generic workflow step message
- **AND** provider SDK details are not exposed to domain or frontend contracts

---

### Requirement: Search results display in paginated card list layout
The system MUST return search results as paginated content items with video thumbnail, metadata, and provider source tag, suitable for card-based UI display.

#### Scenario: Display search results
- **GIVEN** content retrieval steps have completed successfully
- **WHEN** the user requests workflow results
- **THEN** the system returns a paginated list of content result items
- **AND** each item includes video URL, thumbnail, title, metrics, and source network tag
- **AND** the frontend renders results in a card list layout

---

### Requirement: Results pagination for month-scale content volume
The system MUST support pagination in workflow results because monthly content retrieval can return high-volume candidate lists.

#### Scenario: Request paginated result set
- **GIVEN** a workflow session with more results than one page
- **WHEN** the user requests `page=2` with `pageSize=20`
- **THEN** the system returns only results for that page
- **AND** response metadata includes `page`, `pageSize`, `totalItems`, `totalPages`, `hasNextPage`, and `hasPreviousPage`
- **AND** ordering is stable across pages for the same filter/sort set

#### Scenario: Frontend navigates large result sets
- **GIVEN** a workflow session with month-scale results
- **WHEN** the user moves between pages
- **THEN** the frontend can request next/previous pages without re-running retrieval
- **AND** selected content state remains consistent when navigating pages

---

### Requirement: Preview panel with video analysis and prebuilt script tabs
The system MUST provide a preview endpoint for selected content that returns video analysis and a prebuilt script to replicate the content style, suitable for a right-side panel with tabs.

#### Scenario: Preview selected content
- **GIVEN** a workflow session with retrieved results
- **WHEN** the user selects a content result for preview
- **THEN** the system returns preview data with two sections: "Video Analysis" and "Prebuilt Script to Replicate Style"
- **AND** the frontend displays this data in a right-side preview panel with tabs (email-like side pane behavior)

#### Scenario: AI generates video analysis and script
- **GIVEN** a content result is selected for preview
- **WHEN** the system processes the preview request
- **THEN** the system calls `IAiContentAnalyzer` through application port
- **AND** the AI provider generates video analysis (hook, format, topic, angle, CTA, visual pattern)
- **AND** the AI provider generates a prebuilt script/outline to replicate the style
- **AND** the system stores analysis and script with model/provider metadata

---

### Requirement: Adaptive social preview emulator by network and format
The frontend MUST provide an adaptive publication emulator that allows users to choose how content mutates (reel, story video, story carousel, carousel post, feed post) and preview it across network tabs in an iPad-style Instagram-first device frame.

#### Scenario: Select mutation format and preview adaptation
- **GIVEN** a workflow session with retrieved results
- **WHEN** the user selects a mutation format (e.g., story carousel)
- **THEN** the preview emulator adapts the rendered composition to that format
- **AND** the rendered composition updates using the currently selected result data

#### Scenario: Switch cross-network preview tabs
- **GIVEN** a selected content result in preview
- **WHEN** the user switches preview tabs between Instagram, TikTok, and YouTube
- **THEN** the emulator updates to show network-specific preview chrome
- **AND** keeps the same selected result and mutation format context

#### Scenario: Selected result updates emulator context
- **GIVEN** the preview emulator is open
- **WHEN** the user selects a different result card from the paginated list
- **THEN** emulator title/copy/metrics update to the newly selected result
- **AND** analysis/script tabs remain available for the new selection

#### Scenario: Future AI media readiness
- **GIVEN** the MVP preview emulator
- **WHEN** AI-generated client videos are introduced in future iterations
- **THEN** the preview surface remains compatible with dynamic media rendering
- **AND** this future behavior is explicitly communicated as roadmap in current UX

---

### Requirement: Multi-content selection and package generation
The system MUST allow users to select one or multiple content results and generate a simulated monthly content package in ZIP format (video generation deferred).

#### Scenario: Select multiple content items
- **GIVEN** a workflow session with retrieved results
- **WHEN** the user selects one or more content result items
- **THEN** the system records the selection in the workflow session
- **AND** the selection is persisted for future package generation

#### Scenario: Generate simulated monthly content package
- **GIVEN** a workflow session with at least one content item selected
- **WHEN** the user triggers package generation
- **THEN** the system calls `IContentPackageGenerator` through application port
- **AND** the generator creates a simulated ZIP package structure with metadata and content references
- **AND** the system returns package metadata (actual video generation is deferred; placeholder files or references are included in MVP)
- **AND** the workflow session records package generation status

---

### Requirement: Content-level operational actions
The system MUST allow users to run operational actions on selected content items, including at least: download audio, transcribe and build script, and build carousel.

#### Scenario: Execute action on selected content item
- **GIVEN** a workflow session with retrieved results
- **AND** a content item is selected
- **WHEN** the user triggers `download_audio`
- **THEN** the system registers an action request for that content item
- **AND** exposes action execution status (`queued`, `running`, `completed`, `failed`)
- **AND** the frontend shows action status feedback for the user

#### Scenario: Execute action over multiple selected items
- **GIVEN** multiple content items are selected by the user
- **WHEN** the user triggers `transcribe_and_build_script` or `build_carousel`
- **THEN** the system executes the action per selected item
- **AND** records per-item outcomes in workflow history/audit
- **AND** returns actionable failures without hiding successful item results

#### Scenario: Extensible action catalog
- **GIVEN** the product adds new actions in future iterations
- **WHEN** a new action type is introduced
- **THEN** existing contracts remain backward-compatible
- **AND** action execution continues through provider-agnostic application ports

---

### Requirement: History and resume capability
The system MUST provide history/resume functionality so community managers can revisit workflow progress, replace rejected selections, and regenerate packages.

#### Scenario: View workflow history
- **GIVEN** an authenticated user with organization access
- **WHEN** the user requests workflow history
- **THEN** the system returns a list of past workflow sessions with client, criteria, strategy, networks, status, and timestamps
- **AND** the user can select a session to resume or review

#### Scenario: Replace rejected selections and regenerate
- **GIVEN** a completed workflow session with generated package
- **WHEN** the user replaces one or more selected content items with new selections
- **AND** triggers regeneration
- **THEN** the system updates the workflow session with new selections
- **AND** regenerates the content package with updated items
- **AND** preserves previous package versions for audit/rollback

---

### Requirement: Provider-agnostic multi-network aggregation
The system MUST retrieve content from multiple networks (TikTok API, Instagram via Apify, Data365) through provider-agnostic application ports without coupling domain or application layers to provider SDK details.

#### Scenario: Multi-network retrieval with provider abstraction
- **GIVEN** a workflow session with multiple networks selected (e.g., TikTok + Instagram)
- **WHEN** the content retrieval step executes
- **THEN** the system calls `IMultiNetworkContentAggregator` application port
- **AND** the aggregator orchestrates parallel/sequential calls to `IContentResearchProvider` implementations for each network
- **AND** infrastructure adapters (TikTokAdapter, ApifyInstagramAdapter, Data365Adapter) handle provider-specific SDK calls
- **AND** the aggregator merges results into a unified list of content items
- **AND** each content item tags its source network without exposing provider SDK details

---

### Requirement: Explicit workflow step status and provider errors
The system MUST expose step-level states (Pending, InProgress, Completed, Failed) and provider errors without silent fallbacks, supporting retry and user-visible error reporting.

#### Scenario: Provider step failure with explicit status
- **GIVEN** a workflow step call to an external provider (e.g., TikTok API)
- **WHEN** the provider returns an error or timeout
- **THEN** the workflow step is marked Failed with explicit reason metadata
- **AND** the workflow session remains queryable for retry or operator action
- **AND** the frontend displays the error message and retry option

#### Scenario: Partial success with multi-network retrieval
- **GIVEN** a workflow session with TikTok and Instagram selected
- **WHEN** TikTok API succeeds but Instagram/Apify fails
- **THEN** the workflow step is marked PartiallyCompleted
- **AND** the system returns TikTok results
- **AND** the Instagram error is recorded and exposed to the user
- **AND** the user can retry Instagram retrieval or proceed with TikTok results only

---

### Requirement: Wizard/stepper UX with full width layout
The frontend MUST implement a wizard/stepper interface with step-by-step navigation and progress visibility, using full screen width for dense information display.

#### Scenario: Navigate wizard steps
- **GIVEN** a user starts a content generation workflow
- **WHEN** the user completes each step (Client → Criteria → Strategy → Networks → Generate → Results → Preview → Select → Package)
- **THEN** the frontend displays a stepper/wizard UI with current step highlighted
- **AND** the user can navigate forward/backward between completed steps
- **AND** the UI uses full screen width for card lists and preview panels

#### Scenario: Display progress bar during generation
- **GIVEN** the user triggers content generation
- **WHEN** retrieval steps are in progress
- **THEN** the frontend displays a progress bar or timeline
- **AND** shows real-time/polled provider activity messages (e.g., "TikTok API retrieving...", "Apify scraping Instagram...")
- **AND** updates progress percentage based on completed steps

---

### Requirement: Network source selectors with recognizable social icons
The frontend MUST render each source-network checkbox with its corresponding social icon so users can identify networks quickly.

#### Scenario: Render source network icons
- **GIVEN** the source network selection step
- **WHEN** network options are displayed
- **THEN** each option includes its recognizable icon (e.g., TikTok, Instagram, YouTube)
- **AND** icon+label remain visible for selected and unselected states

---

### Requirement: Dense layout optimization for large screens
The shell and workflow page layout MUST reduce excessive lateral spacing on large screens to maximize usable content width for cards, detail panes, and preview surfaces.

#### Scenario: Reduce side spacing and increase usable width
- **GIVEN** a large desktop display
- **WHEN** the user opens the content workflow
- **THEN** left navigation and right content paddings are reduced from previous defaults
- **AND** available workspace width increases for the combined video list, details, and preview emulator

---

### Requirement: Simulated vs real functionality in MVP
The system MUST clearly distinguish between simulated and real functionality in the MVP.

#### Scenario: Simulated ZIP package generation
- **GIVEN** a workflow session with content selections
- **WHEN** package generation is triggered
- **THEN** the system creates a simulated ZIP package structure
- **AND** the package includes metadata files and content references (URLs, thumbnails, scripts)
- **AND** actual video files are NOT included (placeholder references only)
- **AND** the system logs that video generation is deferred to future iteration

#### Scenario: Real multi-network retrieval and AI analysis
- **GIVEN** a workflow session with networks and criteria configured
- **WHEN** content retrieval and preview steps execute
- **THEN** the system performs real API calls to TikTok, Apify, Data365 (if configured)
- **AND** the system performs real AI analysis and script generation via configured AI providers
- **AND** results are persisted and returned to the user

#### Scenario: Frontend mock mode for workflow UX iteration
- **GIVEN** backend integrations are still in progress for a frontend iteration
- **WHEN** the workflow UI runs in mock mode
- **THEN** the UI returns deterministic mocked multi-network videos, details, preview analysis, scripts, actions, and history
- **AND** pagination behavior matches the real contract (`page`, `pageSize`, `totalCount`, `totalPages`, `hasNextPage`, `hasPreviousPage`)
- **AND** switching from mock mode back to real endpoints does not require UX contract changes

---

### Requirement: Organization-scoped workflow persistence with audit trail
The system MUST persist workflow sessions with organization_id scope, maintain audit trail of selections and regenerations, and support history queries.

#### Scenario: Persist workflow session with audit trail
- **GIVEN** a user creates and modifies a workflow session
- **WHEN** selections are made, replaced, or packages generated
- **THEN** the system records each action with timestamp, user, and affected entities
- **AND** the audit trail is queryable for compliance and debugging
- **AND** all workflow data is scoped to organization_id for multi-tenant isolation

---

## MODIFIED Requirements
None (this is a new feature; no existing requirements are modified).

---

## REMOVED Requirements
None (this is a new feature; no existing requirements are removed).
