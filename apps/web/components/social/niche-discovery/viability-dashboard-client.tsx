"use client";

/**
 * Viability dashboard — the whole point of the change (design §11, ND-12). Answers "is this niche
 * worth trying in this market?" by ranking a run's opportunities and letting the operator sort and
 * filter across all seven required axes (opportunity, confidence, market, format, trend,
 * competition, automation).
 *
 * Scoped to ONE `DiscoveryRun` per instance (the API surface, design §10, has no cross-run listing
 * endpoint): `runId` is the run whose opportunities are shown. `market`/`format` are constant
 * across a run's opportunities (they are run-level parameters — design §2.1), fetched once from
 * `GET /runs/{runId}` and merged onto every opportunity summary so the shared `ranking`/`filters`
 * modules can treat them uniformly. Comparing two markets is done by opening the dashboard for two
 * runs, matching the run-scoped read model this change persists (ND-11).
 */

import { useEffect, useMemo, useState } from "react";
import { formatAvailableNumber } from "@/lib/social/niche-discovery/availability";
import { applyFilters, type OpportunityFilters } from "@/lib/social/niche-discovery/filters";
import { RANKING_AXES, sortByAxis, type RankingAxis, type SortDirection } from "@/lib/social/niche-discovery/ranking";
import {
  getDiscoveryRun,
  listRunOpportunities,
  type DiscoveryRunStatusResponse,
  type NicheOpportunitySummary,
} from "@/lib/api/niche-discovery";
import { ScorePairBadge } from "@/components/social/niche-discovery/score-pair-badge";
import { interpretRunScores } from "@/lib/social/niche-discovery/score-interpretation";

interface ViabilityDashboardClientProps {
  runId: string;
  token?: string;
  onSelectOpportunity?: (opportunityId: string) => void;
}

const NO_FILTERS: OpportunityFilters = {
  marketCode: null,
  contentFormat: null,
  hasTrend: null,
  competitionBand: null,
};

export function ViabilityDashboardClient({ runId, token, onSelectOpportunity }: ViabilityDashboardClientProps) {
  const [run, setRun] = useState<DiscoveryRunStatusResponse | null>(null);
  const [opportunities, setOpportunities] = useState<NicheOpportunitySummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [axis, setAxis] = useState<RankingAxis>("opportunityScore");
  const [direction, setDirection] = useState<SortDirection>("desc");
  const [filters, setFilters] = useState<OpportunityFilters>(NO_FILTERS);

  useEffect(() => {
    let cancelled = false;
    Promise.all([getDiscoveryRun(runId, token), listRunOpportunities(runId, token)])
      .then(([runStatus, opportunitiesResponse]) => {
        if (cancelled) return;
        setRun(runStatus);
        setOpportunities(opportunitiesResponse.opportunities);
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : "Could not load this run's opportunities.");
      });
    return () => {
      cancelled = true;
    };
  }, [runId, token]);

  // Market and format are run-level, not per-opportunity (design §2.1) — merged here so the shared
  // `ranking`/`filters` modules see one uniform shape across market/format/trend/competition/
  // automation without a second round trip per row.
  const merged = useMemo(
    () =>
      opportunities.map((item) => ({
        ...item,
        marketCode: run?.targetMarketCode ?? "",
        contentFormat: run?.contentFormat ?? "",
      })),
    [opportunities, run]
  );

  const visible = useMemo(() => sortByAxis(applyFilters(merged, filters), axis, direction), [merged, filters, axis, direction]);

  if (error) {
    return <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-4 text-sm">{error}</div>;
  }

  if (!run) {
    return <div className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground">Loading…</div>;
  }

  if (run.status !== "Completed" && run.status !== "PartiallyCompleted") {
    return (
      <div className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground" data-testid="run-not-ready">
        This run is {run.status} — opportunities become available once it completes.
      </div>
    );
  }

  const interpretation = interpretRunScores(opportunities.length);

  return (
    <div className="space-y-4" data-testid="viability-dashboard">
      {/* A run-relative score read as a probability is how an operator builds for a market with no
          audience. The semantics travel with the numbers, not in a doc nobody opens. */}
      <div
        className="rounded-xl border border-border bg-background/30 p-3 text-xs text-muted-foreground"
        data-testid="score-interpretation"
      >
        <p>{interpretation.headline}</p>
        {interpretation.caveat ? (
          <p className="mt-1.5 text-amber-600 dark:text-amber-500" data-testid="score-interpretation-caveat">
            {interpretation.caveat}
          </p>
        ) : null}
      </div>
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <label>
          Sort by:{" "}
          <select value={axis} onChange={(e) => setAxis(e.target.value as RankingAxis)} className="rounded border border-border bg-background px-2 py-1">
            {RANKING_AXES.map((a) => (
              <option key={a} value={a}>
                {a}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          onClick={() => setDirection((d) => (d === "asc" ? "desc" : "asc"))}
          className="rounded border border-border px-2 py-1"
        >
          {direction === "asc" ? "Ascending" : "Descending"}
        </button>
        <label>
          Format:{" "}
          <select
            value={filters.contentFormat ?? ""}
            onChange={(e) => setFilters((f) => ({ ...f, contentFormat: e.target.value || null }))}
            className="rounded border border-border bg-background px-2 py-1"
          >
            <option value="">All</option>
            <option value="video">Video</option>
            <option value="short">Short</option>
          </select>
        </label>
      </div>

      {visible.length === 0 ? (
        <div className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground">
          No opportunities match the current filters.
        </div>
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {visible.map((opportunity) => (
            <button
              type="button"
              key={opportunity.id}
              onClick={() => onSelectOpportunity?.(opportunity.id)}
              className="w-full rounded-2xl border border-border bg-background/40 p-4 text-left transition-colors hover:border-primary/50"
              data-testid="opportunity-row"
            >
              <div className="flex items-start justify-between gap-2">
                <p className="font-semibold">{opportunity.subNicheLabel ?? opportunity.contentPattern}</p>
                <span className="text-xs text-muted-foreground">#{opportunity.rank}</span>
              </div>
              <p className="mt-1 text-xs text-muted-foreground">
                {opportunity.contentPattern} · {run.targetMarketCode} · {run.contentFormat}
              </p>
              <div className="mt-3">
                <ScorePairBadge opportunityScore={opportunity.opportunityScore} confidenceScore={opportunity.confidenceScore} />
              </div>
              {opportunity.excludedSignals.length > 0 ? (
                <p className="mt-2 text-[11px] text-amber-600 dark:text-amber-400">
                  {opportunity.excludedSignals.length} signal(s) excluded — weights renormalized
                  (denominator {formatAvailableNumber(opportunity.effectiveWeightDenominator)})
                </p>
              ) : null}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
