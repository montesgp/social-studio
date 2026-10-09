import type { BadgeVariant } from "@/components/ui/badge";

/**
 * Row-level badge tone for the `/workflow/progress` landing list (Feature: persistent, free-standing
 * navigation to enqueued work — reachable without having just enqueued something). Mirrors the inline
 * mapping `WorkflowHistoryPanel` already used (`item.status === "completed" ? "success" : "muted"`),
 * extracted so it is unit-testable and shared instead of duplicated verbatim on the new page.
 */
export function historyStatusBadgeVariant(status: string): BadgeVariant {
  return status === "completed" ? "success" : "muted";
}
