"use client";

/**
 * Opportunity detail view (ND-13, design §11). Exposes: name/description, niche/sub-niche, formats
 * and content patterns found, both scores (side by side, never averaged), channels analyzed,
 * videos analyzed, analysis period, data freshness, median views, outlier distribution,
 * independent-channel repetition count, estimated target-market competition, automation
 * suitability, main content patterns, risks/limitations, and navigable evidence.
 *
 * "Creator independence" (ND-13) and "independent-channel repetition count" (ND-13) are the SAME
 * measured signal here — `CrossChannelRepetition` counts independent channels publishing the same
 * pattern, which is exactly what establishes creator independence for this opportunity (authored
 * decision, flagged in the WU-12 commit; the DTO has one field, not two).
 */

import { useEffect, useState } from "react";
import { formatAvailableNumber } from "@/lib/social/niche-discovery/availability";
import { describeAnalysisPeriod, describeDataFreshness, describeOpportunityName, deriveRisks } from "@/lib/social/niche-discovery/opportunity-narrative";
import { getOpportunityDetail, type OpportunityDetailResponse } from "@/lib/api/niche-discovery";
import { ScorePairBadge } from "@/components/social/niche-discovery/score-pair-badge";
import { ExcludedSignalsNote } from "@/components/social/niche-discovery/excluded-signals-note";
import { EvidenceList } from "@/components/social/niche-discovery/evidence-list";

interface OpportunityDetailProps {
  opportunityId: string;
  /** From the run's status projection — market/format are run-level, not per-opportunity. */
  runWindowDays: number;
  runSemanticDegraded: boolean;
  runUnconfiguredSourceKeys: string[];
  runFailedSourceKeys: string[];
  token?: string;
}

export function OpportunityDetail({
  opportunityId,
  runWindowDays,
  runSemanticDegraded,
  runUnconfiguredSourceKeys,
  runFailedSourceKeys,
  token,
}: OpportunityDetailProps) {
  const [detail, setDetail] = useState<OpportunityDetailResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    getOpportunityDetail(opportunityId, token)
      .then((response) => {
        if (!cancelled) setDetail(response);
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : "Could not load this opportunity.");
      });
    return () => {
      cancelled = true;
    };
  }, [opportunityId, token]);

  if (error) {
    return <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-4 text-sm">{error}</div>;
  }

  if (!detail) {
    return <div className="rounded-xl border border-border bg-background/30 p-6 text-sm text-muted-foreground">Loading…</div>;
  }

  const { opportunity, excludedSignals, evidence } = detail;
  const risks = deriveRisks({
    semanticDegraded: runSemanticDegraded,
    unconfiguredSourceKeys: runUnconfiguredSourceKeys,
    failedSourceKeys: runFailedSourceKeys,
    channelSampleSize: opportunity.channelSampleSize,
    videoSampleSize: opportunity.videoSampleSize,
    excludedSignalKeys: excludedSignals.map((s) => s.key),
  });

  return (
    <div className="space-y-5" data-testid="opportunity-detail">
      <header>
        <h3 className="text-lg font-semibold">
          {describeOpportunityName(opportunity.contentPattern, opportunity.subNicheLabel)}
        </h3>
        <p className="mt-1 text-sm text-muted-foreground">
          {opportunity.contentPattern} — {opportunity.topicLabels.join(", ") || "no topic labels"}
        </p>
      </header>

      <ScorePairBadge opportunityScore={opportunity.opportunityScore} confidenceScore={opportunity.confidenceScore} />
      <ExcludedSignalsNote excludedSignals={excludedSignals} effectiveWeightDenominator={opportunity.effectiveWeightDenominator} />

      <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm md:grid-cols-3">
        <Field label="Channels analyzed" value={String(opportunity.channelSampleSize)} />
        <Field label="Videos analyzed" value={String(opportunity.videoSampleSize)} />
        <Field label="Analysis period" value={describeAnalysisPeriod(runWindowDays)} />
        <Field
          label="Data freshness"
          value={describeDataFreshness(new Date(opportunity.observedAtUtc), new Date())}
        />
        <Field label="Median views" value={formatAvailableNumber(opportunity.medianViews)} />
        <Field label="Outlier ratio" value={formatAvailableNumber(opportunity.outlierRatio)} />
        <Field label="Outlier density" value={formatAvailableNumber(opportunity.outlierDensity)} />
        <Field
          label="Independent-channel repetition (creator independence)"
          value={formatAvailableNumber(opportunity.crossChannelRepetition)}
        />
        <Field label="Est. target-market competition" value={formatAvailableNumber(opportunity.targetMarketCompetition)} />
        <Field label="Target-market demand (rank in run)" value={formatAvailableNumber(opportunity.targetMarketDemand)} />
        <Field label="Target-market demand (absolute)" value={formatAvailableNumber(opportunity.targetMarketRawDemand)} />
        <Field label="Target-market gap" value={formatAvailableNumber(opportunity.targetMarketGap)} />
        <Field label="Automation suitability" value={formatAvailableNumber(opportunity.automationSuitability)} />
      </dl>

      {risks.length > 0 && (
        <div className="rounded-lg border border-border bg-background/40 p-3 text-xs" data-testid="opportunity-risks">
          <p className="font-semibold">Risks / limitations</p>
          <ul className="mt-1 list-disc space-y-0.5 pl-4 text-muted-foreground">
            {risks.map((risk) => (
              <li key={risk}>{risk}</li>
            ))}
          </ul>
        </div>
      )}

      <EvidenceList evidence={evidence} />
    </div>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={`font-medium ${isAvailableValue(value) ? "" : "italic text-muted-foreground"}`}>{value}</dd>
    </div>
  );
}

function isAvailableValue(value: string): boolean {
  return value !== "unavailable";
}
