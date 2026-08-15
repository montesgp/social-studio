import { redirect } from "next/navigation";
import { NicheDiscoveryClient } from "@/components/social/niche-discovery/niche-discovery-client";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { readRunIdFromSearchParams } from "@/lib/social/niche-discovery/run-url";

interface NicheDiscoveryPageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export default async function NicheDiscoveryPage({ searchParams }: NicheDiscoveryPageProps) {
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }

  const resolvedSearchParams = await searchParams;

  return <NicheDiscoveryClient initialRunId={readRunIdFromSearchParams(resolvedSearchParams)} />;
}
