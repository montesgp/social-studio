import {
  Briefcase,
  Building2,
  Car,
  Cpu,
  Dumbbell,
  GraduationCap,
  Hammer,
  Home,
  Landmark,
  Leaf,
  PawPrint,
  Plane,
  Scissors,
  ShoppingBag,
  Sparkles,
  Stethoscope,
  Tag,
  Utensils
} from "lucide-react";
import type { LucideIcon } from "lucide-react";

/**
 * Icon catalog for niche/sector entries, mirroring `social-network-icons`.
 *
 * Keys match the domain rule for `IconKey` (lowercase alphanumeric with optional hyphens, max 32)
 * and the keys the platform seeder writes: `cpu`, `stethoscope`, `utensils`, `dumbbell`, `home`.
 */
export const nicheIconOptions: Array<{ key: string; label: string; icon: LucideIcon }> = [
  { key: "cpu", label: "Software & Technology", icon: Cpu },
  { key: "stethoscope", label: "Medical & Health", icon: Stethoscope },
  { key: "utensils", label: "Gastronomy", icon: Utensils },
  { key: "dumbbell", label: "Fitness & Wellness", icon: Dumbbell },
  { key: "home", label: "Real Estate", icon: Home },
  { key: "building", label: "Construction & Property", icon: Building2 },
  { key: "briefcase", label: "Professional Services", icon: Briefcase },
  { key: "graduation-cap", label: "Education", icon: GraduationCap },
  { key: "shopping-bag", label: "Retail & E-Commerce", icon: ShoppingBag },
  { key: "landmark", label: "Finance & Legal", icon: Landmark },
  { key: "plane", label: "Travel & Hospitality", icon: Plane },
  { key: "car", label: "Automotive", icon: Car },
  { key: "scissors", label: "Beauty & Grooming", icon: Scissors },
  { key: "sparkles", label: "Beauty & Wellness", icon: Sparkles },
  { key: "paw-print", label: "Pets & Animals", icon: PawPrint },
  { key: "leaf", label: "Sustainability & Agriculture", icon: Leaf },
  { key: "hammer", label: "Trades & Industry", icon: Hammer },
  { key: "tag", label: "Generic niche", icon: Tag }
];

export const nicheIcons: Record<string, LucideIcon> = Object.fromEntries(
  nicheIconOptions.map((option) => [option.key, option.icon])
);

/** Rendered whenever a niche carries no icon key, or one this build does not know. */
export const GENERIC_NICHE_ICON: LucideIcon = Tag;

export function getNicheIconOption(iconKey: string | null | undefined) {
  if (!iconKey) return null;
  return nicheIconOptions.find((option) => option.key === iconKey.toLowerCase()) ?? null;
}

export function getNicheIcon(iconKey: string | null | undefined): LucideIcon {
  if (!iconKey) return GENERIC_NICHE_ICON;
  return nicheIcons[iconKey.toLowerCase()] ?? GENERIC_NICHE_ICON;
}
