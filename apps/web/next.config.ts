import type { NextConfig } from "next";

const rawApiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL;
if (!rawApiBaseUrl) {
  throw new Error("NEXT_PUBLIC_API_BASE_URL must be configured.");
}

const apiBaseUrl = rawApiBaseUrl.endsWith("/") ? rawApiBaseUrl.slice(0, -1) : rawApiBaseUrl;
const distDir = process.env.NEXT_DIST_DIR;

const nextConfig: NextConfig = {
  reactStrictMode: true,
  ...(distDir ? { distDir } : {}),
  async rewrites() {
    return [
      // API proxy
      { source: "/hive-api/:path*", destination: `${apiBaseUrl}/:path*` },
      // Clean workspace URLs → existing pages
      { source: "/content-generator", destination: "/workflow" },
      { source: "/single-analyzer", destination: "/analyzer" },
      { source: "/ai-ideas", destination: "/ideas" },
      // Admin URLs → existing pages
      { source: "/admin/organizations", destination: "/organizations" },
      { source: "/admin/users", destination: "/members" },
      { source: "/admin/social-networks", destination: "/social-networks" },
      { source: "/admin/email-templates", destination: "/platform-email-templates" },
      { source: "/admin/platform-integrations", destination: "/platform-integrations" }
    ];
  }
};

export default nextConfig;
