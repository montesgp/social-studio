"use client";

import { FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { ClientReferentAccountsSection } from "@/components/social/clients/client-referent-accounts-section";
import { ClientCompetitorAccountsSection } from "@/components/social/clients/client-competitor-accounts-section";
import { ClientOwnAccountsSection } from "@/components/social/clients/client-own-accounts-section";
import { ClientBrandManualSection } from "@/components/social/clients/client-brand-manual-section";
import { ClientStrategySection } from "@/components/social/clients/client-strategy-section";
import { ClientContentPlanSection } from "@/components/social/clients/client-content-plan-section";
import type { ClientFormFieldErrors } from "@/components/social/clients/use-client-form";
import type { ClientCompetitorAccount, ClientOwnAccount, ClientReferentAccount } from "@/lib/api/types";

interface ClientFormPanelProps {
  mode: "create" | "edit";
  clientId: string | null;
  name: string;
  description: string;
  sectorOrNiche: string | null;
  fieldErrors: ClientFormFieldErrors;
  referentAccounts: ClientReferentAccount[];
  referentAccountsLoading: boolean;
  competitorAccounts: ClientCompetitorAccount[];
  competitorAccountsLoading: boolean;
  ownAccounts: ClientOwnAccount[];
  ownAccountsLoading: boolean;
  submitting: boolean;
  onNameChange: (value: string) => void;
  onDescriptionChange: (value: string) => void;
  onSectorOrNicheChange: (value: string | null) => void;
  onReferentAccountsChange: (value: ClientReferentAccount[]) => void;
  onCompetitorAccountsChange: (value: ClientCompetitorAccount[]) => void;
  onOwnAccountsChange: (value: ClientOwnAccount[]) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  onCancel: () => void;
  token: string;
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
  competitorAccounts,
  competitorAccountsLoading,
  ownAccounts,
  ownAccountsLoading,
  submitting,
  onNameChange,
  onDescriptionChange,
  onSectorOrNicheChange,
  onReferentAccountsChange,
  onCompetitorAccountsChange,
  onOwnAccountsChange,
  onSubmit,
  onCancel,
  token,
}: ClientFormPanelProps) {
  const basicInfoFields = (
    <div className="space-y-4 pt-4">
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
  );

  return (
    <form className="space-y-4" noValidate onSubmit={(e) => void onSubmit(e)}>
      {mode === "create" || !clientId ? (
        basicInfoFields
      ) : (
        <Tabs defaultValue="info">
          <TabsList className="w-full">
            <TabsTrigger value="info">Basic Info</TabsTrigger>
            <TabsTrigger value="brand-manual">Brand Manual</TabsTrigger>
            <TabsTrigger value="strategy">General Strategy</TabsTrigger>
            <TabsTrigger value="content-plan">Content Plan</TabsTrigger>
            <TabsTrigger value="accounts">Accounts</TabsTrigger>
          </TabsList>

          <TabsContent value="info">
            {basicInfoFields}
          </TabsContent>

          <TabsContent value="brand-manual" className="pt-4">
            <ClientBrandManualSection clientId={clientId} />
          </TabsContent>

          <TabsContent value="strategy" className="pt-4">
            <ClientStrategySection clientId={clientId} token={token} />
          </TabsContent>

          <TabsContent value="content-plan" className="pt-4">
            <ClientContentPlanSection clientId={clientId} token={token} />
          </TabsContent>

          <TabsContent value="accounts" className="pt-4">
            <Tabs defaultValue="own">
              <TabsList>
                <TabsTrigger value="own">Own Accounts</TabsTrigger>
                <TabsTrigger value="referent">Referent Accounts</TabsTrigger>
                <TabsTrigger value="competitor">Competitor Accounts</TabsTrigger>
              </TabsList>
              <TabsContent value="own">
                <ClientOwnAccountsSection
                  clientId={clientId}
                  accounts={ownAccounts}
                  loading={ownAccountsLoading}
                  deferPersistence
                  onAccountsChange={onOwnAccountsChange}
                />
              </TabsContent>
              <TabsContent value="referent">
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
              </TabsContent>
              <TabsContent value="competitor">
                <ClientCompetitorAccountsSection
                  clientId={clientId}
                  accounts={competitorAccounts}
                  loading={competitorAccountsLoading}
                  deferPersistence
                  onAccountsChange={onCompetitorAccountsChange}
                />
              </TabsContent>
            </Tabs>
          </TabsContent>
        </Tabs>
      )}

      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button
          type="submit"
          disabled={submitting || referentAccountsLoading || competitorAccountsLoading || ownAccountsLoading}
        >
          {submitting ? "Saving..." : mode === "create" ? "Create" : "Save client"}
        </Button>
      </div>
    </form>
  );
}
