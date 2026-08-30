"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshCcw, Settings, Sparkles } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useProgress } from "@/hooks/use-progress";
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
import {
  approveItem,
  bulkOperation,
  deleteItem,
  fetchContentItemAsset,
  rejectItem,
  stageItem,
  type ContentReviewState,
} from "@/lib/api/content-studio";
import { ContentBankGrid } from "@/components/social/publications/content-bank-grid";
import { isOrphanedItem } from "@/lib/publications/review-actions";
import { PublicationPlannerBoard } from "@/components/social/publications/publication-planner-board";
import { PublishedList } from "@/components/social/publications/published-list";
import { DestinationPicker } from "@/components/social/publications/destination-picker";
import { ClientAccountConnectionsPanel } from "@/components/social/publications/client-account-connections-panel";
import { ContentStudioPanel } from "@/components/social/content-studio-panel";

/** Saves a fetched blob to disk via a transient object URL. */
function triggerBlobDownload(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

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
  const progress = useProgress();

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
    /** Explicit multi-piece selection; when set, the studio ignores `setId` and loads exactly these. */
    itemIds: string[] | null;
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

    // Loading the Bank also triggers server-side lazy materialization of any completed analysis that
    // was never opened in Studio, so this call can run for a while on a client with a lot of output.
    // The tab skeletons alone left the operator staring at a frozen page with no idea work was
    // happening; the shared progress card gives the load a visible, page-level identity.
    progress.start("Loading Content Bank");
    try {
      progress.update(35, "Loading Content Bank", "Materializing generated content…");
      const [bankItems, clientConnections] = await Promise.all([
        listContentBankItems(clientId, { fromUtc, toUtc, state: stateFilter ?? undefined }, token),
        listClientAccountConnections(clientId, token),
      ]);
      progress.update(90, "Loading Content Bank", `${bankItems.length} pieces`);
      setItems(bankItems);
      setConnections(clientConnections);
      progress.complete();
    } catch (err) {
      setError(errorMessage(err));
      progress.reset();
    } finally {
      setLoading(false);
    }
    // `progress` is a stable façade over a module store; including it would rebuild this callback
    // on every render and re-fire the load effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clientId, token, fromUtc, toUtc, stateFilter]);

  // The Bank reload is suppressed while Content Studio is open.
  //
  // `loadItems` is rebuilt whenever its inputs change — including `token`, which the auth hook
  // refreshes on its own schedule — and it drives the PAGE-LEVEL progress card. An operator editing
  // a slide would suddenly see "Loading Content Bank" run across a screen that is not even showing
  // the Bank. Studio's own `onClose` already reloads, so nothing goes stale.
  const studioOpen = studioTarget !== null;
  useEffect(() => {
    if (studioOpen) return;
    void loadItems();
  }, [loadItems, studioOpen]);

  function openItem(item: ContentBankItemResponse) {
    // Bank items are already materialized, so the ONLY correct entry point is the existing owning
    // set. The old fallback passed item.origin.sourceRunId as an analysis id, but that field holds
    // the generation JOB id — materializing by it could never succeed and surfaced the misleading
    // "This analysis produced no successful formats to materialize." Fail loudly instead.
    if (!item.setId) {
      setError(
        "This piece has no owning content set, so it cannot be opened in Studio. Reload the Bank; if it persists the item's set membership is missing.",
      );
      return;
    }

    setError(null);
    setStudioTarget({
      analysisId: null,
      setId: item.setId,
      itemIds: null,
      selectedItemId: item.id,
      clientId: item.clientId,
    });
  }

  /**
   * Opens a whole Bank selection in ONE Studio session. The pieces may come from different sets, so
   * this does NOT go through the set entry point — the studio loads the explicit id list and its
   * left rail lists every selected piece, which is the point: reviewing a triaged batch in a single
   * pass instead of reopening the studio per piece.
   */
  function openItems(itemIds: string[]) {
    const selectable = items.filter((i) => itemIds.includes(i.id) && !isOrphanedItem(i));
    if (selectable.length === 0) {
      setError("None of the selected pieces can be opened in Studio. Orphaned pieces can only be deleted.");
      return;
    }

    const skipped = itemIds.length - selectable.length;
    setError(
      skipped > 0
        ? `${skipped} orphaned piece${skipped === 1 ? "" : "s"} left out of the Studio session — orphans can only be deleted.`
        : null,
    );

    setStudioTarget({
      analysisId: null,
      setId: null,
      itemIds: selectable.map((i) => i.id),
      selectedItemId: selectable[0].id,
      clientId: selectable[0].clientId,
    });
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

    // The network key comes from the CHOSEN connection. It used to be hardcoded to "instagram",
    // which silently routed every destination through the Instagram connector no matter which
    // account the operator picked.
    if (!connection.networkKey) {
      setError("This connection has no resolvable network. Fix it in Connections settings first.");
      return;
    }

    void runAction(() =>
      publishItemNow(
        target.id,
        {
          clientId: target.clientId,
          clientAccountConnectionId: connection.id,
          socialNetworkId: connection.socialNetworkId,
          networkKey: connection.networkKey,
        },
        token,
      ),
    );
  }

  // Editorial-axis actions, available straight from the Bank cards so an operator can triage a whole
  // client's output without opening each piece in Content Studio.
  function onApproveItem(item: ContentBankItemResponse) {
    void runAction(() => approveItem(item.id, token));
  }

  function onRejectItem(item: ContentBankItemResponse, reason: string) {
    void runAction(() => rejectItem(item.id, reason, token));
  }

  /** Approved → ReadyForPublication. Keeps the piece in the Planner rather than retiring it. */
  function onStageItem(item: ContentBankItemResponse) {
    void runAction(() => stageItem(item.id, token));
  }

  /**
   * Permanent purge of one piece: the item, its stored assets, its set membership and its
   * publication records. This is the only resolution for an orphaned piece, which the Bank can show
   * but Studio cannot open.
   */
  function onDeleteItem(item: ContentBankItemResponse) {
    void runAction(() => deleteItem(item.id, token));
  }

  /**
   * Same purge across a selection. The endpoint answers 200 with per-item outcomes rather than
   * throwing, so a partial failure has to be read out of the result — otherwise a refused piece
   * would silently look deleted until the next reload put it back on screen.
   */
  async function onDeleteItems(itemIds: string[]) {
    setBusy(true);
    setError(null);
    try {
      const result = await bulkOperation(itemIds, "Delete", null, token);

      // The reload clears the error banner on its way in, so the partial-failure report has to be
      // written after it or the operator never sees which pieces were refused.
      await loadItems();

      if (result.failed > 0) {
        const blocked = result.outcomes.filter(
          (o) => o.errorCode === "social.content_studio.purge_blocked_by_publication",
        ).length;
        setError(
          `${result.succeeded} of ${result.total} pieces deleted. ${result.failed} could not be deleted` +
            (blocked > 0 ? ` (${blocked} still scheduled or already published).` : "."),
        );
      }
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  /**
   * Download for script/copy deliverables, which have no publish path. Assets are fetched through
   * the authenticated content-item asset endpoint, so a plain anchor href would 401.
   */
  async function onDownloadItem(item: ContentBankItemResponse) {
    setError(null);
    if (item.assets.length === 0) {
      // Text-only pieces carry their body inline rather than as a stored asset.
      const body = item.text ?? item.caption ?? "";
      if (!body) {
        setError("This item has nothing to download yet.");
        return;
      }
      triggerBlobDownload(new Blob([body], { type: "text/markdown" }), `${item.formatKey}.md`);
      return;
    }

    try {
      const blob = await fetchContentItemAsset(item.id, 0, token);
      triggerBlobDownload(blob, item.assets[0].storageUri.split("/").pop() || item.formatKey);
    } catch (err) {
      setError(errorMessage(err));
    }
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
              busy={busy}
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
              onApprove={onApproveItem}
              onReject={onRejectItem}
              onStage={onStageItem}
              onDownload={(item) => void onDownloadItem(item)}
              onDelete={onDeleteItem}
              onDeleteMany={(itemIds) => void onDeleteItems(itemIds)}
              onOpenMany={openItems}
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
              token={token}
              busy={busy}
              loading={loading}
              showCancelled={showCancelled}
              onOpenItem={openItem}
              onPublishNow={setPublishTarget}
              onRetry={onRetry}
              onCancel={onCancel}
            />
          </TabsContent>

          <TabsContent value="published">
            <PublishedList items={items} loading={loading} onOpenItem={openItem} />
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
          itemIds={studioTarget.itemIds}
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
