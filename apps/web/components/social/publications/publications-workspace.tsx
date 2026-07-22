"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshCcw, Settings, Sparkles } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import { listSocialClients } from "@/lib/api/social-clients";
import type { SocialClient } from "@/lib/api/types";
import { lastSevenDaysRange } from "@/lib/publications/date-range";
import {
  cancelPublication,
  listClientAccountConnections,
  listContentBankItems,
  publishItemNow,
  retryPublication,
  type ClientAccountConnectionResponse,
  type ContentBankItemResponse,
  type PublicationSummaryDto,
} from "@/lib/api/publications";
import type { ContentReviewState } from "@/lib/api/content-studio";
import { ContentBankGrid } from "@/components/social/publications/content-bank-grid";
import { PublicationPlannerBoard } from "@/components/social/publications/publication-planner-board";
import { PublishedList } from "@/components/social/publications/published-list";
import { DestinationPicker } from "@/components/social/publications/destination-picker";
import { ClientAccountConnectionsPanel } from "@/components/social/publications/client-account-connections-panel";
import { ContentStudioPanel } from "@/components/social/content-studio-panel";

function errorMessage(err: unknown): string {
  if (err && typeof err === "object" && "message" in err) {
    return String((err as { message?: unknown }).message ?? "Something went wrong.");
  }
  return "Something went wrong.";
}

/**
 * Container for `/publications` (design §10). Owns the client selection + date-range filter state,
 * fetches the Content Bank listing once, and hands the same data to the Bank/Planner/Published tabs
 * (each a pure presentational projection).
 */
