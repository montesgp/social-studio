"use client";

/**
 * ND-13 — navigable evidence: the actual channels and videos backing the recommendation, reachable as
 * real links, not summarized away. URLs are derived (never trusted from a raw provider field) by
 * `buildEvidenceUrl` (lib/social/niche-discovery/evidence-links.ts).
 *
 * CHANNELS ARE THE DELIVERABLE. The operator is choosing a channel to replicate, so channels get the
 * avatar, the metrics and the description; videos are the raw material the niche was detected from, so
 * they collapse into a supporting list. Both remain navigable — demoting videos must not hide them.
 */

import { useState } from "react";
import { ChevronDown, ChevronRight, ExternalLink } from "lucide-react";
import { formatAvailableNumber } from "@/lib/social/niche-discovery/availability";
import { buildEvidenceUrl } from "@/lib/social/niche-discovery/evidence-links";
import { formatCountryLabel } from "@/lib/social/niche-discovery/market-label";
import type { NicheDiscoveryEvidenceRecord } from "@/lib/api/niche-discovery";

interface EvidenceListProps {
  evidence: NicheDiscoveryEvidenceRecord[];
}

export function EvidenceList({ evidence }: EvidenceListProps) {
  const channels = evidence.filter((item) => item.evidenceKind === "Channel");
  const videos = evidence.filter((item) => item.evidenceKind === "Video");

  return (
    <div className="space-y-5" data-testid="evidence-list">
      <ChannelEvidenceGroup channels={channels} />
      <VideoEvidenceGroup videos={videos} />
    </div>
  );
}

function ChannelEvidenceGroup({ channels }: { channels: NicheDiscoveryEvidenceRecord[] }) {
  if (channels.length === 0) {
    return null;
  }

  return (
    <div>
      <h4 className="text-sm font-semibold">Channels working this niche</h4>
      <p className="mt-0.5 text-xs text-muted-foreground">
        Open a channel to judge it yourself — these are the ones the niche was measured on.
      </p>
      <ul className="mt-3 space-y-2">
        {channels.map((channel) => (
          <ChannelRow key={channel.id} channel={channel} />
        ))}
      </ul>
    </div>
  );
}

function ChannelRow({ channel }: { channel: NicheDiscoveryEvidenceRecord }) {
  const url = buildEvidenceUrl("Channel", channel.sourceKey, channel.externalRef);
  const country = formatCountryLabel(channel.country);
  const name = channel.title || channel.channelHandle;

  return (
    <li className="flex gap-3 rounded-xl border border-border bg-background/40 p-3" data-testid="channel-evidence-row">
      {channel.thumbnailUrl ? (
        // eslint-disable-next-line @next/next/no-img-element -- provider-hosted avatar, no known dimensions
        <img
          src={channel.thumbnailUrl}
          alt=""
          className="h-11 w-11 shrink-0 rounded-full object-cover"
          loading="lazy"
        />
      ) : (
        <div className="h-11 w-11 shrink-0 rounded-full bg-muted" aria-hidden="true" />
      )}

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          {url ? (
            <a
              href={url}
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-1 text-sm font-medium text-primary hover:underline"
            >
              {name}
              <ExternalLink className="h-3 w-3" />
            </a>
          ) : (
            <span className="text-sm font-medium">{name}</span>
          )}
          {country ? (
            <span className="rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground" data-testid="evidence-country">
              {country}
            </span>
          ) : null}
        </div>

        {channel.description ? (
          <p className="mt-1 line-clamp-2 text-xs text-muted-foreground" data-testid="channel-description">
            {channel.description}
          </p>
        ) : null}

        <dl className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
          <Metric label="Subscribers" value={channel.subscriberCount} />
          <Metric label="Total views" value={channel.totalViews} />
          <Metric label="Videos" value={channel.videoCount} />
        </dl>
      </div>
    </li>
  );
}

function Metric({ label, value }: { label: string; value: number | null }) {
  return (
    <div className="flex gap-1">
      <dt>{label}:</dt>
      <dd className="font-medium text-foreground">{formatAvailableNumber(value)}</dd>
    </div>
  );
}

/** Supporting evidence: how the niche was detected, not what the operator is choosing between. */
function VideoEvidenceGroup({ videos }: { videos: NicheDiscoveryEvidenceRecord[] }) {
  const [expanded, setExpanded] = useState(false);

  if (videos.length === 0) {
    return null;
  }

  return (
    <div>
      <button
        type="button"
        onClick={() => setExpanded((open) => !open)}
        className="flex items-center gap-1 text-sm font-semibold hover:underline"
        data-testid="video-evidence-toggle"
        aria-expanded={expanded}
      >
        {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        Videos this niche was detected from ({videos.length})
      </button>

      {expanded ? (
        <ul className="mt-2 space-y-1.5">
          {videos.map((video) => {
            const url = buildEvidenceUrl("Video", video.sourceKey, video.externalRef);
            return (
              <li key={video.id} className="flex items-center justify-between gap-2 text-xs">
                {url ? (
                  <a
                    href={url}
                    target="_blank"
                    rel="noreferrer"
                    className="flex items-center gap-1 text-primary hover:underline"
                  >
                    {video.title || video.channelHandle}
                    <ExternalLink className="h-3 w-3" />
                  </a>
                ) : (
                  <span>{video.title || video.channelHandle}</span>
                )}
                <span className="text-muted-foreground">Views: {formatAvailableNumber(video.views)}</span>
              </li>
            );
          })}
        </ul>
      ) : null}
    </div>
  );
}
