"use client";

/**
 * ND-13 — navigable evidence: the actual channels and videos backing the recommendation, reachable
 * as real links, not summarized away. URLs are derived (never trusted from a raw provider field)
 * by `buildEvidenceUrl` (lib/social/niche-discovery/evidence-links.ts).
 */

import { ExternalLink } from "lucide-react";
import { formatAvailableNumber } from "@/lib/social/niche-discovery/availability";
import { buildEvidenceUrl } from "@/lib/social/niche-discovery/evidence-links";
import type { NicheDiscoveryEvidenceRecord } from "@/lib/api/niche-discovery";

interface EvidenceListProps {
  evidence: NicheDiscoveryEvidenceRecord[];
}

export function EvidenceList({ evidence }: EvidenceListProps) {
  const channels = evidence.filter((item) => item.evidenceKind === "Channel");
  const videos = evidence.filter((item) => item.evidenceKind === "Video");

  return (
    <div className="space-y-4" data-testid="evidence-list">
      <EvidenceGroup title="Channels analyzed" items={channels} metricLabel="Subscribers" metricKey="subscriberCount" />
      <EvidenceGroup title="Videos analyzed" items={videos} metricLabel="Views" metricKey="views" />
    </div>
  );
}

function EvidenceGroup({
  title,
  items,
  metricLabel,
  metricKey,
}: {
  title: string;
  items: NicheDiscoveryEvidenceRecord[];
  metricLabel: string;
  metricKey: "subscriberCount" | "views";
}) {
  if (items.length === 0) {
    return null;
  }

  return (
    <div>
      <h4 className="text-sm font-semibold">{title}</h4>
      <ul className="mt-2 space-y-1.5">
        {items.map((item) => {
          const url = buildEvidenceUrl(item.evidenceKind, item.sourceKey, item.externalRef);
          return (
            <li key={item.id} className="flex items-center justify-between gap-2 text-xs">
              {url ? (
                <a
                  href={url}
                  target="_blank"
                  rel="noreferrer"
                  className="flex items-center gap-1 text-primary hover:underline"
                >
                  {item.title || item.channelHandle}
                  <ExternalLink className="h-3 w-3" />
                </a>
              ) : (
                <span>{item.title || item.channelHandle}</span>
              )}
              <span className="text-muted-foreground">
                {metricLabel}: {formatAvailableNumber(item[metricKey])}
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