export function PublicationsWorkspace() {
  const token = useAccessToken();
  const { orgId, isInitialized } = useActiveOrg();

  const [clients, setClients] = useState<SocialClient[]>([]);
  const [clientId, setClientId] = useState("");
  const [items, setItems] = useState<ContentBankItemResponse[]>([]);
  const [connections, setConnections] = useState<ClientAccountConnectionResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showConnections, setShowConnections] = useState(false);
  const [showCancelled, setShowCancelled] = useState(false);
  const [stateFilter, setStateFilter] = useState<ContentReviewState | null>(null);

  const defaultRange = useMemo(() => lastSevenDaysRange(), []);
  const [fromUtc, setFromUtc] = useState(defaultRange.fromUtc);
  const [toUtc, setToUtc] = useState(defaultRange.toUtc);

  const [studioTarget, setStudioTarget] = useState<{
    analysisId: string | null;
    setId: string | null;
    selectedItemId: string | null;
    clientId: string;
  } | null>(null);
  const [publishTarget, setPublishTarget] = useState<ContentBankItemResponse | null>(null);

  useEffect(() => {
    if (!isInitialized || !token || !orgId) return;
    let cancelled = false;
    listSocialClients({ page: 1, pageSize: 100 }, token)
      .then((page) => {
        if (cancelled) return;
        setClients(page.items);
        setClientId((current) => current || page.items[0]?.id || "");
      })
      .catch((err) => {
        if (!cancelled) setError(errorMessage(err));
      });
    return () => {
      cancelled = true;
    };
  }, [isInitialized, token, orgId]);

  const loadItems = useCallback(async () => {
    if (!clientId || !token) return;
    setLoading(true);
    setError(null);
    try {
      const [bankItems, clientConnections] = await Promise.all([
        listContentBankItems(clientId, { fromUtc, toUtc, state: stateFilter ?? undefined }, token),
        listClientAccountConnections(clientId, token),
      ]);
      setItems(bankItems);
      setConnections(clientConnections);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setLoading(false);
    }
  }, [clientId, token, fromUtc, toUtc, stateFilter]);

  useEffect(() => {
    void loadItems();
  }, [loadItems]);

  function openItem(item: ContentBankItemResponse) {
    // Bank items are already materialized. Prefer opening the EXISTING owning set directly
    // (`setId`, resolved server-side) so the studio never re-materializes by analysis id — that path
    // is analysis-centric and fails/hangs for content whose source run is not a re-materializable
    // single-analyzer analysis (e.g. MagicGenerator runs). Fall back to the analysis-id path only
    // when the server could not resolve an owning set.
    setStudioTarget(
      item.setId
        ? { analysisId: null, setId: item.setId, selectedItemId: item.id, clientId: item.clientId }
        : { analysisId: item.origin.sourceRunId, setId: null, selectedItemId: null, clientId: item.clientId },
    );
  }

  async function runAction(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      await loadItems();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  function onSelectDestination(connection: ClientAccountConnectionResponse) {
    if (!publishTarget) return;
    const target = publishTarget;
    setPublishTarget(null);
    void runAction(() =>
      publishItemNow(
        target.id,
        {
          clientId: target.clientId,
          clientAccountConnectionId: connection.id,
          socialNetworkId: connection.socialNetworkId,
          networkKey: "instagram",
        },
        token,
      ),
    );
  }

  function onRetry(_item: ContentBankItemResponse, publication: PublicationSummaryDto) {
    void runAction(() => retryPublication(publication.id, token));
  }

  function onCancel(_item: ContentBankItemResponse, publication: PublicationSummaryDto) {
    void runAction(() => cancelPublication(publication.id, token));
  }

  if (!isInitialized) {
    return <div className="h-40 animate-pulse rounded-lg border border-border bg-muted/40" />;
  }

  if (!orgId) {
    return (
      <div className="rounded-lg border border-border bg-muted/20 p-8 text-center text-sm text-muted-foreground">
        Select an organization to view Publications.
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <section className="relative overflow-hidden rounded-3xl border border-border bg-card p-6 shadow-2xl">
        <div className="relative flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div className="max-w-2xl space-y-2">
            <Badge variant="success" className="w-fit">
              <Sparkles className="mr-1 h-3 w-3" /> Publications
            </Badge>
            <h1 className="text-3xl font-semibold tracking-tight md:text-4xl">Content Bank &amp; Planner</h1>
            <p className="text-sm text-muted-foreground">
              Review every generated piece for a client, publish it now, and track it through delivery.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Select value={clientId} onChange={(e) => setClientId(e.target.value)} className="min-w-[220px]">
              {clients.length === 0 ? <option value="">No clients</option> : null}
              {clients.map((client) => (
                <option key={client.id} value={client.id}>
                  {client.name}
                </option>
              ))}
            </Select>
            <Button variant="outline" onClick={() => void loadItems()} disabled={loading}>
              <RefreshCcw className="h-4 w-4" /> Refresh
            </Button>
            <Button variant="outline" onClick={() => setShowConnections((v) => !v)}>
              <Settings className="h-4 w-4" /> Connections
            </Button>
          </div>
        </div>
      </section>

      {error ? (
        <div className="rounded-xl border border-rose-500/40 bg-rose-500/10 p-4 text-sm text-rose-700 dark:text-rose-200">
          {error}
        </div>
      ) : null}

      {showConnections && clientId ? (
        <ClientAccountConnectionsPanel clientId={clientId} organizationId={orgId} token={token} />
      ) : null}

      {!clientId ? (
        <div className="rounded-lg border border-border bg-muted/20 p-8 text-center text-sm text-muted-foreground">
          Select a client to view its Content Bank.
        </div>
      ) : (
        <Tabs defaultValue="bank">
          <TabsList>
            <TabsTrigger value="bank">Bank</TabsTrigger>
            <TabsTrigger value="planner">Planner</TabsTrigger>
            <TabsTrigger value="published">Published</TabsTrigger>
          </TabsList>

          <TabsContent value="bank">
            <ContentBankGrid
              items={items}
              loading={loading}
              fromUtc={fromUtc}
              toUtc={toUtc}
              onRangeChange={(next1, next2) => {
                setFromUtc(next1);
                setToUtc(next2);
              }}
              stateFilter={stateFilter}
              onStateFilterChange={setStateFilter}
              onOpenItem={openItem}
              onPublishNow={setPublishTarget}
              token={token}
            />
          </TabsContent>

          <TabsContent value="planner">
            <div className="mb-2 flex justify-end">
              <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <input
                  type="checkbox"
                  checked={showCancelled}
                  onChange={(e) => setShowCancelled(e.target.checked)}
                  className="h-3.5 w-3.5"
                />
                Show cancelled
              </label>
            </div>
            <PublicationPlannerBoard
              items={items}
              busy={busy}
              showCancelled={showCancelled}
              onOpenItem={openItem}
              onPublishNow={setPublishTarget}
              onRetry={onRetry}
              onCancel={onCancel}
            />
          </TabsContent>

          <TabsContent value="published">
            <PublishedList items={items} onOpenItem={openItem} />
          </TabsContent>
        </Tabs>
      )}

      <DestinationPicker
        open={publishTarget !== null}
        onOpenChange={(open) => {
          if (!open) setPublishTarget(null);
        }}
        connections={connections}
        busy={busy}
        onSelect={onSelectDestination}
      />

      {studioTarget ? (
        <ContentStudioPanel
          analysisId={studioTarget.analysisId}
          setId={studioTarget.setId}
          selectedItemId={studioTarget.selectedItemId}
          clientId={studioTarget.clientId}
          token={token}
          onClose={() => {
            setStudioTarget(null);
            void loadItems();
          }}
        />
      ) : null}
    </div>
  );
}
