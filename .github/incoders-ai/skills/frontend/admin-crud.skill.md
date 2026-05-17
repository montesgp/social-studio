# Admin CRUD

## Purpose
A CRUD/admin management module is not considered implemented unless it is fully manageable. Define what "complete" means.

## Scope
Any admin or operator management screen.

## Load Conditions
A frontend or fullstack profile is active and an admin/CRUD module is being created or refactored.

## Do Not Load When
Public-facing screens that are read-only by design.

## Inputs
- The entity contract.
- The required record-level actions.

## Procedure
1. Deliver the full page shell: page header, help button, primary "new" action, real route-backed screen.
2. Deliver discovery and control: filter panel, sorting controls (or sorting builder), paginated list/table/card view, loading state, empty state, error state.
3. Deliver record actions: edit, delete, row selection, bulk selection when applicable, bulk delete when applicable, confirmation dialog for destructive actions.
4. Deliver the data boundary: typed contracts, repository/port interface, mock implementation if the API is missing, zero direct data access in the component.
5. Deliver forms: shared validators, primary actions disabled until technical and business rules pass, shared form-shell when available.
6. Reuse shared primitives first: button variants, page header, section card/panel, filter panel, form shell, list/table shell, empty/error state, confirmation dialog, helper dialog, blocking loader overlay, status badge/chips, pagination controls.
7. For catalog/reference entities: include `is_active` in forms, show active status in lists, filter active records by default in consumer flows, expose inactive records only when explicitly requested.
8. For orderable entities: use shared drag-and-drop behavior; persist through `sort_order`; use a separate prominence/weight field (e.g. `preponderance`) when the entity also requires weighted business prominence. Do not implement visual-only reorder.

## Forbidden outcomes
- Menu item without a real CRUD screen
- Plain table without management actions
- Edit-only list
- Screen without the filter/sort/pagination pattern where management is expected
- Direct API/infrastructure logic in components
- "Backend is missing" as an excuse not to build the screen

## Output Expectations
- A new admin module is delivered complete in one task, not as a menu placeholder.
- The shared list/form/notification primitives are reused, not duplicated per feature.
- Mock-backed CRUDs are fully functional and clearly marked as mock-backed.
