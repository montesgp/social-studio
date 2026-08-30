"use client";

import { FormEvent, useEffect, useState } from "react";
import { Loader2, Mail, MessageCircle, PlugZap, Save } from "lucide-react";
import {
  getEmailSmtpConfiguration,
  listPlatformIntegrations,
  upsertEmailSmtpConfiguration
} from "@/lib/api/platform-configuration";
import { ApiError, EmailSmtpConfiguration, PlatformIntegrationSummary } from "@/lib/api/types";
import { useAccessToken } from "@/components/auth/use-access-token";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/ui/password-input";
import { useToast } from "@/components/ui/toaster";

export function PlatformIntegrationsClient() {
  const token = useAccessToken();
  const toast = useToast();
  const [items, setItems] = useState<PlatformIntegrationSummary[]>([]);
  const [smtp, setSmtp] = useState<EmailSmtpConfiguration | null>(null);
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;
    void load(token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  async function load(accessToken: string) {
    setLoading(true);
    setError(null);

    try {
      const [summary, smtpConfig] = await Promise.all([
        listPlatformIntegrations(accessToken),
        getEmailSmtpConfiguration(accessToken)
      ]);
      setItems(summary);
      setSmtp(smtpConfig);
      setPassword("");
    } catch (err) {
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }

  function patchSmtp(patch: Partial<EmailSmtpConfiguration>) {
    setSmtp((current) => (current ? { ...current, ...patch } : current));
  }

  async function saveSmtp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !smtp) return;

    setSaving(true);
    setError(null);

    try {
      const updated = await upsertEmailSmtpConfiguration(
        {
          enabled: smtp.enabled,
          host: smtp.host,
          port: smtp.port,
          useSsl: smtp.useSsl,
          username: smtp.username,
          password: password || null,
          fromEmail: smtp.fromEmail,
          fromName: smtp.fromName
        },
        token
      );
      setSmtp(updated);
      setPassword("");
      const summary = await listPlatformIntegrations(token);
      setItems(summary);
      toast.success("SMTP configuration saved", updated.enabled ? "Email delivery is enabled." : "Email delivery is disabled.");
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not save SMTP configuration", message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      <section className="rounded-3xl border border-border bg-card p-6 shadow-2xl">
        <Badge variant="success" className="w-fit">
          <PlugZap className="mr-1 h-3 w-3" /> Platform integrations
        </Badge>
        <h1 className="mt-3 text-3xl font-semibold tracking-tight md:text-5xl">Platform integrations</h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground md:text-base">
          Configure platform-owned messaging services used by authentication and future system workflows.
        </p>
      </section>

      {error ? (
        <div className="rounded-xl border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      ) : null}

      {loading || !smtp ? (
        <Card>
          <CardContent className="flex items-center gap-2 p-6 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading platform integrations...
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 xl:grid-cols-[320px_1fr]">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Available services</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              {items.map((item) => {
                const Icon = item.key === "whatsapp" ? MessageCircle : Mail;
                return (
                  <div key={item.key} className="rounded-lg border border-border bg-background/40 p-3">
                    <div className="flex items-start justify-between gap-3">
                      <div className="flex gap-2">
                        <span className="rounded-md border border-border bg-muted p-2">
                          <Icon className="h-4 w-4 text-primary" />
                        </span>
                        <div>
                          <p className="font-medium">{item.displayName}</p>
                          <p className="text-xs text-muted-foreground">{item.description}</p>
                        </div>
                      </div>
                      <Badge variant={item.enabled ? "success" : item.isConfigured ? "outline" : "muted"}>
                        {item.enabled ? "Enabled" : item.isConfigured ? "Configured" : "Not configured"}
                      </Badge>
                    </div>
                  </div>
                );
              })}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <Mail className="h-4 w-4 text-primary" /> Email SMTP
              </CardTitle>
              <p className="text-sm text-muted-foreground">
                Password is stored encrypted. Leave it empty to keep the current value.
              </p>
            </CardHeader>
            <CardContent>
              <form className="grid gap-4 lg:grid-cols-2" onSubmit={saveSmtp}>
                <label className="flex items-center gap-2 text-sm lg:col-span-2">
                  <Checkbox
                    checked={smtp.enabled}
                    onChange={(event) => patchSmtp({ enabled: event.target.checked })}
                  />
                  Enabled
                </label>

                <div className="space-y-2">
                  <label className="text-sm font-medium">SMTP host</label>
                  <Input value={smtp.host} onChange={(event) => patchSmtp({ host: event.target.value })} />
                </div>

                <div className="space-y-2">
                  <label className="text-sm font-medium">Port</label>
                  <Input
                    type="number"
                    value={smtp.port}
                    onChange={(event) => patchSmtp({ port: Number(event.target.value) })}
                  />
                </div>

                <label className="flex items-center gap-2 text-sm lg:col-span-2">
                  <Checkbox
                    checked={smtp.useSsl}
                    onChange={(event) => patchSmtp({ useSsl: event.target.checked })}
                  />
                  Use SSL / STARTTLS
                </label>

                <div className="space-y-2">
                  <label className="text-sm font-medium">Username</label>
                  <Input value={smtp.username} onChange={(event) => patchSmtp({ username: event.target.value })} />
                </div>

                <div className="space-y-2">
                  <label className="text-sm font-medium">
                    Password {smtp.passwordConfigured ? <span className="text-xs text-muted-foreground">(configured)</span> : null}
                  </label>
                  <PasswordInput value={password} onChange={(event) => setPassword(event.target.value)} />
                </div>

                <div className="space-y-2">
                  <label className="text-sm font-medium">From email</label>
                  <Input value={smtp.fromEmail} onChange={(event) => patchSmtp({ fromEmail: event.target.value })} />
                </div>

                <div className="space-y-2">
                  <label className="text-sm font-medium">From name</label>
                  <Input value={smtp.fromName} onChange={(event) => patchSmtp({ fromName: event.target.value })} />
                </div>

                <div className="flex justify-end lg:col-span-2">
                  <Button type="submit" disabled={saving}>
                    {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                    {saving ? "Saving..." : "Save SMTP"}
                  </Button>
                </div>
              </form>
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}
