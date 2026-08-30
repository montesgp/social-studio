"use client";

import { ChevronDown, ChevronUp } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import type { ContentWorkflowStatus, ContentWorkflowStep } from "@/lib/api/types";

export interface GenerationProgressPanelProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  status: ContentWorkflowStatus | null;
  steps: ContentWorkflowStep[];
}

/** Discovery-only progress (analysis/generation progress moved to content-generation-jobs). */
export function GenerationProgressPanel({ open, onOpenChange, status, steps }: GenerationProgressPanelProps) {
  return (
    <Card>
      <Collapsible open={open} onOpenChange={onOpenChange}>
        <CardHeader className="flex-row items-center justify-between">
          <CardTitle className="flex items-center gap-2 text-base">
            <span>Generation progress</span>
            <Badge variant={status?.status === "completed" ? "success" : "muted"}>{status?.status ?? "idle"}</Badge>
          </CardTitle>
          <CollapsibleTrigger className="text-muted-foreground hover:text-foreground">
            {open ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
          </CollapsibleTrigger>
        </CardHeader>
        <CollapsibleContent>
          <CardContent className="space-y-3">
            <div className="h-2 overflow-hidden rounded-full bg-muted">
              <div className="h-full bg-primary transition-all" style={{ width: `${status?.progressPercentage ?? 0}%` }} />
            </div>
            <p className="text-sm text-muted-foreground">{status?.progressMessage ?? "Ready to discover candidates"}</p>
            <div className="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
              {steps.map((step) => (
                <div key={step.key} className="rounded-xl border border-border bg-background/40 p-3">
                  <p className="text-sm font-medium">{step.label}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{step.message}</p>
                  <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-muted">
                    <div className="h-full rounded-full bg-primary transition-all" style={{ width: `${step.progressPercentage}%` }} />
                  </div>
                  <Badge
                    variant={step.status === "completed" ? "success" : step.status === "in_progress" ? "default" : "muted"}
                    className="mt-2 w-fit"
                  >
                    {step.status}
                  </Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </CollapsibleContent>
      </Collapsible>
    </Card>
  );
}
