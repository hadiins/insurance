import { createContext, useContext } from "react";

interface TabContextValue {
  tabKey: string;
  /** True while this tab is the visible one. Hidden tabs stay mounted, so this is how a page
   * knows whether a data reload is worth doing right now. */
  active: boolean;
}

const TabContext = createContext<TabContextValue | null>(null);

export function TabKeyProvider({
  tabKey,
  active,
  children,
}: {
  tabKey: string;
  active: boolean;
  children: React.ReactNode;
}) {
  return <TabContext.Provider value={{ tabKey, active }}>{children}</TabContext.Provider>;
}

export function useTab(): TabContextValue {
  const value = useContext(TabContext);
  if (!value) throw new Error("useTab must be used within a TabKeyProvider");
  return value;
}

export function useTabKey(): string {
  return useTab().tabKey;
}
