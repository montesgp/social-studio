"use client";

import { useEffect, useState } from "react";
import { CheckCircle2, Plus, RefreshCcw, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { useSocialNetworks } from "@/hooks/use-social-networks";
import { SocialNetworkSelect } from "@/components/social/social-network-select";
import { listOrganizationIntegrations } from "@/lib/api/integrations";
import {
  createClientAccountConnection,
  listClientAccountConnections,
  testClientAccountConnection,
  type ClientAccountConnectionResponse,
} from "@/lib/api/publications";

function errorMessage(err: unknown): string {
  if (err && typeof err === "object" && "message" in err) {
    return String((err as { message?: unknown }).message ?? "Something went wrong.");
  }
  return "Something went wrong.";
}

const STATUS_VARIANT: Record<ClientAccountConnectionResponse["status"], BadgeVariant> = {
  Connected: "success",
  Failed: "destructive",
  Disabled: "muted",
};

interface ClientAccountConnectionsPanelProps {
  clientId: string;
  organizationId: string;
  token: string;
}

/**
 * Settings surface for managing per-client publish account connections (design §10, §15). Org-admin
 * only on the backend; the token input is write-only — never pre-filled, never returned.
 */
export function ClientAccountConnectionsPanel({ clientId, organizationId, token }: ClientAccountConnectionsPanelProps) {
  const [connections, setConnections] = useState<ClientAccountConnectionResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [externalAccountId, setExternalAccountId] = useState("");
  const [accessToken, setAccessToken] = useState("");
  const [socialNetworkId, setSocialNetworkId] = useState("");

  const { networks, loading: networksLoading } = useSocialNetworks(token);
  const instagramNetwork = networks.find((n) => n.code.toLowerCase() === "instagram") ?? null;

  useEffect(() => {
    if (instagramNetwork && !socialNetworkId) {
      setSocialNetworkId(instagramNetwork.id);
    }
  }, [instagramNetwork, socialNetworkId]);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const list = await listClientAccountConnections(clientId, token);
      setConnections(list);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clientId, token]);

  async function onCreate() {
    if (!socialNetworkId || !externalAccountId.trim() || !accessToken.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const overview = await listOrganizationIntegrations(organizationId, token);
      const instagramIntegration = overview.integrations.find((i) => i.providerKey === "instagram");
      if (!instagramIntegration?.integrationId) {
        throw { message: "Configure the organization-level Instagram integration first." };
      }
      await createClientAccountConnection(
        clientId,
        {
          socialNetworkId,
          organizationIntegrationId: instagramIntegration.integrationId,
          externalAccountId: externalAccountId.trim(),
          accessToken: accessToken.trim(),
        },
        token,
      );
      setExternalAccountId("");
      setAccessToken("");
      setFormOpen(false);
      await load();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function onTest(connectionId: string) {
    setBusy(true);
    setError(null);
    try {
      await testClientAccountConnection(connectionId, token);
      await load();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border border-border bg-background p-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">Account connections</h3>
        <div className="flex items-center gap-2">
          <Button size="sm" variant="ghost" onClick={() => void load()} disabled={loading}>
            <RefreshCcw className="h-3.5 w-3.5" />
          </Button>
          <Button size="sm" variant="outline" onClick={() => setFormOpen((v) => !v)}>
            <Plus className="h-3.5 w-3.5" /> Connect account
          </Button>
        </div>
      </div>

      {error ? (
        <div className="rounded-md border border-rose-600/35 bg-rose-100 px-3 py-2 text-xs text-rose-800 dark:bg-rose-500/15 dark:text-rose-200">
          {error}
        </div>
      ) : null}

      {formOpen ? (
        <div className="flex flex-col gap-2 rounded-md border border-border bg-muted/20 p-3">
          <label className="flex flex-col gap-1 text-xs text-muted-foreground">
            Network
            <SocialNetworkSelect
              networks={networks}
              loading={networksLoading}
              value={socialNetworkId}
              onChange={setSocialNetworkId}
            />
          </label>
          <label className="flex flex-col gap-1 text-xs text-muted-foreground">
            Instagram account id
            <Input value={externalAccountId} onChange={(e) => setExternalAccountId(e.target.value)} placeholder="17841400000000000" />
          </label>
          <label className="flex flex-col gap-1 text-xs text-muted-foreground">
            Access token (write-only — never displayed again)
            <Input type="password" value={accessToken} onChange={(e) => setAccessToken(e.target.value)} />
          </label>
          <div className="flex justify-end gap-2">
            <Button size="sm" variant="ghost" onClick={() => setFormOpen(false)}>
              Cancel
            </Button>
            <Button
              size="sm"
              disabled={busy || !socialNetworkId || !externalAccountId.trim() || !accessToken.trim()}
              onClick={() => void onCreate()}
            >
              Save connection
            </Button>
          </div>
        </div>
      ) : null}

      {loading ? (
        <div className="h-16 animate-pulse rounded-md border border-border bg-muted/40" />
      ) : connections.length === 0 ? (
        <p className="text-xs text-muted-foreground">No account connections yet.</p>
      ) : (
        <div className="flex flex-col gap-2">
          {connections.map((connection) => (
            <div key={connection.id} className="flex items-center justify-between gap-2 rounded-md border border-border p-2 text-xs">
              <div className="flex flex-col gap-0.5">
                <span className="font-medium">{connection.externalAccountId}</span>
                {connection.lastError ? <span className="text-rose-600 dark:text-rose-400">{connection.lastError}</span> : null}
              </div>
              <div className="flex items-center gap-2">
                <Badge variant={STATUS_VARIANT[connection.status]}>
                  {connection.status === "Connected" ? (
                    <CheckCircle2 className="mr-1 h-3 w-3" />
                  ) : connection.status === "Failed" ? (
                    <XCircle className="mr-1 h-3 w-3" />
                  ) : null}
                  {connection.status}
                </Badge>
                <Button size="sm" variant="ghost" disabled={busy} onClick={() => void onTest(connection.id)}>
                  Test
                </Button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
