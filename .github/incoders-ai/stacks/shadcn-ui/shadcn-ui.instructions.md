# shadcn/ui Instructions

## Scope
React/Next.js applications using shadcn/ui, Tailwind and CSS variables.

## Rules
- Treat `components/ui/*` as low-level primitives.
- Do not put business logic inside `components/ui`.
- Business components belong in `features/*/components`.
- Layout components belong in `components/layout`.
- Shared generic components belong in `components/shared`.

## Styling
- Prefer variants and semantic tokens.
- Do not hardcode colors repeatedly.
- Keep dark mode compatibility.
- Use CSS variables for theme-level values.
- Use `cn()` for conditional class names.

## Components
- Reuse existing shadcn components before creating custom primitives.
- Do not fork shadcn components unless necessary.
- If a primitive is customized, keep the API predictable.
- Avoid one-off visual components when a reusable primitive already exists.

## UX
- All forms must show validation states.
- All async actions must show loading states.
- Empty states must be intentional.
- Destructive actions must require confirmation.
- Dialogs, dropdowns, and menus must remain keyboard accessible.