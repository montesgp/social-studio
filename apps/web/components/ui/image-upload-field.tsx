"use client";

import { ChangeEvent, DragEvent, useId, useState } from "react";
import { ImageUp, Loader2, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

const ACCEPT = "image/jpeg,image/png,image/webp,image/gif";

export interface ImageUploadFieldProps {
  value: string;
  onChange: (value: string) => void;
  onUpload: (file: File) => Promise<string>;
  previewAlt: string;
  disabled?: boolean;
  className?: string;
}

export function ImageUploadField({
  value,
  onChange,
  onUpload,
  previewAlt,
  disabled = false,
  className,
}: ImageUploadFieldProps) {
  const inputId = useId();
  const [uploading, setUploading] = useState(false);
  const [dragActive, setDragActive] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function processFile(file: File | null) {
    if (!file || disabled || uploading) return;

    setUploading(true);
    setError(null);
    try {
      const uploadedUrl = await onUpload(file);
      onChange(uploadedUrl);
    } catch (err) {
      const message =
        (err as { message?: string })?.message ?? "No se pudo subir la imagen.";
      setError(message);
    } finally {
      setUploading(false);
    }
  }

  function handleFileInputChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null;
    void processFile(file);
    event.target.value = "";
  }

  function handleDrop(event: DragEvent<HTMLLabelElement>) {
    event.preventDefault();
    setDragActive(false);
    const file = event.dataTransfer.files?.[0] ?? null;
    void processFile(file);
  }

  function triggerSelect() {
    const input = document.getElementById(inputId);
    input?.click();
  }

  return (
    <div className={cn("space-y-2", className)}>
      <input
        id={inputId}
        type="file"
        accept={ACCEPT}
        className="hidden"
        onChange={handleFileInputChange}
        disabled={disabled || uploading}
      />

      {value ? (
        <div className="rounded-md border border-border bg-muted/20 p-2 space-y-2">
          <img
            src={value}
            alt={previewAlt}
            className="h-24 w-full rounded-md object-cover border border-border bg-muted"
          />
          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={triggerSelect}
              disabled={disabled || uploading}
            >
              {uploading ? (
                <>
                  <Loader2 className="h-3 w-3 animate-spin" />
                  Subiendo...
                </>
              ) : (
                <>
                  <ImageUp className="h-3 w-3" />
                  Reemplazar
                </>
              )}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => onChange("")}
              disabled={disabled || uploading}
            >
              <Trash2 className="h-3 w-3" />
              Quitar
            </Button>
          </div>
        </div>
      ) : (
        <label
          htmlFor={inputId}
          onDragOver={(event) => {
            event.preventDefault();
            if (!disabled && !uploading) setDragActive(true);
          }}
          onDragLeave={() => setDragActive(false)}
          onDrop={handleDrop}
          className={cn(
            "flex cursor-pointer items-center justify-center gap-2 rounded-md border border-dashed p-3 text-xs text-muted-foreground transition-colors",
            dragActive && "border-primary text-foreground bg-primary/5",
            (disabled || uploading) && "cursor-not-allowed opacity-60",
          )}
        >
          {uploading ? (
            <>
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
              Subiendo imagen...
            </>
          ) : (
            <>
              <ImageUp className="h-3.5 w-3.5" />
              Arrastra una imagen o haz click para subir
            </>
          )}
        </label>
      )}

      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
