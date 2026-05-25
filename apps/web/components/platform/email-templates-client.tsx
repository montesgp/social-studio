"use client";

import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Code2, Eye, Loader2, Mail, Save } from "lucide-react";
import { listEmailTemplates, upsertEmailTemplate } from "@/lib/api/platform-configuration";
import { ApiError, EmailTemplate } from "@/lib/api/types";
import { useAccessToken } from "@/components/auth/use-access-token";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { useToast } from "@/components/ui/toaster";
import { cn } from "@/lib/utils";

export function EmailTemplatesClient() {
  const token = useAccessToken();
  const toast = useToast();
  const hasLoadedTemplatesRef = useRef(false);
  const previewFrameRef = useRef<HTMLIFrameElement | null>(null);
  const [templates, setTemplates] = useState<EmailTemplate[]>([]);
  const [selectedKey, setSelectedKey] = useState("");
  const [subject, setSubject] = useState("");
  const [htmlBody, setHtmlBody] = useState("");
  const [textBody, setTextBody] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selected = useMemo(
    () => templates.find((template) => template.key === selectedKey) ?? templates[0] ?? null,
    [selectedKey, templates]
  );

  useEffect(() => {
    if (!token || hasLoadedTemplatesRef.current) return;
    hasLoadedTemplatesRef.current = true;
    void loadTemplates(token);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  const renderPreview = useCallback(() => {
    const doc = previewFrameRef.current?.contentDocument;
    if (!doc || !doc.body) return;

    const parsed = new DOMParser().parseFromString(htmlBody, "text/html");

    doc.documentElement.lang = parsed.documentElement.lang || "es";

    // Only swap head when it actually changed to avoid re-fetching external CSS/fonts
    const newHeadHtml = parsed.head.innerHTML;
    if (doc.head.innerHTML !== newHeadHtml) {
      doc.head.innerHTML = newHeadHtml;
    }

    doc.body.innerHTML = parsed.body.innerHTML;

    const bodyStyle = parsed.body.getAttribute("style");
    bodyStyle ? doc.body.setAttribute("style", bodyStyle) : doc.body.removeAttribute("style");

    const bodyClass = parsed.body.getAttribute("class");
    bodyClass ? doc.body.setAttribute("class", bodyClass) : doc.body.removeAttribute("class");
  }, [htmlBody]);

  // Keep preview in sync with every htmlBody change
  useEffect(() => {
    renderPreview();
  }, [renderPreview]);

  useEffect(() => {
    if (!selected) return;
    setSelectedKey(selected.key);
    setSubject(selected.subject);
    setHtmlBody(selected.htmlBody);
    setTextBody(selected.textBody ?? "");
  }, [selected]);

  async function loadTemplates(accessToken: string) {
    setLoading(true);
    setError(null);

    try {
      const data = await listEmailTemplates(accessToken);
      setTemplates(data);
      setSelectedKey((current) => current || data[0]?.key || "");
    } catch (err) {
      hasLoadedTemplatesRef.current = false;
      setError((err as ApiError).message);
    } finally {
      setLoading(false);
    }
  }

  async function saveTemplate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selected) return;

    setSaving(true);
    setError(null);

    try {
      const updated = await upsertEmailTemplate(
        selected.key,
        {
          subject,
          htmlBody,
          textBody: textBody.trim() ? textBody : null
        },
        token
      );
      setTemplates((current) => current.map((item) => (item.key === updated.key ? updated : item)));
      toast.success("Email template saved", updated.displayName);
    } catch (err) {
      const message = (err as ApiError).message;
      setError(message);
      toast.error("Could not save template", message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      <section className="rounded-3xl border border-border bg-card p-6 shadow-2xl">
        <Badge variant="success" className="w-fit">
          <Mail className="mr-1 h-3 w-3" /> Platform email
        </Badge>
        <h1 className="mt-3 text-3xl font-semibold tracking-tight md:text-5xl">Email templates</h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground md:text-base">
          Configure authentication and system email bodies with a live HTML preview.
        </p>
      </section>

      {error ? (
        <div className="rounded-xl border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      ) : null}

      {loading ? (
        <Card>
          <CardContent className="flex items-center gap-2 p-6 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading email templates...
          </CardContent>
        </Card>
      ) : selected ? (
        <div className="grid gap-4 xl:grid-cols-[280px_1fr]">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Templates</CardTitle>
            </CardHeader>
            <CardContent className="space-y-2">
              {templates.map((template) => (
                <Button
                  key={template.key}
                  type="button"
                  variant={template.key === selected.key ? "secondary" : "ghost"}
                  className="w-full justify-start"
                  onClick={() => setSelectedKey(template.key)}
                >
                  <Mail className="h-4 w-4" />
                  <span className="truncate">{template.displayName}</span>
                  {template.isConfigured ? <Badge variant="outline">Saved</Badge> : null}
                </Button>
              ))}
            </CardContent>
          </Card>

          <div className="grid gap-4 xl:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Code2 className="h-4 w-4 text-primary" /> {selected.displayName}
                </CardTitle>
                <p className="text-sm text-muted-foreground">{selected.description}</p>
              </CardHeader>
              <CardContent>
                <form className="space-y-4" onSubmit={saveTemplate}>
                  <div className="space-y-2">
                    <label className="text-sm font-medium">Subject</label>
                    <Input value={subject} onChange={(event) => setSubject(event.target.value)} />
                  </div>

                  <div className="space-y-2">
                    <label className="text-sm font-medium">HTML body</label>
                    <textarea
                      className={cn(
                        "min-h-72 w-full rounded-md border border-border bg-background p-3 font-mono text-sm text-foreground",
                        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                      )}
                      value={htmlBody}
                      onChange={(event) => setHtmlBody(event.target.value)}
                    />
                  </div>

                  <div className="space-y-2">
                    <label className="text-sm font-medium">Text body (optional)</label>
                    <textarea
                      className={cn(
                        "min-h-28 w-full rounded-md border border-border bg-background p-3 text-sm text-foreground",
                        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                      )}
                      value={textBody}
                      onChange={(event) => setTextBody(event.target.value)}
                    />
                  </div>

                  <div className="flex flex-wrap gap-2">
                    {selected.variables.map((variable) => (
                      <Badge key={variable} variant="muted" className="font-mono">
                        {variable}
                      </Badge>
                    ))}
                  </div>

                  <div className="flex justify-end">
                    <Button type="submit" disabled={saving}>
                      {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                      {saving ? "Saving..." : "Save template"}
                    </Button>
                  </div>
                </form>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Eye className="h-4 w-4 text-primary" /> Preview
                </CardTitle>
                <p className="text-sm text-muted-foreground">{subject}</p>
              </CardHeader>
              <CardContent>
                <iframe
                  ref={previewFrameRef}
                  title="Email preview"
                  sandbox="allow-same-origin"
                  srcDoc="<!DOCTYPE html><html lang='es'><head></head><body></body></html>"
                  onLoad={renderPreview}
                  className="h-[620px] w-full rounded-md border border-border bg-white"
                />
              </CardContent>
            </Card>
          </div>
        </div>
      ) : null}
    </div>
  );
}
