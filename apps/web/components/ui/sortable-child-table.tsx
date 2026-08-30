"use client";

import React, { useState } from "react";
import {
  DndContext,
  DragEndEvent,
  DragOverlay,
  DragStartEvent,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { GripVertical, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export interface Column<T> {
  key: string;
  label: string;
  render: (item: T) => React.ReactNode;
}

export interface SortableChildTableProps<T> {
  items: T[];
  getId: (item: T) => string;
  columns: Column<T>[];
  onReorder: (orderedIds: string[]) => void;
  onToggleActive?: (id: string, currentlyActive: boolean) => void;
  onDelete?: (id: string) => void;
  primaryLabel?: string;
  emptyState?: React.ReactNode;
  isLoading?: boolean;
}

interface SortableRowProps<T> {
  item: T;
  id: string;
  isPrimary: boolean;
  primaryLabel: string;
  columns: Column<T>[];
  onToggleActive?: (id: string, currentlyActive: boolean) => void;
  onDelete?: (id: string) => void;
  isOverlay?: boolean;
}

function SortableRow<T>({
  item,
  id,
  isPrimary,
  primaryLabel,
  columns,
  onToggleActive,
  onDelete,
  isOverlay = false,
}: SortableRowProps<T>) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id });

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0 : 1,
  };

  return (
    <tr
      ref={isOverlay ? undefined : setNodeRef}
      style={isOverlay ? undefined : style}
      className={cn(
        "border-b border-border hover:bg-muted/40",
        isOverlay && "bg-muted/60 shadow-md rounded-md opacity-95"
      )}
    >
      <td className="w-8 p-2">
        <button
          type="button"
          className="cursor-grab touch-none text-muted-foreground hover:text-foreground active:cursor-grabbing"
          aria-label="Drag to reorder"
          {...(isOverlay ? {} : { ...attributes, ...listeners })}
        >
          <GripVertical className="h-4 w-4" />
        </button>
      </td>

      {isPrimary && (
        <td className="p-2">
          <Badge variant="default">{primaryLabel}</Badge>
        </td>
      )}
      {!isPrimary && <td className="p-2" />}

      {columns.map((col) => (
        <td key={col.key} className="p-4 align-middle text-sm">
          {col.render(item)}
        </td>
      ))}

      {(onToggleActive ?? onDelete) ? (
        <td className="p-2 text-right">
          <div className="inline-flex gap-1">
            {onDelete ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => onDelete(id)}
                aria-label="Delete"
              >
                <Trash2 className="h-4 w-4 text-red-400" />
              </Button>
            ) : null}
          </div>
        </td>
      ) : null}
    </tr>
  );
}

export function SortableChildTable<T>({
  items,
  getId,
  columns,
  onReorder,
  onToggleActive,
  onDelete,
  primaryLabel = "Principal",
  emptyState,
  isLoading = false,
}: SortableChildTableProps<T>) {
  const [orderedItems, setOrderedItems] = useState<T[]>(items);
  const [activeId, setActiveId] = useState<string | null>(null);

  React.useEffect(() => {
    setOrderedItems(items);
  }, [items]);

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
  );

  const ids = orderedItems.map(getId);

  function handleDragStart(event: DragStartEvent) {
    setActiveId(String(event.active.id));
  }

  function handleDragEnd(event: DragEndEvent) {
    setActiveId(null);
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    const oldIndex = ids.indexOf(String(active.id));
    const newIndex = ids.indexOf(String(over.id));
    const reordered = arrayMove(orderedItems, oldIndex, newIndex);
    setOrderedItems(reordered);
    onReorder(reordered.map(getId));
  }

  const hasActions = !!(onToggleActive ?? onDelete);
  const colSpan = 2 + columns.length + (hasActions ? 1 : 0);

  if (isLoading) {
    return (
      <div className="w-full overflow-auto">
        <table className="w-full caption-bottom text-sm">
          <thead>
            <HeaderRow columns={columns} hasActions={hasActions} />
          </thead>
          <tbody>
            {Array.from({ length: 3 }).map((_, i) => (
              <tr key={i} className="border-b border-border">
                <td colSpan={colSpan} className="p-4">
                  <div className="h-4 w-full animate-pulse rounded bg-muted" />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  if (orderedItems.length === 0) {
    return (
      <div className="w-full overflow-auto">
        <table className="w-full caption-bottom text-sm">
          <thead>
            <HeaderRow columns={columns} hasActions={hasActions} />
          </thead>
          <tbody>
            <tr>
              <td colSpan={colSpan} className="p-8 text-center text-sm text-muted-foreground">
                {emptyState ?? "No items."}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    );
  }

  const activeItem = activeId ? orderedItems.find((item) => getId(item) === activeId) : null;

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      onDragStart={handleDragStart}
      onDragEnd={handleDragEnd}
    >
      <SortableContext items={ids} strategy={verticalListSortingStrategy}>
        <div className="w-full overflow-auto">
          <table className="w-full caption-bottom text-sm">
            <thead>
              <HeaderRow columns={columns} hasActions={hasActions} />
            </thead>
            <tbody>
              {orderedItems.map((item, index) => {
                const id = getId(item);
                return (
                  <SortableRow
                    key={id}
                    item={item}
                    id={id}
                    isPrimary={index === 0}
                    primaryLabel={primaryLabel}
                    columns={columns}
                    onToggleActive={onToggleActive}
                    onDelete={onDelete}
                  />
                );
              })}
            </tbody>
          </table>
        </div>
      </SortableContext>

      <DragOverlay>
        {activeItem ? (
          <table className="w-full text-sm">
            <tbody>
              <SortableRow
                item={activeItem}
                id={getId(activeItem)}
                isPrimary={orderedItems.indexOf(activeItem) === 0}
                primaryLabel={primaryLabel}
                columns={columns}
                isOverlay
              />
            </tbody>
          </table>
        ) : null}
      </DragOverlay>
    </DndContext>
  );
}

function HeaderRow<T>({ columns, hasActions }: { columns: Column<T>[]; hasActions: boolean }) {
  return (
    <tr className="border-b border-border">
      <th className="h-10 w-8 px-2 text-left font-medium text-muted-foreground" />
      <th className="h-10 px-2 text-left font-medium text-muted-foreground" />
      {columns.map((col) => (
        <th key={col.key} className="h-10 px-4 text-left font-medium text-muted-foreground">
          {col.label}
        </th>
      ))}
      {hasActions ? <th className="h-10 px-2 text-right font-medium text-muted-foreground" /> : null}
    </tr>
  );
}
