"use client";

import { Facebook, Globe, Instagram, Linkedin, Music2, Twitter, Youtube } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { ClientAccountConnectionResponse } from "@/lib/api/publications";

const STATUS_VARIANT: Record<ClientAccountConnectionResponse["status"], BadgeVariant> = {
  Connected: "success",
  Failed: "destructive",
  Disabled: "muted",
};

/**
 * Icon per network catalog key. Unknown networks fall back to a neutral globe rather than
 * misrepresenting the destination with another network's mark.
 */
const NETWORK_ICON: Record<string, typeof Instagram> = {
  instagram: Instagram,
  facebook: Facebook,
  meta: Facebook,
  tiktok: Music2,
  youtube: Youtube,
  linkedin: Linkedin,
  x: Twitter,
  twitter: Twitter,
};

function networkIcon(connection: ClientAccountConnectionResponse) {
  const key = (connection.networkIconKey || connection.networkKey || "").toLowerCase();
  return NETWORK_ICON[key] ?? Globe;
}

function networkLabel(connection: ClientAccountConnectionResponse) {
  return connection.networkDisplayName || connection.networkKey || "Unknown network";
}

interface DestinationPickerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  connections: ClientAccountConnectionResponse[];
  loading?: boolean;
  busy?: boolean;
  onSelect: (connection: ClientAccountConnectionResponse) => void;
}

/**
 * Lists the client's connected publishing accounts for the "Publish now" action (design §10),
 * across every configured network. The destination's network is read from the connection itself
 * rather than assumed, so adding a connector needs no change here.
 */
export function DestinationPicker({ open, onOpenChange, connections, loading, busy, onSelect }: DestinationPickerProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Publish now</DialogTitle>
          <DialogDescription>Choose which connected account to publish this item to.</DialogDescription>
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
            {connections.map((connection) => {
              const Icon = networkIcon(connection);
              // A connection whose network row could not be resolved carries no network key, and the
              // publish command cannot select a connector without one — surface it as unusable
              // instead of letting the caller guess a network.
              const unresolvedNetwork = !connection.networkKey;
              return (
                <button
                  key={connection.id}
                  type="button"
                  disabled={busy || connection.status !== "Connected" || unresolvedNetwork}
                  onClick={() => onSelect(connection)}
                  title={unresolvedNetwork ? "This connection's network could not be resolved." : undefined}
                  className="flex items-center justify-between gap-3 rounded-md border border-border bg-background px-3 py-2 text-left text-sm transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-50"
                >
                  <span className="flex min-w-0 items-center gap-2">
                    <Icon className="h-4 w-4 shrink-0" />
                    <span className="flex min-w-0 flex-col">
                      <span className="truncate font-medium">{connection.externalAccountId}</span>
                      <span className="truncate text-[11px] text-muted-foreground">
                        {networkLabel(connection)}
                      </span>
                    </span>
                  </span>
                  <Badge variant={STATUS_VARIANT[connection.status]} className="shrink-0">
                    {connection.status}
                  </Badge>
                </button>
              );
            })}
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
