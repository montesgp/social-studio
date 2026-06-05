# UX Focus Management

## Purpose
Ensure every form, tool-panel, dialog, or routed section immediately guides user attention to the correct starting point — on mount, on navigation, and on validation failure. No interactive surface should open without a designated focus target.

## Scope
Any frontend surface that appears, opens, or becomes active due to a user action (create, edit, navigate, route, step transition).

## Load Conditions
A frontend or fullstack profile is active.

## Do Not Load When
Static content-only pages with no forms, dialogs, or interactive panels.

## Inputs
- The trigger: mount, route, dialog open, step transition, or failed form submit.

## Procedure

### 1. On mount / open
Apply to every form, dialog, sheet, drawer, side-panel, or modal that opens in response to a user action:
- Designate a focus target: the first required field, the primary input, or the first editable control.
- Use the active stack's focus API (e.g., `autoFocus` prop, `useEffect` + `ref.focus()`, Angular `ElementRef.nativeElement.focus()`).
- For containers that trap focus (dialogs, drawers, sheets): configure initial focus to land on the primary input field — not the close button or the container wrapper itself.
- On wizard or multi-step form step transitions: move focus to the first input of the incoming step immediately after the transition completes.

### 2. On route / scroll navigation
When a user CTA routes to a page containing a primary form or tool:
- If the form/tool is below the fold or not immediately visible, scroll it into view on mount.
- When navigating to a specific section (hash link, deep link, anchor): ensure the viewport positions at that section. Never rely on the user to scroll to locate the relevant widget.
- Respect `prefers-reduced-motion`: skip the animation but still reposition the viewport.

### 3. On validation failure (submit with errors)
After a failed form submit, apply this fallback chain in order:
1. Focus and scroll to the **first tabbable invalid control in DOM (tab) order**.
2. If the invalid control is a composite widget (custom select, date picker, checkbox group, file input): focus its trigger element or the fieldset legend of its group.
3. If no individual field can receive focus: focus an error summary / alert region at the top of the form.

Use the active form library's focus API (e.g., `form.setFocus(field)` in React Hook Form, `control.markAsTouched()` + scroll in Angular Reactive Forms).

Do not rely on toast notifications or inline error messages alone — direct cursor focus on the invalid element is mandatory after a failed submit.

## Output Expectations
- Every form, dialog, sheet, or drawer designates a focus target on open. No surface opens without one.
- Validation failures direct the cursor to the first field to fix without requiring manual scroll.
- Route and deep-link navigation positions the viewport at the relevant section or form.
- Motion-sensitive users receive a no-animation fallback that still repositions the scroll.
