"use client";

import { Instagram } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { ClientAccountConnectionResponse } from "@/lib/api/publications";

const STATUS_VARIANT: Record<ClientAccountConnectionResponse["status"], BadgeVariant> = {
  Connected: "success",
  Failed: "destructive",
  Disabled: "muted",
};

interface DestinationPickerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  connections: ClientAccountConnectionResponse[];
  loading?: boolean;
  busy?: boolean;
  onSelect: (connection: ClientAccountConnectionResponse) => void;
}

/** Lists the client's Instagram `ClientAccountConnection`s for the "Publish now" action (design §10). */
export function DestinationPicker({ open, onOpenChange, connections, loading, busy, onSelect }: DestinationPickerProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Publish now</DialogTitle>
          <DialogDescription>Choose which connected Instagram account to publish this item to.</DialogDescription>
        </DialogHeader>

        {loading ? (
          <div className="flex flex-col gap-2">
            {Array.from({ length: 2 }).map((_, i) => (
              <div key={i} className="h-14 animate-pulse rounded-md border border-border bg-muted/40" />
            ))}
          </div>
        ) : connections.length === 0 ? (
          <p className="rounded-md border border-border bg-muted/40 p-4 text-sm text-muted-foreground">
            No account connections found for this client. Add one from Connections settings first.
          </p>
        ) : (
          <div className="flex flex-col gap-2">
            {connections.map((connection) => (
              <button
                key={connection.id}
                type="button"
                disabled={busy || connection.status !== "Connected"}
                onClick={() => onSelect(connection)}
                className="flex items-center justify-between gap-3 rounded-md border border-border bg-background px-3 py-2 text-left text-sm transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-50"
              >
                <span className="flex items-center gap-2">
                  <Instagram className="h-4 w-4" />
                  <span className="font-medium">{connection.externalAccountId}</span>
                </span>
                <Badge variant={STATUS_VARIANT[connection.status]}>{connection.status}</Badge>
              </button>
            ))}
          </div>
        )}

        <div className="mt-4 flex justify-end">
          <Button variant="ghost" size="sm" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
