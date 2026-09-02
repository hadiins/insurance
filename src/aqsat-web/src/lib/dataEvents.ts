type Listener = () => void;

const listeners = new Set<Listener>();

let emitTimer: ReturnType<typeof setTimeout> | null = null;

/** Tell every mounted list/detail page that server data changed, so it refetches instead of
 * waiting for the user to hit F5. Emitted by api.ts after any successful mutation, debounced so
 * a form that fires three requests in a row produces one reload wave. */
export function emitDataChanged(): void {
  if (emitTimer !== null) clearTimeout(emitTimer);
  emitTimer = setTimeout(() => {
    emitTimer = null;
    for (const listener of listeners) listener();
  }, 300);
}

export function subscribeToDataChanges(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
