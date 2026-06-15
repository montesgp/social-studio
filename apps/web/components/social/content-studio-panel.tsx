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
  getContentSet,
  linkItemToPlan,
  listContentItems,
  materializeRun,
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
  jobId: string | null;
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
  slotGeometry?: SlotGeometry[];
  [key: string]: unknown;
}

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
    return [];
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

export function ContentStudioPanel({ jobId, clientId, token, onClose }: ContentStudioPanelProps) {
  const [set, setSet] = useState<ContentSet | null>(null);
  const [items, setItems] = useState<ContentItem[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [igHandle, setIgHandle] = useState<string | null>(null);

  const [draftText, setDraftText] = useState("");
  const [rejecting, setRejecting] = useState(false);
  const [rejectReason, setRejectReason] = useState("");
  const [bulkSelected, setBulkSelected] = useState<Set<string>>(new Set());
  const [bulkReason, setBulkReason] = useState("");

  useEffect(() => {
    if (!jobId || !clientId) return;
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const materialized = await materializeRun(jobId, clientId, token);
        const list = await listContentItems(materialized.id, undefined, token);
        if (cancelled) return;
        setSet(materialized);
        setItems(list);
        setSelectedId((prev) => prev ?? list[0]?.id ?? null);
      } catch (err) {
        if (!cancelled) setError(errorMessage(err));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [jobId, clientId, token]);

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

        {!jobId || !clientId ? (
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
                  igHandle={igHandle}
                  busy={busy}
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
  igHandle: string | null;
  busy: boolean;
  draftText: string;
  onDraftChange: (value: string) => void;
  rejecting: boolean;
  rejectReason: string;
  onRejectReasonChange: (value: string) => void;
  onStartReject: () => void;
  onCancelReject: () => void;
  onSaveText: () => void;
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

      <InstagramPost
        key={`${item.id}:${item.updatedAtUtc}`}
        item={item}
        igHandle={props.igHandle}
        caption={draftText}
        canEdit={canEdit && isVisual}
        busy={busy}
        onSaveVisual={isVisual ? props.onSaveVisual : undefined}
      />

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

/** Instagram-faithful post: editable design (carousel) on the left, IG sidebar on the right. */
function InstagramPost({
  item,
  igHandle,
  caption,
  canEdit,
  busy,
  onSaveVisual,
}: {
  item: ContentItem;
  igHandle: string | null;
  caption: string;
  canEdit: boolean;
  busy: boolean;
  onSaveVisual?: (markup: string | null, assets: VisualAssetInput[]) => void;
}) {
  const [envelopes, setEnvelopes] = useState<Envelope[]>(() => parseEnvelopes(item.visualSpecMarkup));
  const [current, setCurrent] = useState(0);
  const canvasRef = useRef<DesignCanvasHandle>(null);
  const initialJson = useMemo(() => serializeEnvelopes(parseEnvelopes(item.visualSpecMarkup)), [item.visualSpecMarkup]);
  const [dirty, setDirty] = useState(false);

  const total = envelopes.length;
  const idx = total === 0 ? 0 : Math.min(current, total - 1);
  const slide = envelopes[idx];
  const handle = igHandle ?? "your_account";

  const commitCurrent = useCallback((): Envelope[] => {
    const state = canvasRef.current?.getState();
    if (!state) return envelopes;
    const next = envelopes.map((e, i) =>
      i === idx ? { ...e, markup: state.markup, slotGeometry: syncGeometry(e.slotGeometry, state.slotStyles) } : e,
    );
    setEnvelopes(next);
    setDirty(serializeEnvelopes(next) !== initialJson);
    return next;
  }, [envelopes, idx, initialJson]);

  const goTo = (target: number) => {
    commitCurrent();
    setCurrent(((target % total) + total) % total);
  };

  if (total === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-background p-10 text-center text-sm italic text-muted-foreground">
        No rendered design available for this piece.
      </div>
    );
  }

  const width = slide?.canvas?.width ?? 1080;
  const height = slide?.canvas?.height ?? 1080;

  return (
    <div className="flex flex-col gap-3">
      <div className="overflow-hidden rounded-xl border border-border bg-background shadow-sm md:flex md:max-h-[78vh] md:items-stretch">
        {/* Media (editable design) */}
        <div className="relative flex min-w-0 flex-1 items-start justify-center bg-black">
          <div className="relative w-full">
            <DesignCanvas
              key={`${item.id}:${idx}`}
              ref={canvasRef}
              markup={slide?.markup ?? ""}
              width={width}
              height={height}
              canEdit={canEdit}
              onDirty={() => setDirty(true)}
            />
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
          <div className="min-h-0 flex-1 overflow-y-auto px-3 py-2.5 text-sm">
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
          {onSaveVisual ? (
            <Button
              size="sm"
              variant="secondary"
              disabled={busy || !dirty}
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

/**
 * Renders the design markup inline (imperatively, so React never wipes edits) and turns its
 * [data-slot] elements into selectable, draggable, editable text boxes with a floating toolbar.
 */
const DesignCanvas = forwardRef<
  DesignCanvasHandle,
  { markup: string; width: number; height: number; canEdit: boolean; onDirty: () => void }
>(function DesignCanvas({ markup, width, height, canEdit, onDirty }, ref) {
  const containerRef = useRef<HTMLDivElement>(null);
  const designRef = useRef<HTMLDivElement>(null);
  const scaleRef = useRef(0);
  const [scale, setScale] = useState(0);
  const selectedRef = useRef<HTMLElement | null>(null);
  const [, setSelectedTick] = useState(0);
  const [toolbar, setToolbar] = useState<{ top: number; left: number } | null>(null);
  const dragRef = useRef<{ el: HTMLElement; sx: number; sy: number; left: number; top: number; moved: boolean } | null>(
    null,
  );

  // Imperative innerHTML — React does not manage these children, so our DOM edits are never wiped.
  useEffect(() => {
    const el = designRef.current;
    if (!el) return;
    el.innerHTML = markup;
    if (canEdit) {
      el.querySelectorAll<HTMLElement>("[data-slot]").forEach((n) => {
        n.style.cursor = "move";
      });
    }
    selectedRef.current = null;
    setToolbar(null);
  }, [markup, canEdit]);

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

  const positionToolbar = useCallback((target: HTMLElement) => {
    const cont = containerRef.current;
    if (!cont) return;
    const r = target.getBoundingClientRect();
    const cr = cont.getBoundingClientRect();
    setToolbar({ top: Math.max(2, r.top - cr.top - 40), left: Math.min(Math.max(2, r.left - cr.left), cont.clientWidth - 320) });
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
        positionToolbar(target);
      } else {
        setToolbar(null);
      }
      setSelectedTick((t) => t + 1);
    },
    [positionToolbar],
  );

  useEffect(() => {
    const el = designRef.current;
    if (!el) return;

    const onPointerDown = (e: PointerEvent) => {
      if (!canEdit) return;
      const slot = (e.target as HTMLElement).closest<HTMLElement>("[data-slot]");
      if (!slot || !el.contains(slot)) return;
      if (slot.isContentEditable) return; // editing text — let the caret work
      select(slot);
      dragRef.current = { el: slot, sx: e.clientX, sy: e.clientY, left: slot.offsetLeft, top: slot.offsetTop, moved: false };
      e.preventDefault();
    };
    const onPointerMove = (e: PointerEvent) => {
      const d = dragRef.current;
      if (!d) return;
      const sc = scaleRef.current || 1;
      let nl = d.left + (e.clientX - d.sx) / sc;
      let nt = d.top + (e.clientY - d.sy) / sc;
      nl = Math.max(0, Math.min(nl, width - d.el.offsetWidth));
      nt = Math.max(0, Math.min(nt, height - d.el.offsetHeight));
      d.el.style.left = `${Math.round(nl)}px`;
      d.el.style.top = `${Math.round(nt)}px`;
      d.el.style.right = "auto";
      d.el.style.bottom = "auto";
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
      const slot = (e.target as HTMLElement).closest<HTMLElement>("[data-slot]");
      if (!slot || !el.contains(slot)) return;
      slot.contentEditable = "true";
      select(slot);
      slot.focus();
    };
    const onInput = () => onDirty();
    const onDocPointerDown = (e: PointerEvent) => {
      const t = e.target as HTMLElement;
      if (!t.closest("[data-cs-canvas]") && !t.closest("[data-cs-toolbar]")) select(null);
    };

    el.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("pointermove", onPointerMove);
    window.addEventListener("pointerup", onPointerUp);
    el.addEventListener("dblclick", onDblClick);
    el.addEventListener("input", onInput);
    document.addEventListener("pointerdown", onDocPointerDown);
    return () => {
      el.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
      el.removeEventListener("dblclick", onDblClick);
      el.removeEventListener("input", onInput);
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
      clone.querySelectorAll<HTMLElement>("[data-slot]").forEach((node) => {
        node.style.outline = "";
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
        ref={designRef}
        style={{ width, height, transform: `scale(${scale})`, transformOrigin: "top left" }}
      />
      {toolbar && selectedRef.current ? (
        <div
          data-cs-toolbar
          className="absolute z-10 flex items-center gap-0.5 rounded-md border border-border bg-background p-1 shadow-lg"
          style={{ top: toolbar.top, left: toolbar.left }}
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
            aria-label="Text color"
            onChange={(e) => apply((el) => { el.style.color = e.target.value; })}
            className="h-6 w-6 cursor-pointer rounded border border-border bg-transparent p-0"
          />
        </div>
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
