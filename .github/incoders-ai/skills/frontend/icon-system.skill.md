# Icon System

## Purpose
One consistent, themable, accessible icon system across the application.

## Scope
Any UI that renders icons.

## Load Conditions
A frontend or fullstack profile is active.

## Do Not Load When
Headless or non-visual code.

## Inputs
- The icon source declared by the active stack/manifest.

## Procedure
1. Use the single icon source the active manifest declares. Do not mix multiple icon libraries.
2. Do not import random SVG sets per feature.
3. Render icons through a shared, reusable wrapper if abstraction is needed.
4. Icon names must be explicit and semantic; usage reflects intent, not visual similarity.
5. Reuse the same icon for the same semantic meaning.
6. Sizing is consistent; avoid inline arbitrary sizing.
7. Never encode meaning only in an icon — text must remain translatable.
8. No hardcoded colors for icons; semantic tokens only; icons adapt to themes.
9. Accessibility: include accessible labels when needed; never use icons as the only indicator of meaning; support screen readers where required.

## Output Expectations
- One icon family across the product.
- Themes affect icons consistently.
- Screen-reader users do not lose meaning that sighted users see.
