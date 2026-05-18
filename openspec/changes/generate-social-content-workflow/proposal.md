# Proposal: Generate Social Content Workflow

## Change ID
`generate-social-content-workflow`

## Summary
Implement a comprehensive social content generation workflow that enables community managers to research, generate, evaluate, and package content ideas based on multiple search criteria and source networks. The workflow includes client selection, flexible idea search (niche, references, viral, competitors), content strategy mode selection, multi-network source selection, real-time progress tracking with provider activity visibility, paginated search results exploration with video preview and analysis, per-content operational actions, multi-content selection, and simulated monthly content package generation (ZIP format for MVP; video generation deferred).

## Problem
The PRD defines the workflow path, but there is no implementation-ready change package that connects product intent, technical design, and executable tasks. Community managers need an intuitive, wizard-style UI with step-by-step progress visibility, multi-criteria content search, side-by-side content preview with AI analysis, and the ability to resume/replace selections after client rejections.

## Goals
- Establish a full SDD artifact set for this feature with explicit UI flow requirements.
- Enable flexible content search with multiple criteria: niche, references, viral trends, competitors.
- Support mixed or specific content strategy modes (educational, informative, entertainment).
- Allow simultaneous multi-network source selection (TikTok, Instagram, etc.).
- Provide real-time progress tracking with step-by-step provider activity updates visible to users.
- Display search results in paginated card layout with right-side preview panel (video analysis + prebuilt script tabs).
- Provide content-level actions for selected items (download audio, transcribe + script, build carousel, extensible action set).
- Support multi-content selection and generate simulated ZIP monthly content package.
- Implement history/resume capability so community managers can revisit, replace selections, and regenerate.
- Keep provider/model choices configurable and abstracted (TikTok API, Apify for IG scraping, Data365, AI providers).
- Respect hexagonal boundaries and multi-tenant organization isolation.
- Design intuitive, modern, wizard/stepper-based UX using full screen width for dense information display.

## Out of Scope (MVP)
- Actual video generation (simulated/deferred for future iteration).
- Real ZIP file generation (simulated in MVP; placeholder package structure).
- Deep integration with all external providers in this change (focus on architecture/contracts).
- Altering authentication model.
- Native video editor or complex post-processing.

## Scope Boundaries
- Focus on the social content workflow module and required contracts between frontend and backend.
- Preserve provider-agnostic behavior through application ports/adapters.
- UI must support wizard/stepper flow with progress visibility.
- Backend must expose step-by-step workflow state and provider activity for frontend consumption.

## Detailed User Flow Requirements
1. **Client Selection**: User selects a client from organization context.
2. **Idea Search Criterion**: User selects search mode: niche, references, viral, competitors.
3. **Content Strategy Mode**: User selects mix vs specific type (educational, informative, entertainment).
4. **Multi-Network Source Selection**: User selects one or more source social networks (e.g., TikTok + Instagram simultaneously).
5. **Generate Trigger & Progress Display**: After selecting criteria, user triggers Generate; frontend shows progress bar/timeline with step-by-step provider activity updates (e.g., "TikTok API retrieving...", "Apify scraping Instagram...", "Data365 analyzing...") while preserving adapter abstraction in backend.
6. **Search Results Display**: Show results in paginated card list layout with video thumbnail + metadata; clicking a card shows detail pane.
7. **Preview Panel with Tabs**: When selecting a result, show right-side preview panel with tabs: "Video Analysis" and "Prebuilt Script to Replicate Style" (email-like side preview behavior).
8. **Multi-Content Selection & Package Generation**: User selects one or multiple content items and generates output package; MVP simulates ZIP monthly content package (video generation deferred).
9. **History/Resume Tool**: Provide interface for community manager to revisit workflow progress, replace rejected selections, and regenerate packages.
10. **UX Design**: Intuitive, modern, flow-oriented; wizard/stepper preferred; use full screen width due to dense information needs.
11. **Pagination**: Result list must support pagination because monthly retrieval can return high volume candidates.
12. **Operational Actions per Content**: From selected content, user can trigger typical actions like download audio, transcribe/build script, and build carousel.
13. **Adaptive Emulator Preview**: Preview must let user choose mutation mode (reel/story/carousel/feed) and render an iPad-style Instagram-first emulator that adapts the selected content.
14. **Cross-Network Preview Tabs**: Preview must include tabs per social network so the same selected content can be inspected in each network rendering.
15. **Iconized Network Selection + Dense Layout**: Source-network checks must include social icons and shell/layout spacing should be reduced on large screens to maximize workspace for cards + details + preview.

## Risks
- Workflow complexity across multiple bounded contexts and UI steps.
- AI output quality variance without explicit evaluation criteria.
- Potential coupling to specific providers (TikTok API, Apify, Data365) if ports are not enforced.
- Progress tracking requires real-time or polling mechanism for frontend updates.
- Multi-network aggregation complexity (combining TikTok + Instagram results with different schemas).
- Simulated package generation may not fully represent production requirements.

## Traceability
- PRD: `docs/PRD.md` sections 6 and 9.
- SDD: `docs/SDD.md` sections 5, 6, 7, 9, and 10.
- Governance: `docs/ai-governance.md` rules on traceability and configurable model selection.
