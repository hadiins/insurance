import { create } from "zustand";

export interface HeldLock {
  entityType: string;
  entityId: string;
  mine: boolean;
  holderDisplayName: string;
}

interface EditLockState {
  /** Keyed by `entityType:entityId`. Record pages mount and unmount their own PresenceLockBar, so
   * this is a mirror of what is on screen right now, not a server-side list. */
  locks: Record<string, HeldLock>;
  publish: (lock: HeldLock) => void;
  clear: (entityType: string, entityId: string) => void;
}

/** Lets the shell's status bar report editing locks without a second round trip. The lock endpoint
 * is per-record (`/api/locks/status` needs an entityType and an entityId), so there is no global
 * "am I editing something" call to make — the only honest source is the record pages that already
 * hold the state. */
export const useEditLockStore = create<EditLockState>((set) => ({
  locks: {},
  publish: (lock) =>
    set((state) => ({ locks: { ...state.locks, [`${lock.entityType}:${lock.entityId}`]: lock } })),
  clear: (entityType, entityId) =>
    set((state) => {
      const key = `${entityType}:${entityId}`;
      if (!(key in state.locks)) return state;
      const next = { ...state.locks };
      delete next[key];
      return { locks: next };
    }),
}));
