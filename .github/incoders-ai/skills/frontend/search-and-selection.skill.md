# Search and Selection

## Purpose
Centralize and reuse search-driven and selection-driven components (autocomplete, async search, remote lookup, multiselect, entity selector, chip selection, searchable filter controls).

## Scope
Any frontend component that lets the user search, look up, or select from a set of options.

## Load Conditions
A frontend or fullstack profile is active.

## Do Not Load When
Pure backend or infrastructure repos.

## Inputs
- The data source (local options, async backend query).
- The selection model (single, multi).

## Procedure
1. Reuse the project's shared search/selection primitives before creating new ones.
2. Keep HttpClient/network transport out of components; the search calls a service, gateway, or facade.
3. For async search: debounce requests, handle loading, empty, and error states explicitly, avoid duplicate concurrent requests.
4. For multiselect: support translated labels, clear selected-state rendering, search within options, clear-selection action, disabled state, empty state.
5. Align with the project's shared filter-panel and shared data-table patterns; do not build isolated mini-filter systems per feature.
6. All labels, placeholders, and empty-state messages use translation keys.
7. No hardcoded colors; state styles use semantic design tokens.

## Output Expectations
- Search/selection feels like one component family across the product.
- Tests cover loading, empty, error, selection, and multiselect state.
- Components are transport-agnostic.
