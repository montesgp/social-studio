/**
 * Root cause fix: `ThemeProvider`'s backend sync effect used to run on every `accessToken` change
 * (a token refresh publishes a new token), unconditionally overwriting local `themeKey`/`dateFormat`
 * state with the GET response. That created a race — a background resync could arrive AFTER a user
 * just changed their date format locally and clobber it back to the stale server value.
 *
 * The fix: hydrate from the backend at most ONCE per session. `ThemeProvider` tracks whether it has
 * already applied a successful sync (a ref, since it must survive re-renders without itself
 * triggering one) and calls this pure decision function to decide whether the CURRENT sync attempt
 * should be applied to state.
 */
export function shouldApplyBackendSync(hasHydratedOnce: boolean): boolean {
  return !hasHydratedOnce;
}
