"use client";

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ThemeSelector } from "@/components/theme/theme-selector";
import { DateFormatSelector } from "@/components/theme/date-format-selector";

/** `/settings/preferences` (Issue #… — moved out of the top toolbar). Owns per-user display settings. */
export function PreferencesSettingsClient() {
  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Preferences</h1>
        <p className="text-sm text-muted-foreground">Personal display settings for your account.</p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Date format</CardTitle>
          <CardDescription>Choose how dates are displayed across the workspace.</CardDescription>
        </CardHeader>
        <CardContent>
          <DateFormatSelector />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Theme</CardTitle>
          <CardDescription>Light or dark mode for your account.</CardDescription>
        </CardHeader>
        <CardContent>
          <ThemeSelector />
        </CardContent>
      </Card>
    </div>
  );
}
