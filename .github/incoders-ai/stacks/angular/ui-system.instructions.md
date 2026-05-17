# Angular UI System

## Purpose
Define the shared UI framework and reusable primitives for Angular projects in this ecosystem.

## Scope
Angular projects.

## Load Conditions
The active project manifest declares an Angular stack.

## Do Not Load When
Non-Angular frontends.

## Rules
- MUST use Angular Material 3 + Angular CDK as the single shared UI framework unless the project manifest explicitly approves otherwise.
- MUST NOT mix multiple component libraries (no Bootstrap, PrimeNG, Ant Design, Tailwind UI kits, or ad-hoc third-party design systems unless the manifest explicitly approves).
- MUST NOT create duplicate UI primitives.
- SHOULD prioritize creating/extending these shared reusable primitives instead of duplicating: button variants, page header, section card or information panel, filter panel, form shell, list or table shell, empty state, error state, confirmation dialog, helper dialog, blocking loader overlay, status badge/chips, pagination controls.
- MUST use semantic design tokens; never direct hex colors inside feature components.
- MUST respect consistent spacing, radius, typography, and density.
- SHOULD prefer accessible Material/CDK primitives over custom JavaScript-heavy widgets.
- MUST keep dialogs, buttons, filters, forms, and lists feeling like one product.

## Form pattern (strict)
Every non-trivial form follows: feature container → shared form-shell → Angular Reactive Forms → shared validators when applicable → centralized service for submit → shared async-button for primary actions. Required states: loading, submitting, success, error.

## Shared list and pagination
All non-trivial list-based screens reuse the shared list pattern: data-table, list-toolbar, bulk-actions-bar, pagination. Shared pagination supports server-side pagination, configurable page size, page index display, total item count, optional pagination placement (footer only / header only / both). Shared list pattern supports row selection, bulk selection, bulk actions, empty/loading/error states, responsive adaptation. Do not create feature-specific pagination components unless explicitly justified.

## Output Expectations
- One framework, one token system across the application.
- New screens reuse the shared primitives; pull requests that duplicate them are sent back for refactor.
