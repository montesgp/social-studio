# Final Compliance Audit

## Purpose
Final checklist before a task is considered complete: catches architectural drift, duplication, and quality gaps.

## Scope
Reviews, pull requests, and self-checks at the end of any non-trivial change.

## Load Conditions
Loaded by reviewer agents and orchestrators before declaring work complete.

## Do Not Load When
Trivial in-flight scratch work that has no review milestone.

## Inputs
- The diff or change set to audit.

## Procedure
1. Verify repository instructions and active stack/profile rules were followed.
2. Verify no duplicate shared primitive was introduced.
3. Verify the declared UI framework / shared design system remained the single source.
4. Verify components/features are complete to the project's delivery contract (files, tests, docs as required).
5. Verify documentation exists where required by the project's documentation rules.
6. Verify translation/localization readiness is preserved.
7. Verify theme tokens / design tokens are used correctly; no hardcoded colors in feature components.
8. Verify accessibility and responsiveness expectations are respected.
9. Verify complexity is justified — no speculative abstractions.

## Output Expectations
- If duplication or architectural drift is detected: stop and refactor instead of proceeding.
- The audit produces a short, citable summary of what passed and what blocked completion.
