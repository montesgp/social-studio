"use client";

import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { getSocialNetworkIcon } from "@/lib/social-network-icons";
import { cn } from "@/lib/utils";
import type { ContentWorkflowResult, SocialNetwork } from "@/lib/api/types";

function strategyBadgeVariant(strategy: string): BadgeVariant {
  switch (strategy) {
    case "educational":
      return "info";
    case "informative":
      return "accent";
    case "entertainment":
      return "warning";
    default:
      return "muted";
  }
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/60 px-2 py-1">
      <p className="text-[10px] uppercase tracking-widest text-muted-foreground">{label}</p>
      <p className="text-sm font-medium">{value}</p>
    </div>
  );
}

export interface CandidateCardProps {
  result: ContentWorkflowResult;
  viewMode: "grid" | "video";
  isActive: boolean;
  isSelected: boolean;
  isComposed: boolean;
  availableNetworks: SocialNetwork[];
  onToggleSelected: (resultId: string) => void;
  onOpen: (resultId: string) => void;
}

/**
 * Discovery-only candidate presentation. No per-card generation actions (script/carousel/story/analyze
 * were fake, deleted per spec's "Removal of mock generation paths"). Selecting a candidate opens the
 * format-composition editor for it.
 */
export function CandidateCard({
  result,
  viewMode,
  isActive,
  isSelected,
  isComposed,
  availableNetworks,
  onToggleSelected,
  onOpen
}: CandidateCardProps) {
  return (
    <article
      className={cn(
        "rounded-2xl border bg-background/40 p-4",
        isActive ? "border-primary/70 bg-primary/10" : isComposed ? "border-emerald-500/50" : "border-border"
      )}
    >
      <div className={cn(viewMode === "video" && "flex items-start gap-3")}>
        {viewMode === "video" && result.thumbnailUrl && (
          <a href={result.videoUrl} target="_blank" rel="noreferrer" className="flex-shrink-0">
            <img src={result.thumbnailUrl} alt={result.title} className="h-12 w-[72px] rounded-md object-cover" />
          </a>
        )}
        <div className={cn(viewMode === "video" && "min-w-0 flex-1")}>
          <div className="flex flex-wrap items-start justify-between gap-2">
            <label className="flex items-center gap-2 text-xs text-muted-foreground">
              <input type="checkbox" checked={isSelected} onChange={() => onToggleSelected(result.id)} />
              Select
            </label>
            <div className="flex flex-wrap items-center gap-1.5">
              {isComposed && (
                <Badge variant="success" className="text-[10px]">
                  Composed
                </Badge>
              )}
              <Badge variant={strategyBadgeVariant(result.strategyMode)} className="text-[10px] uppercase tracking-widest">
                {result.strategyMode}
              </Badge>
              {(() => {
                const NetIcon = getSocialNetworkIcon(result.networkSource);
                const networkLabel =
                  availableNetworks.find((n) => n.code === result.networkSource)?.displayName ?? result.networkSource;
                return (
                  <span title={networkLabel}>
                    <NetIcon className="h-4 w-4 text-muted-foreground shrink-0" />
                  </span>
                );
              })()}
            </div>
          </div>

          <button type="button" className="mt-2 w-full text-left" onClick={() => onOpen(result.id)}>
            <p className="font-semibold">{result.title}</p>
            <p className="mt-1 text-xs text-muted-foreground">{result.summary}</p>
          </button>

          <div className="mt-3 grid grid-cols-3 gap-2 text-xs text-muted-foreground">
            <Fact label="Views" value={result.views.toLocaleString()} />
            <Fact label="Likes" value={result.likes.toLocaleString()} />
            <Fact label="Comments" value={result.comments.toLocaleString()} />
          </div>
        </div>
      </div>
    </article>
  );
}
