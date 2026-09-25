import { useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../lib/api";
import { subscribeToDataChanges } from "../../lib/dataEvents";
import type { NavBadgeKey } from "../../app/types";

export interface NavBadgesDto {
  installmentWorklist: number;
  cheques: number;
  riskReviews: number;
  renewalWatches: number;
}

const QUERY_KEY = ["nav-badges"];

/** Sidebar counts from GET /api/nav/badges. The sidebar renders on every page, so this is
 * deliberately its own tiny call rather than the dashboard's payload.
 *
 * On failure the result is `null` and the caller renders no badge at all. A zero would be a lie of
 * exactly the kind rule 15 is about: this user genuinely may have six overdue installments and a
 * badge reading «۰» tells them to stop looking. */
export function useNavBadges(): NavBadgesDto | null {
  const queryClient = useQueryClient();

  const { data } = useQuery({
    queryKey: QUERY_KEY,
    queryFn: () => api.get<NavBadgesDto>("/nav/badges"),
    staleTime: 60_000,
    // A 403 (a marketer without Policy.Read) and a dead backend are both settled answers here, not
    // something to retry three times behind a sidebar that has other work to do.
    retry: false,
  });

  // Badges are counts of open work, so recording a payment or logging a call must move them — the
  // same signal that refreshes the pages themselves.
  useEffect(
    () =>
      subscribeToDataChanges(() => {
        void queryClient.invalidateQueries({ queryKey: QUERY_KEY });
      }),
    [queryClient],
  );

  return data ?? null;
}

export function badgeCount(badges: NavBadgesDto | null, key: NavBadgeKey): number | null {
  if (badges === null) return null;
  return badges[key];
}
