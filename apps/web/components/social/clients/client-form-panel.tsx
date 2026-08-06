"use client";

import { FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { ClientReferentAccountsSection } from "@/components/social/clients/client-referent-accounts-section";
import { ClientCompetitorAccountsSection } from "@/components/social/clients/client-competitor-accounts-section";
import { ClientOwnAccountsSection } from "@/components/social/clients/client-own-accounts-section";
import { ClientAestheticProfileSection } from "@/components/social/clients/client-aesthetic-profile-section";
import { ClientBrandManualSection } from "@/components/social/clients/client-brand-manual-section";
import { ClientStrategySection } from "@/components/social/clients/client-strategy-section";
import { ClientContentPlanSection } from "@/components/social/clients/client-content-plan-section";
import type { ClientFormFieldErrors } from "@/components/social/clients/use-client-form";
import type { BackgroundSourcingPolicy, ClientCompetitorAccount, ClientOwnAccount, ClientReferentAccount } from "@/lib/api/types";

const BACKGROUND_SOURCING_POLICY_OPTIONS: Array<{
  value: BackgroundSourcingPolicy;
  label: string;
  description: string;
}> = [
  {
    value: null,
    label: "Legacy (3-tier rotation)",
    description: "Use the background tier system defined in the content plan.",
  },
  {
    value: "ClientDefinedSet",
    label: "Client-defined set",
    description: "Rotate through the client's own background templates.",
  },
  {
    value: "AiPalette",
    label: "AI palette",
    description: "Let the AI derive backgrounds from the brand color palette.",
  },
  {
    value: "ClientAesthetic",
    label: "Client aesthetic",
    description: "Source backgrounds from detected client visual style descriptors.",
  },
];

interface ClientFormPanelProps {
  mode: "create" | "edit";
  clientId: string | null;
  name: string;
  description: string;
  sectorOrNiche: string | null;
  backgroundSourcingPolicy: BackgroundSourcingPolicy;
  aestheticRefreshThresholdDays: number | null;
  maxPiecesPerGeneration: number | null;
  signsOwnSlides: boolean;
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
  onBackgroundSourcingPolicyChange: (value: BackgroundSourcingPolicy) => void;
  onAestheticRefreshThresholdDaysChange: (value: number | null) => void;
  onMaxPiecesPerGenerationChange: (value: number | null) => void;
  onSignsOwnSlidesChange: (value: boolean) => void;
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
  backgroundSourcingPolicy,
  aestheticRefreshThresholdDays,
  maxPiecesPerGeneration,
  signsOwnSlides,
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
  onBackgroundSourcingPolicyChange,
  onAestheticRefreshThresholdDaysChange,
  onMaxPiecesPerGenerationChange,
  onSignsOwnSlidesChange,
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

      <div className="space-y-1.5">
        <label htmlFor="panel-background-sourcing-policy" className="text-sm font-medium">
          Background sourcing policy{" "}
          <span className="text-muted-foreground text-xs">(optional)</span>
        </label>
        <select
          id="panel-background-sourcing-policy"
          value={backgroundSourcingPolicy ?? ""}
          onChange={(e) => {
            const raw = e.target.value;
            onBackgroundSourcingPolicyChange(
              (raw === "" ? null : raw) as BackgroundSourcingPolicy,
            );
          }}
          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          {BACKGROUND_SOURCING_POLICY_OPTIONS.map((opt) => (
            <option key={opt.value ?? "__unset"} value={opt.value ?? ""}>
              {opt.label}
            </option>
          ))}
        </select>
        <p className="text-xs text-muted-foreground">
          {BACKGROUND_SOURCING_POLICY_OPTIONS.find(
            (opt) => opt.value === backgroundSourcingPolicy,
          )?.description ?? ""}
        </p>
      </div>

      <div className="space-y-1.5">
        <label htmlFor="panel-aesthetic-refresh-threshold" className="text-sm font-medium">
          Aesthetic refresh threshold (days){" "}
          <span className="text-muted-foreground text-xs">(optional)</span>
        </label>
        <Input
          id="panel-aesthetic-refresh-threshold"
          type="number"
          min={1}
          max={365}
          value={aestheticRefreshThresholdDays ?? ""}
          onChange={(e) => {
            const raw = e.target.value;
            onAestheticRefreshThresholdDaysChange(raw === "" ? null : Number(raw));
          }}
          placeholder="Leave empty for default (30 days)"
        />
        <p className="text-xs text-muted-foreground">
          How many days before the AI aesthetic profile is refreshed for this client. Leave empty to use the system default of 30 days.
        </p>
      </div>

      <div className="space-y-1.5">
        <label htmlFor="panel-max-pieces-per-generation" className="text-sm font-medium">
          Max pieces per generation{" "}
          <span className="text-muted-foreground text-xs">(optional)</span>
        </label>
        <Input
          id="panel-max-pieces-per-generation"
          type="number"
          min={1}
          max={20}
          value={maxPiecesPerGeneration ?? ""}
          onChange={(e) => {
            const raw = e.target.value;
            onMaxPiecesPerGenerationChange(raw === "" ? null : Number(raw));
          }}
          placeholder="Leave empty for no limit"
        />
        <p className="text-xs text-muted-foreground">
          Caps how many pieces a single generation run builds for this client. Useful for cheap test
          runs: set it to 2 and only the first two requested formats are generated. Leave empty to
          generate every requested format.
        </p>
      </div>

      <div className="flex items-start gap-2">
        <input
          id="panel-signs-own-slides"
          type="checkbox"
          className="mt-1 h-4 w-4"
          checked={signsOwnSlides}
          onChange={(e) => onSignsOwnSlidesChange(e.target.checked)}
        />
        <div className="space-y-1">
          <label htmlFor="panel-signs-own-slides" className="text-sm font-medium">
            Signs own slides
          </label>
          <p className="text-xs text-muted-foreground">
            When enabled, generated slides include an @handle attribution signature for this
            client. Disabled by default (no signature).
          </p>
        </div>
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
            <TabsTrigger value="analysis-data">Analysis Data</TabsTrigger>
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

          <TabsContent value="analysis-data" className="pt-4">
            <ClientAestheticProfileSection clientId={clientId} token={token} />
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
