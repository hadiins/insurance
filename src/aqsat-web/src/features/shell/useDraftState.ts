import { useCallback, useState } from "react";
import { useTabKey } from "./TabContext";

/** Draft form values that survive a page refresh, scoped to the tab that owns them.
 * Persisted to localStorage under `aqsat_draft:{tabKey}:{key}`; wiped when the tab closes
 * (tabsStore.clearDrafts) or after a successful submit (`clear()`). */
export function useDraftState<T>(key: string, initial: T): [T, (value: T) => void, () => void] {
  const tabKey = useTabKey();
  const storageKey = `aqsat_draft:${tabKey}:${key}`;
  const [value, setValue] = useState<T>(() => {
    try {
      const stored = localStorage.getItem(storageKey);
      if (stored !== null) return JSON.parse(stored) as T;
    } catch {
      // Corrupt or non-JSON draft — fall through to the initial value.
    }
    return initial;
  });

  const setValuePersisted = useCallback(
    (next: T) => {
      setValue(next);
      try {
        localStorage.setItem(storageKey, JSON.stringify(next));
      } catch {
        // Quota exceeded or storage disabled — the draft just won't survive refresh.
      }
    },
    [storageKey],
  );

  const clear = useCallback(() => {
    localStorage.removeItem(storageKey);
  }, [storageKey]);

  return [value, setValuePersisted, clear];
}
