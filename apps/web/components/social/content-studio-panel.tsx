"use client";

import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { AnimatePresence, motion } from "framer-motion";
import {
  AlignCenter,
  AlignLeft,
  AlignRight,
  BadgeCheck,
  Bold,
  Bookmark,
  Heart,
  Italic,
  MessageCircle,
  Minus,
  MoreHorizontal,
  Plus,
  Send,
  Underline,
} from "lucide-react";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import {
  approveItem,
  bulkOperation,
  deleteItem,
  discardItem,
  downloadPresentationPptx,
  duplicateItem,
  editItemText,
  editItemVisual,
  fetchContentItemAsset,
  getContentSet,
  linkItemToPlan,
  listContentItems,
  materializeAnalysis,
  rejectItem,
  spawnVariant,
  stageItem,
  unlinkItemFromPlan,
  type BulkOperation,
  type ContentItem,
  type ContentReviewState,
  type ContentSet,
  type VisualAssetInput,
} from "@/lib/api/content-studio";
import { getContentPlan, listContentPlans } from "@/lib/api/content-plans";
import { listClientOwnAccounts } from "@/lib/api/client-own-accounts";
import type { ContentPlan, ContentPlanSummary } from "@/lib/api/types";

interface ContentStudioPanelProps {
  analysisId: string | null;
  clientId: string | null;
  token: string;
  onClose: () => void;
}

const STATE_VARIANT: Record<ContentReviewState, BadgeVariant> = {
  Draft: "muted",
  InReview: "info",
  Edited: "warning",
  Approved: "success",
  Rejected: "destructive",
  Discarded: "muted",
  ReadyForPublication: "accent",
  HandedOff: "default",
};

const TERMINAL: ContentReviewState[] = ["Discarded", "ReadyForPublication", "HandedOff"];

// ── Composition envelope helpers ───────────────────────────────────────────────

interface SlotGeometry {
  slot: string;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  fontSizePx?: number;
  fontWeight?: string | null;
  fontStyle?: string | null;
  color?: string | null;
  [key: string]: unknown;
}

interface Envelope {
  markup: string;
  canvas?: { width?: number; height?: number };
  flattenedImageRef?: string | null;
  slotGeometry?: SlotGeometry[];
  [key: string]: unknown;
}

type AutosaveStatus = "idle" | "saving" | "saved" | "error";

interface SlotStyle {
  slot: string;
  x: number;
  y: number;
  width: number;
  height: number;
  fontSizePx: number;
  fontWeight: string;
  fontStyle: string;
  color: string;
}

function parseEnvelopes(visualSpecMarkup: string | null): Envelope[] {
  if (!visualSpecMarkup) return [];
  try {
    const parsed = JSON.parse(visualSpecMarkup) as unknown;
    const list = Array.isArray(parsed) ? parsed : [parsed];
    return list.filter((e): e is Envelope => Boolean(e) && typeof (e as Envelope).markup === "string");
  } catch {
    return /<([a-z][\w:-]*)(\s|>)/i.test(visualSpecMarkup)
      ? [{ markup: visualSpecMarkup, canvas: { width: 1080, height: 1080 } }]
      : [];
  }
}

function serializeEnvelopes(envelopes: Envelope[]): string {
  return envelopes.length === 1 ? JSON.stringify(envelopes[0]) : JSON.stringify(envelopes);
}

/** Updates the envelope's slot geometry (position + fonts) from the edited DOM so the PPTX stays faithful. */
function syncGeometry(geometry: SlotGeometry[] | undefined, styles: SlotStyle[]): SlotGeometry[] {
  const bySlot = new Map(styles.map((s) => [s.slot, s]));
  return (geometry ?? []).map((g) => {
    const s = bySlot.get(g.slot);
    return s
      ? {
          ...g,
          x: s.x,
          y: s.y,
          width: s.width,
          height: s.height,
          fontSizePx: s.fontSizePx,
          fontWeight: s.fontWeight,
          fontStyle: s.fontStyle,
          color: s.color,
        }
      : g;
  });
}

function errorMessage(err: unknown): string {
  if (err && typeof err === "object" && "message" in err) {
    return String((err as { message?: unknown }).message ?? "Something went wrong.");
  }
  return "Something went wrong.";
}

function useVisualAutosaveRevision() {
  const latestRevisionRef = useRef(0);
  const savedRevisionRef = useRef(0);
  const [pendingRevision, setPendingRevision] = useState(0);

  const markEdited = useCallback(() => {
    const nextRevision = latestRevisionRef.current + 1;
    latestRevisionRef.current = nextRevision;
    setPendingRevision(nextRevision);
  }, []);

  const markSaveNeeded = useCallback((hasUnsavedChanges: boolean) => {
    if (!hasUnsavedChanges || latestRevisionRef.current !== savedRevisionRef.current) return;

    const nextRevision = latestRevisionRef.current + 1;
    latestRevisionRef.current = nextRevision;
    setPendingRevision(nextRevision);
  }, []);

  const markSavedIfLatest = useCallback((revision: number) => {
    if (latestRevisionRef.current !== revision) return false;

    savedRevisionRef.current = revision;
    setPendingRevision(revision);
    return true;
  }, []);

  return { markEdited, markSaveNeeded, markSavedIfLatest, pendingRevision, savedRevisionRef };
}

