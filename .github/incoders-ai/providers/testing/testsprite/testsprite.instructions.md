# TestSprite Provider Instructions

## Purpose
AI-assisted QA validation for live applications, specs, and release candidates.

## Capabilities
- Derive UI and API validation flows from PRD, SDD, OpenSpec, and acceptance criteria.
- Explore a live app and compare observed behavior against intended behavior.
- Produce reviewable QA evidence for failures, regressions, and coverage gaps.
- Complement deterministic unit and integration tests without replacing them.

## Abstractions
- Treat TestSprite as an external QA provider behind ecosystem instructions and skills.
- Keep provider-specific setup, credentials, and execution details out of domain/application code.

## Security
- Use least-privilege credentials for any live app, preview, or API target.
- Never embed secrets in prompts, docs, or generated artifacts.
- Prefer non-production targets whenever a project offers them.

## Configuration
- Load when the active project manifest declares TestSprite or a task asks for AI test generation/validation.
- Use the project PRD, SDD, OpenSpec, and acceptance criteria as the source of truth for QA scope.
- Keep deterministic unit/integration tests as source-controlled assets; TestSprite supplements them.

## Failure Modes
- Treat discovery drift, auth failures, and environment mismatches as QA findings to report back.
- If a live flow cannot be validated, record the missing dependency or setup gap instead of masking it.

## Testing
- Use TestSprite for selective UI/API validation on critical journeys, especially release or preview checks.
- Keep failures reproducible with a clear path back to the affected flow, endpoint, or spec.
