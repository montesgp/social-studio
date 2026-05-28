"use client";

import { FormEvent, useState } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ClientReferentAccountsSection } from "@/components/social/clients/client-referent-accounts-section";
import { ClientBrandManualSection } from "@/components/social/clients/client-brand-manual-section";
import { ClientStrategySection } from "@/components/social/clients/client-strategy-section";
import { MonthlyObjectivesSection } from "@/components/social/clients/monthly-objectives-section";
import type { ClientFormFieldErrors } from "@/components/social/clients/use-client-form";
import type { ClientReferentAccount } from "@/lib/api/types";

interface ClientFormPanelProps {
  mode: "create" | "edit";
  clientId: string | null;
  name: string;
  description: string;
  sectorOrNiche: string | null;
  fieldErrors: ClientFormFieldErrors;
  referentAccounts: ClientReferentAccount[];
  referentAccountsLoading: boolean;
  submitting: boolean;
  onNameChange: (value: string) => void;
  onDescriptionChange: (value: string) => void;
  onSectorOrNicheChange: (value: string | null) => void;
  onReferentAccountsChange: (value: ClientReferentAccount[]) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  onCancel: () => void;
  token: string;
}

function CollapsibleSection({
  title,
  defaultOpen = false,
  children,
}: {
  title: string;
  defaultOpen?: boolean;
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);

  return (
    <div className="rounded-lg border border-border overflow-hidden">
      <Button
        type="button"
        variant="ghost"
        className="flex w-full items-center justify-between px-4 py-3 text-sm font-medium hover:bg-muted/40 transition-colors"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span>{title}</span>
        {open ? (
          <ChevronDown className="h-4 w-4 text-muted-foreground" />
        ) : (
          <ChevronRight className="h-4 w-4 text-muted-foreground" />
        )}
      </Button>
      {open && (
        <div className="border-t border-border px-4 py-4">
          {children}
        </div>
      )}
    </div>
  );
}

export function ClientFormPanel({
  mode,
  clientId,
  name,
  description,
  sectorOrNiche,
  fieldErrors,
  referentAccounts,
  referentAccountsLoading,
  submitting,
  onNameChange,
  onDescriptionChange,
  onSectorOrNicheChange,
  onReferentAccountsChange,
  onSubmit,
  onCancel,
  token,
}: ClientFormPanelProps) {
  return (
    <form className="space-y-4" noValidate onSubmit={(e) => void onSubmit(e)}>
      <CollapsibleSection title="Basic Info" defaultOpen={mode === "create"}>
        <div className="space-y-4">
          <div className="space-y-1.5">
            <label htmlFor="panel-client-name" className="text-sm font-medium">
              Name <span className="text-destructive">*</span>
            </label>
            <Input
              id="panel-client-name"
              value={name}
              onChange={(e) => onNameChange(e.target.value)}
              placeholder="e.g. Acme Growth Studio"
              aria-invalid={fieldErrors.name ? "true" : "false"}
              aria-describedby={fieldErrors.name ? "panel-client-name-error" : undefined}
            />
            {fieldErrors.name ? (
              <p id="panel-client-name-error" className="text-xs text-destructive">
                {fieldErrors.name}
              </p>
            ) : null}
          </div>

          <div className="space-y-1.5">
            <label htmlFor="panel-client-description" className="text-sm font-medium">
              Description <span className="text-muted-foreground text-xs">(optional)</span>
            </label>
            <textarea
              id="panel-client-description"
              value={description}
              onChange={(e) => onDescriptionChange(e.target.value)}
              placeholder="Brief description of this client…"
              rows={3}
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              aria-invalid={fieldErrors.description ? "true" : "false"}
              aria-describedby={fieldErrors.description ? "panel-client-description-error" : undefined}
            />
            {fieldErrors.description ? (
              <p id="panel-client-description-error" className="text-xs text-destructive">
                {fieldErrors.description}
              </p>
            ) : null}
          </div>

          <div className="space-y-1.5">
            <label htmlFor="panel-sector-niche" className="text-sm font-medium">
              Sector / Niche{" "}
              <span className="text-muted-foreground text-xs">(optional, max 300)</span>
            </label>
            <textarea
              id="panel-sector-niche"
              value={sectorOrNiche ?? ""}
              onChange={(e) => onSectorOrNicheChange(e.target.value || null)}
              maxLength={300}
              placeholder="e.g. Beauty & Wellness, SaaS B2B…"
              rows={2}
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              aria-invalid={fieldErrors.sectorOrNiche ? "true" : "false"}
              aria-describedby={fieldErrors.sectorOrNiche ? "panel-sector-niche-error" : undefined}
            />
            {fieldErrors.sectorOrNiche ? (
              <p id="panel-sector-niche-error" className="text-xs text-destructive">
                {fieldErrors.sectorOrNiche}
              </p>
            ) : null}
          </div>
        </div>
      </CollapsibleSection>

      {clientId && mode === "edit" && (
        <>
          <CollapsibleSection title="Brand Manual">
            <ClientBrandManualSection clientId={clientId} />
          </CollapsibleSection>

          <CollapsibleSection title="Strategy">
            <ClientStrategySection clientId={clientId} token={token} />
          </CollapsibleSection>

          <CollapsibleSection title="Monthly Objectives">
            <MonthlyObjectivesSection clientId={clientId} token={token} />
          </CollapsibleSection>

          <CollapsibleSection title="Referent Accounts">
            <ClientReferentAccountsSection
              clientId={clientId}
              accounts={referentAccounts}
              loading={referentAccountsLoading}
              deferPersistence
              onAccountsChange={onReferentAccountsChange}
            />
            {fieldErrors.referentAccounts ? (
              <p className="pt-2 text-xs text-destructive">{fieldErrors.referentAccounts}</p>
            ) : null}
          </CollapsibleSection>
        </>
      )}

      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={submitting || referentAccountsLoading}>
          {submitting ? "Saving..." : mode === "create" ? "Create" : "Save client"}
        </Button>
      </div>
    </form>
  );
}
