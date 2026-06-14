"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AnimatePresence, motion } from "framer-motion";
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

// ── Composition envelope helpers (the visual content of a piece) ───────────────

interface Envelope {
  markup: string;
  canvas?: { width?: number; height?: number };
  [key: string]: unknown;
}

interface Slide {
  html: string;
  width: number;
  height: number;
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

function extractSlots(markup: string): { slot: string; text: string }[] {
  if (typeof window === "undefined") return [];
  try {
    const doc = new DOMParser().parseFromString(markup, "text/html");
    return [...doc.querySelectorAll("[data-slot]")]
      .map((el) => ({ slot: el.getAttribute("data-slot") ?? "", text: el.textContent ?? "" }))
      .filter((s) => s.slot.length > 0);
  } catch {
    return [];
  }
}

function replaceSlotText(markup: string, slot: string, text: string): string {
  if (typeof window === "undefined") return markup;
  try {
    const doc = new DOMParser().parseFromString(markup, "text/html");
    const el = doc.querySelector(`[data-slot="${slot}"]`);
    if (el) el.textContent = text;
    return doc.body.innerHTML;
  } catch {
    return markup;
  }
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

  const [draftText, setDraftText] = useState("");
  const [rejecting, setRejecting] = useState(false);
  const [rejectReason, setRejectReason] = useState("");
  const [bulkSelected, setBulkSelected] = useState<Set<string>>(new Set());
  const [bulkReason, setBulkReason] = useState("");

  useEffect(() => {
    if (!jobId || !clientId) {
      return;
    }
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
            className="grid flex-1 grid-cols-[260px_1fr] overflow-hidden"
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

      {isVisual ? (
        <VisualStudio
          key={`${item.id}:${item.updatedAtUtc}`}
          item={item}
          busy={busy}
          canEdit={canEdit}
          onSaveVisual={props.onSaveVisual}
        />
      ) : (
        <TextPreview item={item} />
      )}

      <div className="flex flex-col gap-2">
        <label className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Caption / post copy</label>
        <textarea
          value={draftText}
          onChange={(e) => props.onDraftChange(e.target.value)}
          disabled={!canEdit || busy}
          rows={isVisual ? 3 : 10}
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

/** Carousel-style preview of the rendered design + live editing of its text slots. */
function VisualStudio({
  item,
  busy,
  canEdit,
  onSaveVisual,
}: {
  item: ContentItem;
  busy: boolean;
  canEdit: boolean;
  onSaveVisual: (markup: string | null, assets: VisualAssetInput[]) => void;
}) {
  const [envelopes, setEnvelopes] = useState<Envelope[]>(() => parseEnvelopes(item.visualSpecMarkup));
  const [current, setCurrent] = useState(0);
  const initialJson = useMemo(() => serializeEnvelopes(parseEnvelopes(item.visualSpecMarkup)), [item.visualSpecMarkup]);

  const total = envelopes.length;
  const idx = total === 0 ? 0 : Math.min(current, total - 1);
  const activeSlide = envelopes[idx];
  const slots = activeSlide ? extractSlots(activeSlide.markup) : [];
  const dirty = total > 0 && serializeEnvelopes(envelopes) !== initialJson;

  // Debounced markup feeding the iframe, so live edits don't reload the frame on every keystroke.
  const [previewMarkup, setPreviewMarkup] = useState(activeSlide?.markup ?? "");
  useEffect(() => {
    const markup = envelopes[idx]?.markup ?? "";
    const timer = setTimeout(() => setPreviewMarkup(markup), 200);
    return () => clearTimeout(timer);
  }, [envelopes, idx]);

  const editSlot = (slot: string, text: string) => {
    setEnvelopes((prev) => prev.map((e, i) => (i === idx ? { ...e, markup: replaceSlotText(e.markup, slot, text) } : e)));
  };

  if (total === 0) {
    return (
      <div className="rounded-lg border border-dashed border-border bg-background p-8 text-center text-sm italic text-muted-foreground">
        No rendered design available for this piece.
      </div>
    );
  }

  const slideWidth = activeSlide?.canvas?.width ?? 1080;
  const slideHeight = activeSlide?.canvas?.height ?? 1080;
  const portrait = item.targetPlacement === "story" || item.targetPlacement === "reel";

  return (
    <div className="flex flex-col gap-3">
      <div className="rounded-xl border border-border bg-muted/30 p-4">
        <div className="mb-2 flex items-center justify-between">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Preview — {item.targetNetwork} {item.targetPlacement}
          </p>
          {total > 1 ? (
            <span className="text-xs text-muted-foreground">
              {idx + 1}/{total}
            </span>
          ) : null}
        </div>
        <div className={cn("relative mx-auto w-full", portrait ? "max-w-[300px]" : "max-w-[420px]")}>
          <RenderedSlide slide={{ html: previewMarkup, width: slideWidth, height: slideHeight }} />
          {total > 1 ? (
            <>
              <button
                type="button"
                aria-label="Previous slide"
                onClick={() => setCurrent((c) => (Math.min(c, total - 1) - 1 + total) % total)}
                className="absolute left-1 top-1/2 grid h-8 w-8 -translate-y-1/2 place-items-center rounded-full bg-background/80 text-lg shadow hover:bg-background"
              >
                ‹
              </button>
              <button
                type="button"
                aria-label="Next slide"
                onClick={() => setCurrent((c) => (Math.min(c, total - 1) + 1) % total)}
                className="absolute right-1 top-1/2 grid h-8 w-8 -translate-y-1/2 place-items-center rounded-full bg-background/80 text-lg shadow hover:bg-background"
              >
                ›
              </button>
            </>
          ) : null}
        </div>
        {total > 1 ? (
          <div className="mt-3 flex items-center justify-center gap-1.5">
            {envelopes.map((_, i) => (
              <button
                key={i}
                type="button"
                aria-label={`Go to slide ${i + 1}`}
                onClick={() => setCurrent(i)}
                className={cn(
                  "h-1.5 rounded-full transition-all",
                  i === idx ? "w-5 bg-primary" : "w-1.5 bg-muted-foreground/40 hover:bg-muted-foreground/70",
                )}
              />
            ))}
          </div>
        ) : null}
      </div>

      {slots.length > 0 ? (
        <div className="flex flex-col gap-2 rounded-md border border-border p-3">
          <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Design text{total > 1 ? ` — slide ${idx + 1}` : ""}
          </span>
          {slots.map((s) => (
            <div key={s.slot} className="flex flex-col gap-1">
              <label className="text-[11px] capitalize text-muted-foreground">{s.slot}</label>
              <textarea
                value={s.text}
                onChange={(e) => editSlot(s.slot, e.target.value)}
                disabled={!canEdit || busy}
                rows={2}
                className="w-full rounded-md border border-border bg-background px-2 py-1 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
              />
            </div>
          ))}
        </div>
      ) : (
        <p className="px-1 text-xs text-muted-foreground">This design has no editable text slots.</p>
      )}

      <div>
        <Button
          size="sm"
          variant="secondary"
          disabled={!canEdit || busy || !dirty}
          onClick={() =>
            onSaveVisual(
              serializeEnvelopes(envelopes),
              item.assets.map((a) => ({ role: a.role, storageUri: a.storageUri, mime: a.mime })),
            )
          }
        >
          Save design changes
        </Button>
      </div>
    </div>
  );
}

function TextPreview({ item }: { item: ContentItem }) {
  return (
    <div className="rounded-xl border border-border bg-muted/30 p-4">
      <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        Preview — {item.targetNetwork} {item.targetPlacement}
      </p>
      <div className="mx-auto max-w-[420px] rounded-lg border border-border bg-background p-4">
        {item.text ? (
          <p className="whitespace-pre-wrap text-sm">{item.text}</p>
        ) : (
          <p className="text-sm italic text-muted-foreground">No text content.</p>
        )}
      </div>
    </div>
  );
}

/** Renders one designer-markup slide in a sandboxed iframe, scaled to fit the container width. */
function RenderedSlide({ slide }: { slide: Slide }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const update = () => setScale(el.clientWidth / slide.width);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(el);
    return () => observer.disconnect();
  }, [slide.width]);

  const doc = `<!doctype html><html><head><meta charset="utf-8"><style>html,body{margin:0;padding:0}</style></head><body>${slide.html}</body></html>`;

  return (
    <div
      ref={containerRef}
      className="w-full overflow-hidden rounded-lg border border-border bg-white"
      style={{ height: scale > 0 ? slide.height * scale : undefined }}
    >
      {scale > 0 ? (
        <iframe
          title="content preview"
          sandbox=""
          scrolling="no"
          srcDoc={doc}
          style={{
            width: slide.width,
            height: slide.height,
            border: 0,
            transform: `scale(${scale})`,
            transformOrigin: "top left",
          }}
        />
      ) : null}
    </div>
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
