# Frontend Code Style

## Purpose
Keep generated and edited frontend files consistent with the repository's established style so new code never requires manual cleanup.

## Scope
TypeScript/JavaScript, HTML/templates, CSS/SCSS/styles, Markdown, tests, and configuration files in frontend repos.

## Load Conditions
A frontend or fullstack profile is active.

## Do Not Load When
Pure backend or infrastructure repos.

## Inputs
- The repository's explicit conventions (`.editorconfig`, `Prettier`, `ESLint`, framework conventions declared by the active stack).

## Procedure
1. Prefer consistency over personal style. Match existing repository conventions when they are explicit. Do not introduce formatting drift. Do not mix multiple formatting styles in the same file or feature.
2. **TypeScript**: clear naming, small focused methods, explicit readonly fields when appropriate, predictable DI style, modern framework patterns (per the active stack), no unnecessary comments, no dead code, no unexplained abbreviations. Avoid deeply nested logic when simpler structure works; avoid mixing responsibilities in one class; never leak data-access concerns into UI components.
3. **HTML/templates**: semantic structure, clear indentation, minimal template complexity, translated user-facing strings only, reusable components instead of duplicated blocks. Avoid large monolithic templates, hardcoded UI strings, and duplicated markup that should be a shared component.
4. **CSS/SCSS**: semantic token usage, CSS variables and theme tokens, shallow readable nesting, reusable patterns over repetition. Avoid hardcoded color literals, deep selector chains, duplicated style blocks, and component-specific hacks that should live in shared primitives.
5. **Markdown**: clear section headings, concise purpose-first documentation, consistent bullet usage, repository-specific wording. Avoid overly verbose explanations and generic documentation not aligned with the project.
6. **Tests**: behavior-focused, clear naming, consistent structure, readable arrange/act/assert. Use the active stack's declared test framework (Jasmine/Jest/Vitest as the manifest declares); do not mix test frameworks. Avoid implementation-coupled assertions, shallow placeholder tests, duplicated setup, and inconsistent async handling.
7. Follow the active stack's component conventions (standalone components, server components, function components — whatever the active stack declares).
8. Keep UI, orchestration, and data layers separated. Components do not call HTTP/network transport directly.
9. When `.editorconfig`, `Prettier`, or `ESLint` configuration exists, follow it strictly. Do not intentionally format against those standards.

## Output Expectations
- Generated code looks like it belongs to the repository without manual style cleanup.
- A new reuse-able pattern matches existing naming, file structure, and documentation conventions.
