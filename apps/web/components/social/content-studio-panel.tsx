"use client";

import { useDateFormatter } from "@/lib/format/use-date-formatter";
import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
  type RefObject,
} from "react";
import { AnimatePresence, motion } from "framer-motion";
import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  horizontalListSortingStrategy,
  sortableKeyboardCoordinates,
  useSortable,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog";
import { createPortal } from "react-dom";
import {
  AlignCenter,
  AlignLeft,
  AlignRight,
  ArrowDown,
  ArrowUp,
  BadgeCheck,
  Bold,
  Bookmark,
  GripVertical,
  Heart,
  Check,
  Italic,
  // Aliased: the bare name `Lock` resolves to the DOM Web Locks API type, not the icon.
  Lock as LockIcon,
  MessageCircle,
  Minus,
  MoreHorizontal,
  Pin,
  PinOff,
  Plus,
  Redo2,
  Send,
  Smile,
  Square,
  Trash2,
  Type,
  Underline,
  Undo2,
} from "lucide-react";
import EmojiPicker, { EmojiStyle, Theme as EmojiPickerTheme } from "emoji-picker-react";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { hasVisibleBorder, pickRecolorProperty } from "@/lib/studio/decoration-edit";
import { resolveToolbarDock } from "@/lib/studio/toolbar-dock";
import { ensureFontsLoaded, fontStackFor } from "@/lib/studio/fonts";
import { useDismissOnOutsideClick } from "@/hooks/use-dismiss-on-outside-click";
import {
  ensureDecorLayer,
  isBackgroundLayer,
  pickElement,
  upgradeLegacyShapes,
} from "@/lib/studio/canvas-hit-test";
import {
  canRedo as canRedoStack,
  canUndo as canUndoStack,
  commit as commitHistory,
  createHistory,
  redo as redoStack,
  resolveShortcut,
  shouldClearRegistry,
  undo as undoStack,
  withStack,
  type EditSignal,
  type HistoryRegistry,
  type HistoryState,
  type Snapshot,
} from "@/lib/studio/edit-history";
import { computeCaptionLimits, CAPTION_MAX_HASHTAGS, CAPTION_MAX_LENGTH } from "@/lib/studio/caption-limits";
import { groupFontOptions } from "@/lib/studio/font-options";
import { canOfferSlideEmojiPicker } from "@/lib/studio/emoji-capability";
import { insertTextAtCaret } from "@/lib/studio/text-insertion";
import {
  approveItem,
  bulkOperation,
  deleteItem,
  discardItem,
  reopenItem,
  downloadPresentationPptx,
  duplicateItem,
  editItemCaption,
  editItemText,
  editItemVisual,
  fetchContentItemAsset,
  getContentSet,
  getRenderCapabilities,
  linkItemToPlan,
  listContentItems,
  listContentItemsByIds,
  materializeAnalysis,
  rejectItem,
  spawnVariant,
  stageItem,
  unlinkItemFromPlan,
  type BulkOperation,
  type ContentItem,
  type ContentReviewState,
  type ContentSet,
  type RenderCapabilities,
  type VisualAssetInput,
} from "@/lib/api/content-studio";
import { getContentPlan, listContentPlans } from "@/lib/api/content-plans";
import { listClientOwnAccounts } from "@/lib/api/client-own-accounts";
import type { ContentPlan, ContentPlanSummary } from "@/lib/api/types";
import {
  tryParseReelScript,
  type ReelScript,
  type ReelScriptScene,
} from "@/lib/publications/reel-script";

interface ContentStudioPanelProps {
  /** Analysis-driven entry point: materializes (creates/updates) a studio set from an analysis. */
  analysisId: string | null;
  /**
   * Direct entry point for an ALREADY-materialized set (e.g. opened from the Content Bank). When
   * provided, `analysisId` is ignored and the studio loads this set's items directly — it never
   * calls `materializeAnalysis`, which is analysis-centric and fails/hangs for content that is not a
   * re-materializable single-analyzer analysis (e.g. MagicGenerator runs).
   */
  setId?: string | null;
  /** Item to select once the set's items are loaded (only meaningful together with `setId`). */
  selectedItemId?: string | null;
  /**
   * Explicit multi-piece entry point (Content Bank selection). When provided, `analysisId` and
   * `setId` are ignored and the studio loads exactly these items, in this order, so an operator can
   * review a whole triaged selection in ONE session instead of reopening the studio per piece. The
   * pieces need not share a set, so no `ContentSet` is loaded and set-scoped actions (the PPTX
   * export) stay disabled.
   */
  itemIds?: string[] | null;
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

/**
 * States a piece cannot be EDITED from. This is not the same thing as "terminal": the domain's
 * transition table (`ReviewStateTransitions`) treats only `HandedOff` as terminal, but `Edited` is
 * unreachable from all three of these, so the server would reject an edit made here.
 *
 * See {@link editLockFor} for the way back out of each one.
 */
const TERMINAL: ContentReviewState[] = ["Discarded", "ReadyForPublication", "HandedOff"];

/**
 * Why a piece is read-only, and the domain-sanctioned action that makes it editable again.
 *
 * A locked piece used to render every field disabled with NO explanation anywhere in the UI, which
 * is indistinguishable from a studio that failed to initialize — the caption, the copy and every
 * slide textbox all go dead at once because they share a single `canEdit` flag.
 */
function editLockFor(
  state: ContentReviewState,
  actions: { onReopen: () => void; onApprove: () => void },
): { reason: string; action: { label: string; run: () => void } | null } | null {
  switch (state) {
    case "Discarded":
      // Discarded -> InReview is explicitly allowed by the domain ("recover").
      return {
        reason: "This piece is discarded, so it cannot be edited. Recover it to continue working on it.",
        action: { label: "Recover", run: actions.onReopen },
      };
    case "ReadyForPublication":
      // ReadyForPublication -> Approved is the domain's un-stage transition.
      return {
        reason: "This piece is staged for publication and locked. Send it back to Approved to edit it.",
        action: { label: "Back to approved", run: actions.onApprove },
      };
    case "HandedOff":
      // The only genuinely terminal state — there is no transition out of it.
      return {
        reason: "This piece was handed off. It is final and can no longer be edited.",
        action: null,
      };
    default:
      return null;
  }
}

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
  kind?: "text";
  x: number;
  y: number;
  width: number;
  height: number;
  fontSizePx: number;
  fontWeight: string;
  fontStyle: string;
  color: string;
  /** Family selected via the font picker (R2.4). Optional so pre-Phase-6 persisted styles remain valid. */
  fontFamily?: string;
}

/**
 * Typed identity + geometry + style for a decorative shape (Decision B2/B4). Shapes address the same
 * way text slots do — a stable `data-slot` — rather than being recoverable only as opaque serialized
 * markup, so they survive selection, reorder, and reload (R1.3/R1.4).
 */
interface ShapeStyle {
  slot: string;
  kind: "shape";
  x: number;
  y: number;
  width: number;
  height: number;
  background: string;
  borderRadius: number;
  opacity: number;
  rotation?: number;
}

/** Discriminated union addressable by `"kind" in s && s.kind === "shape"`; `kind` stays optional on the text arm so every already-persisted `SlotStyle` remains a valid `ElementStyle`. */
type ElementStyle = SlotStyle | ShapeStyle;

