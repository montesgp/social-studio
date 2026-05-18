# TestSprite Validation Skill

## Purpose
Run a reusable AI-assisted QA pass with TestSprite for live-app and API validation.

## Scope
Projects that declare the TestSprite provider and want a QA step beyond deterministic unit and integration tests.

## Load Conditions
- The active project manifest declares TestSprite.
- A task asks for QA validation, exploratory verification, or AI-assisted E2E coverage.

## Do Not Load When
- The task is pure unit-test authoring or a code-only refactor.
- No live URL, base URL, or acceptance criteria are available.

## Inputs
- App URL or API base URL.
- Credentials, preview target, or other approved access details.
- PRD, SDD, OpenSpec, and acceptance criteria.
- Known risk areas or flows that need focus.

## Procedure
1. Identify the live surface to validate and the user journeys or endpoints that matter most.
2. Cross-check the requested flows against the PRD, SDD, OpenSpec, and acceptance criteria.
3. Run TestSprite against the live target with the smallest useful scope first.
4. Review the generated plan, failures, screenshots, and flow evidence.
5. Separate product defects from environment/setup issues and report both explicitly.
6. Convert stable failures into deterministic tests or spec gaps in the repository's normal workflow.

## Output Expectations
- A concise QA report with validated flows, failures, and coverage gaps.
- Clear next actions for spec updates, deterministic tests, or environment fixes.
- No claim of pass if the live flow could not actually be exercised.
