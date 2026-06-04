"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Building2, UserCircle, Link2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { OptionCardSelector, type OptionCardSelectorOption } from "@/components/ui/option-card-selector";
import { listInstagramOwnAccounts } from "@/lib/api/profile-analyzer";
import { listSocialClients } from "@/lib/api/social-clients";
import { cn } from "@/lib/utils";
import type { SocialClient } from "@/lib/api/types";
import type { OwnAccountItem } from "@/lib/api/types/profile-analyzer";

// ── Instagram URL validation (mirrors AnalyzeProfileValidator regex) ──────────

const INSTAGRAM_URL_RE =
  /^https?:\/\/(www\.)?instagram\.com\/[A-Za-z0-9._]{1,30}\/?([?#].*)?$/;

// ── Handle normalization — stored handles already include the leading "@" ─────

function formatHandle(h: string | null | undefined): string | null {
  if (!h) return null;
  return h.startsWith("@") ? h : `@${h}`;
}

function isValidInstagramUrl(url: string): boolean {
  return INSTAGRAM_URL_RE.test(url.trim());
}

function normalizeInstagramProfileUrl(url: string): string {
  const trimmed = url.trim();

  try {
    const parsed = new URL(trimmed);
    const isInstagram =
      parsed.hostname.toLowerCase() === "instagram.com" ||
      parsed.hostname.toLowerCase() === "www.instagram.com";
    const username = parsed.pathname.split("/").filter(Boolean)[0];

    if (isInstagram && username) {
      return `https://www.instagram.com/${username}/`;
    }
  } catch {
    // Validation handles malformed URLs; submit path simply falls back to trimming.
  }

  return trimmed.split(/[?#]/, 1)[0].replace(/\/$/, "");
}

// ── Source mode ───────────────────────────────────────────────────────────────

type SourceMode = "client" | "url";

// ── Props ─────────────────────────────────────────────────────────────────────

export interface ProfileSourcePickerSubmitPayload {
  /** Client-path: both fields set; url-path: both null */
  clientId: string | null;
  ownAccountId: string | null;
  /** URL-path: non-null; client-path: null */
  profileUrl: string | null;
}

interface ProfileSourcePickerProps {
  token?: string;
  isSubmitting?: boolean;
  onSubmit: (payload: ProfileSourcePickerSubmitPayload) => void;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ProfileSourcePicker({
  token,
  isSubmitting = false,
  onSubmit,
}: ProfileSourcePickerProps) {
  const [sourceMode, setSourceMode] = useState<SourceMode>("client");

  // Client-path state
  const [clients, setClients] = useState<SocialClient[]>([]);
  const [clientsLoading, setClientsLoading] = useState(false);
  const [selectedClientId, setSelectedClientId] = useState("");
  const [ownAccounts, setOwnAccounts] = useState<OwnAccountItem[]>([]);
  const [ownAccountsLoading, setOwnAccountsLoading] = useState(false);
  const [selectedOwnAccountId, setSelectedOwnAccountId] = useState("");

  // URL-path state
  const [profileUrl, setProfileUrl] = useState("");

  // ── Client options for OptionCardSelector ────────────────────────────────────

  const clientOptions = useMemo<OptionCardSelectorOption[]>(
    () =>
      clients.map((c) => ({
        value: c.id,
        label: c.name,
        icon: <Building2 className="h-4 w-4" />,
      })),
    [clients]
  );

  // ── Load clients ─────────────────────────────────────────────────────────────

  const loadClients = useCallback(async () => {
    if (!token) return;
    setClientsLoading(true);
    try {
      const result = await listSocialClients(
        { page: 1, pageSize: 100, includeInactive: false },
        token
      );
      const active = result.items.filter((c) => c.isActive);
      setClients(active);
    } catch {
      setClients([]);
    } finally {
      setClientsLoading(false);
    }
  }, [token]);

  useEffect(() => {
    void loadClients();
  }, [loadClients]);

  // ── Load own accounts when client changes ─────────────────────────────────────

  useEffect(() => {
    if (!selectedClientId || !token) {
      setOwnAccounts([]);
      setSelectedOwnAccountId("");
      return;
    }
    setOwnAccountsLoading(true);
    let cancelled = false;
    void listInstagramOwnAccounts(selectedClientId, token).then((accounts) => {
      if (cancelled) return;
      const active = accounts.filter((a) => a.isActive);
      setOwnAccounts(active);
      // Auto-select when exactly one account
      setSelectedOwnAccountId(active.length === 1 ? active[0].id : "");
      setOwnAccountsLoading(false);
    }).catch(() => {
      if (!cancelled) {
        setOwnAccounts([]);
        setOwnAccountsLoading(false);
      }
    });
    return () => { cancelled = true; };
  }, [selectedClientId, token]);

  // ── Derived validation ────────────────────────────────────────────────────────

  const clientPathReady =
    sourceMode === "client" &&
    selectedClientId.length > 0 &&
    selectedOwnAccountId.length > 0;

  const urlPathReady =
    sourceMode === "url" && isValidInstagramUrl(profileUrl);

  const canSubmit = (clientPathReady || urlPathReady) && !isSubmitting;

  // ── Submit ────────────────────────────────────────────────────────────────────

  function handleSubmit() {
    if (!canSubmit) return;
    if (sourceMode === "client") {
      onSubmit({
        clientId: selectedClientId,
        ownAccountId: selectedOwnAccountId,
        profileUrl: null,
      });
    } else {
      onSubmit({
        clientId: null,
        ownAccountId: null,
        profileUrl: normalizeInstagramProfileUrl(profileUrl),
      });
    }
  }

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="space-y-4">
      {/* Mode toggle — PA-03: mutually exclusive */}
      <div className="flex rounded-lg border border-border/40 overflow-hidden w-fit">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => setSourceMode("client")}
          className={cn(
            "h-auto rounded-none px-4 py-2 text-sm",
            sourceMode === "client"
              ? "bg-primary/10 text-primary font-medium"
              : "text-muted-foreground hover:bg-muted/40"
          )}
        >
          <UserCircle className="h-4 w-4" />
          Own account
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => setSourceMode("url")}
          className={cn(
            "h-auto rounded-none border-l border-border/40 px-4 py-2 text-sm",
            sourceMode === "url"
              ? "bg-primary/10 text-primary font-medium"
              : "text-muted-foreground hover:bg-muted/40"
          )}
        >
          <Link2 className="h-4 w-4" />
          Public URL
        </Button>
      </div>

      {/* Client-path inputs */}
      {sourceMode === "client" && (
        <div className="space-y-3">
          {/* Client selector */}
          <OptionCardSelector
            label="Client"
            value={selectedClientId}
            options={clientOptions}
            onChange={(value) => {
              setSelectedClientId(value);
              setSelectedOwnAccountId("");
            }}
            noneOption={{
              value: "",
              label: "No client",
              description: "Profile-only analysis",
              icon: <Building2 className="h-4 w-4" />,
            }}
            loading={clientsLoading}
            loadingLabel="Loading clients…"
            emptyLabel="No active clients"
            selectionLabel="Single selection"
            optionsGridClassName="xl:grid-cols-3"
          />

          {/* Own Instagram account selector — shown once a client is selected */}
          {selectedClientId && (
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground font-medium">
                Own Instagram account
              </label>
              {ownAccountsLoading ? (
                <p className="text-xs text-muted-foreground/60">Loading accounts…</p>
              ) : ownAccounts.length === 0 ? (
                <p className="text-xs text-destructive">
                  This client has no active own Instagram accounts. Add one in the
                  clients section.
                </p>
              ) : ownAccounts.length === 1 ? (
                <p className="text-xs text-muted-foreground/70">
                  {formatHandle(ownAccounts[0].handle) ?? ownAccounts[0].profileUrl}{" "}
                  <span className="text-green-600 dark:text-green-400">(selected automatically)</span>
                </p>
              ) : (
                <Select
                  value={selectedOwnAccountId}
                  onChange={(e) => setSelectedOwnAccountId(e.target.value)}
                  disabled={isSubmitting}
                  className="w-full"
                >
                  <option value="">— Select an account —</option>
                  {ownAccounts.map((acc) => (
                    <option key={acc.id} value={acc.id}>
                      {acc.label ?? (formatHandle(acc.handle) ?? acc.profileUrl)}
                    </option>
                  ))}
                </Select>
              )}
            </div>
          )}
        </div>
      )}

      {/* URL-path input */}
      {sourceMode === "url" && (
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground font-medium">
            Instagram profile URL
          </label>
          <Input
            type="url"
            placeholder="https://www.instagram.com/username"
            value={profileUrl}
            onChange={(e) => setProfileUrl(e.target.value)}
            disabled={isSubmitting}
            className="w-full"
            title={
              profileUrl.trim().length > 0 && !isValidInstagramUrl(profileUrl)
                ? "This profile analyzer does not support that URL."
                : "Enter the public Instagram profile URL you want to analyze."
            }
          />
          {profileUrl.trim().length > 0 && !isValidInstagramUrl(profileUrl) && (
            <p className="text-xs text-destructive">
              Enter a valid Instagram profile URL (for example: https://www.instagram.com/username)
            </p>
          )}
        </div>
      )}

      {/* Submit */}
      <Button
        onClick={handleSubmit}
        disabled={!canSubmit}
        className="w-full"
      >
        {isSubmitting ? (
          <span className="flex items-center gap-2">
            <span className="h-3 w-3 rounded-full border-2 border-t-transparent border-current animate-spin" />
            Starting analysis…
          </span>
        ) : (
          "Analyze profile"
        )}
      </Button>
    </div>
  );
}
