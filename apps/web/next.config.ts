import type { NextConfig } from "next";

const rawApiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL;
if (!rawApiBaseUrl) {
  throw new Error("NEXT_PUBLIC_API_BASE_URL must be configured.");
}

const apiBaseUrl = rawApiBaseUrl.endsWith("/") ? rawApiBaseUrl.slice(0, -1) : rawApiBaseUrl;

const nextConfig: NextConfig = {
  reactStrictMode: true,
  async rewrites() {
    return [
      // API proxy
      { source: "/hive-api/:path*", destination: `${apiBaseUrl}/:path*` },
      // Clean workspace URLs → existing pages
      { source: "/content-workflow", destination: "/workflow" },
      { source: "/ai-ideas", destination: "/ideas" },
      // Admin URLs → existing pages
      { source: "/admin/organizations", destination: "/organizations" },
      { source: "/admin/users", destination: "/members" }
    ];
  }
};

export default nextConfig;
