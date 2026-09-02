const TABS_KEY = "aqsat_tabs";
const DRAFT_PREFIX = "aqsat_draft:";

/** Wipes everything another user must never inherit: the persisted tab list (which tab titles
 * reveal records opened) and any half-typed form drafts. Called on logout and session expiry. */
export function clearPersistedWorkspace(): void {
  localStorage.removeItem(TABS_KEY);

  const doomed: string[] = [];
  for (let i = 0; i < localStorage.length; i++) {
    const key = localStorage.key(i);
    if (key?.startsWith(DRAFT_PREFIX)) doomed.push(key);
  }
  for (const key of doomed) localStorage.removeItem(key);
}
