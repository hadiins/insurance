import { createContext, useContext } from "react";

const TabKeyContext = createContext<string | null>(null);

export function TabKeyProvider({ tabKey, children }: { tabKey: string; children: React.ReactNode }) {
  return <TabKeyContext.Provider value={tabKey}>{children}</TabKeyContext.Provider>;
}

export function useTabKey(): string {
  const key = useContext(TabKeyContext);
  if (!key) throw new Error("useTabKey must be used within a TabKeyProvider");
  return key;
}
