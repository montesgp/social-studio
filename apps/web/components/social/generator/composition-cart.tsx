"use client";

import { useMemo, useState } from "react";
import {
  DndContext,
  DragEndEvent,
  DragOverlay,
  DragStartEvent,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors
} from "@dnd-kit/core";
import { Dices, Minus, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { BatchCompositionItem, ContentFormatOption } from "@/lib/api/content-batches";

/**
 * The composition cart: drag format tiles in, dial the count, hit generate.
 *
 * The tile list comes from the backend format catalog — never a literal here — so a new deliverable
 * format appears without a frontend change.
 *
 * Dragging is the pleasant path, not the only one. A pointer sensor with an activation distance
 * keeps a plain click working as "add one", which matters for touch and for anyone who does not want
 * to drag five times to order five pieces.
 */

/** The assorted tile is not a catalog format; it is a request for the system to choose the mix. */
const ASSORTED_KEY = "__assorted__";

export interface CompositionCartValue {
  composition: BatchCompositionItem[];
  assortedTotal: number;
}

interface CompositionCartProps {
  formats: ContentFormatOption[];
  value: CompositionCartValue;
  onChange: (value: CompositionCartValue) => void;
  /** Resolved from the client record, or the platform default. */
  maxPieces: number;
  disabled?: boolean;
}

export function CompositionCart({
  formats,
  value,
  onChange,
  maxPieces,
  disabled = false
}: CompositionCartProps) {
  const [draggingKey, setDraggingKey] = useState<string | null>(null);

  const sensors = useSensors(
    // 6px of travel before a drag starts, so a click stays a click.
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } })
  );

  const totalPieces = useMemo(
    () => value.composition.reduce((sum, item) => sum + item.count, 0) + value.assortedTotal,
    [value]
  );

  const remaining = maxPieces - totalPieces;

  function addPiece(formatKey: string, amount = 1) {
    if (disabled || remaining < amount) return;

    if (formatKey === ASSORTED_KEY) {
      onChange({ ...value, assortedTotal: value.assortedTotal + amount });
      return;
    }

    const existing = value.composition.find((item) => item.formatKey === formatKey);

    onChange({
      ...value,
      composition: existing
        ? value.composition.map((item) =>
            item.formatKey === formatKey ? { ...item, count: item.count + amount } : item
          )
        : [...value.composition, { formatKey, count: amount }]
    });
  }

  function setCount(formatKey: string, count: number) {
    if (disabled) return;

    if (formatKey === ASSORTED_KEY) {
      onChange({ ...value, assortedTotal: Math.max(0, count) });
      return;
    }

    onChange({
      ...value,
      composition:
        count <= 0
          ? value.composition.filter((item) => item.formatKey !== formatKey)
          : value.composition.map((item) =>
              item.formatKey === formatKey ? { ...item, count } : item
            )
    });
  }

  function handleDragEnd(event: DragEndEvent) {
    setDraggingKey(null);
    if (event.over?.id === "composition-cart") {
      addPiece(String(event.active.id));
    }
  }

  function handleDragStart(event: DragStartEvent) {
    setDraggingKey(String(event.active.id));
  }

  const labelFor = (formatKey: string) =>
    formatKey === ASSORTED_KEY
      ? "Assorted"
      : formats.find((format) => format.formatKey === formatKey)?.displayLabel ?? formatKey;

  return (
    <DndContext sensors={sensors} onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
      <div className="grid gap-4 lg:grid-cols-[1fr_1.2fr]">
        <section aria-label="Available formats" className="space-y-2">
          <h3 className="text-sm font-medium">Formats</h3>
          <p className="text-xs text-muted-foreground">
            Drag a format into the cart, or click it to add one piece.
          </p>

          <div className="grid grid-cols-2 gap-2">
            {formats.map((format) => (
              <FormatTile
                key={format.formatKey}
                id={format.formatKey}
                label={format.displayLabel}
                hint={format.isMultiAsset ? "multi-slide" : format.medium}
                disabled={disabled || remaining <= 0}
                onClick={() => addPiece(format.formatKey)}
              />
            ))}

            <FormatTile
              id={ASSORTED_KEY}
              label="Assorted"
              hint="system picks the mix"
              icon={<Dices className="h-4 w-4" aria-hidden="true" />}
              disabled={disabled || remaining <= 0}
              onClick={() => addPiece(ASSORTED_KEY)}
            />
          </div>
        </section>

        <CartDropZone
          items={[
            ...value.composition,
            ...(value.assortedTotal > 0
              ? [{ formatKey: ASSORTED_KEY, count: value.assortedTotal }]
              : [])
          ]}
          labelFor={labelFor}
          totalPieces={totalPieces}
          maxPieces={maxPieces}
          disabled={disabled}
          onIncrement={(formatKey) => addPiece(formatKey)}
          onDecrement={(formatKey, current) => setCount(formatKey, current - 1)}
          onRemove={(formatKey) => setCount(formatKey, 0)}
        />
      </div>

      <DragOverlay>
        {draggingKey ? (
          <div className="rounded-lg border border-primary bg-card px-3 py-2 text-xs font-medium shadow-lg">
            {labelFor(draggingKey)}
          </div>
        ) : null}
      </DragOverlay>
    </DndContext>
  );
}

