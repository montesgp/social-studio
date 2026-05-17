# Multi-Theme Support

## Purpose
Implement theming through semantic design tokens so the application scales to multiple themes without rewriting feature styles.

## Scope
Any frontend code that styles user-facing components.

## Load Conditions
A frontend or fullstack profile is active and the active manifest declares more than one theme (or expects future themes).

## Do Not Load When
A single-theme application with no foreseeable theming requirement.

## Inputs
- The themes declared by the active project manifest.

## Procedure
1. Implement themes through semantic design tokens, not raw palette values.
2. Components consume semantic tokens (e.g. `--text-primary`, `--surface-muted`, `--status-warning-bg`), never hex colors.
3. Theme switching must affect both the UI framework's components and the project's custom components consistently.
4. Preserve consistent elevation, radius, borders, focus styles, density, and typography across themes.
5. Maintain AA contrast (or higher when specified) in every theme.
6. Use light/dark-aware tokens when a theme set spans both modes.
7. Themes must scale: adding a new theme requires no feature-style refactor.

## Output Expectations
- No feature file contains a literal hex/rgb color.
- Adding a theme is a token-pack change, not a feature change.
- Accessibility (contrast, focus) is preserved across all declared themes.
