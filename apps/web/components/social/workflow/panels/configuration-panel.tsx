"use client";

import { ChevronDown, ChevronUp, Loader2, Plus, Sparkles, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { Select } from "@/components/ui/select";
import { getSocialNetworkIcon } from "@/lib/social-network-icons";
import type { ContentWorkflowSearchCriterion, ContentWorkflowStrategyMode, SocialClient, SocialNetwork } from "@/lib/api/types";

const searchOptions: Array<{ value: ContentWorkflowSearchCriterion; label: string }> = [
  { value: "niche", label: "Niche" },
  { value: "references", label: "References" },
  { value: "viral", label: "Viral" },
  { value: "competitors", label: "Competitors" },
  { value: "own", label: "Own best performers" }
];

const strategyOptions: Array<{ value: ContentWorkflowStrategyMode; label: string }> = [
  { value: "mixed", label: "Mixed" },
  { value: "educational", label: "Educational" },
  { value: "informative", label: "Informative" },
  { value: "entertainment", label: "Entertainment" }
];

export interface ConfigurationPanelProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  clients: SocialClient[];
  selectedClientId: string;
  onSelectedClientIdChange: (value: string) => void;
  searchCriterion: ContentWorkflowSearchCriterion;
  onSearchCriterionChange: (value: ContentWorkflowSearchCriterion) => void;
  strategyMode: ContentWorkflowStrategyMode;
  onStrategyModeChange: (value: ContentWorkflowStrategyMode) => void;
  maxResults: number;
  onMaxResultsChange: (value: number) => void;
  availableNetworks: SocialNetwork[];
  networkSources: string[];
  onToggleNetwork: (code: string) => void;
  busy: boolean;
  onDiscover: () => void;
}

/**
 * Discovery-only configuration. The action button below is deliberately labelled "Discover candidates"
 * (spec: "Discovery-only candidate action" — must not imply content is generated at discovery time; also
 * renamed away from the old ambiguous "Generate" label).
 */
export function ConfigurationPanel({
  open,
  onOpenChange,
  clients,
  selectedClientId,
  onSelectedClientIdChange,
  searchCriterion,
  onSearchCriterionChange,
  strategyMode,
  onStrategyModeChange,
  maxResults,
  onMaxResultsChange,
  availableNetworks,
  networkSources,
  onToggleNetwork,
  busy,
  onDiscover
}: ConfigurationPanelProps) {
  return (
    <Card>
      <Collapsible open={open} onOpenChange={onOpenChange}>
        <CardHeader className="flex-row items-center justify-between">
          <CardTitle className="text-base">Configuration</CardTitle>
          <CollapsibleTrigger className="text-muted-foreground hover:text-foreground">
            {open ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
          </CollapsibleTrigger>
        </CardHeader>
        <CollapsibleContent>
          <CardContent className="grid gap-4 lg:grid-cols-2 xl:grid-cols-6">
            <div className="space-y-2">
              <label className="text-sm font-medium">Client</label>
              <Select className="w-full" value={selectedClientId} onChange={(event) => onSelectedClientIdChange(event.target.value)}>
                <option value="" disabled>
                  Select client
                </option>
                {clients.map((client) => (
                  <option key={client.id} value={client.id}>
                    {client.name}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-2">
              <label className="text-sm font-medium">Search criterion</label>
              <Select
                className="w-full"
                value={searchCriterion}
                onChange={(event) => onSearchCriterionChange(event.target.value as ContentWorkflowSearchCriterion)}
              >
                {searchOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-2">
              <label className="text-sm font-medium">Content strategy</label>
              <Select
                className="w-full"
                value={strategyMode}
                onChange={(event) => onStrategyModeChange(event.target.value as ContentWorkflowStrategyMode)}
              >
                {strategyOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-2">
              <label className="text-sm font-medium">Max results</label>
              <input
                type="number"
                min={1}
                max={100}
                className="h-10 w-full rounded-md border border-border bg-background px-3 text-sm"
                value={maxResults}
                onChange={(event) => onMaxResultsChange(Number(event.target.value))}
              />
              <p className="text-xs text-muted-foreground">Limit candidates to save AI analysis tokens.</p>
            </div>
            <div className="space-y-2 xl:col-span-2">
              <label className="text-sm font-medium">Source networks</label>
              {availableNetworks.length === 0 ? (
                <p className="text-xs text-muted-foreground">Loading networks…</p>
              ) : (
                <div className="flex flex-wrap gap-2">
                  {networkSources.map((code) => {
                    const network = availableNetworks.find((n) => n.code === code);
                    if (!network) return null;
                    const Icon = getSocialNetworkIcon(code);
                    return (
                      <span
                        key={code}
                        className="flex items-center gap-1.5 rounded-full border border-primary/50 bg-primary/15 px-3 py-1 text-xs font-medium text-primary"
                      >
                        <Icon className="h-3.5 w-3.5 shrink-0" />
                        {network.displayName}
                        <button
                          type="button"
                          onClick={() => onToggleNetwork(code)}
                          className="ml-0.5 text-primary/60 hover:text-primary transition-colors"
                          aria-label={`Remove ${network.displayName}`}
                        >
                          <X className="h-3 w-3" />
                        </button>
                      </span>
                    );
                  })}
                  {availableNetworks
                    .filter((n) => !networkSources.includes(n.code))
                    .map((network) => {
                      const Icon = getSocialNetworkIcon(network.code);
                      return (
                        <button
                          key={network.code}
                          type="button"
                          onClick={() => onToggleNetwork(network.code)}
                          className="flex items-center gap-1.5 rounded-full border border-border/60 bg-background/60 px-3 py-1 text-xs text-muted-foreground hover:border-primary/40 hover:text-foreground transition-colors"
                          aria-label={`Add ${network.displayName}`}
                        >
                          <Icon className="h-3.5 w-3.5 shrink-0" />
                          {network.displayName}
                          <Plus className="h-3 w-3" />
                        </button>
                      );
                    })}
                </div>
              )}
            </div>
            <div className="lg:col-span-2 xl:col-span-6">
              <Button onClick={onDiscover} disabled={busy || !selectedClientId || networkSources.length === 0}>
                {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                Discover candidates
              </Button>
            </div>
          </CardContent>
        </CollapsibleContent>
      </Collapsible>
    </Card>
  );
}