function FormatTile({
  id,
  label,
  hint,
  icon,
  disabled,
  onClick
}: {
  id: string;
  label: string;
  hint?: string;
  icon?: React.ReactNode;
  disabled: boolean;
  onClick: () => void;
}) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({ id, disabled });

  return (
    <button
      type="button"
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      onClick={onClick}
      disabled={disabled}
      className={`flex flex-col items-start gap-1 rounded-lg border border-border bg-card/60 p-3 text-left transition hover:border-primary/60 disabled:cursor-not-allowed disabled:opacity-50 ${
        isDragging ? "opacity-40" : ""
      }`}
    >
      <span className="flex items-center gap-1.5 text-xs font-medium">
        {icon}
        {label}
      </span>
      {hint ? <span className="text-[10px] text-muted-foreground">{hint}</span> : null}
    </button>
  );
}

function CartDropZone({
  items,
  labelFor,
  totalPieces,
  maxPieces,
  disabled,
  onIncrement,
  onDecrement,
  onRemove
}: {
  items: BatchCompositionItem[];
  labelFor: (formatKey: string) => string;
  totalPieces: number;
  maxPieces: number;
  disabled: boolean;
  onIncrement: (formatKey: string) => void;
  onDecrement: (formatKey: string, current: number) => void;
  onRemove: (formatKey: string) => void;
}) {
  const { setNodeRef, isOver } = useDroppable({ id: "composition-cart" });
  const atCap = totalPieces >= maxPieces;

  return (
    <section
      ref={setNodeRef}
      aria-label="Composition cart"
      className={`min-h-[220px] rounded-lg border border-dashed p-3 transition ${
        isOver ? "border-primary bg-primary/5" : "border-border"
      }`}
    >
      <header className="mb-3 flex items-baseline justify-between">
        <h3 className="text-sm font-medium">Cart</h3>
        <span className={`text-xs ${atCap ? "text-amber-400" : "text-muted-foreground"}`}>
          {totalPieces} / {maxPieces} pieces
        </span>
      </header>

      {items.length === 0 ? (
        <p className="py-10 text-center text-xs text-muted-foreground">
          Empty. Drop a format here to start composing the batch.
        </p>
      ) : (
        <ul className="space-y-2">
          {items.map((item) => (
            <li
              key={item.formatKey}
              className="flex items-center justify-between gap-2 rounded-md border border-border bg-card/60 px-3 py-2"
            >
              <span className="text-xs font-medium">{labelFor(item.formatKey)}</span>

              <div className="flex items-center gap-1">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={`One less ${labelFor(item.formatKey)}`}
                  disabled={disabled}
                  onClick={() => onDecrement(item.formatKey, item.count)}
                >
                  <Minus className="h-3 w-3" />
                </Button>

                <span className="w-6 text-center text-xs tabular-nums">{item.count}</span>

                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={`One more ${labelFor(item.formatKey)}`}
                  disabled={disabled || atCap}
                  onClick={() => onIncrement(item.formatKey)}
                >
                  <Plus className="h-3 w-3" />
                </Button>

                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={`Remove ${labelFor(item.formatKey)}`}
                  disabled={disabled}
                  onClick={() => onRemove(item.formatKey)}
                >
                  <Trash2 className="h-3 w-3" />
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {atCap ? (
        <p className="mt-3 text-[11px] text-amber-400">
          Cap reached. Every piece is a full AI run, so this ceiling is a cost control.
        </p>
      ) : null}
    </section>
  );
}
