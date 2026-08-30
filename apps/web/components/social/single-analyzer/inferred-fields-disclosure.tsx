"use client";

import { useState } from "react";
import { ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";

interface InferredFieldsDisclosureProps {
  fields: string[];
  className?: string;
}

/**
 * Collapsible disclosure listing inferred brand fields used during content generation.
 * Collapsed by default — informational, not alarming.
 */
export function InferredFieldsDisclosure({ fields, className }: InferredFieldsDisclosureProps) {
  const [isOpen, setIsOpen] = useState(false);

  if (fields.length === 0) return null;

  return (
    <div className={cn("rounded-md border border-border/30 bg-muted/20", className)}>
      <button
        type="button"
        onClick={() => setIsOpen((v) => !v)}
        className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left"
        aria-expanded={isOpen}
      >
        <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <ChevronRight
            className={cn(
              "h-3 w-3 transition-transform duration-150",
              isOpen && "rotate-90"
            )}
          />
          Inferred fields
          <Badge variant="outline" className="text-[10px] px-1.5 py-0 h-4">
            {fields.length}
          </Badge>
        </span>
        <span className="text-[10px] text-muted-foreground/50">
          {isOpen ? "Hide" : "Show"}
        </span>
      </button>

      {isOpen && (
        <div
          id="inferred-fields-list"
          className="flex flex-wrap gap-1 px-3 pb-2.5"
        >
          {fields.map((field) => (
            <span
              key={field}
              className="inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-mono bg-muted/40 text-muted-foreground border border-border/30"
            >
              {field}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
