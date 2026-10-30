# Project AI Manifest - Social Studio

## Project Type
fullstack-monorepo-ai-native-saas

## Active Global Manifests
- .github/incoders-ai/manifests/fullstack-monorepo-nextjs-dotnet-aspire.manifest.md
- .github/incoders-ai/manifests/ai-multi-model.manifest.md

## Active Provider Manifests
- .github/incoders-ai/manifests/provider-supabase.manifest.md
- .github/incoders-ai/manifests/provider-testsprite.manifest.md

## Active Product Providers
Cross-project providers live under `.github/incoders-ai/providers/` (loaded via global manifests). Providers below are specific to this product and live under `.github/project-ai/providers/`.

- data (social content research):
  - .github/project-ai/providers/data/apify/apify.instructions.md
  - .github/project-ai/providers/data/data365/data365.instructions.md
- design (social post composition):
  - .github/project-ai/providers/design/canva/canva.instructions.md
- publishing (social distribution):
  - .github/project-ai/providers/social/blotato/blotato.instructions.md
- ai (model routing — cross-project): see `.github/incoders-ai/manifests/ai-multi-model.manifest.md`

## Project Documents
- README.md
- AGENTS.md
- docs/PRD.md
- docs/SDD.md
- docs/ai-governance.md
- docs/quality-gates.md
- docs/architecture/adr/
- openspec/specs/

## Quality Gates (Phase 1)
- .github/incoders-ai/gates/README.md
- .github/incoders-ai/gates/gates.policy.md
- .github/incoders-ai/gates/ai-agent.gate.md
- project.gates.yml
- scripts/gates/Run-Gates.ps1

## Project Instructions
- .github/project-ai/instructions/project.context.md
- .github/project-ai/instructions/project.architecture.md
- .github/project-ai/instructions/project.workflow.md
- .github/project-ai/instructions/project.domain.md
- .github/project-ai/instructions/project.ai-orchestration.md
- .github/project-ai/instructions/project.api-contract.md
- .github/project-ai/instructions/project.database.md
- .github/project-ai/instructions/project.testing.md

## Project Skills
- .github/project-ai/skills/content-workflow-modeling.skill.md
- .github/project-ai/skills/social-publication-pipeline.skill.md
- .github/project-ai/skills/ai-idea-evaluation.skill.md

## Project Agents
- .github/project-ai/agents/social-domain-agent.md
- .github/project-ai/agents/content-workflow-agent.md
- .github/project-ai/agents/ai-quality-evaluator-agent.md
