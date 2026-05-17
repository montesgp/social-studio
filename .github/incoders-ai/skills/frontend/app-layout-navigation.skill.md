# App Layout and Navigation

## Purpose
One reusable, modern, responsive layout shell and navigation system across the application.

## Scope
App shell, sidebar, top navigation, mobile drawer, route groups, reusable navigation structures.

## Load Conditions
A frontend or fullstack profile is active and shell/navigation work is in scope.

## Do Not Load When
Pure backend or infrastructure repos.

## Inputs
- The route map and primary navigation sections.
- The supported breakpoints.

## Procedure
1. Use one shared app shell. Reuse before create.
2. Avoid multiple unrelated navbar/sidebar patterns.
3. Default structure: top-level shell, sidebar for desktop, drawer or collapsible nav for mobile, content area separated from navigation chrome, support for nested navigation when needed.
4. Navigation supports: primary sections, optional subitems, active-route indication, collapse/expand, user-controlled visibility when appropriate, responsive adaptation.
5. Desktop: prefer persistent sidebar when screen size allows; allow collapse to compact state when justified.
6. Mobile: prefer drawer/overlay nav; do not force desktop sidebar behavior into mobile layouts.
7. Before creating any new navigation structure: check if a shared shell exists, reuse it, extend shared nav config if necessary, never duplicate shell behavior.
8. Prefer route-driven menu configuration with explicit translation-key labels, icon metadata where needed, role-aware visibility only when business rules require.
9. Accessibility: keyboard accessible, screen-reader understandable, clear active and collapsed states, usable on mobile and desktop.
10. i18n: every label/tooltip uses translation keys. Theming: no hardcoded colors; semantic tokens only. State: centralize nav state (expand/collapse, mobile drawer open/closed, selected group); do not scatter it across unrelated components.

## Output Expectations
- One shell, one navigation system across the product.
- Navigation is themable, translatable, accessible, and responsive.
- New sections cost configuration, not new shells.
