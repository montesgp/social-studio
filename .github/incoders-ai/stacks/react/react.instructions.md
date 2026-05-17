
## `.github/incoders-ai/stacks/react/react.instructions.md`

```md
# React Instructions

## Scope
React applications and component models using TypeScript.

## Component Rules
- Use TypeScript for all public component contracts.
- Use `.tsx` for components and `.ts` for non-visual logic.
- Prefer function components.
- Prefer named exports.
- Prefer composition over inheritance.
- Avoid large monolithic components.
- Split by responsibility:
  - visual component
  - hook
  - service
  - schema
  - types

## Naming
- Use PascalCase for component names.
- Use kebab-case for file names.
- Examples:
  - `client-card.tsx`
  - `idea-evaluation-badge.tsx`
  - `use-current-user.ts`
- Do not use Angular file naming conventions.

## Props
- Define explicit prop types.
- Avoid `any`.
- Avoid excessive optional props.
- If a component has too many props, consider composition.
- Keep domain-specific components inside feature folders.

## Hooks
- Put reusable side effects in hooks.
- Hook names must start with `use`.
- Keep hooks focused.
- Do not hide large business workflows inside UI components.
- Clean up subscriptions, timers, and event listeners.

## State
- Keep state as close as possible to where it is used.
- Avoid unnecessary global state.
- Separate server state from UI state.
- Do not duplicate the same state in multiple places.

## Effects
- Avoid unnecessary `useEffect`.
- Do not use `useEffect` for derived state when a calculation is enough.
- Keep side effects isolated.
- Always include correct dependencies.
- Clean up effects properly.

## Styling
- Prefer Tailwind and shared UI primitives.
- Use `cn()` for conditional classes.
- Avoid repeated hardcoded styles.
- Keep components theme-compatible.

## Reusability
- Reuse existing components before creating new ones.
- Do not duplicate form fields, cards, buttons, modals, badges, or layout primitives.
- Extract shared UI only when it is genuinely reusable.
- Keep business-specific components in their feature folder.

## Data and Services
- Components should not know low-level API details.
- Use services/hooks for API access.
- Keep DTO mapping centralized.
- Always represent loading, error, empty, and success states.

## Accessibility
- Use semantic elements.
- Buttons must be buttons.
- Links must be links.
- Inputs must have labels.
- Interactive elements must be keyboard accessible.

## Testing / Quality
- Keep components deterministic where possible.
- Avoid hidden side effects in render.
- Run targeted checks for affected code.
- Do not introduce duplicated patterns.