# Loading and Progress

## Purpose
Choose the right loading-feedback pattern based on operation semantics so the application feels trustworthy and responsive.

## Scope
Any frontend operation that takes perceptible time.

## Load Conditions
A frontend or fullstack profile is active.

## Do Not Load When
Headless backends or pure CLI tools.

## Inputs
- The operation's expected duration and stages.
- Whether real progress data is available from the backend.

## Procedure
1. Choose pattern by operation semantics, not visual preference:
   - **Spinner / button-loading / loading-state**: short or unknown duration, content fetch, lightweight action (login, save, update, delete, list refresh, opening an edit form, simple search).
   - **Skeleton**: known layout, predictable structure, user benefits from seeing the future shape (cards, dashboard sections, admin tables, detail page shells, list pages).
   - **Progress bar**: long-running, multi-stage, strategically important, measurable or reasonably simulatable progress (bulk imports, generation/compilation workflows, publish pipelines, ranking/aggregate recalculations, complex orchestrations).
2. If real backend progress is unavailable for an important workflow, use simulated staged progress: move quickly at the start, slow near the end, never instant-jump from 0 to 100, optionally expose stage labels, only complete when the action resolves.
3. Expose meaningful status semantics and accessible labels for screen readers.
4. Avoid purely decorative motion that conveys no information.
5. Deliver every non-trivial shared loading/progress primitive complete per the project's component-completeness contract (TS + template + style + spec + docs).

## Output Expectations
- The user understands whether the system is fetching, working, or stalled.
- Important workflows feel deliberate and intentional, not flickering.
- Loading states are accessible.
