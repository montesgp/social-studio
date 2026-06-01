import { useEffect, useState } from "react";
import { listSocialNetworks, SocialNetworksQuery } from "@/lib/api/social-networks";
import type { SocialNetwork } from "@/lib/api/types";

export interface UseSocialNetworksOptions {
  query?: SocialNetworksQuery;
}

export interface UseSocialNetworksReturn {
  networks: SocialNetwork[];
  loading: boolean;
}

/**
 * Fetches the list of active social networks.
 * Handles cleanup to discard stale responses when the token changes or the
 * component unmounts.
 */
export function useSocialNetworks(
  token?: string,
  options?: UseSocialNetworksOptions,
): UseSocialNetworksReturn {
  const [networks, setNetworks] = useState<SocialNetwork[]>([]);
  const [loading, setLoading] = useState(true);

  const query = options?.query ?? { page: 1, pageSize: 100 };

  useEffect(() => {
    if (!token) {
      setNetworks([]);
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);

    void listSocialNetworks(query, token)
      .then((result) => {
        if (!cancelled) setNetworks(result.items);
      })
      .catch(() => {
        // Silent — callers handle empty list gracefully.
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
    // query is intentionally excluded from deps — callers pass object literals
    // and we only want to re-fetch when the token changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  return { networks, loading };
}
