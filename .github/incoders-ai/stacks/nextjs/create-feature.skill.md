# Create Next.js Feature

## Purpose
Scaffold a new feature in a Next.js (App Router) project following the active stack's conventions and the project's component-completeness contract.

## Scope
Any feature added to a Next.js application that loads this ecosystem.

## Load Conditions
The active project manifest declares a Next.js stack and a new feature/route is being created.

## Do Not Load When
- Non-Next.js frontends.
- Page-level tweaks that do not introduce a new feature surface.

## Inputs
- Feature name and its place in the product information architecture.
- Route group, if any (e.g. `(auth)`, `(dashboard)`).
- Data contract the feature consumes (real endpoint or mock).
- The translation source-of-truth and locale set declared by the project manifest.

## Procedure
1. Place the feature under a feature folder (`app/<feature>/` or `app/(group)/<feature>/`). Prefer route groups for layout-sharing or auth-gating concerns.
2. Be explicit about server/client boundaries: default to Server Components; mark Client Components with `"use client"` only when interactivity, browser APIs, or stateful hooks are required.
3. Define the typed data contract before writing UI. Consume it through a service/facade/gateway — never call `fetch` or HTTP transport directly from a component.
4. Deliver every relevant state: loading (`loading.tsx` or skeleton), empty, error (`error.tsx` or inline boundary), success. The screen is not complete without all four.
5. Use the active design system declared by the manifest (e.g. shadcn/ui + Radix + Tailwind). Do not introduce a parallel UI library.
6. Use Lucide for icons or whatever the manifest declares. Do not mix multiple icon sources.
7. User-facing text uses translation keys against the project's translation source-of-truth. No hardcoded strings.
8. Use semantic design tokens for color/spacing/typography. No hex literals inside the feature.
9. Add tests for behavior the user can observe: state transitions, error handling, accessibility-relevant outputs. Follow the testing framework the manifest declares.
10. Run the project's quality gates (`scripts/gates/Run-Gates.ps1` or equivalent) before marking the feature complete.

## Output Expectations
- Folder structure mirrors the active stack's feature-first convention.
- No HTTP transport inside components; data flows through a typed service boundary.
- Loading / empty / error / success states are all delivered.
- New copy lands in the translation source-of-truth in every declared locale.
- Tests pass and gates are green before the task is considered complete.
