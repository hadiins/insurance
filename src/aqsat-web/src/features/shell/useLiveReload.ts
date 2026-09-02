import { useEffect, useRef } from "react";
import { subscribeToDataChanges } from "../../lib/dataEvents";
import { useTab } from "./TabContext";

/** Keeps a page's data fresh without a browser refresh: refetches when any mutation succeeds
 * elsewhere in the app (dataEvents), and again when this tab becomes visible — Stage keeps
 * hidden tabs mounted, so switching back to a tab must not show stale data. */
export function useLiveReload(reload: () => void): void {
  const { active } = useTab();
  const reloadRef = useRef(reload);
  reloadRef.current = reload;

  useEffect(() => {
    return subscribeToDataChanges(() => reloadRef.current());
  }, []);

  useEffect(() => {
    if (active) reloadRef.current();
  }, [active]);
}
