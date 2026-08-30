/**
 * Pure grouping for the Content Studio font-family picker (R2.1/R2.2 — Decision Q5/F4). The catalog
 * from the render-capabilities endpoint already orders brand fonts first, then the system-safe list
 * (`FontCatalog.For`); this just splits that single list into the two picker sections so brand fonts
 * can be visually marked as brand.
 */

export interface FontOption {
  family: string;
  isBrand: boolean;
}

export interface GroupedFontOptions {
  brand: FontOption[];
  system: FontOption[];
}

export function groupFontOptions(fonts: FontOption[]): GroupedFontOptions {
  return {
    brand: fonts.filter((f) => f.isBrand),
    system: fonts.filter((f) => !f.isBrand),
  };
}
