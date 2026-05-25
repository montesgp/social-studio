import {
  AtSign,
  Facebook,
  Hash,
  Instagram,
  Linkedin,
  MessageCircle,
  Music2,
  Radio,
  Rss,
  Send,
  Share2,
  Twitch,
  Twitter,
  Video,
  Youtube
} from "lucide-react";
import type { LucideIcon } from "lucide-react";

export const socialNetworkIconOptions: Array<{ key: string; label: string; icon: LucideIcon }> = [
  { key: "instagram", label: "Instagram", icon: Instagram },
  { key: "facebook", label: "Facebook", icon: Facebook },
  { key: "tiktok", label: "TikTok", icon: Music2 },
  { key: "youtube", label: "YouTube", icon: Youtube },
  { key: "twitter", label: "Twitter / X", icon: Twitter },
  { key: "linkedin", label: "LinkedIn", icon: Linkedin },
  { key: "twitch", label: "Twitch", icon: Twitch },
  { key: "threads", label: "Threads", icon: AtSign },
  { key: "discord", label: "Discord", icon: MessageCircle },
  { key: "telegram", label: "Telegram", icon: Send },
  { key: "whatsapp", label: "WhatsApp", icon: MessageCircle },
  { key: "pinterest", label: "Pinterest", icon: Hash },
  { key: "snapchat", label: "Snapchat", icon: Radio },
  { key: "vimeo", label: "Vimeo", icon: Video },
  { key: "rss", label: "RSS", icon: Rss },
  { key: "generic-social", label: "Generic social", icon: Share2 }
];

export const socialNetworkIcons: Record<string, LucideIcon> = Object.fromEntries(
  socialNetworkIconOptions.map((option) => [option.key, option.icon])
);

export function getSocialNetworkIconOption(iconKey: string) {
  return socialNetworkIconOptions.find((option) => option.key === iconKey.toLowerCase()) ?? null;
}

export function getSocialNetworkIcon(iconKey: string): LucideIcon {
  return socialNetworkIcons[iconKey.toLowerCase()] ?? Share2;
}
