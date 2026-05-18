import { Instagram, Facebook, Music2, Share2 } from "lucide-react";
import type { LucideIcon } from "lucide-react";

/**
 * Icon SSOT for social network codes (ADR 9).
 * Adding a new network = 1 line here + 1 seed entry in SocialNetworkCatalogSeeder.
 * Used by networks-admin-client.tsx and available for future SDDs (referent accounts, brand manual, etc.)
 */
export const socialNetworkIcons: Record<string, LucideIcon> = {
  instagram: Instagram,
  facebook: Facebook,
  tiktok: Music2,
};

/**
 * Returns the Lucide icon for a given social network code.
 * Falls back to Share2 for unknown codes — never throws.
 */
export function getSocialNetworkIcon(code: string): LucideIcon {
  return socialNetworkIcons[code.toLowerCase()] ?? Share2;
}