function isShapeStyle(s: ElementStyle): s is ShapeStyle {
  return "kind" in s && s.kind === "shape";
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

/** Updates the envelope's slot geometry (position + style) from the edited DOM so the PPTX stays faithful. */
function syncGeometry(geometry: SlotGeometry[] | undefined, styles: ElementStyle[]): SlotGeometry[] {
  const bySlot = new Map(styles.map((s) => [s.slot, s]));
  return (geometry ?? []).map((g) => {
    const s = bySlot.get(g.slot);
    if (!s) return g;
    if (isShapeStyle(s)) {
      return {
        ...g,
        kind: "shape",
        x: s.x,
        y: s.y,
        width: s.width,
        height: s.height,
        background: s.background,
        borderRadius: s.borderRadius,
        opacity: s.opacity,
        ...(s.rotation !== undefined ? { rotation: s.rotation } : {}),
      };
    }
    return {
      ...g,
      x: s.x,
      y: s.y,
      width: s.width,
      height: s.height,
      fontSizePx: s.fontSizePx,
      fontWeight: s.fontWeight,
      fontStyle: s.fontStyle,
      color: s.color,
      ...(s.fontFamily !== undefined ? { fontFamily: s.fontFamily } : {}),
    };
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

/**
 * Per-slide undo/redo history (R4-R9, DD2). One registry, keyed by slide index, defined once and
 * shared by both editor call sites instead of duplicating the wiring per editor. All policy — the
 * stack reducer, the coalescing window, the depth cap — lives in `lib/studio/edit-history.ts`; this
 * hook is DOM/React glue only (Glue Site #2): read the current markup, build a `Snapshot`, delegate to
 * the pure module, store the result.
 */
function useSlideEditHistory({
  canvasRef,
  envelopesRef,
  idxRef,
  commitCurrent,
}: {
  canvasRef: RefObject<DesignCanvasHandle | null>;
  envelopesRef: RefObject<Envelope[]>;
  idxRef: RefObject<number>;
  // Not called by this hook — history never re-commits the draft itself (`goTo` already runs
  // `commitCurrent` before the active index changes, per DD2's lifecycle table). Kept in the shared
  // hook's signature so both call sites wire the same four values without a bespoke shape per editor.
  commitCurrent: () => Envelope[];
}) {
  void commitCurrent;
  // R5: this ref's lifetime is tied to the enclosing editor component's remount on item switch
  // (`ItemDetail key={selected.id}`) — that remount is what naturally clears history across items.
  // A future refactor MUST NOT lift this ref above that keyed boundary, or history would leak
  // between unrelated content items.
  const registryRef = useRef<HistoryRegistry>(new Map());
  const slideCountRef = useRef(envelopesRef.current.length);
  const [, setVersion] = useState(0);
  const rerender = useCallback(() => setVersion((v) => v + 1), []);

  // R4: a slide-count change (add/remove) misaligns index-keyed stacks — discard the registry
  // wholesale (Glue Site #5: a guard on `shouldClearRegistry`'s pure result, no threshold logic here).
  useEffect(() => {
    const nextCount = envelopesRef.current.length;
    if (shouldClearRegistry(slideCountRef.current, nextCount)) {
      registryRef.current = new Map();
      rerender();
    }
    slideCountRef.current = nextCount;
  });

  const onDirty = useCallback(
    (edit?: EditSignal) => {
      const state = canvasRef.current?.getState();
      if (!state) return;
      const idx = idxRef.current;
      const snapshot: Snapshot = {
        markup: state.markup,
        kind: edit?.kind ?? "structural",
        slot: edit?.slot ?? null,
        at: Date.now(),
      };
      // DD4: the first capture for a slide seeds its baseline from the pre-edit envelope markup —
      // there is no prior stack yet, so nothing to coalesce or push against.
      const existing = registryRef.current.get(idx);
      const nextStack = existing ? commitHistory(existing, snapshot) : createHistory(snapshot);
      registryRef.current = withStack(registryRef.current, idx, nextStack);
      rerender();
    },
    [canvasRef, idxRef, rerender],
  );

  const restoreFrom = useCallback(
    (step: (state: HistoryState) => { state: HistoryState; restored: Snapshot | null }): boolean => {
      const idx = idxRef.current;
      const stack = registryRef.current.get(idx);
      if (!stack) return false;
      const { state: nextStack, restored } = step(stack);
      registryRef.current = withStack(registryRef.current, idx, nextStack);
      rerender();
      if (!restored) return false;
      canvasRef.current?.restore(restored.markup);
      return true;
    },
    [canvasRef, idxRef, rerender],
  );

  const undo = useCallback(() => restoreFrom(undoStack), [restoreFrom]);
  const redo = useCallback(() => restoreFrom(redoStack), [restoreFrom]);

  const currentStack = registryRef.current.get(idxRef.current);

  /**
   * Discards every stack. Required whenever slides are REORDERED: the registry is keyed by slide
   * INDEX, and a reorder keeps the count identical, so the automatic count-change guard above never
   * fires — undo would then replay one slide's markup onto whichever slide now sits at that index.
   */
  const reset = useCallback(() => {
    registryRef.current = new Map();
    rerender();
  }, [rerender]);

  return {
    onDirty,
    undo,
    redo,
    reset,
    canUndo: canUndoStack(currentStack),
    canRedo: canRedoStack(currentStack),
  };
}

/** Tracks the viewport height so the editable canvas can be sized to fit the screen, not just the column width. */
function useViewportHeight() {
  const [height, setHeight] = useState(() => (typeof window === "undefined" ? 0 : window.innerHeight));
  useEffect(() => {
    const onResize = () => setHeight(window.innerHeight);
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);
  return height;
}

export function ContentStudioPanel({
  analysisId,
  setId = null,
  selectedItemId = null,
  itemIds = null,
  clientId,
  token,
  onClose,
}: ContentStudioPanelProps) {
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
  const [draftCaption, setDraftCaption] = useState("");
  const [renderCapabilities, setRenderCapabilities] = useState<RenderCapabilities | null>(null);
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
    // The `setId` entry point (Content Bank) is already materialized — never re-materialize by
    // analysis id here. Doing so is analysis-centric and fails/hangs for content whose source run
    // is not a re-materializable single-analyzer analysis (e.g. MagicGenerator runs).
    if (itemIds?.length || setId || !analysisId || !clientId) return;
    const key = `${analysisId}:${clientId}`;
    if (materializeKeyRef.current === key) return;
    materializeKeyRef.current = key;
    void (async () => {
      setLoading(true);
      setError(null);
      // `owned` tracks whether THIS run still owns the slot. It cannot be derived from the ref in
      // `finally`, because the error path deliberately clears the ref to allow a retry — reading
      // the ref there made the comparison false and skipped `setLoading(false)`, leaving the studio
      // spinning on "Entering Content Studio…" forever behind the error banner.
      let owned = true;
      try {
        const materialized = await materializeAnalysis(analysisId, clientId, token);
        const list = await listContentItems(materialized.id, undefined, token);
        if (materializeKeyRef.current !== key) {
          owned = false;
          return;
        }
        setSet(materialized);
        setItems(list);
        setSelectedId((prev) => prev ?? list[0]?.id ?? null);
      } catch (err) {
        if (materializeKeyRef.current !== key) {
          owned = false;
          return;
        }
        setError(errorMessage(err));
        materializeKeyRef.current = null; // allow a retry on the next deps change
      } finally {
        if (owned) setLoading(false);
      }
    })();
  }, [analysisId, clientId, setId, itemIds, token]);

  // Direct entry point (Content Bank "Open in Studio"): the set already exists, so just load it —
  // no materialize POST, no re-derivation. This also guarantees the blocking loader always resolves
  // (success or error), unlike a materialize call that can fail against a non-re-materializable run.
  const loadedSetKeyRef = useRef<string | null>(null);
  useEffect(() => {
    if (itemIds?.length || !setId) return;
    const key = setId;
    if (loadedSetKeyRef.current === key) return;
    loadedSetKeyRef.current = key;
    void (async () => {
      setLoading(true);
      setError(null);
      // See the materialize effect above: `owned` must not be re-derived from the ref in `finally`,
      // because the error path clears it to allow a retry and would otherwise strand the loader.
      let owned = true;
      try {
        const [loadedSet, list] = await Promise.all([
          getContentSet(setId, token),
          listContentItems(setId, undefined, token),
        ]);
        if (loadedSetKeyRef.current !== key) {
          owned = false;
          return;
        }
        setSet(loadedSet);
        setItems(list);
        setSelectedId(selectedItemId ?? list[0]?.id ?? null);
      } catch (err) {
        if (loadedSetKeyRef.current !== key) {
          owned = false;
          return;
        }
        setError(errorMessage(err));
        loadedSetKeyRef.current = null; // allow a retry on the next deps change
      } finally {
        if (owned) setLoading(false);
      }
    })();
  }, [setId, selectedItemId, itemIds, token]);

  // Multi-piece entry point (Content Bank selection). No set is loaded: the selection is explicit and
  // may span sets, so there is no single owning ContentSet to show or export.
  const loadedSelectionKeyRef = useRef<string | null>(null);
  useEffect(() => {
    if (!itemIds?.length) return;
    const key = itemIds.join(",");
    if (loadedSelectionKeyRef.current === key) return;
    loadedSelectionKeyRef.current = key;
    void (async () => {
      setLoading(true);
      setError(null);
      let owned = true;
      try {
        const list = await listContentItemsByIds(itemIds, token);
        if (loadedSelectionKeyRef.current !== key) {
          owned = false;
          return;
        }
        setSet(null);
        setItems(list);
        setSelectedId(selectedItemId ?? list[0]?.id ?? null);
      } catch (err) {
        if (loadedSelectionKeyRef.current !== key) {
          owned = false;
          return;
        }
        setError(errorMessage(err));
        loadedSelectionKeyRef.current = null; // allow a retry on the next deps change
      } finally {
        if (owned) setLoading(false);
      }
    })();
  }, [itemIds, selectedItemId, token]);

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

  // Render-capability contract (R2.3/F3): the font picker and the emoji picker both gate on this —
  // it is the SAME catalog + probe the guardrail and the renderer use, never a hardcoded frontend
  // list. Fetched once per client; an unconfigured/missing brand manual still resolves successfully
  // (system-list-only, per R2.2), so a fetch failure here is the only case that leaves it null.
  useEffect(() => {
    if (!clientId) return;
    let cancelled = false;
    getRenderCapabilities(clientId, token)
      .then((capabilities) => {
        if (cancelled) return;
        setRenderCapabilities(capabilities);
        // Without this, every family the host does not already have falls back to the SAME default,
        // so changing font looked like it only ever worked once.
        ensureFontsLoaded(capabilities.fonts.map((f) => f.family));
      })
      .catch(() => {
        /* picker degrades to "no fonts / no emoji" until this resolves */
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
    setDraftCaption(selected?.caption ?? "");
    // Intentionally keyed on selectedId only: re-running this reset whenever `selected` itself
    // changes identity (e.g. after autosave patches the item in place) would clobber in-progress
    // edits before the operator saves.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId]);

  const refresh = useCallback(
    async (keepId?: string) => {
      // A multi-piece session has no owning set to re-read, so it reloads the explicit selection
      // instead. Without this branch every action in such a session left the rail showing stale
      // state, because the set-based refresh bailed out on the first line.
      if (itemIds?.length) {
        const freshItems = await listContentItemsByIds(itemIds, token);
        setItems(freshItems);
        if (keepId !== undefined) setSelectedId(keepId);
        return;
      }

      if (!set) return;
      const [freshSet, freshItems] = await Promise.all([
        getContentSet(set.id, token),
        listContentItems(set.id, undefined, token),
      ]);
      setSet(freshSet);
      setItems(freshItems);
      if (keepId !== undefined) setSelectedId(keepId);
    },
    [set, itemIds, token],
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

  // Rendered into <body>, not in place.
  //
  // `position: fixed` is resolved against the nearest ancestor that establishes a containing block —
  // any `transform`, `filter`, `perspective`, `contain` or `will-change` up the tree does that, and
  // this panel mounts deep inside animated page content. When one of those was present, `inset-0`
  // measured the CONTENT COLUMN instead of the viewport and the shell's sidebar stayed visible
  // beside the studio. A portal to <body> makes full-screen unconditional, whatever the page does.
  const overlay = (
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
            {set ? (
              <ReviewSummaryBar set={set} />
            ) : itemIds?.length ? (
              // A cross-set selection has no set-level review summary to roll up; naming the session
              // keeps the header honest instead of leaving a blank where the summary usually sits.
              <Badge variant="info">
                Reviewing {items.length} selected piece{items.length === 1 ? "" : "s"}
              </Badge>
            ) : null}
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

        {/* A multi-piece session carries neither a set nor an analysis — its context IS the explicit
            item selection, so it must not be turned away by the set/analysis guard. */}
        {(!setId && !analysisId && !itemIds?.length) || !clientId ? (
          <div className="flex flex-1 items-center justify-center p-8 text-center text-sm text-muted-foreground">
            Select a client context with a generated package to open Content Studio.
          </div>
        ) : loading && !set && items.length === 0 ? (
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
                  draftCaption={draftCaption}
                  onCaptionChange={setDraftCaption}
                  renderCapabilities={renderCapabilities}
                  rejecting={rejecting}
                  rejectReason={rejectReason}
                  onRejectReasonChange={setRejectReason}
                  onStartReject={() => setRejecting(true)}
                  onCancelReject={() => {
                    setRejecting(false);
                    setRejectReason("");
                  }}
                  onSaveText={() => runAction(() => editItemText(selected.id, draftText, token), selected.id)}
                  onSaveCaption={() => runAction(() => editItemCaption(selected.id, draftCaption, token), selected.id)}
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
                  onReopen={() => runAction(() => reopenItem(selected.id, token), selected.id)}
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

  // `document` is absent during SSR; the panel only ever opens from a client interaction.
  return typeof document === "undefined" ? overlay : createPortal(overlay, document.body);
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
  draftCaption: string;
  onCaptionChange: (value: string) => void;
  renderCapabilities: RenderCapabilities | null;
  rejecting: boolean;
  rejectReason: string;
  onRejectReasonChange: (value: string) => void;
  onStartReject: () => void;
  onCancelReject: () => void;
  onSaveText: () => void;
  onSaveCaption: () => void;
  onAutosaveText: (text: string) => Promise<ContentItem>;
  onAutosaveVisual: (markup: string | null, assets: VisualAssetInput[]) => Promise<ContentItem>;
  onItemSaved: (item: ContentItem) => void;
  onSaveStatusChange: (status: AutosaveStatus, message?: string | null) => void;
  onApprove: () => void;
  onConfirmReject: () => void;
  onDiscard: () => void;
  onReopen: () => void;
  onDuplicate: () => void;
  onDelete: () => void;
  onStage: () => void;
  onSaveVisual: (markup: string | null, assets: VisualAssetInput[]) => void;
  onCreateVariant: () => void;
  planLink: ReactNode;
}

/**
 * Publication caption editor (R5.2-R5.5). Surfaces Instagram's 2,200-character / 30-hashtag
 * invariants (R5.3) as live feedback instead of a silent server rejection, and offers an emoji
 * picker (R6.1 — always available for captions, independent of any render-capability gate since a
 * caption is never rasterized).
 */
function CaptionEditor({
  value,
  onChange,
  onSave,
  canEdit,
  busy,
  isDirty,
}: {
  value: string;
  onChange: (value: string) => void;
  onSave: () => void;
  canEdit: boolean;
  busy: boolean;
  isDirty: boolean;
}) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const [emojiPickerOpen, setEmojiPickerOpen] = useState(false);
  // Wraps the trigger AND the panel — see the hook's contract.
  const emojiRef = useRef<HTMLDivElement>(null);
  const closeEmojiPicker = useCallback(() => setEmojiPickerOpen(false), []);
  useDismissOnOutsideClick(emojiRef, emojiPickerOpen, closeEmojiPicker);
  const limits = computeCaptionLimits(value);

  return (
    <div className="flex flex-col gap-2">
      <label className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Caption / post copy</label>
      <div className="relative">
        <textarea
          ref={textareaRef}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          disabled={!canEdit || busy}
          rows={4}
          // `scrollbar-gutter: stable` always reserves the scrollbar track, so the button below can
          // sit beside it at a fixed offset instead of guessing whether a scrollbar is present.
          style={{ scrollbarGutter: "stable" }}
          className="w-full rounded-md border border-border bg-background py-2 pl-3 pr-12 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
          placeholder="No caption. Required before this item can be staged for publication."
        />
        {/* Inside the box, top-right, clear of BOTH the scrollbar gutter (right-7 sits left of the
            reserved track) and the Save button below it. Anchoring it outside the box overlapped the
            Save control; anchoring it at right-1.5 sat on the scrollbar itself. */}
        <div ref={emojiRef}>
        <button
          type="button"
          aria-label="Insert emoji"
          title="Insert emoji"
          disabled={!canEdit || busy}
          onClick={() => setEmojiPickerOpen((open) => !open)}
          className="absolute right-7 top-2 grid h-6 w-6 place-items-center rounded text-muted-foreground hover:bg-muted hover:text-foreground disabled:opacity-40"
        >
          <Smile className="h-4 w-4" />
        </button>
        {emojiPickerOpen ? (
          <div className="absolute right-0 top-9 z-[70]">
            <EmojiPicker
              theme={EmojiPickerTheme.AUTO}
              emojiStyle={EmojiStyle.NATIVE}
              onEmojiClick={(emojiData) => {
                const ta = textareaRef.current;
                const caretIndex = ta?.selectionStart ?? value.length;
                const { text, caretIndex: nextCaret } = insertTextAtCaret(value, emojiData.emoji, caretIndex);
                onChange(text);
                setEmojiPickerOpen(false);
                requestAnimationFrame(() => {
                  ta?.focus();
                  ta?.setSelectionRange(nextCaret, nextCaret);
                });
              }}
            />
          </div>
        ) : null}
        </div>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-3 text-xs">
          <span className={limits.isOverLength ? "font-medium text-rose-600 dark:text-rose-400" : "text-muted-foreground"}>
            {limits.length} / {CAPTION_MAX_LENGTH} characters
          </span>
          <span className={limits.isOverHashtags ? "font-medium text-rose-600 dark:text-rose-400" : "text-muted-foreground"}>
            {limits.hashtagCount} / {CAPTION_MAX_HASHTAGS} hashtags
          </span>
          {limits.isEmpty ? <span className="text-amber-600 dark:text-amber-400">Caption cannot be empty.</span> : null}
        </div>
        <Button
          size="sm"
          variant="secondary"
          disabled={!canEdit || busy || !isDirty || limits.isEmpty || limits.isOverLength || limits.isOverHashtags}
          onClick={onSave}
        >
          Save caption
        </Button>
      </div>
    </div>
  );
}

function ItemDetail(props: ItemDetailProps) {
  const { item, busy, draftText, draftCaption } = props;
  const isTerminal = TERMINAL.includes(item.state);
  const canEdit = !isTerminal;
  const lock = editLockFor(item.state, { onReopen: props.onReopen, onApprove: props.onApprove });
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

      {lock ? (
        // Editing is blocked because `Edited` is not reachable from this state in the domain's
        // transition table. Rendering disabled fields with no explanation read as a broken studio,
        // so the reason is stated and the domain-sanctioned way back is offered where one exists.
        <div className="flex flex-wrap items-center gap-2 rounded-lg border border-amber-500/40 bg-amber-500/10 px-3 py-2">
          <LockIcon className="h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
          <span className="text-xs text-amber-800 dark:text-amber-200">{lock.reason}</span>
          {lock.action ? (
            <Button size="sm" variant="outline" className="h-7 px-2 text-xs" disabled={busy} onClick={lock.action.run}>
              {lock.action.label}
            </Button>
          ) : null}
        </div>
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
      ) : !isVisual ? (
        // Text medium whose body is NOT a structured script (plain prose). This used to fall through
        // to the Instagram post branch below, which renders slides and a caption — so the piece's
        // actual body was nowhere on screen.
        <EditableProse
          itemId={item.id}
          value={draftText}
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
              renderCapabilities={props.renderCapabilities}
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
              caption={draftCaption}
              canEdit={canEdit && isVisual}
              busy={busy}
              saveStatus={props.saveStatus}
              saveError={props.saveError}
              renderCapabilities={props.renderCapabilities}
              onAutosaveVisual={props.onAutosaveVisual}
              onItemSaved={props.onItemSaved}
              onSaveStatusChange={props.onSaveStatusChange}
              onSaveVisual={isVisual ? props.onSaveVisual : undefined}
            />
          )}

          {/* R5.4: caption UI is enabled for every visual placement, INCLUDING stories — it used to be
              gated off here for `targetPlacement === "story"`, which was simply wrong (stories publish
              with captions too). Bound to the dedicated `caption` DTO field (R5.1), never the generic
              `text` field, which keeps carrying reel scripts. */}
          <CaptionEditor
            value={draftCaption}
            onChange={props.onCaptionChange}
            onSave={props.onSaveCaption}
            canEdit={canEdit}
            busy={busy}
            isDirty={draftCaption !== (item.caption ?? "")}
          />
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
/**
 * Editor for a text-medium piece whose body is plain prose rather than a structured reel script.
 * Deliberately minimal: one textarea over the same `text` field the script editor writes, so a piece
 * whose generator produced unstructured output is still readable and editable instead of invisible.
 */
function EditableProse({
  itemId,
  value,
  baseText,
  canEdit,
  busy,
  onChange,
  onSave,
}: {
  itemId: string;
  value: string;
  baseText: string;
  canEdit: boolean;
  busy: boolean;
  onChange: (text: string) => void;
  onSave: () => void;
}) {
  // Same seeding hazard as EditableScript: on the render where the item first appears the parent's
  // draft is still empty, so fall back to the persisted text rather than showing a blank box.
  const text = value || baseText;
  const dirty = value !== baseText && value.length > 0;

  return (
    <div className="flex flex-col gap-3 rounded-xl border border-border bg-background p-6 shadow-sm">
      <span className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">Body</span>
      <textarea
        key={itemId}
        className="min-h-[18rem] w-full rounded-md border border-border bg-background px-3 py-2 text-sm leading-relaxed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60"
        disabled={!canEdit || busy}
        value={text}
        onChange={(e) => onChange(e.target.value)}
      />
      {canEdit ? (
        <div className="flex justify-end">
          <Button size="sm" variant="secondary" disabled={busy || !dirty} onClick={onSave}>
            Save
          </Button>
        </div>
      ) : null}
    </div>
  );
}

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
  // Seeded from the PERSISTED text, never only from the in-flight draft.
  //
  // `json` is the parent's `draftText`, which is empty on the render where an item is first shown —
  // the parent sets it from an effect that runs AFTER this child mounts. Seeding from it alone left
  // the model at `{ scenes: [] }`, so the script rendered completely blank, and the old re-seed keyed
  // on `itemId` never repaired it because the id had not changed. Worse on a switch: child effects
  // run before parent effects, so the re-seed read the PREVIOUS item's draft.
  //
  // `baseText` is the item's saved text and is correct on the very first render. It changes only when
  // the item is saved or reloaded — never while typing — so re-seeding on it cannot clobber an edit
  // in progress.
  const [model, setModel] = useState<ReelScript>(
    () => tryParseReelScript(json) ?? tryParseReelScript(baseText) ?? { scenes: [] },
  );

  useEffect(() => {
    setModel(tryParseReelScript(baseText) ?? { scenes: [] });
  }, [itemId, baseText]);

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
              // w-16 (64px) fit "0:00" but not the RANGE the generator actually writes
              // ("0:00–0:03"), so every timestamp was clipped by about three characters.
              className={cn(field, "w-24 shrink-0 px-1.5 text-center font-semibold tabular-nums")}
              disabled={disabled}
              value={scene.timestamp ?? ""}
              onChange={(e) => setScene(i, { timestamp: e.target.value })}
              placeholder="0:00–0:03"
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

/*
 * Autosave is temporarily disabled. Keep this implementation here so it can be
 * restored once autosave is behind an explicit environment/client setting.
 *
 * function CaptionAutosave({
 *   item,
 *   value,
 *   canEdit,
 *   busy,
 *   onAutosaveText,
 *   onItemSaved,
 *   onSaveStatusChange,
 * }: {
 *   item: ContentItem;
 *   value: string;
 *   canEdit: boolean;
 *   busy: boolean;
 *   onAutosaveText: (text: string) => Promise<ContentItem>;
 *   onItemSaved: (item: ContentItem) => void;
 *   onSaveStatusChange: (status: AutosaveStatus, message?: string | null) => void;
 * }) {
 *   const seq = useRef(0);
 *   const persistedRef = useRef(item.text ?? "");
 *
 *   useEffect(() => {
 *     persistedRef.current = item.text ?? "";
 *   }, [item.id, item.text]);
 *
 *   useEffect(() => {
 *     if (!canEdit || busy || value === persistedRef.current) return;
 *     const saveId = ++seq.current;
 *     onSaveStatusChange("saving");
 *     const timer = window.setTimeout(() => {
 *       onAutosaveText(value)
 *         .then((updated) => {
 *           if (seq.current !== saveId) return;
 *           persistedRef.current = updated.text ?? "";
 *           onItemSaved(updated);
 *           onSaveStatusChange("saved");
 *         })
 *         .catch((err) => {
 *           if (seq.current !== saveId) return;
 *           onSaveStatusChange("error", errorMessage(err));
 *         });
 *     }, 700);
 *
 *     return () => {
 *       seq.current++;
 *       window.clearTimeout(timer);
 *     };
 *   }, [value, canEdit, busy, onAutosaveText, onItemSaved, onSaveStatusChange]);
 *
 *   return null;
 * }
 *
 * function AutosaveIndicator({ status, error }: { status: AutosaveStatus; error: string | null }) {
 *   if (status === "idle") return null;
 *   const label =
 *     status === "saving" ? "Saving…" : status === "saved" ? "Saved" : `Save failed${error ? `: ${error}` : ""}`;
 *   return (
 *     <p className={cn("mb-2 text-xs", status === "error" ? "text-destructive" : "text-muted-foreground")}>
 *       {label}
 *     </p>
 *   );
 * }
 */

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
  renderCapabilities,
  // Autosave is temporarily disabled.
  // saveStatus,
  // saveError,
  // onAutosaveVisual,
  // onItemSaved,
  // onSaveStatusChange,
  onSaveVisual,
}: {
  item: ContentItem;
  token: string;
  igHandle: string | null;
  canEdit: boolean;
  busy: boolean;
  saveStatus: AutosaveStatus;
  saveError: string | null;
  renderCapabilities: RenderCapabilities | null;
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
  const { markEdited, markSaveNeeded, pendingRevision, savedRevisionRef } = useVisualAutosaveRevision();

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

  const history = useSlideEditHistory({ canvasRef, envelopesRef, idxRef, commitCurrent });

  /*
   * Autosave is temporarily disabled so local/test/dev environments do not keep
   * calling the API while the user edits. Manual saving remains available
   * through "Save design changes".
   *
   * const saveSeq = useRef(0);
   * const { markSavedIfLatest } = useVisualAutosaveRevision();
   *
   * useEffect(() => {
   *   if (!canEdit || busy || pendingRevision <= savedRevisionRef.current) return;
   *   const saveId = ++saveSeq.current;
   *   const revisionToSave = pendingRevision;
   *   onSaveStatusChange("saving");
   *   const timer = window.setTimeout(() => {
   *     const next = commitCurrent();
   *     const markup = serializeEnvelopes(next);
   *     const assets = assetsRef.current.map((a) => ({ role: a.role, storageUri: a.storageUri, mime: a.mime }));
   *     onAutosaveVisual(markup, assets)
   *       .then((updated) => {
   *         if (saveSeq.current !== saveId) return;
   *         if (!markSavedIfLatest(revisionToSave)) return;
   *         persistedJsonRef.current = markup;
   *         onItemSaved(updated);
   *         onSaveStatusChange("saved");
   *       })
   *       .catch((err) => {
   *         if (saveSeq.current !== saveId) return;
   *         onSaveStatusChange("error", errorMessage(err));
   *       });
   *   }, 900);
   *
   *   return () => {
   *     window.clearTimeout(timer);
   *   };
   * }, [
   *   pendingRevision,
   *   savedRevisionRef,
   *   canEdit,
   *   busy,
   *   commitCurrent,
   *   markSavedIfLatest,
   *   onAutosaveVisual,
   *   onItemSaved,
   *   onSaveStatusChange,
   * ]);
   */

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
              onDirty={(edit) => {
                markEdited();
                history.onDirty(edit);
              }}
              canUndo={history.canUndo}
              canRedo={history.canRedo}
              onUndo={() => {
                if (history.undo()) markEdited();
              }}
              onRedo={() => {
                if (history.redo()) markEdited();
              }}
              fallbackImageUrl={slide.flattenedImageRef ?? null}
              renderCapabilities={renderCapabilities}
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
          {/* AutosaveIndicator is hidden while autosave is temporarily disabled. */}
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
  renderCapabilities,
  // Autosave is temporarily disabled.
  // saveStatus,
  // saveError,
  // onAutosaveVisual,
  // onItemSaved,
  // onSaveStatusChange,
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
  renderCapabilities: RenderCapabilities | null;
  onAutosaveVisual: (markup: string | null, assets: VisualAssetInput[]) => Promise<ContentItem>;
  onItemSaved: (item: ContentItem) => void;
  onSaveStatusChange: (status: AutosaveStatus, message?: string | null) => void;
  onSaveVisual?: (markup: string | null, assets: VisualAssetInput[]) => void;
}) {
  const { formatDate } = useDateFormatter();
  const [envelopes, setEnvelopes] = useState<Envelope[]>(() => parseEnvelopes(item.visualSpecMarkup));
  const [current, setCurrent] = useState(0);
  const canvasRef = useRef<DesignCanvasHandle>(null);
  const envelopesRef = useRef(envelopes);
  const idxRef = useRef(0);
  const assetsRef = useRef(item.assets);
  const initialJson = useMemo(() => serializeEnvelopes(parseEnvelopes(item.visualSpecMarkup)), [item.visualSpecMarkup]);
  const persistedJsonRef = useRef(initialJson);
  const { markEdited, markSaveNeeded, pendingRevision, savedRevisionRef } = useVisualAutosaveRevision();

  const viewportHeight = useViewportHeight();
  // Budget ~68% of the viewport for the editable media so the tall 4:5 portrait is large enough to edit
  // comfortably while leaving room for the slide strip and the caption editor below.
  const feedMaxHeight = viewportHeight > 0 ? Math.round(viewportHeight * 0.68) : undefined;
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

  const history = useSlideEditHistory({ canvasRef, envelopesRef, idxRef, commitCurrent });

  /*
   * Autosave is temporarily disabled so local/test/dev environments do not keep
   * calling the API while the user edits. Manual saving remains available
   * through "Save design changes".
   *
   * const saveSeq = useRef(0);
   * const { markSavedIfLatest } = useVisualAutosaveRevision();
   *
   * useEffect(() => {
   *   if (!canEdit || busy || pendingRevision <= savedRevisionRef.current) return;
   *   const saveId = ++saveSeq.current;
   *   const revisionToSave = pendingRevision;
   *   onSaveStatusChange("saving");
   *   const timer = window.setTimeout(() => {
   *     const next = commitCurrent();
   *     const markup = serializeEnvelopes(next);
   *     const assets = assetsRef.current.map((a) => ({ role: a.role, storageUri: a.storageUri, mime: a.mime }));
   *     onAutosaveVisual(markup, assets)
   *       .then((updated) => {
   *         if (saveSeq.current !== saveId) return;
   *         if (!markSavedIfLatest(revisionToSave)) return;
   *         persistedJsonRef.current = markup;
   *         onItemSaved(updated);
   *         onSaveStatusChange("saved");
   *       })
   *       .catch((err) => {
   *         if (saveSeq.current !== saveId) return;
   *         onSaveStatusChange("error", errorMessage(err));
   *       });
   *   }, 900);
   *
   *   return () => {
   *     window.clearTimeout(timer);
   *   };
   * }, [
   *   pendingRevision,
   *   savedRevisionRef,
   *   canEdit,
   *   busy,
   *   commitCurrent,
   *   markSavedIfLatest,
   *   onAutosaveVisual,
   *   onItemSaved,
   *   onSaveStatusChange,
   * ]);
   */

  const goTo = (target: number) => {
    commitCurrent();
    setCurrent(((target % total) + total) % total);
  };

  /**
   * Moves a slide within the carousel. `commitCurrent()` runs FIRST so unsaved edits on the visible
   * slide are folded into the array before it is reordered — otherwise reordering would discard them.
   * The viewer follows the slide it was showing rather than staying on an index that now holds a
   * different slide.
   */
  const reorderSlides = (from: number, to: number) => {
    const committed = commitCurrent();
    const next = arrayMove(committed, from, to);
    envelopesRef.current = next;
    setEnvelopes(next);
    markSaveNeeded(serializeEnvelopes(next) !== persistedJsonRef.current);

    // Undo stacks are keyed by slide index and the count has not changed, so nothing else would
    // invalidate them — an undo after a reorder would write the wrong slide's markup.
    history.reset();

    const followed = idx === from ? to : idx > from && idx <= to ? idx - 1 : idx < from && idx >= to ? idx + 1 : idx;
    setCurrent(followed);
  };

  /** Removes a slide. Local only until "Save design changes" — same contract as every other edit. */
  const deleteSlide = (index: number) => {
    const committed = commitCurrent();
    const next = committed.filter((_, i) => i !== index);
    envelopesRef.current = next;
    setEnvelopes(next);
    markSaveNeeded(serializeEnvelopes(next) !== persistedJsonRef.current);
    setCurrent((prev) => Math.max(0, Math.min(prev > index ? prev - 1 : prev, next.length - 1)));
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
      {/* Instagram-web post: a large editable portrait + the engagement sidebar. The media is sized to
          the viewport height (see feedMaxHeight) so it is big enough to edit without forcing a scroll. */}
      <div className="mx-auto w-full max-w-[1040px] overflow-hidden rounded-xl border border-border bg-background shadow-sm md:flex md:items-stretch">
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
                onDirty={(edit) => {
                  markEdited();
                  history.onDirty(edit);
                }}
                canUndo={history.canUndo}
                canRedo={history.canRedo}
                onUndo={() => {
                  if (history.undo()) markEdited();
                }}
                onRedo={() => {
                  if (history.redo()) markEdited();
                }}
                fallbackImageUrl={slide?.flattenedImageRef ?? null}
                maxHeight={feedMaxHeight}
                renderCapabilities={renderCapabilities}
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
              {formatDate(item.createdAtUtc)}
            </p>
          </div>
          <div className="shrink-0 border-t border-border px-3 py-2 text-sm text-muted-foreground">Add a comment…</div>
        </div>
      </div>

      {total > 1 ? (
        <SlideThumbnails
          envelopes={envelopes}
          idx={idx}
          canEdit={canEdit}
          onSelect={goTo}
          onReorder={reorderSlides}
          onDelete={deleteSlide}
        />
      ) : null}

      {canEdit ? (
        <div className="flex items-center justify-between gap-2">
          <p className="text-xs text-muted-foreground">Click a text to select it, drag to move, double-click to edit.</p>
          {/* AutosaveIndicator is hidden while autosave is temporarily disabled. */}
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

/**
 * Carousel filmstrip: a clickable preview of every slide so the user can see what is coming next and
 * jump straight to any slide — like the slide rail in Canva. The active slide is highlighted.
 */
/** One draggable slide tile in the filmstrip. */
function SortableSlide({
  index,
  envelope,
  isActive,
  canEdit,
  onSelect,
  onRequestDelete,
}: {
  index: number;
  envelope: Envelope;
  isActive: boolean;
  canEdit: boolean;
  onSelect: () => void;
  onRequestDelete: () => void;
}) {
  const slideWidth = envelope.canvas?.width ?? 1080;
  const slideHeight = envelope.canvas?.height ?? 1350;
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: `slide-${index}`,
    disabled: !canEdit,
  });

  return (
    <div
      ref={setNodeRef}
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
        width: 76,
        height: Math.round((76 * slideHeight) / slideWidth),
      }}
      className={cn("relative shrink-0", isDragging && "z-10 opacity-80")}
    >
      <button
        type="button"
        aria-label={`Go to slide ${index + 1}`}
        aria-current={isActive}
        onClick={onSelect}
        // The drag listeners live on the tile itself: dnd-kit's pointer sensor only starts a drag
        // after the activation distance, so a plain click still selects the slide.
        {...attributes}
        {...listeners}
        className={cn(
          "relative h-full w-full overflow-hidden rounded-md border-2 transition-colors",
          canEdit ? "cursor-grab active:cursor-grabbing" : "cursor-pointer",
          isActive ? "border-primary ring-2 ring-primary/30" : "border-border hover:border-primary/50",
        )}
      >
        <SlideThumbnail
          markup={envelope.markup ?? ""}
          fallbackImageUrl={envelope.flattenedImageRef ?? null}
          width={slideWidth}
          height={slideHeight}
        />
        <span className="absolute bottom-0.5 left-0.5 rounded bg-black/55 px-1 text-[10px] font-medium leading-tight text-white">
          {index + 1}
        </span>
      </button>

      {canEdit ? (
        <button
          type="button"
          aria-label={`Delete slide ${index + 1}`}
          title="Delete slide"
          // Deliberately OUTSIDE the draggable button: nesting it would make every press on the bin
          // start a drag instead of a click.
          onClick={(e) => {
            e.stopPropagation();
            onRequestDelete();
          }}
          className="absolute bottom-0.5 right-0.5 grid h-5 w-5 place-items-center rounded bg-black/55 text-white transition-colors hover:bg-destructive"
        >
          <Trash2 className="h-3 w-3" />
        </button>
      ) : null}
    </div>
  );
}

/**
 * Carousel filmstrip: select, REORDER (drag) and delete slides.
 *
 * Order is the publication order of the carousel, so it has to be editable here — regenerating the
 * piece just to move slide 2 ahead of slide 1 was the only previous option.
 */
function SlideThumbnails({
  envelopes,
  idx,
  canEdit,
  onSelect,
  onReorder,
  onDelete,
}: {
  envelopes: Envelope[];
  idx: number;
  canEdit: boolean;
  onSelect: (target: number) => void;
  onReorder: (from: number, to: number) => void;
  onDelete: (index: number) => void;
}) {
  const [pendingDelete, setPendingDelete] = useState<number | null>(null);
  const sensors = useSensors(
    // 5px of travel before a drag begins, so tapping a tile still selects it.
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const ids = envelopes.map((_, i) => `slide-${i}`);

  return (
    <>
      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragEnd={(event: DragEndEvent) => {
          const { active, over } = event;
          if (!over || active.id === over.id) return;
          const from = ids.indexOf(String(active.id));
          const to = ids.indexOf(String(over.id));
          if (from >= 0 && to >= 0) onReorder(from, to);
        }}
      >
        <SortableContext items={ids} strategy={horizontalListSortingStrategy}>
          <div className="mx-auto flex w-full max-w-[1040px] items-center gap-2 overflow-x-auto pb-1">
            {envelopes.map((envelope, i) => (
              <SortableSlide
                key={ids[i]}
                index={i}
                envelope={envelope}
                isActive={i === idx}
                canEdit={canEdit}
                onSelect={() => onSelect(i)}
                onRequestDelete={() => setPendingDelete(i)}
              />
            ))}
          </div>
        </SortableContext>
      </DndContext>

      <ConfirmationDialog
        open={pendingDelete !== null}
        title={`Delete slide ${(pendingDelete ?? 0) + 1}?`}
        description="The slide and its design are removed from this piece. Save the design changes to persist it."
        confirmLabel="Delete slide"
        confirmVariant="destructive"
        onConfirm={() => {
          if (pendingDelete !== null) onDelete(pendingDelete);
          setPendingDelete(null);
        }}
        onCancel={() => setPendingDelete(null)}
      />
    </>
  );
}

/** A non-interactive, scaled-down render of a single slide's markup, used inside the carousel filmstrip. */
function SlideThumbnail({
  markup,
  fallbackImageUrl,
  width,
  height,
}: {
  markup: string;
  fallbackImageUrl: string | null;
  width: number;
  height: number;
}) {
  const boxRef = useRef<HTMLDivElement>(null);
  const hostRef = useRef<HTMLDivElement>(null);
  const shadowRef = useRef<ShadowRoot | null>(null);
  const [scale, setScale] = useState(0);
  const hasMarkup = markup.trim().length > 0;

  useEffect(() => {
    const host = hostRef.current;
    if (!host || !hasMarkup) return;
    const shadow = shadowRef.current ?? host.attachShadow({ mode: "open" });
    shadowRef.current = shadow;
    shadow.innerHTML = `
      <style>
        :host { all: initial; display: block; }
        *, *::before, *::after { box-sizing: border-box; }
        [data-cs-thumb-root] { position: relative; overflow: hidden; background: #fff; }
        .composition { position: relative; }
      </style>
      <div data-cs-thumb-root>${sanitizeDesignMarkup(markup)}</div>`;
  }, [markup, hasMarkup]);

  useEffect(() => {
    const el = boxRef.current;
    if (!el) return;
    const update = () => setScale(el.clientWidth / width);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(el);
    return () => observer.disconnect();
  }, [width]);

  return (
    <div ref={boxRef} className="relative h-full w-full overflow-hidden bg-white">
      {hasMarkup ? (
        <div
          ref={hostRef}
          style={{ width, height, transform: `scale(${scale})`, transformOrigin: "top left" }}
        />
      ) : fallbackImageUrl ? (
        <img src={fallbackImageUrl} alt="" aria-hidden="true" className="h-full w-full object-cover" />
      ) : null}
    </div>
  );
}

interface DesignCanvasHandle {
  getState: () => { markup: string; slotStyles: ElementStyle[] };
  /**
   * Imperatively replaces the shadow-DOM markup (undo/redo restore, DD5/DD7). Does NOT write to the
   * `markup` prop or `envelopes` — writing back a value equal to the current prop is a no-op React
   * never re-runs, which is exactly why the prop route silently does nothing on the first undo.
   */
  restore: (markup: string) => void;
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

/**
 * `isBackgroundLayer`, `isContentLayer`, and `pickElement` moved to `@/lib/studio/canvas-hit-test`
 * (Phase 4, characterization; Phase 5, allow-list rewrite) so they can be unit-tested without a
 * browser/jsdom (this repo's test runner is plain Node, no DOM globals). See that module for their
 * documentation and for `isDecorLayer`, `ensureDecorLayer`, and `upgradeLegacyShapes`.
 */

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
  {
    markup: string;
    width: number;
    height: number;
    canEdit: boolean;
    /** Widened for undo/redo (R2): every site keeps calling `onDirty()` unchanged (⇒ "structural");
     * only the `contentEditable` `input` handler passes an explicit `{ kind: "typing", slot }`. */
    onDirty: (edit?: EditSignal) => void;
    fallbackImageUrl?: string | null;
    /** Caps the rendered height (px) so a tall portrait fits the screen instead of forcing a long scroll. */
    maxHeight?: number;
    /** Font contract (R2.3) + color-emoji signal (R6.2) — null while it has not loaded yet. */
    renderCapabilities?: RenderCapabilities | null;
    /** Undo/redo (R6): reachable regardless of selection — never gated on `selRect`. */
    canUndo: boolean;
    canRedo: boolean;
    onUndo: () => void;
    onRedo: () => void;
  }
>(function DesignCanvas(
  {
    markup,
    width,
    height,
    canEdit,
    onDirty,
    fallbackImageUrl,
    maxHeight,
    renderCapabilities = null,
    canUndo,
    canRedo,
    onUndo,
    onRedo,
  },
  ref,
) {
  const measureRef = useRef<HTMLDivElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const hostRef = useRef<HTMLDivElement>(null);
  const shadowRef = useRef<ShadowRoot | null>(null);
  const designRef = useRef<HTMLDivElement | null>(null);
  const scaleRef = useRef(0);
  const [scale, setScale] = useState(0);
  const selectedRef = useRef<HTMLElement | null>(null);
  const [selIsBackground, setSelIsBackground] = useState(false);
  const [selIsText, setSelIsText] = useState(false);
  const [selFontFamily, setSelFontFamily] = useState<string | null>(null);
  const [emojiPickerOpen, setEmojiPickerOpen] = useState(false);
  // Wraps the trigger AND the panel — see the hook's contract.
  const slideEmojiRef = useRef<HTMLDivElement>(null);
  const closeSlideEmojiPicker = useCallback(() => setEmojiPickerOpen(false), []);
  useDismissOnOutsideClick(slideEmojiRef, emojiPickerOpen, closeSlideEmojiPicker);
  const [selRect, setSelRect] = useState<{ top: number; left: number; width: number; height: number } | null>(null);
  // The edit toolbar is a floating, viewport-fixed panel (not pinned onto the component) so it never
  // covers the element being edited nor hides behind sibling components. The user can drag it anywhere
  // and pin it to lock the position. `toolbarPos` is viewport (fixed) coordinates; null = use the
  // default right dock, computed from the canvas so the column sits beside the slide render.
  const [toolbarPos, setToolbarPos] = useState<{ x: number; y: number } | null>(null);
  const [toolbarPinned, setToolbarPinned] = useState(false);
  const dragRef = useRef<
    | { mode: "move"; el: HTMLElement; sx: number; sy: number; left: number; top: number; moved: boolean }
    | { mode: "resize"; el: HTMLElement; sx: number; sy: number; w: number; h: number; fs: number; moved: boolean }
    | null
  >(null);

  // `onDirty` is passed as an INLINE arrow by both call sites, so it is a brand-new function on every
  // parent render. Depending on it directly re-ran the effects below constantly — including the one
  // that rewrites the shadow root — which repainted the whole design and dropped the selection: the
  // toolbar appeared for a frame and vanished. Effects read it through this ref instead, so a repaint
  // happens only when the MARKUP genuinely changes.
  const onDirtyRef = useRef(onDirty);
  const onUndoRef = useRef(onUndo);
  const onRedoRef = useRef(onRedo);
  useEffect(() => {
    onDirtyRef.current = onDirty;
    onUndoRef.current = onUndo;
    onRedoRef.current = onRedo;
  });

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
        /* Layer order (paint bottom -> top): background -> decor (shapes) -> content (text). The
           content wrapper must never intercept pointer events meant for a shape behind it — it is a
           pure layout wrapper (R1.6) — so it (and the inert decor container itself) are click-through;
           only their addressable [data-slot] children opt back in. */
        [data-layer="background"] { position: relative; z-index: 0; }
        [data-layer="decor"] { position: absolute; inset: 0; z-index: 1; pointer-events: none; }
        [data-layer="decor"] [data-slot] { pointer-events: auto; }
        [data-layer="content"] { position: relative; z-index: 2; pointer-events: none; }
        [data-layer="content"] [data-slot] { pointer-events: auto; }
      </style>
      <div data-cs-design-root>${sanitizeDesignMarkup(markup)}</div>`;
    designRef.current = shadow.querySelector<HTMLDivElement>("[data-cs-design-root]");
    const el = designRef.current;
    if (!el) return;
    selectedRef.current = null;
    setSelRect(null);

    // Pre-Phase-5 drafts may carry shapes with no typed identity (R1.5) — upgrade them to the typed
    // model immediately so they are selectable on first open, not only after the next blind save.
    if (upgradeLegacyShapes(el)) onDirtyRef.current();
    // Deliberately keyed on `markup` alone — see `onDirtyRef` above.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [markup]);

  useEffect(() => {
    const el = measureRef.current;
    if (!el) return;
    const update = () => {
      // Scale to the available column width, then clamp by the height budget so the 4:5/9:16 portrait
      // stays inside the viewport. The aspect ratio is preserved either way — only the scale changes.
      let sc = el.clientWidth / width;
      if (maxHeight && maxHeight > 0 && height * sc > maxHeight) sc = maxHeight / height;
      scaleRef.current = sc;
      setScale(sc);
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(el);
    return () => observer.disconnect();
  }, [width, height, maxHeight]);

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
        target.style.cursor = isBackgroundLayer(target) ? "default" : "move";
      }
      setSelIsBackground(target ? isBackgroundLayer(target) : false);
      // Font-family and emoji pickers only make sense on text — not on the background fill or a
      // decorative shape (data-el-kind="shape", Decision B1/B2).
      setSelIsText(target ? !isBackgroundLayer(target) && target.getAttribute("data-el-kind") !== "shape" : false);
      // First token of the computed stack, unquoted — what the picker highlights as active.
      setSelFontFamily(
        target ? (target.style.fontFamily.split(",")[0] ?? "").replace(/['"]/g, "").trim() || null : null,
      );
      setEmojiPickerOpen(false);
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
      if (!target) {
        // R3.2: an explicit click on the canvas that doesn't hit a selectable element (empty
        // background/decor space) clears the current selection and hides the toolbar.
        select(null);
        return;
      }
      if (target.isContentEditable) return; // editing text — let the caret work
      select(target);
      if (isBackgroundLayer(target)) {
        // Background is selectable (recolor its fill via the toolbar) but must never move or resize.
        e.preventDefault();
        return;
      }
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
        const nw = Math.max(8, d.w + (e.clientX - d.sx) / sc);
        const nh = Math.max(8, d.h + (e.clientY - d.sy) / sc);
        d.el.style.width = `${Math.round(nw)}px`;
        // Text keeps content-driven height and scales its font; decorative shapes/lines get an explicit
        // height so a bar's thickness or a shape's proportions can be resized (not only its width).
        if ((d.el.textContent ?? "").trim().length > 0) {
          d.el.style.fontSize = `${Math.max(8, Math.round(d.fs * (nh / d.h)))}px`;
        } else {
          d.el.style.height = `${Math.round(nh)}px`;
        }
      }
      d.moved = true;
      positionToolbar(d.el);
    };
    const onPointerUp = () => {
      const d = dragRef.current;
      if (d?.moved) onDirtyRef.current();
      dragRef.current = null;
    };
    const onDblClick = (e: MouseEvent) => {
      if (!canEdit) return;
      const root = designRef.current;
      if (!root) return;
      const target = pickElement(e.target as Node, root);
      if (!target || isBackgroundLayer(target)) return; // background has no text to edit in place
      target.contentEditable = "true";
      select(target);
      target.focus();
    };
    const onInput = () => {
      // Only site (of 13) that reports a typing capture — this is what lets `shouldCoalesce` (R2)
      // merge a typed burst into one history entry instead of one per keystroke.
      onDirtyRef.current({ kind: "typing", slot: selectedRef.current?.getAttribute("data-slot") ?? null });
      if (selectedRef.current) positionToolbar(selectedRef.current);
    };
    // Decision C3/R3.1-R3.3: deselection is scoped entirely to the canvas itself now — an explicit
    // click on empty canvas space (onPointerDown's !target branch, R3.2) or an explicit delete
    // (deleteSelected, R3.1). There is deliberately NO document-level "click anywhere else clears
    // selection" listener anymore: that blanket check used to fire for every unrelated app
    // interaction (another panel button, a modal trigger, a sidebar control — R3.3) and hid the
    // toolbar on nearly every click outside the canvas/toolbar/handle.
    const onShadowPointerDown: EventListener = (event) => onPointerDown(event as PointerEvent);
    const onShadowDblClick: EventListener = (event) => onDblClick(event as MouseEvent);

    shadow.addEventListener("pointerdown", onShadowPointerDown);
    window.addEventListener("pointermove", onPointerMove);
    window.addEventListener("pointerup", onPointerUp);
    shadow.addEventListener("dblclick", onShadowDblClick);
    shadow.addEventListener("input", onInput);
    return () => {
      shadow.removeEventListener("pointerdown", onShadowPointerDown);
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
      shadow.removeEventListener("dblclick", onShadowDblClick);
      shadow.removeEventListener("input", onInput);
    };
    // `onDirty` intentionally omitted — read through `onDirtyRef` so an unstable prop identity cannot
    // tear down and re-attach every pointer listener mid-interaction.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canEdit, width, height, select, positionToolbar]);

  useImperativeHandle(ref, () => ({
    getState() {
      const el = designRef.current;
      if (!el) return { markup, slotStyles: [] };
      // Every [data-slot] element round-trips here — text slots AND shapes alike (R1.3: shapes are
      // typed identity + geometry + style, not only opaque serialized markup).
      const slotStyles: ElementStyle[] = [...el.querySelectorAll<HTMLElement>("[data-slot]")].map((node) => {
        const slot = node.getAttribute("data-slot") ?? "";
        const s = getComputedStyle(node);
        if (node.getAttribute("data-el-kind") === "shape") {
          const shapeStyle: ShapeStyle = {
            slot,
            kind: "shape",
            x: node.offsetLeft,
            y: node.offsetTop,
            width: node.offsetWidth,
            height: node.offsetHeight,
            background: s.backgroundColor,
            borderRadius: parseFloat(s.borderRadius) || 0,
            opacity: s.opacity ? parseFloat(s.opacity) : 1,
          };
          return shapeStyle;
        }
        const textStyle: SlotStyle = {
          slot,
          x: node.offsetLeft,
          y: node.offsetTop,
          width: node.offsetWidth,
          height: node.offsetHeight,
          fontSizePx: parseFloat(s.fontSize) || 0,
          fontWeight: s.fontWeight,
          fontStyle: s.fontStyle,
          color: s.color,
          fontFamily: node.style.fontFamily || undefined,
        };
        return textStyle;
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
    restore(nextMarkup: string) {
      const el = designRef.current;
      if (!el) return;

      // Which slot was selected, by its STABLE identity rather than the node object — every node is
      // replaced by the innerHTML write below.
      const previousSlot = selectedRef.current?.getAttribute("data-slot") ?? null;

      el.innerHTML = sanitizeDesignMarkup(nextMarkup);
      // Restored nodes are new objects, so the stale `selectedRef` must be dropped before anything
      // can touch a detached element.
      select(null);
      setSelRect(null);

      // Re-select the same slot in the restored markup. Undo is a SLIDE-content operation; dropping
      // the selection made it also close the floating toolbar, so an undo appeared to "undo" opening
      // the toolbar itself — a screen action the history never recorded and must never touch.
      if (previousSlot) {
        const restored = el.querySelector<HTMLElement>(`[data-slot="${escapeAttributeValue(previousSlot)}"]`);
        if (restored) select(restored);
      }
    },
  }));

  // Ctrl+Z / Ctrl+Shift+Z anywhere in the studio, not only while the canvas div happens to hold
  // focus (it is `tabIndex={-1}`, so after clicking a slot or the toolbar the shortcut was simply
  // lost). Typing in an unrelated input still gets the browser's own undo — `resolveShortcut`
  // already declines while text is being edited, and form fields outside the canvas are skipped.
  useEffect(() => {
    if (!canEdit) return;

    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const inFormField =
        target?.closest?.("input, textarea, select, [contenteditable='true']") != null &&
        target.closest?.("[data-cs-canvas]") == null;
      if (inFormField) return;

      const action = resolveShortcut({
        key: e.key,
        ctrlOrMeta: e.ctrlKey || e.metaKey,
        shift: e.shiftKey,
        isTextEditing: selectedRef.current?.isContentEditable === true,
      });
      if (action === "undo") {
        e.preventDefault();
        onUndoRef.current();
      } else if (action === "redo") {
        e.preventDefault();
        onRedoRef.current();
      }
    };

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [canEdit]);

  const apply = (mutator: (el: HTMLElement) => void) => {
    const el = selectedRef.current;
    if (!el) return;
    mutator(el);
    onDirty();
    positionToolbar(el);
  };

  /** Removes the selected element (never a structural wrapper / the background art layer). */
  const deleteSelected = () => {
    const el = selectedRef.current;
    if (!el || isBackgroundLayer(el)) return;
    el.remove();
    select(null);
    onDirty();
  };

  /** Reorders the selected element among its siblings (later in DOM paints on top). */
  const reorderSelected = (direction: "forward" | "backward") => {
    const el = selectedRef.current;
    const parent = el?.parentElement;
    if (!el || !parent) return;
    if (direction === "forward") {
      const next = el.nextElementSibling;
      if (next) parent.insertBefore(next, el);
    } else {
      const prev = el.previousElementSibling;
      if (prev) parent.insertBefore(el, prev);
    }
    onDirty();
    positionToolbar(el);
  };

  /** Adds a new editable text box (content layer) or a typed decorative shape (decor layer), then selects it. */
  const addElement = (kind: "text" | "shape") => {
    const root = designRef.current;
    if (!root) return;
    const el = document.createElement("div");
    if (kind === "text") {
      el.setAttribute("data-slot", `custom-${Date.now()}`);
      el.textContent = "Texto";
      el.style.cssText =
        `position:absolute;left:${Math.round(width / 2 - 120)}px;top:${Math.round(height / 2 - 24)}px;` +
        "width:240px;min-height:1em;color:#FFFFFF;font-size:40px;font-weight:700;line-height:1.1;text-align:center;";
      (root.querySelector<HTMLElement>('[data-layer="content"]') ?? root).appendChild(el);
    } else {
      // Decision B1/B4: shapes get a stable data-slot identity (so getState()'s existing [data-slot]
      // walk picks them up for free — no parallel persistence pipeline) and a visibly shape-like
      // default (160x160, not the old 200x8 divider) so the operator can tell it was created.
      el.setAttribute("data-slot", `shape-${crypto.randomUUID()}`);
      el.setAttribute("data-el-kind", "shape");
      el.style.cssText =
        `position:absolute;left:${Math.round(width / 2 - 80)}px;top:${Math.round(height / 2 - 80)}px;` +
        "width:160px;height:160px;background:#8C6A45;border-radius:8px;opacity:1;";
      ensureDecorLayer(root).appendChild(el);
    }
    select(el);
    onDirty();
  };

  // The floating toolbar lives in viewport (fixed) space and stacks its controls VERTICALLY, so it
  // docks as a narrow column beside the slide render instead of overlapping it. The dock rule
  // (right by default, left when the viewport has no room) is a pure function so it stays unit-tested.
  const effectiveToolbarPos =
    toolbarPos ??
    (() => {
      const cr = containerRef.current?.getBoundingClientRect();
      if (!cr) return { x: 16, y: 120 };
      return resolveToolbarDock(cr, window.innerWidth);
    })();

  // Drag the whole panel by its header. Disabled while pinned so a pinned toolbar stays put.
  const onToolbarDragStart = (e: ReactPointerEvent<HTMLDivElement>) => {
    if (toolbarPinned) return;
    const start = { x: e.clientX, y: e.clientY };
    const origin = effectiveToolbarPos;
    const move = (ev: PointerEvent) => {
      setToolbarPos({
        x: Math.max(4, origin.x + (ev.clientX - start.x)),
        y: Math.max(4, origin.y + (ev.clientY - start.y)),
      });
    };
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    e.preventDefault();
    e.stopPropagation();
  };

  return (
    <div ref={measureRef} className="w-full">
    {/* The add/undo bar sits over the black media backdrop, where bare `text-foreground` controls
        were effectively invisible. It carries its own surface so it stays readable on any backdrop. */}
    {canEdit ? (
      <div
        data-cs-add
        className="mb-2 flex flex-wrap items-center gap-1 rounded-md border border-border bg-background/95 p-1 shadow-sm"
      >
        <button
          type="button"
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => addElement("text")}
          className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-foreground hover:bg-muted"
        >
          <Type className="h-3.5 w-3.5" /> Texto
        </button>
        <button
          type="button"
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => addElement("shape")}
          className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-foreground hover:bg-muted"
        >
          <Square className="h-3.5 w-3.5" /> Forma
        </button>
        <button
          type="button"
          onMouseDown={(e) => e.preventDefault()}
          onClick={onUndo}
          disabled={!canUndo}
          className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-foreground hover:bg-muted disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent"
        >
          <Undo2 className="h-3.5 w-3.5" /> Deshacer
        </button>
        <button
          type="button"
          onMouseDown={(e) => e.preventDefault()}
          onClick={onRedo}
          disabled={!canRedo}
          className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-foreground hover:bg-muted disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent"
        >
          <Redo2 className="h-3.5 w-3.5" /> Rehacer
        </button>
      </div>
    ) : null}
    <div
      data-cs-canvas
      ref={containerRef}
      tabIndex={-1}
      onKeyDown={(e: ReactKeyboardEvent<HTMLDivElement>) => {
        const action = resolveShortcut({
          key: e.key,
          ctrlOrMeta: e.ctrlKey || e.metaKey,
          shift: e.shiftKey,
          isTextEditing: selectedRef.current?.isContentEditable === true,
        });
        if (action === "undo") {
          e.preventDefault();
          onUndo();
        } else if (action === "redo") {
          e.preventDefault();
          onRedo();
        }
      }}
      className="relative mx-auto overflow-hidden bg-white"
      style={{ width: scale > 0 ? width * scale : "100%", height: scale > 0 ? height * scale : undefined }}
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
      {selRect && !selIsBackground ? (
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
      ) : null}
    </div>
    {selRect ? (
        <div
          data-cs-toolbar
          className="fixed z-[60] w-max rounded-md border border-border bg-background shadow-2xl"
          style={{ top: effectiveToolbarPos.y, left: effectiveToolbarPos.x }}
        >
          {/* Draggable header — move the panel anywhere; pin to lock it in place. Stacked vertically
              so the header never forces the column wider than its controls; the "Edit" affordance is
              carried by the grip icon's title instead of a text label. */}
          <div
            onPointerDown={onToolbarDragStart}
            className={cn(
              "flex flex-col items-center gap-1 rounded-t-md border-b border-border bg-muted/60 px-1.5 py-1",
              toolbarPinned ? "cursor-default" : "cursor-grab active:cursor-grabbing",
            )}
          >
            <GripVertical className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
            <span className="sr-only">Edit</span>
            <button
              type="button"
              aria-label={toolbarPinned ? "Unpin toolbar" : "Pin toolbar"}
              aria-pressed={toolbarPinned}
              title={toolbarPinned ? "Unpin toolbar" : "Pin toolbar in place"}
              onPointerDown={(e) => e.stopPropagation()}
              onClick={() => setToolbarPinned((p) => !p)}
              className={cn(
                "grid h-6 w-6 place-items-center rounded hover:bg-muted",
                toolbarPinned ? "text-primary" : "text-muted-foreground",
              )}
            >
              {toolbarPinned ? <Pin className="h-3.5 w-3.5" /> : <PinOff className="h-3.5 w-3.5" />}
            </button>
          </div>
          <div className="flex flex-col items-center gap-0.5 p-1">
          {/* Undo/redo also live up on the bar above the canvas, but that bar sits over the black
              media backdrop where the controls are hard to make out. The floating toolbar is where
              the operator already is while editing. */}
          <div className="flex items-center gap-0.5">
            <ToolbarButton label="Undo (Ctrl+Z)" onClick={onUndo} disabled={!canUndo}>
              <Undo2 className="h-4 w-4" />
            </ToolbarButton>
            <ToolbarButton label="Redo (Ctrl+Shift+Z)" onClick={onRedo} disabled={!canRedo}>
              <Redo2 className="h-4 w-4" />
            </ToolbarButton>
          </div>
          {selIsText && renderCapabilities && renderCapabilities.fonts.length > 0 ? (
            <FontFamilyPicker
              fonts={renderCapabilities.fonts}
              activeFamily={selFontFamily}
              onSelect={(family) =>
                apply((el) => {
                  // A full stack with fallbacks — a bare multi-word name is unquoted and gives the
                  // renderer nothing to fall back to.
                  el.style.fontFamily = fontStackFor(family);
                  setSelFontFamily(family);
                })
              }
            />
          ) : null}
          {selIsText && canOfferSlideEmojiPicker(renderCapabilities) ? (
            <div className="relative" ref={slideEmojiRef}>
              <ToolbarButton label="Insert emoji" onClick={() => setEmojiPickerOpen((open) => !open)}>
                <Smile className="h-4 w-4" />
              </ToolbarButton>
              {emojiPickerOpen ? (
                <div className="absolute left-full top-0 z-[70] ml-1">
                  <EmojiPicker
                    theme={EmojiPickerTheme.AUTO}
                    emojiStyle={EmojiStyle.NATIVE}
                    onEmojiClick={(emojiData) => {
                      apply((el) => {
                        el.focus();
                        const inserted = document.execCommand?.("insertText", false, emojiData.emoji);
                        if (!inserted) {
                          el.textContent = `${el.textContent ?? ""}${emojiData.emoji}`;
                        }
                      });
                      setEmojiPickerOpen(false);
                    }}
                  />
                </div>
              ) : null}
            </div>
          ) : null}
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
                // Text → glyph color; border-only decorations (thin lines/rings) → borderColor
                // (setting backgroundColor on a border-drawn line was a no-op); filled shapes → fill.
                const cs = getComputedStyle(el);
                const property = pickRecolorProperty({
                  hasText: (el.textContent ?? "").trim().length > 0,
                  hasVisibleBorder: hasVisibleBorder(cs.borderStyle, parseFloat(cs.borderWidth) || 0),
                });
                el.style[property] = value;
              })
            }
            className="h-6 w-6 cursor-pointer rounded border border-border bg-transparent p-0"
          />
          {/* Group separator — horizontal rule, matching the vertical stack direction. */}
          <div className="my-0.5 h-px w-5 bg-border" aria-hidden="true" />
          <ToolbarButton label="Send backward" onClick={() => reorderSelected("backward")}>
            <ArrowDown className="h-4 w-4" />
          </ToolbarButton>
          <ToolbarButton label="Bring forward" onClick={() => reorderSelected("forward")}>
            <ArrowUp className="h-4 w-4" />
          </ToolbarButton>
          {selIsBackground ? null : (
            <ToolbarButton label="Delete" onClick={deleteSelected}>
              <Trash2 className="h-4 w-4" />
            </ToolbarButton>
          )}
          </div>
        </div>
      ) : null}
    </div>
  );
});

/**
 * Escapes a value for use inside a double-quoted attribute selector.
 *
 * `CSS.escape` is unavailable by that name in this module: `@dnd-kit/utilities` exports a `CSS`
 * helper object that shadows the global one.
 */
function escapeAttributeValue(value: string): string {
  return value.replace(/["\\]/g, "\\$&");
}

function ToolbarButton({
  children,
  label,
  onClick,
  disabled = false,
}: {
  children: ReactNode;
  label: string;
  onClick: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      disabled={disabled}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
      className="grid h-7 w-7 place-items-center rounded text-foreground hover:bg-muted disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent"
    >
      {children}
    </button>
  );
}

/**
 * Font chooser: a popover of large, self-previewing swatches — each option is rendered IN the family
 * it selects, which is the only way to actually choose a typeface.
 *
 * Replaces a 64px native `<select>`. Beyond looking dated, that control could not preview anything
 * and reset itself to a placeholder after each pick, so it never showed which family was active.
 */
function FontFamilyPicker({
  fonts,
  activeFamily,
  onSelect,
}: {
  fonts: { family: string; isBrand: boolean }[];
  activeFamily: string | null;
  onSelect: (family: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const close = useCallback(() => setOpen(false), []);
  useDismissOnOutsideClick(containerRef, open, close);
  const { brand, system } = groupFontOptions(fonts);

  const renderGroup = (label: string, options: { family: string; isBrand: boolean }[]) =>
    options.length === 0 ? null : (
      <div className="flex flex-col gap-1">
        <span className="px-1 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
          {label}
        </span>
        {options.map((option) => {
          const isActive = activeFamily === option.family;
          return (
            <button
              key={option.family}
              type="button"
              onPointerDown={(e) => e.stopPropagation()}
              onClick={() => {
                onSelect(option.family);
                setOpen(false);
              }}
              // Previewed in its own family, so the list reads as the typefaces themselves.
              style={{ fontFamily: fontStackFor(option.family) }}
              className={cn(
                "flex items-center justify-between gap-3 rounded-md border px-3 py-2 text-left text-base transition-colors",
                isActive
                  ? "border-primary bg-primary/10 text-foreground"
                  : "border-transparent hover:border-border hover:bg-muted",
              )}
            >
              <span className="truncate">{option.family}</span>
              {isActive ? <Check className="h-4 w-4 shrink-0 text-primary" /> : null}
            </button>
          );
        })}
      </div>
    );

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        aria-label="Font family"
        aria-expanded={open}
        title="Font family"
        onPointerDown={(e) => e.stopPropagation()}
        onClick={() => setOpen((v) => !v)}
        className="flex h-8 w-14 flex-col items-center justify-center rounded-md border border-border bg-background text-foreground transition-colors hover:bg-muted"
      >
        <Type className="h-3.5 w-3.5" />
        <span className="w-full truncate px-1 text-[9px] leading-tight text-muted-foreground">
          {activeFamily ?? "Font"}
        </span>
      </button>

      {open ? (
        <div
          onPointerDown={(e) => e.stopPropagation()}
          className="absolute left-full top-0 z-[70] ml-1 flex max-h-80 w-60 flex-col gap-3 overflow-y-auto rounded-lg border border-border bg-background p-2 shadow-xl"
        >
          {renderGroup("Brand", brand)}
          {renderGroup("System", system)}
        </div>
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
