# UI Primitives Contract

## Purpose
Enforce a single implementation for every UI pattern. If a component exists in `components/ui/`,
it is the ONLY valid implementation of that pattern across the entire application — regardless of
where it is used, what data it loads, or what name the feature gives it.

## Scope
All React/Next.js frontend code in any app or package in this repository.

## Load Conditions
Always loaded when the frontend or fullstack profile is active.

## Do Not Load When
Backend-only or infrastructure-only work.

---

## The Core Principle

> **One pattern → one component.**

If `components/ui/` already has a `Button`, there is no "delete button", "save button", or
"edit button" — there is only `<Button variant="...">`. If it has a `Dialog`, there is no
"confirm modal" or "form overlay" — there is only `<Dialog>`. The name of the feature using it
is irrelevant.

---

## Registered Primitives

These components exist in `components/ui/`. Every listed pattern MUST use its primitive.
Feature code that re-implements any of these — inline or as a local component — is a violation.

| Pattern                     | Primitive              | Import path                          |
|-----------------------------|------------------------|--------------------------------------|
| Text input                  | `<Input>`              | `@/components/ui/input`              |
| Password input              | `<PasswordInput>`      | `@/components/ui/password-input`     |
| Checkbox / toggle input     | `<Checkbox>`           | `@/components/ui/checkbox`           |
| Dropdown / select           | `<Select>`             | `@/components/ui/select`             |
| Action button               | `<Button>`             | `@/components/ui/button`             |
| Status label / tag          | `<Badge>`              | `@/components/ui/badge`              |
| Content container           | `<Card>` / `<CardContent>` etc. | `@/components/ui/card`      |
| Modal / overlay             | `<Dialog>` etc.        | `@/components/ui/dialog`             |
| Data table                  | `<Table>` etc.         | `@/components/ui/table`              |
| Sortable child collection   | `<SortableChildTable>` | `@/components/ui/sortable-child-table` |
| Loading / skeleton          | `<HttpLoader>`         | `@/components/ui/http-loader`        |
| Step / upload progress      | `<ProgressBar>`        | `@/components/ui/progress-bar`       |
| Toast / notification        | `useToast`             | `@/components/ui/toaster`            |

> Raw HTML equivalents (`<select>`, `<input>`, `<button>`, etc.) are **FORBIDDEN** in feature
> code. The only place allowed to use raw HTML for these patterns is inside `components/ui/`
> itself.

> `<textarea>` has no primitive yet — use it with design-token classes until one is registered.

---

## Select — Size Contract

| `size` prop         | Height | Text    | Padding | Use when                                      |
|---------------------|--------|---------|---------|-----------------------------------------------|
| `"default"` (omit)  | h-9    | text-sm | px-3    | Form fields, panels, dialogs, settings        |
| `"sm"`              | h-8    | text-xs | px-2    | Inline toolbar controls, compact filter bars  |

`h-10` is not a valid select height in this design system.

---

## Rules

### Using primitives
- MUST use the registered primitive for any pattern in the table above. No exceptions in feature code.
- MUST NOT re-implement a registered pattern as a local component, even if it "looks simpler" or "only needs a small change".
- MUST NOT apply inline `className` overrides that duplicate what the primitive already provides (height, padding, text-size, border, background, focus ring, border-radius).
- MUST pass only structural/layout overrides as `className` (`w-full`, `w-[Xpx]`, `min-w-[...]`, `cursor-pointer`, `flex-1`).

### Extending primitives
- When a primitive does not cover a needed variant, MUST extend the primitive's props (`size`, `variant`, `intent`, etc.) and document the new value in this file.
- MUST NOT fork the primitive by creating a new component with a different name that wraps it with hardcoded props.
- MUST NOT add ad-hoc size/color/shape classes inline to approximate a variant that doesn't exist yet.

### Adding new primitives
- When a UI pattern is needed that has no registered primitive, MUST create it in `components/ui/` first, register it in this table, then use it in feature code.
- The primitive MUST be generic (not named after the feature that introduced it).
- MUST NOT ship a one-off local component for a pattern that will clearly appear in multiple places.

---

## Output Expectations

- Reviews MUST flag any raw `<select>`, `<input>`, `<button>`, or equivalent outside `components/ui/`.
- Reviews MUST flag any local component that re-implements a pattern already in `components/ui/`.
- PRs that introduce a parallel implementation of a registered primitive are sent back for migration before merge.
- When a violation is found in existing code during unrelated work: SHOULD fix it in the same PR unless scope risk is high — in that case, open a follow-up issue before merging.