export function ContentStudioPanel({ analysisId, clientId, token, onClose }: ContentStudioPanelProps) {
  const [set, setSet] = useState<ContentSet | null>(null);
  const [items, setItems] = useState<ContentItem[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [igHandle, setIgHandle] = useState<string | null>(null);
  const [saveStatusByItemId, setSaveStatusByItemId] = useState<Record<string, AutosaveStatus>>({});
  const [saveErrorByItemId, setSaveErrorByItemId] = useState<Record<string, string | null>>({});

  const [draftText, setDraftText] = useState("");
  const [rejecting, setRejecting] = useState(false);
  const [rejectReason, setRejectReason] = useState("");
  const [bulkSelected, setBulkSelected] = useState<Set<string>>(new Set());
  const [bulkReason, setBulkReason] = useState("");

  // Lock the page scroll behind the full-screen studio so only the studio's own scroll shows.
  useEffect(() => {
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = previous;
    };
  }, []);

  // Materialize once per (analysis, client). materialize is a POST that creates/updates the studio
  // set; React StrictMode double-invokes effects in dev, so two runs would otherwise fire two
  // concurrent POSTs that race on the server. We de-dupe via this ref and DO NOT cancel on cleanup —
  // the second StrictMode run is skipped here, and the result is applied only while this key is still
  // the active one (so a fast client switch can't write stale data, and the lone request's result is
  // never discarded — discarding it left the studio stuck loading).
  const materializeKeyRef = useRef<string | null>(null);
  useEffect(() => {
    if (!analysisId || !clientId) return;
    const key = `${analysisId}:${clientId}`;
    if (materializeKeyRef.current === key) return;
    materializeKeyRef.current = key;
    void (async () => {
      setLoading(true);
      setError(null);
      try {
        const materialized = await materializeAnalysis(analysisId, clientId, token);
        const list = await listContentItems(materialized.id, undefined, token);
        if (materializeKeyRef.current !== key) return;
        setSet(materialized);
        setItems(list);
        setSelectedId((prev) => prev ?? list[0]?.id ?? null);
      } catch (err) {
        if (materializeKeyRef.current === key) {
          setError(errorMessage(err));
          materializeKeyRef.current = null; // allow a retry on the next deps change
        }
      } finally {
        if (materializeKeyRef.current === key) setLoading(false);
      }
    })();
  }, [analysisId, clientId, token]);

  useEffect(() => {
    if (!clientId) return;
    let cancelled = false;
    listClientOwnAccounts(clientId, { pageSize: 5 }, token)
      .then((paged) => {
        if (cancelled) return;
        const account = paged.items?.find((a) => a.isActive) ?? paged.items?.[0];
        setIgHandle(account?.handle ?? null);
      })
      .catch(() => {
        /* no account handle available */
      });
    return () => {
      cancelled = true;
    };
  }, [clientId, token]);

  const selected = useMemo(() => items.find((i) => i.id === selectedId) ?? null, [items, selectedId]);

  useEffect(() => {
    setRejecting(false);
    setRejectReason("");
    setDraftText(selected?.text ?? "");
  }, [selectedId]);

  const refresh = useCallback(
    async (keepId?: string) => {
      if (!set) return;
      const [freshSet, freshItems] = await Promise.all([
        getContentSet(set.id, token),
        listContentItems(set.id, undefined, token),
      ]);
      setSet(freshSet);
      setItems(freshItems);
      if (keepId !== undefined) setSelectedId(keepId);
    },
    [set, token],
  );

  const runAction = useCallback(
    async (action: () => Promise<unknown>, keepId?: string) => {
      setBusy(true);
      setError(null);
      try {
        await action();
        await refresh(keepId);
      } catch (err) {
        setError(errorMessage(err));
      } finally {
        setBusy(false);
      }
    },
    [refresh],
  );

  const patchItem = useCallback((updated: ContentItem) => {
    setItems((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
  }, []);

  const setItemSaveStatus = useCallback((itemId: string, status: AutosaveStatus, message: string | null = null) => {
    setSaveStatusByItemId((prev) => (prev[itemId] === status ? prev : { ...prev, [itemId]: status }));
    setSaveErrorByItemId((prev) => (prev[itemId] === message ? prev : { ...prev, [itemId]: message }));
  }, []);

  const toggleBulk = useCallback((id: string) => {
    setBulkSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const runBulk = useCallback(
    (operation: BulkOperation, reason: string | null) => {
      const ids = [...bulkSelected];
      if (ids.length === 0) return;
      void runAction(async () => {
        await bulkOperation(ids, operation, reason, token);
        setBulkSelected(new Set());
        setBulkReason("");
      });
    },
    [bulkSelected, runAction, token],
  );

  const exportPresentation = useCallback(async () => {
    if (!set) return;
    setBusy(true);
    setError(null);
    try {
      await downloadPresentationPptx(set.id, token);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }, [set, token]);

  const autosaveSelectedText = useCallback(
    (text: string) => {
      if (!selectedId) return Promise.reject(new Error("No content item selected."));
      return editItemText(selectedId, text, token);
    },
    [selectedId, token],
  );

  const autosaveSelectedVisual = useCallback(
    (markup: string | null, assets: VisualAssetInput[]) => {
      if (!selectedId) return Promise.reject(new Error("No content item selected."));
      return editItemVisual(selectedId, markup, assets, token);
    },
    [selectedId, token],
  );

  const handleSelectedSaveStatusChange = useCallback(
    (status: AutosaveStatus, message: string | null = null) => {
      if (!selectedId) return;
      setItemSaveStatus(selectedId, status, message);
    },
    [selectedId, setItemSaveStatus],
  );

  return (
    <AnimatePresence>
      <motion.div
        key="content-studio"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        transition={{ duration: 0.2 }}
        className="fixed inset-0 z-50 flex flex-col bg-background"
        role="dialog"
        aria-label="Content Studio"
      >
        <header className="flex items-center justify-between gap-3 border-b border-border px-5 py-3">
          <div className="flex items-center gap-3">
            <span className="inline-flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-purple-500/30 to-blue-500/30 text-sm font-bold">
              CS
            </span>
            <h2 className="text-lg font-semibold">Content Studio</h2>
            {set ? <ReviewSummaryBar set={set} /> : null}
          </div>
          <div className="flex items-center gap-2">
            <Button size="sm" variant="outline" disabled={busy || !set} onClick={() => void exportPresentation()}>
              Export PPTX
            </Button>
            <Button size="sm" variant="ghost" onClick={onClose}>
              Close
            </Button>
          </div>
        </header>

        {error ? (
          <div className="mx-5 mt-3 rounded-md border border-rose-600/35 bg-rose-100 px-3 py-2 text-sm text-rose-800 dark:bg-rose-500/15 dark:text-rose-200">
            {error}
          </div>
        ) : null}

        {!analysisId || !clientId ? (
          <div className="flex flex-1 items-center justify-center p-8 text-center text-sm text-muted-foreground">
            Select a client context with a generated package to open Content Studio.
          </div>
        ) : loading && !set ? (
          <StudioLoading />
        ) : (
          <motion.div
            initial={{ y: 14, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            transition={{ duration: 0.25, delay: 0.05 }}
            className="grid flex-1 grid-cols-[240px_1fr] overflow-hidden"
          >
            <aside className="flex flex-col gap-2 overflow-y-auto border-r border-border p-3">
              {bulkSelected.size > 0 ? (
                <div className="flex flex-col gap-2 rounded-md border border-primary/40 bg-primary/5 p-2">
                  <span className="text-xs font-medium">{bulkSelected.size} selected</span>
                  <input
                    value={bulkReason}
                    onChange={(e) => setBulkReason(e.target.value)}
                    placeholder="reason (for reject)"
                    className="rounded-md border border-border bg-background px-2 py-1 text-xs"
                  />
                  <div className="flex flex-wrap gap-1.5">
                    <Button size="sm" variant="default" disabled={busy} onClick={() => runBulk("Approve", null)}>
                      Approve
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busy || bulkReason.trim().length === 0}
                      onClick={() => runBulk("Reject", bulkReason)}
                    >
                      Reject
                    </Button>
                    <Button size="sm" variant="ghost" disabled={busy} onClick={() => runBulk("Discard", null)}>
                      Discard
                    </Button>
                    <Button size="sm" variant="destructive" disabled={busy} onClick={() => runBulk("Delete", null)}>
                      Delete
                    </Button>
                    <Button size="sm" variant="ghost" disabled={busy} onClick={() => setBulkSelected(new Set())}>
                      Clear
                    </Button>
                  </div>
                </div>
              ) : null}
              <ItemList
                items={items}
                selectedId={selectedId}
                onSelect={setSelectedId}
                bulkSelected={bulkSelected}
                onToggleBulk={toggleBulk}
              />
            </aside>

            <main className="overflow-y-auto p-5">
              {selected ? (
                <ItemDetail
                  key={selected.id}
                  item={selected}
                  token={token}
                  igHandle={igHandle}
                  busy={busy}
                  saveStatus={saveStatusByItemId[selected.id] ?? "idle"}
                  saveError={saveErrorByItemId[selected.id] ?? null}
                  draftText={draftText}
                  onDraftChange={setDraftText}
                  rejecting={rejecting}
                  rejectReason={rejectReason}
                  onRejectReasonChange={setRejectReason}
                  onStartReject={() => setRejecting(true)}
                  onCancelReject={() => {
                    setRejecting(false);
                    setRejectReason("");
                  }}
                  onSaveText={() => runAction(() => editItemText(selected.id, draftText, token), selected.id)}
                  onAutosaveText={autosaveSelectedText}
                  onAutosaveVisual={autosaveSelectedVisual}
                  onItemSaved={patchItem}
                  onSaveStatusChange={handleSelectedSaveStatusChange}
                  onApprove={() => runAction(() => approveItem(selected.id, token), selected.id)}
                  onConfirmReject={() =>
                    runAction(async () => {
                      await rejectItem(selected.id, rejectReason, token);
                      setRejecting(false);
                      setRejectReason("");
                    }, selected.id)
                  }
                  onDiscard={() => runAction(() => discardItem(selected.id, token), selected.id)}
                  onDuplicate={() =>
                    runAction(async () => {
                      const created = await duplicateItem(selected.id, token);
                      setSelectedId(created.id);
                    })
                  }
                  onDelete={() =>
                    runAction(async () => {
                      await deleteItem(selected.id, token);
                      setSelectedId(null);
                    })
                  }
                  onStage={() => runAction(() => stageItem(selected.id, token), selected.id)}
                  onSaveVisual={(markup, assets) =>
                    runAction(() => editItemVisual(selected.id, markup, assets, token), selected.id)
                  }
                  onCreateVariant={() =>
                    runAction(async () => {
                      const variant = await spawnVariant(selected.id, token);
                      setSelectedId(variant.id);
                    })
                  }
                  planLink={
                    <PlanLinkControl
                      item={selected}
                      clientId={clientId ?? ""}
                      token={token}
                      busy={busy}
                      onLink={(contentPlanId, year, month, calendarSlotId) =>
                        runAction(
                          () =>
                            linkItemToPlan(
                              selected.id,
                              { contentPlanId, periodKind: 0, year, month, calendarSlotId },
                              token,
                            ),
                          selected.id,
                        )
                      }
                      onUnlink={() => runAction(() => unlinkItemFromPlan(selected.id, token), selected.id)}
                    />
                  }
                />
              ) : (
                <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                  Select a content piece to review.
                </div>
              )}
            </main>
          </motion.div>
        )}
      </motion.div>
    </AnimatePresence>
  );
}

function StudioLoading() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-4">
      <motion.div
        animate={{ rotate: 360 }}
        transition={{ repeat: Infinity, duration: 1, ease: "linear" }}
        className="h-10 w-10 rounded-full border-2 border-primary border-t-transparent"
      />
      <motion.p
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 0.2 }}
        className="text-sm text-muted-foreground"
      >
        Entering Content Studio…
      </motion.p>
    </div>
  );
}

function ReviewSummaryBar({ set }: { set: ContentSet }) {
  const s = set.summary;
  const chips: Array<{ label: string; value: number; variant: BadgeVariant }> = [
    { label: "Total", value: s.total, variant: "outline" },
    { label: "Draft", value: s.draft, variant: "muted" },
    { label: "Edited", value: s.edited, variant: "warning" },
    { label: "Approved", value: s.approved, variant: "success" },
    { label: "Rejected", value: s.rejected, variant: "destructive" },
    { label: "Ready", value: s.readyForPublication, variant: "accent" },
    { label: "Handed off", value: s.handedOff, variant: "default" },
  ];
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {chips
        .filter((c) => c.label === "Total" || c.value > 0)
        .map((c) => (
          <Badge key={c.label} variant={c.variant}>
            {c.label}: {c.value}
          </Badge>
        ))}
    </div>
  );
}

function ItemList({
  items,
  selectedId,
  onSelect,
  bulkSelected,
  onToggleBulk,
}: {
  items: ContentItem[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  bulkSelected: Set<string>;
  onToggleBulk: (id: string) => void;
}) {
  if (items.length === 0) {
    return <div className="rounded-lg border border-border p-4 text-sm text-muted-foreground">No content pieces.</div>;
  }
  return (
    <div className="flex flex-col gap-1.5">
      {items.map((item) => (
        <div key={item.id} className="flex items-center gap-2">
          <input
            type="checkbox"
            aria-label={`Select ${item.formatKey}`}
            checked={bulkSelected.has(item.id)}
            onChange={() => onToggleBulk(item.id)}
            className="h-4 w-4 shrink-0"
          />
          <button
            type="button"
            onClick={() => onSelect(item.id)}
            className={cn(
              "flex flex-1 items-center justify-between gap-2 rounded-md border px-3 py-2 text-left text-sm transition-colors",
              item.id === selectedId ? "border-primary/50 bg-primary/10" : "border-border bg-background hover:bg-muted",
            )}
          >
            <span className="truncate font-medium">{item.formatKey}</span>
            <Badge variant={STATE_VARIANT[item.state]}>{item.state}</Badge>
          </button>
        </div>
      ))}
    </div>
  );
}

interface ItemDetailProps {
  item: ContentItem;
  token: string;
  igHandle: string | null;
  busy: boolean;
  saveStatus: AutosaveStatus;
  saveError: string | null;
  draftText: string;
  onDraftChange: (value: string) => void;
  rejecting: boolean;
  rejectReason: string;
  onRejectReasonChange: (value: string) => void;
  onStartReject: () => void;
  onCancelReject: () => void;
  onSaveText: () => void;
  onAutosaveText: (text: string) => Promise<ContentItem>;
  onAutosaveVisual: (markup: string | null, assets: VisualAssetInput[]) => Promise<ContentItem>;
  onItemSaved: (item: ContentItem) => void;
  onSaveStatusChange: (status: AutosaveStatus, message?: string | null) => void;
  onApprove: () => void;
  onConfirmReject: () => void;
  onDiscard: () => void;
  onDuplicate: () => void;
  onDelete: () => void;
  onStage: () => void;
  onSaveVisual: (markup: string | null, assets: VisualAssetInput[]) => void;
  onCreateVariant: () => void;
  planLink: ReactNode;
}

function ItemDetail(props: ItemDetailProps) {
  const { item, busy, draftText } = props;
  const isTerminal = TERMINAL.includes(item.state);
  const canEdit = !isTerminal;
  const canApprove = !["Approved", ...TERMINAL].includes(item.state);
  const canReject = !["Rejected", ...TERMINAL].includes(item.state);
  const canDiscard = !["Discarded", "ReadyForPublication", "HandedOff"].includes(item.state);
  const canStage = item.state === "Approved";
  const isVisual = item.medium !== "Text";
  const parsedScript = item.medium === "Text" ? tryParseReelScript(item.text ?? "") : null;

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="text-base font-semibold">{item.formatKey}</span>
          <Badge variant="muted">{item.medium}</Badge>
          <Badge variant={STATE_VARIANT[item.state]}>{item.state}</Badge>
          {!item.isPrimaryVariant ? <Badge variant="info">Variant</Badge> : null}
        </div>
        <span className="text-xs text-muted-foreground">
          {item.targetNetwork} · {item.targetPlacement}
        </span>
      </div>

      {item.state === "Rejected" && item.rejectionReason ? (
        <p className="text-sm text-rose-700 dark:text-rose-300">Rejected: {item.rejectionReason}</p>
      ) : null}

      {parsedScript ? (
        <EditableScript
          itemId={item.id}
          json={draftText}
          baseText={item.text ?? ""}
          canEdit={canEdit}
          busy={busy}
          onChange={props.onDraftChange}
          onSave={props.onSaveText}
        />
      ) : (
        <>
          {item.targetPlacement === "story" ? (
            <InstagramStoryPost
              key={item.id}
              item={item}
              token={props.token}
              igHandle={props.igHandle}
              canEdit={canEdit && isVisual}
              busy={busy}
              saveStatus={props.saveStatus}
              saveError={props.saveError}
              onAutosaveVisual={props.onAutosaveVisual}
              onItemSaved={props.onItemSaved}
              onSaveStatusChange={props.onSaveStatusChange}
              onSaveVisual={isVisual ? props.onSaveVisual : undefined}
            />
          ) : (
            <InstagramPost
              key={item.id}
              item={item}
              token={props.token}
              igHandle={props.igHandle}
              caption={draftText}
              canEdit={canEdit && isVisual}
              busy={busy}
              saveStatus={props.saveStatus}
              saveError={props.saveError}
              onAutosaveVisual={props.onAutosaveVisual}
              onItemSaved={props.onItemSaved}
              onSaveStatusChange={props.onSaveStatusChange}
              onSaveVisual={isVisual ? props.onSaveVisual : undefined}
            />
          )}

          {item.targetPlacement !== "story" ? (
            <div className="flex flex-col gap-2">
              <label className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Caption / post copy</label>
              <textarea
                value={draftText}
                onChange={(e) => props.onDraftChange(e.target.value)}
                disabled={!canEdit || busy}
                rows={4}
                className="w-full rounded-md border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
                placeholder="No caption."
              />
              <div>
                <CaptionAutosave
                  item={item}
                  value={draftText}
                  canEdit={canEdit}
                  busy={busy}
                  onAutosaveText={props.onAutosaveText}
                  onItemSaved={props.onItemSaved}
                  onSaveStatusChange={props.onSaveStatusChange}
                />
                <AutosaveIndicator status={props.saveStatus} error={props.saveError} />
                <Button
                  size="sm"
                  variant="secondary"
                  disabled={!canEdit || busy || draftText === (item.text ?? "")}
                  onClick={props.onSaveText}
                >
                  Save caption
                </Button>
              </div>
            </div>
          ) : null}
        </>
      )}

      {props.rejecting ? (
        <div className="flex flex-col gap-2 rounded-md border border-rose-600/30 bg-rose-50 p-3 dark:bg-rose-500/10">
          <label className="text-xs font-medium text-rose-800 dark:text-rose-200">Rejection reason</label>
          <textarea
            value={props.rejectReason}
            onChange={(e) => props.onRejectReasonChange(e.target.value)}
            rows={2}
            className="w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
            placeholder="Why is this piece rejected?"
          />
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="destructive"
              disabled={busy || props.rejectReason.trim().length === 0}
              onClick={props.onConfirmReject}
            >
              Confirm reject
            </Button>
            <Button size="sm" variant="ghost" disabled={busy} onClick={props.onCancelReject}>
              Cancel
            </Button>
          </div>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="default" disabled={busy || !canApprove} onClick={props.onApprove}>
            Approve
          </Button>
          <Button size="sm" variant="outline" disabled={busy || !canReject} onClick={props.onStartReject}>
            Reject
          </Button>
          <Button size="sm" variant="warning" disabled={busy || !canStage} onClick={props.onStage}>
            Stage for publication
          </Button>
          <Button size="sm" variant="ghost" disabled={busy || !canDiscard} onClick={props.onDiscard}>
            Discard
          </Button>
          <Button size="sm" variant="ghost" disabled={busy} onClick={props.onDuplicate}>
            Duplicate
          </Button>
          <Button size="sm" variant="ghost" disabled={busy} onClick={props.onCreateVariant}>
            Create variant
          </Button>
          <Button size="sm" variant="destructive" disabled={busy} onClick={props.onDelete}>
            Delete
          </Button>
        </div>
      )}

      {props.planLink}
    </div>
  );
}

interface ReelScriptScene {
  timestamp?: string;
  voiceover?: string;
  onScreenText?: string;
}

interface ReelScript {
  hook?: string;
  scenes?: ReelScriptScene[];
  ctaText?: string;
  suggestedMusicMood?: string;
}

function tryParseReelScript(text: string): ReelScript | null {
  try {
    const parsed = JSON.parse(text) as ReelScript;
    return parsed && Array.isArray(parsed.scenes) ? parsed : null;
  } catch {
    return null;
  }
}

/** Readable view for text formats (reel scripts etc.): one cue per row with its timecode in bold. */
function ScriptPreview({ text }: { text: string }) {
  const script = tryParseReelScript(text);
  if (script) {
    return (
      <div className="rounded-xl border border-border bg-background p-6 text-sm leading-relaxed shadow-sm">
        {script.hook ? (
          <p className="mb-4">
            <span className="mr-2 rounded bg-primary/10 px-1.5 py-0.5 text-[11px] font-semibold uppercase tracking-wide text-primary">
              Hook
            </span>
            {script.hook}
          </p>
        ) : null}
        <div className="flex flex-col gap-3">
          {(script.scenes ?? []).map((scene, i) => (
            <div key={i} className="flex gap-3">
              <span className="w-14 shrink-0 font-semibold tabular-nums text-foreground">
                {scene.timestamp ?? `#${i + 1}`}
              </span>
              <div className="flex flex-col gap-0.5">
                {scene.voiceover ? <span>{scene.voiceover}</span> : null}
                {scene.onScreenText ? (
                  <span className="text-xs text-muted-foreground">On-screen: {scene.onScreenText}</span>
                ) : null}
              </div>
            </div>
          ))}
        </div>
        {script.ctaText ? (
          <p className="mt-4">
            <span className="mr-2 rounded bg-emerald-500/10 px-1.5 py-0.5 text-[11px] font-semibold uppercase tracking-wide text-emerald-600">
              CTA
            </span>
            {script.ctaText}
          </p>
        ) : null}
        {script.suggestedMusicMood ? (
          <p className="mt-2 text-xs text-muted-foreground">♫ {script.suggestedMusicMood}</p>
        ) : null}
      </div>
    );
  }

  // Fallback: keep line breaks, and bold a leading timecode per line if present.
  const lines = text
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter(Boolean);
  return (
    <div className="rounded-xl border border-border bg-background p-6 text-sm leading-relaxed shadow-sm">
      {lines.map((line, i) => {
        const match = /^(\[?\d{1,2}:\d{2}(?::\d{2})?\]?)(\s*[-–—]?\s*)(.*)$/.exec(line);
        return (
          <div key={i} className="py-0.5">
            {match ? (
              <>
                <span className="font-semibold tabular-nums">{match[1]}</span> <span>{match[3]}</span>
              </>
            ) : (
              <span>{line}</span>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** In-place editor for reel-script text items — edit each cue where it is rendered; saves back as JSON. */
function EditableScript({
  itemId,
  json,
  baseText,
  canEdit,
  busy,
  onChange,
  onSave,
}: {
  itemId: string;
  json: string;
  baseText: string;
  canEdit: boolean;
  busy: boolean;
  onChange: (json: string) => void;
  onSave: () => void;
}) {
  const [model, setModel] = useState<ReelScript>(() => tryParseReelScript(json) ?? { scenes: [] });

  // Re-seed from the persisted value when switching to a different item.
  useEffect(() => {
    setModel(tryParseReelScript(json) ?? { scenes: [] });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [itemId]);

  const commit = (next: ReelScript) => {
    setModel(next);
    onChange(JSON.stringify(next));
  };
  const setScene = (index: number, patch: Partial<ReelScriptScene>) => {
    const scenes = [...(model.scenes ?? [])];
    scenes[index] = { ...scenes[index], ...patch };
    commit({ ...model, scenes });
  };
  const addScene = () => commit({ ...model, scenes: [...(model.scenes ?? []), { timestamp: "", voiceover: "", onScreenText: "" }] });
  const removeScene = (index: number) =>
    commit({ ...model, scenes: (model.scenes ?? []).filter((_, i) => i !== index) });

  const dirty = json !== baseText;
  const field =
    "w-full rounded-md border border-border bg-background px-2 py-1 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60";
  const label = "text-[11px] font-semibold uppercase tracking-wide";
  const disabled = !canEdit || busy;

  return (
    <div className="flex flex-col gap-4 rounded-xl border border-border bg-background p-6 shadow-sm">
      <div className="flex flex-col gap-1">
        <span className={cn(label, "text-primary")}>Hook</span>
        <textarea
          className={field}
          rows={2}
          disabled={disabled}
          value={model.hook ?? ""}
          onChange={(e) => commit({ ...model, hook: e.target.value })}
        />
      </div>

      <div className="flex flex-col gap-2">
        <span className={cn(label, "text-muted-foreground")}>Scenes</span>
        {(model.scenes ?? []).map((scene, i) => (
          <div key={i} className="flex items-start gap-2">
            <input
              className={cn(field, "w-16 shrink-0 font-semibold tabular-nums")}
              disabled={disabled}
              value={scene.timestamp ?? ""}
              onChange={(e) => setScene(i, { timestamp: e.target.value })}
              placeholder="0:00"
            />
            <div className="flex flex-1 flex-col gap-1">
              <textarea
                className={field}
                rows={2}
                disabled={disabled}
                value={scene.voiceover ?? ""}
                onChange={(e) => setScene(i, { voiceover: e.target.value })}
                placeholder="Voiceover"
              />
              <input
                className={field}
                disabled={disabled}
                value={scene.onScreenText ?? ""}
                onChange={(e) => setScene(i, { onScreenText: e.target.value })}
                placeholder="On-screen text"
              />
            </div>
            {canEdit ? (
              <button
                type="button"
                aria-label="Remove scene"
                disabled={busy}
                onClick={() => removeScene(i)}
                className="mt-1 grid h-7 w-7 shrink-0 place-items-center rounded text-muted-foreground hover:bg-muted"
              >
                <Minus className="h-4 w-4" />
              </button>
            ) : null}
          </div>
        ))}
        {canEdit ? (
          <button
            type="button"
            disabled={busy}
            onClick={addScene}
            className="inline-flex w-fit items-center gap-1 rounded-md border border-dashed border-border px-2 py-1 text-xs text-muted-foreground hover:bg-muted"
          >
            <Plus className="h-3.5 w-3.5" /> Add scene
          </button>
        ) : null}
      </div>

      <div className="flex flex-col gap-1">
        <span className={cn(label, "text-emerald-600")}>CTA</span>
        <input
          className={field}
          disabled={disabled}
          value={model.ctaText ?? ""}
          onChange={(e) => commit({ ...model, ctaText: e.target.value })}
        />
      </div>

      <div className="flex flex-col gap-1">
        <span className={cn(label, "text-muted-foreground")}>Music mood</span>
        <input
          className={field}
          disabled={disabled}
          value={model.suggestedMusicMood ?? ""}
          onChange={(e) => commit({ ...model, suggestedMusicMood: e.target.value })}
        />
      </div>

      {canEdit ? (
        <div>
          <Button size="sm" variant="secondary" disabled={busy || !dirty} onClick={onSave}>
            Save script
          </Button>
        </div>
      ) : null}
    </div>
  );
}

function CaptionAutosave({
  item,
  value,
  canEdit,
  busy,
  onAutosaveText,
  onItemSaved,
  onSaveStatusChange,
}: {
  item: ContentItem;
  value: string;
  canEdit: boolean;
  busy: boolean;
  onAutosaveText: (text: string) => Promise<ContentItem>;
  onItemSaved: (item: ContentItem) => void;
  onSaveStatusChange: (status: AutosaveStatus, message?: string | null) => void;
}) {
  const seq = useRef(0);
  const persistedRef = useRef(item.text ?? "");

  useEffect(() => {
    persistedRef.current = item.text ?? "";
  }, [item.id, item.text]);

  useEffect(() => {
    if (!canEdit || busy || value === persistedRef.current) return;
    const saveId = ++seq.current;
    onSaveStatusChange("saving");
    const timer = window.setTimeout(() => {
      onAutosaveText(value)
        .then((updated) => {
          if (seq.current !== saveId) return;
          persistedRef.current = updated.text ?? "";
          onItemSaved(updated);
          onSaveStatusChange("saved");
        })
        .catch((err) => {
          if (seq.current !== saveId) return;
          onSaveStatusChange("error", errorMessage(err));
        });
    }, 700);

    return () => {
      seq.current++;
      window.clearTimeout(timer);
    };
  }, [value, canEdit, busy, onAutosaveText, onItemSaved, onSaveStatusChange]);

  return null;
}

function AutosaveIndicator({ status, error }: { status: AutosaveStatus; error: string | null }) {
  if (status === "idle") return null;
  const label =
    status === "saving" ? "Saving…" : status === "saved" ? "Saved" : `Save failed${error ? `: ${error}` : ""}`;
  return (
    <p className={cn("mb-2 text-xs", status === "error" ? "text-destructive" : "text-muted-foreground")}>
      {label}
    </p>
  );
}

function InstagramStory({
  children,
  username,
  slideCount = 1,
  currentIndex = 0,
}: {
  children: ReactNode;
  username?: string;
  slideCount?: number;
  currentIndex?: number;
}) {
  const segments = Math.max(1, slideCount);
  return (
    <div className="relative overflow-hidden rounded-2xl bg-black" style={{ width: 377, height: 670 }}>
      <div className="absolute inset-0">{children}</div>
      <div className="absolute inset-x-0 top-0 z-10 p-3">
        <div className="flex gap-1 mb-2">
          {Array.from({ length: segments }).map((_, i) => (
            <div
              key={i}
              className={cn(
                "flex-1 h-0.5 rounded-full",
                i < currentIndex ? "bg-white/80" : i === currentIndex ? "bg-white" : "bg-white/40",
              )}
            />
          ))}
        </div>
        <div className="flex items-center gap-2">
          <div className="h-8 w-8 rounded-full bg-white/20 border-2 border-white/60" />
          <span className="text-white text-xs font-semibold">{username ?? "Your Brand"}</span>
          <span className="text-white/60 text-xs">2h</span>
          <div className="ml-auto text-white/80 text-base">✕</div>
        </div>
      </div>
    </div>
  );
}

/**
 * Story viewer: editable design fills the 9:16 frame, story chrome overlaid, no engagement sidebar.
 * Supports carousel (multi-slide) stories — navigation moves between slides while keeping the story
 * chrome (top progress bars reflect slide count + position), unlike a feed carousel which shows the
 * Instagram engagement sidebar.
 */
function InstagramStoryPost({
  item,
  token,
  igHandle,
  canEdit,
  busy,
  saveStatus,
  saveError,
  onAutosaveVisual,
  onItemSaved,
  onSaveStatusChange,
  onSaveVisual,
}: {
  item: ContentItem;
  token: string;
  igHandle: string | null;
  canEdit: boolean;
  busy: boolean;
  saveStatus: AutosaveStatus;
  saveError: string | null;
  onAutosaveVisual: (markup: string | null, assets: VisualAssetInput[]) => Promise<ContentItem>;
  onItemSaved: (item: ContentItem) => void;
  onSaveStatusChange: (status: AutosaveStatus, message?: string | null) => void;
  onSaveVisual?: (markup: string | null, assets: VisualAssetInput[]) => void;
}) {
  const [envelopes, setEnvelopes] = useState<Envelope[]>(() => parseEnvelopes(item.visualSpecMarkup));
  const [current, setCurrent] = useState(0);
  const canvasRef = useRef<DesignCanvasHandle>(null);
  const envelopesRef = useRef(envelopes);
  const idxRef = useRef(0);
  const assetsRef = useRef(item.assets);
  const initialJson = useMemo(() => serializeEnvelopes(parseEnvelopes(item.visualSpecMarkup)), [item.visualSpecMarkup]);
  const persistedJsonRef = useRef(initialJson);
  const saveSeq = useRef(0);
  const { markEdited, markSaveNeeded, markSavedIfLatest, pendingRevision, savedRevisionRef } =
    useVisualAutosaveRevision();

  const total = envelopes.length;
  const idx = total === 0 ? 0 : Math.min(current, total - 1);
  const slide = envelopes[idx];
  const fallbackAssetIndex = idx < item.assets.length ? idx : 0;
  envelopesRef.current = envelopes;
  idxRef.current = idx;
  assetsRef.current = item.assets;
  const hasPendingVisualChanges = pendingRevision > savedRevisionRef.current;

  const commitCurrent = useCallback((): Envelope[] => {
    const state = canvasRef.current?.getState();
    const currentEnvelopes = envelopesRef.current;
    if (!state) return currentEnvelopes;
    const currentIdx = idxRef.current;
    const next = currentEnvelopes.map((e, i) =>
      i === currentIdx ? { ...e, markup: state.markup, slotGeometry: syncGeometry(e.slotGeometry, state.slotStyles) } : e,
    );
    envelopesRef.current = next;
    setEnvelopes(next);
    markSaveNeeded(serializeEnvelopes(next) !== persistedJsonRef.current);
    return next;
  }, [markSaveNeeded]);

  useEffect(() => {
    if (!canEdit || busy || pendingRevision <= savedRevisionRef.current) return;
    const saveId = ++saveSeq.current;
    const revisionToSave = pendingRevision;
    onSaveStatusChange("saving");
    const timer = window.setTimeout(() => {
      const next = commitCurrent();
      const markup = serializeEnvelopes(next);
      const assets = assetsRef.current.map((a) => ({ role: a.role, storageUri: a.storageUri, mime: a.mime }));
      onAutosaveVisual(markup, assets)
        .then((updated) => {
          if (saveSeq.current !== saveId) return;
          if (!markSavedIfLatest(revisionToSave)) return;
          persistedJsonRef.current = markup;
          onItemSaved(updated);
          onSaveStatusChange("saved");
        })
        .catch((err) => {
          if (saveSeq.current !== saveId) return;
          onSaveStatusChange("error", errorMessage(err));
        });
    }, 900);

    return () => {
      window.clearTimeout(timer);
    };
  }, [
    pendingRevision,
    savedRevisionRef,
    canEdit,
    busy,
    commitCurrent,
    markSavedIfLatest,
    onAutosaveVisual,
    onItemSaved,
    onSaveStatusChange,
  ]);

  const goTo = (target: number) => {
    commitCurrent();
    setCurrent(((target % total) + total) % total);
  };

  if (!slide) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-background p-10 text-center text-sm italic text-muted-foreground">
        No rendered design available for this piece.
        <AssetFallbackImage item={item} token={token} assetIndex={fallbackAssetIndex} />
      </div>
    );
  }

  const width = slide.canvas?.width ?? 1080;
  const height = slide.canvas?.height ?? 1920;

  return (
    <div className="flex flex-col gap-3">
      <div className="relative w-fit">
        <InstagramStory username={igHandle ?? undefined} slideCount={total} currentIndex={idx}>
          {(slide.markup ?? "").trim().length > 0 ? (
            <DesignCanvas
              key={item.id}
              ref={canvasRef}
              markup={slide.markup ?? ""}
              width={width}
              height={height}
              canEdit={canEdit}
              onDirty={markEdited}
              fallbackImageUrl={slide.flattenedImageRef ?? null}
            />
          ) : (
            <AssetFallbackImage item={item} token={token} assetIndex={fallbackAssetIndex} />
          )}
        </InstagramStory>
        {total > 1 ? (
          <>
            <button
              type="button"
              aria-label="Previous slide"
              onClick={() => goTo(idx - 1)}
              className="absolute left-2 top-1/2 z-20 grid h-8 w-8 -translate-y-1/2 place-items-center rounded-full bg-white/80 text-lg text-black shadow hover:bg-white"
            >
              ‹
            </button>
            <button
              type="button"
              aria-label="Next slide"
              onClick={() => goTo(idx + 1)}
              className="absolute right-2 top-1/2 z-20 grid h-8 w-8 -translate-y-1/2 place-items-center rounded-full bg-white/80 text-lg text-black shadow hover:bg-white"
            >
              ›
            </button>
          </>
        ) : null}
      </div>
      {canEdit ? (
        <div className="flex items-center justify-between gap-2">
          <p className="text-xs text-muted-foreground">Click a text to select it, drag to move, double-click to edit.</p>
          <AutosaveIndicator status={saveStatus} error={saveError} />
          {onSaveVisual ? (
            <Button
              size="sm"
              variant="secondary"
              disabled={busy || !hasPendingVisualChanges}
              onClick={() => {
                const next = commitCurrent();
                onSaveVisual(
                  serializeEnvelopes(next),
                  item.assets.map((a) => ({ role: a.role, storageUri: a.storageUri, mime: a.mime })),
                );
              }}
            >
              Save design changes
            </Button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

/** Instagram-faithful post: editable design (carousel) on the left, IG sidebar on the right. */
function InstagramPost({
  item,
  token,
  igHandle,
  caption,
  canEdit,
  busy,
  saveStatus,
  saveError,
  onAutosaveVisual,
  onItemSaved,
  onSaveStatusChange,
  onSaveVisual,
}: {
  item: ContentItem;
  token: string;
  igHandle: string | null;
  caption: string;
  canEdit: boolean;
  busy: boolean;
  saveStatus: AutosaveStatus;
  saveError: string | null;
  onAutosaveVisual: (markup: string | null, assets: VisualAssetInput[]) => Promise<ContentItem>;
  onItemSaved: (item: ContentItem) => void;
  onSaveStatusChange: (status: AutosaveStatus, message?: string | null) => void;
  onSaveVisual?: (markup: string | null, assets: VisualAssetInput[]) => void;
}) {
  const [envelopes, setEnvelopes] = useState<Envelope[]>(() => parseEnvelopes(item.visualSpecMarkup));
  const [current, setCurrent] = useState(0);
  const canvasRef = useRef<DesignCanvasHandle>(null);
  const envelopesRef = useRef(envelopes);
  const idxRef = useRef(0);
  const assetsRef = useRef(item.assets);
  const initialJson = useMemo(() => serializeEnvelopes(parseEnvelopes(item.visualSpecMarkup)), [item.visualSpecMarkup]);
  const persistedJsonRef = useRef(initialJson);
  const saveSeq = useRef(0);
  const { markEdited, markSaveNeeded, markSavedIfLatest, pendingRevision, savedRevisionRef } =
    useVisualAutosaveRevision();

  const total = envelopes.length;
  const idx = total === 0 ? 0 : Math.min(current, total - 1);
  const slide = envelopes[idx];
  const handle = igHandle ?? "your_account";
  const fallbackAssetIndex = idx < item.assets.length ? idx : 0;
  envelopesRef.current = envelopes;
  idxRef.current = idx;
  assetsRef.current = item.assets;
  const hasPendingVisualChanges = pendingRevision > savedRevisionRef.current;

  const commitCurrent = useCallback((): Envelope[] => {
    const state = canvasRef.current?.getState();
    const currentEnvelopes = envelopesRef.current;
    if (!state) return currentEnvelopes;
    const currentIdx = idxRef.current;
    const next = currentEnvelopes.map((e, i) =>
      i === currentIdx ? { ...e, markup: state.markup, slotGeometry: syncGeometry(e.slotGeometry, state.slotStyles) } : e,
    );
    envelopesRef.current = next;
    setEnvelopes(next);
    markSaveNeeded(serializeEnvelopes(next) !== persistedJsonRef.current);
    return next;
  }, [markSaveNeeded]);

  useEffect(() => {
    if (!canEdit || busy || pendingRevision <= savedRevisionRef.current) return;
    const saveId = ++saveSeq.current;
    const revisionToSave = pendingRevision;
    onSaveStatusChange("saving");
    const timer = window.setTimeout(() => {
      const next = commitCurrent();
      const markup = serializeEnvelopes(next);
      const assets = assetsRef.current.map((a) => ({ role: a.role, storageUri: a.storageUri, mime: a.mime }));
      onAutosaveVisual(markup, assets)
        .then((updated) => {
          if (saveSeq.current !== saveId) return;
          if (!markSavedIfLatest(revisionToSave)) return;
          persistedJsonRef.current = markup;
          onItemSaved(updated);
          onSaveStatusChange("saved");
        })
        .catch((err) => {
          if (saveSeq.current !== saveId) return;
          onSaveStatusChange("error", errorMessage(err));
        });
    }, 900);

    return () => {
      window.clearTimeout(timer);
    };
  }, [
    pendingRevision,
    savedRevisionRef,
    canEdit,
    busy,
    commitCurrent,
    markSavedIfLatest,
    onAutosaveVisual,
    onItemSaved,
    onSaveStatusChange,
  ]);

  const goTo = (target: number) => {
    commitCurrent();
    setCurrent(((target % total) + total) % total);
  };

  if (total === 0) {
    return caption.trim().length > 0 ? (
      <ScriptPreview text={caption} />
    ) : (
      <div className="rounded-xl border border-dashed border-border bg-background p-10 text-center text-sm italic text-muted-foreground">
        No rendered design available for this piece.
        <AssetFallbackImage item={item} token={token} assetIndex={fallbackAssetIndex} />
      </div>
    );
  }

  const width = slide?.canvas?.width ?? 1080;
  const height = slide?.canvas?.height ?? 1080;

  return (
    <div className="flex flex-col gap-3">
      {/* Cap the post width like Instagram web (image ~470px + sidebar) so the preview fits the
          viewport height instead of growing to the full panel width and forcing a vertical scroll. */}
      <div className="mx-auto w-full max-w-[840px] overflow-hidden rounded-xl border border-border bg-background shadow-sm md:flex md:items-stretch">
        {/* Media (editable design) */}
        <div className="relative flex min-w-0 flex-1 items-start justify-center bg-black">
          <div className="relative w-full">
            {(slide?.markup ?? "").trim().length > 0 ? (
              <DesignCanvas
                key={item.id}
                ref={canvasRef}
                markup={slide?.markup ?? ""}
                width={width}
                height={height}
                canEdit={canEdit}
                onDirty={markEdited}
                fallbackImageUrl={slide?.flattenedImageRef ?? null}
              />
            ) : (
              <AssetFallbackImage item={item} token={token} assetIndex={fallbackAssetIndex} />
            )}
            {total > 1 ? (
              <>
                <button
                  type="button"
                  aria-label="Previous slide"
                  onClick={() => goTo(idx - 1)}
                  className="absolute left-2 top-1/2 grid h-8 w-8 -translate-y-1/2 place-items-center rounded-full bg-white/80 text-lg text-black shadow hover:bg-white"
                >
                  ‹
                </button>
                <button
                  type="button"
                  aria-label="Next slide"
                  onClick={() => goTo(idx + 1)}
                  className="absolute right-2 top-1/2 grid h-8 w-8 -translate-y-1/2 place-items-center rounded-full bg-white/80 text-lg text-black shadow hover:bg-white"
                >
                  ›
                </button>
                <div className="absolute bottom-2 left-1/2 flex -translate-x-1/2 items-center gap-1.5">
                  {envelopes.map((_, i) => (
                    <button
                      key={i}
                      type="button"
                      aria-label={`Slide ${i + 1}`}
                      onClick={() => goTo(i)}
                      className={cn("h-1.5 rounded-full transition-all", i === idx ? "w-4 bg-white" : "w-1.5 bg-white/50")}
                    />
                  ))}
                </div>
              </>
            ) : null}
          </div>
        </div>

        {/* Instagram sidebar */}
        <div className="flex min-h-0 w-full flex-col border-t border-border md:w-[360px] md:shrink-0 md:border-l md:border-t-0">
          <div className="flex items-center gap-2 border-b border-border px-3 py-2.5">
            <div className="grid h-8 w-8 place-items-center rounded-full bg-gradient-to-br from-amber-400 via-rose-500 to-purple-600 text-[11px] font-bold text-white">
              {handle.slice(0, 2).toUpperCase()}
            </div>
            <span className="truncate text-sm font-semibold">{handle}</span>
            <BadgeCheck className="h-3.5 w-3.5 shrink-0 text-sky-500" />
            <MoreHorizontal className="ml-auto h-4 w-4 shrink-0 text-muted-foreground" />
          </div>
          <div className="px-3 py-2.5 text-sm">
            {caption ? (
              <p className="whitespace-pre-wrap break-words">
                <span className="font-semibold">{handle}</span> {caption}
              </p>
            ) : (
              <p className="italic text-muted-foreground">No caption yet — write it below.</p>
            )}
          </div>
          <div className="shrink-0 border-t border-border px-3 py-2">
            <div className="flex items-center gap-4 py-1">
              <Heart className="h-5 w-5" />
              <MessageCircle className="h-5 w-5" />
              <Send className="h-5 w-5" />
              <Bookmark className="ml-auto h-5 w-5" />
            </div>
            <p className="text-sm font-semibold">Likes and comments appear once published</p>
            <p className="text-[11px] uppercase tracking-wide text-muted-foreground">
              {new Date(item.createdAtUtc).toLocaleDateString()}
            </p>
          </div>
          <div className="shrink-0 border-t border-border px-3 py-2 text-sm text-muted-foreground">Add a comment…</div>
        </div>
      </div>

      {canEdit ? (
        <div className="flex items-center justify-between gap-2">
          <p className="text-xs text-muted-foreground">Click a text to select it, drag to move, double-click to edit.</p>
          <AutosaveIndicator status={saveStatus} error={saveError} />
          {onSaveVisual ? (
            <Button
              size="sm"
              variant="secondary"
              disabled={busy || !hasPendingVisualChanges}
              onClick={() => {
                const next = commitCurrent();
                onSaveVisual(
                  serializeEnvelopes(next),
                  item.assets.map((a) => ({ role: a.role, storageUri: a.storageUri, mime: a.mime })),
                );
              }}
            >
              Save design changes
            </Button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

interface DesignCanvasHandle {
  getState: () => { markup: string; slotStyles: SlotStyle[] };
}

function AssetFallbackImage({ item, token, assetIndex }: { item: ContentItem; token: string; assetIndex: number }) {
  const [url, setUrl] = useState<string | null>(null);
  const asset = item.assets[assetIndex];

  useEffect(() => {
    if (!asset) return;

    if (/^(https?:|data:|blob:|\/)/i.test(asset.storageUri)) {
      setUrl(asset.storageUri);
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;
    fetchContentItemAsset(item.id, assetIndex, token)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [asset, assetIndex, item.id, token]);

  if (!asset || !url) return null;

  return (
    <img
      src={url}
      alt="Rendered design fallback"
      className="mt-3 max-h-[70vh] max-w-full rounded-lg border border-border object-contain"
    />
  );
}

/** Picks the editable element under a click — any element except the layout wrappers / background. */
function pickElement(node: Node, root: HTMLElement): HTMLElement | null {
  const el = node instanceof HTMLElement ? node : (node.parentElement as HTMLElement | null);
  if (!el || !root.contains(el) || el === root) return null;
  if (
    el.hasAttribute("data-layer") ||
    el.classList.contains("composition") ||
    el.classList.contains("layer-content") ||
    el.classList.contains("layer-background")
  ) {
    return null;
  }
  return el;
}

/** Promotes a flow element to absolute positioning at its current spot so it becomes draggable. */
function ensurePositioned(el: HTMLElement) {
  const pos = getComputedStyle(el).position;
  if (pos === "static" || pos === "") {
    const left = el.offsetLeft;
    const top = el.offsetTop;
    el.style.position = "absolute";
    el.style.left = `${left}px`;
    el.style.top = `${top}px`;
  }
}

/**
 * Strips document-wrapper drift from designer markup before it is injected inline.
 * Weaker models sometimes wrap the composition in a full HTML document
 * (<html><head><style>…</head><body>…). A <style>/<link> injected via innerHTML applies to the WHOLE
 * page — silently changing the studio navbar/item fonts and button styles. The server renderer paints
 * from the spec slots and ignores these tags, so removing them keeps the preview faithful to the
 * actual rendered asset. When a `.composition` subtree is present we keep just that fragment.
 */
function sanitizeDesignMarkup(markup: string): string {
  if (typeof document === "undefined" || !markup) return markup;
  const parser = new DOMParser();
  const doc = parser.parseFromString(markup, "text/html");
  doc.querySelectorAll("script, iframe, object, embed, link, meta, title, base").forEach((n) => n.remove());
  doc.querySelectorAll<HTMLElement>("*").forEach((node) => {
    [...node.attributes].forEach((attr) => {
      const name = attr.name.toLowerCase();
      const value = attr.value.trim().toLowerCase();
      if (name.startsWith("on") || ((name === "href" || name === "src") && value.startsWith("javascript:"))) {
        node.removeAttribute(attr.name);
      }
    });
  });
  const styles = [...doc.head.querySelectorAll("style")].map((style) => style.outerHTML).join("");
  return `${styles}${doc.body.innerHTML}`;
}

/**
 * Renders the design markup inline (imperatively, so React never wipes edits) and turns its
 * [data-slot] elements into selectable, draggable, editable text boxes with a floating toolbar.
 */
const DesignCanvas = forwardRef<
  DesignCanvasHandle,
  { markup: string; width: number; height: number; canEdit: boolean; onDirty: () => void; fallbackImageUrl?: string | null }
>(function DesignCanvas({ markup, width, height, canEdit, onDirty, fallbackImageUrl }, ref) {
  const containerRef = useRef<HTMLDivElement>(null);
  const hostRef = useRef<HTMLDivElement>(null);
  const shadowRef = useRef<ShadowRoot | null>(null);
  const designRef = useRef<HTMLDivElement | null>(null);
  const scaleRef = useRef(0);
  const [scale, setScale] = useState(0);
  const selectedRef = useRef<HTMLElement | null>(null);
  const [selRect, setSelRect] = useState<{ top: number; left: number; width: number; height: number } | null>(null);
  const dragRef = useRef<
    | { mode: "move"; el: HTMLElement; sx: number; sy: number; left: number; top: number; moved: boolean }
    | { mode: "resize"; el: HTMLElement; sx: number; sy: number; w: number; h: number; fs: number; moved: boolean }
    | null
  >(null);

  // Imperative innerHTML — React does not manage these children, so our DOM edits are never wiped.
  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;
    const shadow = shadowRef.current ?? host.attachShadow({ mode: "open" });
    shadowRef.current = shadow;
    shadow.innerHTML = `
      <style>
        :host { all: initial; display: block; contain: content; }
        *, *::before, *::after { box-sizing: border-box; }
        [data-cs-design-root] { position: relative; isolation: isolate; overflow: hidden; background: #fff; }
        [data-slot] { min-width: 1px; min-height: 1em; overflow-wrap: anywhere; white-space: pre-wrap; line-height: 1.1; }
        .composition { position: relative; contain: layout paint style; }
      </style>
      <div data-cs-design-root>${sanitizeDesignMarkup(markup)}</div>`;
    designRef.current = shadow.querySelector<HTMLDivElement>("[data-cs-design-root]");
    const el = designRef.current;
    if (!el) return;
    selectedRef.current = null;
    setSelRect(null);
  }, [markup]);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const update = () => {
      const sc = el.clientWidth / width;
      scaleRef.current = sc;
      setScale(sc);
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(el);
    return () => observer.disconnect();
  }, [width]);

  const positionToolbar = useCallback((target: HTMLElement | null) => {
    const cont = containerRef.current;
    if (!cont || !target) {
      setSelRect(null);
      return;
    }
    const r = target.getBoundingClientRect();
    const cr = cont.getBoundingClientRect();
    setSelRect({ top: r.top - cr.top, left: r.left - cr.left, width: r.width, height: r.height });
  }, []);

  const select = useCallback(
    (target: HTMLElement | null) => {
      const prev = selectedRef.current;
      if (prev && prev !== target) {
        prev.style.outline = "";
        prev.contentEditable = "false";
      }
      selectedRef.current = target;
      if (target) {
        target.style.outline = "2px solid #6366f1";
        target.style.outlineOffset = "1px";
        target.style.cursor = "move";
      }
      positionToolbar(target);
    },
    [positionToolbar],
  );

  useEffect(() => {
    const shadow = shadowRef.current;
    if (!shadow) return;

    const onPointerDown = (e: PointerEvent) => {
      if (!canEdit) return;
      const root = designRef.current;
      if (!root) return;
      const target = pickElement(e.target as Node, root);
      if (!target) return;
      if (target.isContentEditable) return; // editing text — let the caret work
      select(target);
      ensurePositioned(target);
      dragRef.current = {
        mode: "move",
        el: target,
        sx: e.clientX,
        sy: e.clientY,
        left: target.offsetLeft,
        top: target.offsetTop,
        moved: false,
      };
      e.preventDefault();
    };
    const onPointerMove = (e: PointerEvent) => {
      const d = dragRef.current;
      if (!d) return;
      const sc = scaleRef.current || 1;
      if (d.mode === "move") {
        const halfW = d.el.offsetWidth / 2;
        const halfH = d.el.offsetHeight / 2;
        let nl = d.left + (e.clientX - d.sx) / sc;
        let nt = d.top + (e.clientY - d.sy) / sc;
        // Clamp by the element's center so wide boxes still slide sideways freely.
        nl = Math.max(8 - halfW, Math.min(nl, width - halfW - 8));
        nt = Math.max(8 - halfH, Math.min(nt, height - halfH - 8));
        d.el.style.left = `${Math.round(nl)}px`;
        d.el.style.top = `${Math.round(nt)}px`;
        d.el.style.right = "auto";
        d.el.style.bottom = "auto";
      } else {
        const nw = Math.max(24, d.w + (e.clientX - d.sx) / sc);
        const nh = Math.max(16, d.h + (e.clientY - d.sy) / sc);
        d.el.style.width = `${Math.round(nw)}px`;
        d.el.style.fontSize = `${Math.max(8, Math.round(d.fs * (nh / d.h)))}px`;
      }
      d.moved = true;
      positionToolbar(d.el);
    };
    const onPointerUp = () => {
      const d = dragRef.current;
      if (d?.moved) onDirty();
      dragRef.current = null;
    };
    const onDblClick = (e: MouseEvent) => {
      if (!canEdit) return;
      const root = designRef.current;
      if (!root) return;
      const target = pickElement(e.target as Node, root);
      if (!target) return;
      target.contentEditable = "true";
      select(target);
      target.focus();
    };
    const onInput = () => {
      onDirty();
      if (selectedRef.current) positionToolbar(selectedRef.current);
    };
    const onDocPointerDown = (e: PointerEvent) => {
      const t = e.target as HTMLElement;
      if (!t.closest("[data-cs-canvas]") && !t.closest("[data-cs-toolbar]") && !t.closest("[data-cs-handle]")) {
        select(null);
      }
    };
    const onShadowPointerDown: EventListener = (event) => onPointerDown(event as PointerEvent);
    const onShadowDblClick: EventListener = (event) => onDblClick(event as MouseEvent);

    shadow.addEventListener("pointerdown", onShadowPointerDown);
    window.addEventListener("pointermove", onPointerMove);
    window.addEventListener("pointerup", onPointerUp);
    shadow.addEventListener("dblclick", onShadowDblClick);
    shadow.addEventListener("input", onInput);
    document.addEventListener("pointerdown", onDocPointerDown);
    return () => {
      shadow.removeEventListener("pointerdown", onShadowPointerDown);
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
      shadow.removeEventListener("dblclick", onShadowDblClick);
      shadow.removeEventListener("input", onInput);
      document.removeEventListener("pointerdown", onDocPointerDown);
    };
  }, [canEdit, width, height, onDirty, select, positionToolbar]);

  useImperativeHandle(ref, () => ({
    getState() {
      const el = designRef.current;
      if (!el) return { markup, slotStyles: [] };
      const slotStyles: SlotStyle[] = [...el.querySelectorAll<HTMLElement>("[data-slot]")].map((node) => {
        const s = getComputedStyle(node);
        return {
          slot: node.getAttribute("data-slot") ?? "",
          x: node.offsetLeft,
          y: node.offsetTop,
          width: node.offsetWidth,
          height: node.offsetHeight,
          fontSizePx: parseFloat(s.fontSize) || 0,
          fontWeight: s.fontWeight,
          fontStyle: s.fontStyle,
          color: s.color,
        };
      });
      const clone = el.cloneNode(true) as HTMLElement;
      clone.querySelectorAll<HTMLElement>("*").forEach((node) => {
        node.style.outline = "";
        node.style.outlineOffset = "";
        node.style.cursor = "";
        node.removeAttribute("contenteditable");
      });
      return { markup: clone.innerHTML, slotStyles };
    },
  }));

  const apply = (mutator: (el: HTMLElement) => void) => {
    const el = selectedRef.current;
    if (!el) return;
    mutator(el);
    onDirty();
    positionToolbar(el);
  };

  return (
    <div
      data-cs-canvas
      ref={containerRef}
      className="relative w-full overflow-hidden bg-white"
      style={{ height: scale > 0 ? height * scale : undefined }}
    >
      <div
        ref={hostRef}
        style={{ width, height, transform: `scale(${scale})`, transformOrigin: "top left" }}
      />
      {fallbackImageUrl ? (
        <img
          src={fallbackImageUrl}
          alt=""
          aria-hidden="true"
          className={cn(
            "pointer-events-none absolute inset-0 h-full w-full object-cover",
            markup.trim().length === 0 ? "opacity-100" : "opacity-0",
          )}
          onError={(event) => {
            event.currentTarget.style.opacity = "1";
          }}
        />
      ) : null}
      {selRect ? (
        <>
        <div
          data-cs-handle
          onPointerDown={(e) => {
            const sel = selectedRef.current;
            if (!sel) return;
            dragRef.current = {
              mode: "resize",
              el: sel,
              sx: e.clientX,
              sy: e.clientY,
              w: sel.offsetWidth,
              h: sel.offsetHeight,
              fs: parseFloat(getComputedStyle(sel).fontSize) || 16,
              moved: false,
            };
            e.preventDefault();
            e.stopPropagation();
          }}
          className="absolute z-20 h-3.5 w-3.5 cursor-nwse-resize rounded-sm border-2 border-white bg-indigo-500 shadow"
          style={{ top: selRect.top + selRect.height - 7, left: selRect.left + selRect.width - 7 }}
        />
        <div
          data-cs-toolbar
          className="absolute z-10 flex items-center gap-0.5 rounded-md border border-border bg-background p-1 shadow-lg"
          style={{
            top: Math.max(2, selRect.top - 40),
            left: Math.min(Math.max(2, selRect.left), (containerRef.current?.clientWidth ?? 360) - 320),
          }}
        >
          <ToolbarButton
            label="Smaller"
            onClick={() =>
              apply((el) => {
                const cur = parseFloat(getComputedStyle(el).fontSize) || 16;
                el.style.fontSize = `${Math.max(8, Math.round(cur / 1.1))}px`;
              })
            }
          >
            <Minus className="h-4 w-4" />
          </ToolbarButton>
          <ToolbarButton
            label="Bigger"
            onClick={() =>
              apply((el) => {
                const cur = parseFloat(getComputedStyle(el).fontSize) || 16;
                el.style.fontSize = `${Math.min(600, Math.round(cur * 1.1))}px`;
              })
            }
          >
            <Plus className="h-4 w-4" />
          </ToolbarButton>
          <ToolbarButton
            label="Bold"
            onClick={() =>
              apply((el) => {
                const w = getComputedStyle(el).fontWeight;
                el.style.fontWeight = w === "bold" || Number(w) >= 600 ? "400" : "800";
              })
            }
          >
            <Bold className="h-4 w-4" />
          </ToolbarButton>
          <ToolbarButton
            label="Italic"
            onClick={() =>
              apply((el) => {
                el.style.fontStyle = getComputedStyle(el).fontStyle === "italic" ? "normal" : "italic";
              })
            }
          >
            <Italic className="h-4 w-4" />
          </ToolbarButton>
          <ToolbarButton
            label="Underline"
            onClick={() =>
              apply((el) => {
                const d = getComputedStyle(el).textDecorationLine;
                el.style.textDecoration = d.includes("underline") ? "none" : "underline";
              })
            }
          >
            <Underline className="h-4 w-4" />
          </ToolbarButton>
          <ToolbarButton label="Align left" onClick={() => apply((el) => { el.style.textAlign = "left"; })}>
            <AlignLeft className="h-4 w-4" />
          </ToolbarButton>
          <ToolbarButton label="Align center" onClick={() => apply((el) => { el.style.textAlign = "center"; })}>
            <AlignCenter className="h-4 w-4" />
          </ToolbarButton>
          <ToolbarButton label="Align right" onClick={() => apply((el) => { el.style.textAlign = "right"; })}>
            <AlignRight className="h-4 w-4" />
          </ToolbarButton>
          <input
            type="color"
            aria-label="Color"
            title="Color (text, or fill for decorative shapes/lines)"
            onChange={(e) =>
              apply((el) => {
                const value = e.target.value;
                // Text elements recolor their glyphs; decorative shapes/lines (no text content)
                // recolor their fill, so a divider/line bar can actually change color.
                if ((el.textContent ?? "").trim().length > 0) {
                  el.style.color = value;
                } else {
                  el.style.backgroundColor = value;
                }
              })
            }
            className="h-6 w-6 cursor-pointer rounded border border-border bg-transparent p-0"
          />
        </div>
        </>
      ) : null}
    </div>
  );
});

function ToolbarButton({ children, label, onClick }: { children: ReactNode; label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
      className="grid h-7 w-7 place-items-center rounded text-foreground hover:bg-muted"
    >
      {children}
    </button>
  );
}

function PlanLinkControl({
  item,
  clientId,
  token,
  busy,
  onLink,
  onUnlink,
}: {
  item: ContentItem;
  clientId: string;
  token: string;
  busy: boolean;
  onLink: (contentPlanId: string, year: number, month: number, calendarSlotId: string) => void;
  onUnlink: () => void;
}) {
  const [plans, setPlans] = useState<ContentPlanSummary[]>([]);
  const [periodKey, setPeriodKey] = useState("");
  const [plan, setPlan] = useState<ContentPlan | null>(null);
  const [slotId, setSlotId] = useState("");

  useEffect(() => {
    if (!clientId) return;
    let cancelled = false;
    listContentPlans(clientId, token)
      .then((result) => {
        if (!cancelled) setPlans(result);
      })
      .catch(() => {
        /* picker stays empty on error */
      });
    return () => {
      cancelled = true;
    };
  }, [clientId, token]);

  useEffect(() => {
    if (!periodKey || !clientId) {
      setPlan(null);
      setSlotId("");
      return;
    }
    const [year, month] = periodKey.split("-").map(Number);
    let cancelled = false;
    getContentPlan(clientId, year, month, token)
      .then((result) => {
        if (!cancelled) {
          setPlan(result);
          setSlotId("");
        }
      })
      .catch(() => {
        if (!cancelled) setPlan(null);
      });
    return () => {
      cancelled = true;
    };
  }, [periodKey, clientId, token]);

  if (item.planContext) {
    return (
      <div className="flex flex-wrap items-center gap-2 rounded-md border border-border p-2 text-xs">
        <span className="text-muted-foreground">
          Linked to content plan {item.planContext.year}-{String(item.planContext.month).padStart(2, "0")}
        </span>
        <Button size="sm" variant="ghost" disabled={busy} onClick={onUnlink}>
          Unlink
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-wrap items-center gap-2 rounded-md border border-border p-2 text-xs">
      <span className="text-muted-foreground">Link to content plan:</span>
      <select
        value={periodKey}
        onChange={(e) => setPeriodKey(e.target.value)}
        disabled={busy}
        className="rounded-md border border-border bg-background px-2 py-1"
      >
        <option value="">period…</option>
        {plans.map((p) => (
          <option key={p.id} value={`${p.year}-${p.month}`}>
            {p.year}-{String(p.month).padStart(2, "0")}
          </option>
        ))}
      </select>
      <select
        value={slotId}
        onChange={(e) => setSlotId(e.target.value)}
        disabled={busy || plan === null}
        className="rounded-md border border-border bg-background px-2 py-1"
      >
        <option value="">slot…</option>
        {plan?.slots.map((s) => (
          <option key={s.id} value={s.id}>
            Day {s.day} · {s.contentFormat}
          </option>
        ))}
      </select>
      <Button
        size="sm"
        variant="outline"
        disabled={busy || plan === null || slotId.length === 0}
        onClick={() => {
          if (plan && slotId) onLink(plan.id, plan.year, plan.month, slotId);
        }}
      >
        Link
      </Button>
    </div>
  );
}
