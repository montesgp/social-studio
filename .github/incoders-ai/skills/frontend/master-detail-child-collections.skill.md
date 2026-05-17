# Master-Detail Child Collections

## Purpose
Build parent forms with one or more editable child collections through ONE shared reusable primitive, not a per-feature subtable.

## Scope
Any feature with: header + items, parent form + child table, nested editable collections, subtable inside a form, child rows under tabs, one-to-many editable rows, FK-backed detail rows, reorderable child items, grouped/collapsible child items.

## Load Conditions
A frontend or fullstack profile is active and parent-child editable collections are in scope.

## Do Not Load When
Read-only nested displays, or feature pages with no child editing.

## Inputs
- Parent entity shape.
- Child collection contracts (one or more).
- Whether ordering, tabs, or collapse are required.

## Procedure
1. Do NOT create a feature-specific child-table when the interaction model matches the shared primitive. Build or extend the shared one.
2. Required reusable capabilities: reactive-forms integration, `FormArray` (or equivalent adapter), configurable columns, add row, remove row, row editing, row validation, drag-and-drop reorder, persistence-ready sort mapping, optional tabbed mode, optional collapsible groups, optional collapse-all/expand-all, translated labels/helper text/validation/empty-state/action copy.
3. Ownership split:
   - **Parent form/page** owns aggregate loading, aggregate save, dependency orchestration, tab/section coordination, facade/service/repository calls.
   - **Shared child-collection primitive** owns row rendering, row UX, row actions, drag/reorder, child validation display, collection-change events.
4. When a child row contains semantic date ranges, apply the shared date-range validation at row level and aggregate-submit level.
5. When child rows represent relational persistence: preserve FK semantics, preserve deterministic order via `sort_order`, never use view-only array index as the source of truth.
6. Reuse first: inspect whether a shared primitive exists, reuse it, extend it, never duplicate row-management logic per feature.
7. Deliver the shared child-collection primitive per the project's component-completeness contract (template + style + spec + docs).

## Output Expectations
- Every parent-child editable form across the product feels identical.
- New child collections cost configuration, not implementation.
- Reorder, validation, and accessibility behave consistently.
