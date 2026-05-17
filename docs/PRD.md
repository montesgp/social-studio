# PRD - Incoders Social

## 1. Product Summary
Incoders Social helps agencies and community managers manage multiple clients, discover high-performing content in a niche, transform references into structured insights, generate publication ideas with AI, evaluate those ideas, produce creative assets, publish through external providers and learn from metrics.

## 2. Problem
Content operators repeatedly research viral content, interpret why it worked, ideate variations, create assets, publish, and report results. This process is manual, inconsistent and difficult to scale across many clients.

## 3. Goals
- Centralize client/account/content operations.
- Automate content research and idea generation.
- Support multi-model AI routing and evaluation.
- Create a repeatable publication workflow from reference content to published asset.
- Track metrics and create a feedback loop for future ideas.

## 4. Non-Goals for MVP
- Full billing system.
- Native editing suite replacing Canva.
- Direct support for every social network at launch.
- Fully autonomous publishing without human review.

## 5. Primary Users
- Agency owner/admin.
- Community manager.
- Content strategist.
- Client viewer/approver.

## 6. MVP Success Path
1. User selects an organization/client.
2. User chooses content source strategy: niche, reference accounts, own accounts, or manual URL list.
3. System retrieves candidate high-performing content through configured data provider.
4. System transcribes/maps content into structured attributes: hook, format, topic, angle, CTA, visual pattern, platform metrics.
5. System sends mapped context to selected/default AI model to generate ideas.
6. System runs evaluation model to score and normalize ideas.
7. User selects idea and creates a canvas/design draft.
8. System sends publication draft to publishing provider.
9. System stores publication status and metrics snapshot.

## 7. Open Questions
- Supported social networks for MVP.
- Whether UGC sales flows are part of MVP.
- Whether creative preview is built internally or delegated to Canva first.
- Exact approval workflow for clients.
- Which content providers are legally/contractually acceptable per platform.

## 8. Core Entities
- Organization
- User
- Client
- SocialAccount
- ContentSource
- ReferenceContent
- ContentInsight
- Idea
- IdeaEvaluation
- CanvasDraft
- Publication
- PublicationProvider
- AiProvider
- AiModel
- IntegrationProvider
- OrganizationIntegration
- OrganizationIntegrationCredential
- PromptVersion
- Campaign
- MetricsSnapshot

## 9. Acceptance Criteria
- A CM can create/select a client and run a content research workflow.
- The system can persist retrieved references and AI-generated ideas.
- The system can evaluate ideas with a configurable model.
- Each organization can configure its own third-party integration credentials (AI, Canva, Blotato, etc.) without relying only on appsettings.
- The frontend shows workflow state and errors clearly.
- Backend use cases are provider-agnostic.
