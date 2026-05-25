# Integrations Spec

## Purpose
Define platform-level integration catalog and organization-scoped provider configuration for Incoders Social workflows.

## Capabilities
- Maintain a global `IntegrationProvider` catalog (provider metadata, frontend icon key, category, supported connection modes, credential schema).
- Maintain `OrganizationIntegration` records per organization (status, mode, defaults, readiness).
- Maintain encrypted `OrganizationIntegrationCredential` values per organization integration.
- Support provider categories: AI model providers, design, data extraction, publishing, and future media generation providers.
- Support connection modes: `BringYourOwnKey`, `ManagedByIncoders`, `SystemDefault`.
- Support statuses: `NotConfigured`, `Connected`, `Failed`, `Disabled`.
- Seed baseline providers: Google Drive, Apify, Data365, Claude, OpenAI, GenSpark AI, TikTok, YouTube, Instagram, plus existing workflow providers Gemini, Canva, and Blotato.
- Expose organization setup checklist groups for Social workflows.
- Single Analyzer setup requires at least one connected content data provider (Apify or Data365) and at least one connected AI model provider.
- Generator setup currently requires at least one connected content data provider (Apify or Data365) and at least one connected AI model provider; design and publishing providers are optional until the generator workflow expands.
- Expose Apify as `SystemDefault` readonly by default while allowing future organization-owned credentials.
- Expose API endpoints for listing providers, listing organization integrations, reading one organization integration by provider key, connecting/configuring, deleting, testing connection, and selecting default AI provider/model.
- Ensure Social module resolves providers through `IIntegrationResolver` abstraction.

## Constraints
- Organization credentials MUST NOT be stored only in appsettings.
- Organization credential values MUST be encrypted at rest.
- Domain/Application layers remain provider-agnostic.

## Acceptance Criteria
- Settings > Integrations frontend page shows separate Single Analyzer and Generator setup checklists plus grouped provider cards by category.
- Users can connect/configure integrations through a dynamic credential modal.
- Users can test provider connections and see status badges.
- Users can identify readonly/system default providers.
- Users can select default AI provider/model for the organization.
