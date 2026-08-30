import { Loader2 } from "lucide-react";

/**
 * The pending UI for EVERY dashboard route.
 *
 * Without it the App Router has no Suspense boundary between the shell and a page, so a click on a
 * navigation item leaves the current screen untouched — no spinner, no disabled state, nothing — until
 * the server payload for the next route lands, and then the new page appears all at once. That gap is
 * invisible on a warm route and several seconds long on a cold one (the Single Analyzer bundle is the
 * worst case), which reads as a dead click on the first visit of a session and as a working app on
 * every visit after, because the router cache serves the second one.
 *
 * The shell (sidebar, header, org switcher) stays mounted: only the page area is replaced. The
 * `route-pending-appear` delay keeps a warm navigation from flashing this at all.
 */
export default function DashboardRouteLoading() {
  return (
    <div
      role="status"
      aria-live="polite"
      className="route-pending-appear flex min-h-[60vh] w-full flex-col items-center justify-center gap-3 p-6 text-muted-foreground"
    >
      <Loader2 className="h-6 w-6 animate-spin text-primary" aria-hidden />
      <p className="text-sm">Loading…</p>
    </div>
  );
}
