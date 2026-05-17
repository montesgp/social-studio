# List and Card Views

## Purpose
Render every list of records through one shared listing primitive that supports a table + cards toggle, persisted per page.

## Scope
Any admin or operator screen that renders a list of records (catalogs, dashboards with tabular data, anything with a table or hand-rolled grid of items).

## Load Conditions
A frontend or fullstack profile is active and list/collection screens are in scope.

## Do Not Load When
- Read-only single-record screens
- Forms without an attached collection
- Sub-grids inside a parent form (use the master-detail child-collection skill instead)

## Inputs
- Whether the listing is paginated.
- The columns / row model.
- The row actions (edit, delete, activate/inactivate, etc.) with their permission gates.

## Procedure
1. Render the listing through the project's shared collection-view component. Do not create a parallel wrapper.
2. Provide a unique `viewKey` per page; the user's table/cards choice persists per page.
3. Always provide a card template alongside the table columns. A page without a card template breaks the toggle.
4. Apply the default-mode rule: paginated → default to table, otherwise → default to cards. The user's last choice on this page (saved by `viewKey`) overrides the default on subsequent visits.
5. Drive table and card modes from the same column/row-action/permission/empty-state/loading/error inputs.
6. Ensure card view shows the same information density as a table row: same fields, same row actions, same permission gates, same filters/sorting honored.
7. Use the project's shared card styles (status pill variants, header/title/subtitle/meta/description slots). Do not introduce per-feature card selectors when shared classes apply.
8. All labels are translation keys; no hardcoded colors; verify rendering across every theme declared by the project.

## Output Expectations
- Every list page is consistent in pagination, selection, actions, empty/error states.
- Card and table modes are feature-equivalent (same actions, same permissions, same filters).
- A new list never re-implements pagination, sorting, or selection.
