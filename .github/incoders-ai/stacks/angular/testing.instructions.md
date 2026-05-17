# Angular Testing

## Purpose
Define the testing contract for Angular projects in this ecosystem.

## Scope
Angular projects.

## Load Conditions
Project manifest declares an Angular stack.

## Do Not Load When
Non-Angular frontends — they use their own testing rules.

## Rules
- MUST use Jasmine as the test framework. Tests MUST be compatible with Angular testing utilities (TestBed, ComponentFixture).
- MUST NOT use Jest, Mocha, or mixed runners.
- 100% of created components MUST have tests. Services, pipes, guards, interceptors, and reusable helpers MUST have tests. Shared UI primitives MUST have comprehensive tests.
- Tests focus on behavior, not implementation details. Tests verify rendering correctness; loading/empty/error/success states; user interactions; emitted outputs; public inputs; conditional rendering; accessibility-relevant behavior; translation usage when logic depends on it; theme-dependent class/token behavior when logic is involved.
- Angular specifics: use Angular TestBed properly; prefer standalone component testing patterns; avoid over-mocking Angular internals; test Signals-based state when applicable; avoid relying on private component state.
- API/data rules: components MUST NOT call HttpClient directly (validate via test behavior); test services independently from components; when API is unavailable, use mock services consistent with the api-mocking skill; mock data reflects real scenarios.
- Form/validation rules: tests verify validators, disabled state of primary actions, enabling conditions on both technical and business rules, helper/help behavior when rules affect interaction.
- UI system rules: for shared UI components, test all supported states, reuse scenarios when relevant, interaction contracts (inputs/outputs); avoid duplicating logic across tests.
- Notifications and destructive actions: tests verify success/error/info feedback; destructive actions execute only after confirmation; no execution on cancel; confirm-dialog integration for delete flows.
- i18n rules: do not assert hardcoded strings; prefer asserting rendered content or behavior; ensure components do not break when locale changes.
- Theming rules: do not assert specific colors; assert semantic behavior (class/token usage, state changes); ensure components do not depend on hardcoded styling.
- Style: clear test naming, readable structure, no unnecessary setup duplication, no dead assertions, no commented-out tests.
- Forbidden: shallow placeholder specs with no assertions; assertions on implementation details; duplicated test patterns; inconsistent test styles; bypassing shared mocks or test utilities when they exist.

## Output Expectations
- Every new component has a meaningful `.spec.ts`.
- Tests catch regressions in behavior, not in incidental implementation.
- Coverage gates pass without superficial tests.
