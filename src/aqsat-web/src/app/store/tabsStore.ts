import { create } from "zustand";
import { persist } from "zustand/middleware";
import { fa } from "../../lib/persian";
import type { OpenTab, OpenTabRequest } from "../types";

interface TabsState {
  tabs: OpenTab[];
  activeKey: string | null;
  maxTabs: number;
  pendingCloseKey: string | null;
  toastMessage: string | null;

  openTab: (request: OpenTabRequest) => void;
  closeTab: (key: string, force?: boolean) => void;
  confirmClose: () => void;
  cancelClose: () => void;
  setActive: (key: string) => void;
  setDirty: (key: string, dirty: boolean) => void;
  setTitle: (key: string, title: string) => void;
  cycleNext: () => void;
  dismissToast: () => void;
}

/// crypto.randomUUID() only exists in a secure context (HTTPS or localhost) — plain HTTP throws
/// "crypto.randomUUID is not a function", silently breaking every multi-create tab (e.g. "ثبت
/// بیمه‌نامه" never opens). This tab key has no security requirement, just uniqueness, so a
/// dependency-free fallback avoids the secure-context requirement entirely.
function uniqueId(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

function keyFor(request: OpenTabRequest): string {
  switch (request.kind) {
    case "multi-create":
      return `${request.navType}:${uniqueId()}`;
    case "multi-record":
      return `${request.navType}:${request.recordId}`;
    case "singleton":
    default:
      return request.navType;
  }
}

/** Draft form values of a closing tab must not outlive it — their tab key is gone, so on refresh
 * they would be restored for a tab that no longer exists. */
function clearDrafts(tabKey: string): void {
  const prefix = `aqsat_draft:${tabKey}:`;
  const doomed: string[] = [];
  for (let i = 0; i < localStorage.length; i++) {
    const key = localStorage.key(i);
    if (key?.startsWith(prefix)) doomed.push(key);
  }
  for (const key of doomed) localStorage.removeItem(key);
}

export const useTabsStore = create<TabsState>()(
  persist(
    (set, get) => ({
      tabs: [],
      activeKey: null,
      maxTabs: 12,
      pendingCloseKey: null,
      toastMessage: null,

      openTab: (request) => {
        const { tabs } = get();
        const isNewEveryTime = request.kind === "multi-create";
        if (!isNewEveryTime) {
          const key = keyFor(request);
          const existing = tabs.find((t) => t.key === key);
          if (existing) {
            set({ activeKey: key });
            return;
          }
        }

        if (tabs.length >= get().maxTabs) {
          set({ toastMessage: `حداکثر ${fa(get().maxTabs)} تب باز می‌شود` });
          return;
        }

        const key = keyFor(request);
        const newTab: OpenTab = {
          key,
          navType: request.navType,
          page: request.page,
          title: request.title,
          pinned: request.pinned ?? false,
          dirty: false,
          payload: request.payload,
        };
        set({ tabs: [...tabs, newTab], activeKey: key });
      },

      closeTab: (key, force) => {
        const { tabs, activeKey } = get();
        const tab = tabs.find((t) => t.key === key);
        if (!tab || tab.pinned) return;

        if (tab.dirty && !force) {
          set({ pendingCloseKey: key });
          return;
        }

        const index = tabs.findIndex((t) => t.key === key);
        const nextTabs = tabs.filter((t) => t.key !== key);
        let nextActive = activeKey;
        if (activeKey === key) {
          nextActive = nextTabs[index - 1]?.key ?? nextTabs[0]?.key ?? null;
        }
        clearDrafts(key);
        set({ tabs: nextTabs, activeKey: nextActive, pendingCloseKey: null });
      },

  confirmClose: () => {
    const key = get().pendingCloseKey;
    if (key) get().closeTab(key, true);
  },

  cancelClose: () => {
    const key = get().pendingCloseKey;
    set({ pendingCloseKey: null, activeKey: key ?? get().activeKey });
  },

  setActive: (key) => set({ activeKey: key }),

  setDirty: (key, dirty) =>
    set((s) => ({ tabs: s.tabs.map((t) => (t.key === key ? { ...t, dirty } : t)) })),

  setTitle: (key, title) =>
    set((s) => ({ tabs: s.tabs.map((t) => (t.key === key ? { ...t, title } : t)) })),

  cycleNext: () => {
    const { tabs, activeKey } = get();
    if (tabs.length === 0) return;
    const index = tabs.findIndex((t) => t.key === activeKey);
    const next = tabs[(index + 1) % tabs.length];
    set({ activeKey: next.key });
  },

  dismissToast: () => set({ toastMessage: null }),
    }),
    {
      name: "aqsat_tabs",
      // Only the durable tab list survives a refresh — pendingCloseKey/toastMessage are transient
      // dialog state, restoring them against no dialog would be nonsense.
      partialize: (state) => ({ tabs: state.tabs, activeKey: state.activeKey, maxTabs: state.maxTabs }),
    },
  ),
);
