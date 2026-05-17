# Drag-Sort and Preponderance

## Purpose
Standardize drag-and-drop reordering for any entity that supports user-controlled ordering, with persistent `sort_order` and (when applicable) a separate business-prominence weight.

## Scope
Any frontend or fullstack feature whose users reorder entities by drag-and-drop.

## Load Conditions
A frontend or fullstack profile is active and reorderable entities are in scope.

## Do Not Load When
Read-only lists or lists where order is purely view-time (e.g. sort-by-column).

## Inputs
- The entity collection and its persistence model.
- Whether business prominence (separate weight) applies in addition to user-controlled order.

## Procedure
1. All drag-and-drop reorder operations MUST persist ordering through `sort_order`. Add a separate prominence field (e.g. `preponderance`) when business priority/weighted prominence is required.
2. Do not implement visual-only reorder.
3. Drag-and-drop must update the in-memory list immediately; reorder persists through the repository/API boundary; reordered entities remain stable after refresh.
4. Ordering must not depend only on array index in the UI.
5. `sort_order` represents explicit visual/order sequence. `preponderance` (or equivalent) represents business prominence. If both exist, the feature documents precedence. Collisions resolve deterministically.
6. Use the project's drag-drop primitive (e.g. Angular CDK drag-drop, React DnD, equivalent). Use shared reorder helpers. Do not duplicate drag-drop logic per feature.
7. Encapsulate reorder mapping in reusable helpers/services.
8. Support optimistic UI when appropriate; define rollback behavior on persistence failure.
9. UX: provide drag handle affordance, show reorder feedback clearly, disable drag when business rules forbid reorder, provide translated status/toast messages after save, preserve keyboard and screen-reader semantics.
10. Tests cover: reorder in memory, persistence payload generation, final sorted result, rollback/error handling when save fails.

## Output Expectations
- Reorder is durable, accessible, and consistent across the product.
- New orderable entities use the shared primitive, not a per-feature reorder helper.
- Persisted order matches what the user saw before refresh.
