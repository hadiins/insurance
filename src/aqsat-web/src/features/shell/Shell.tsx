import { useEffect } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { Sidebar } from "./Sidebar";
import { TabBar } from "./TabBar";
import { Stage } from "./Stage";
import { ConfirmCloseDialog } from "./ConfirmCloseDialog";
import { ThemeToggle } from "./ThemeToggle";
import { Toast } from "./Toast";

export function Shell() {
  const tabs = useTabsStore((s) => s.tabs);
  const activeKey = useTabsStore((s) => s.activeKey);
  const openTab = useTabsStore((s) => s.openTab);
  const closeTab = useTabsStore((s) => s.closeTab);
  const cycleNext = useTabsStore((s) => s.cycleNext);

  useEffect(() => {
    if (tabs.length === 0) {
      openTab({ navType: "today", page: "today", kind: "singleton", title: "امروز", pinned: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (!e.ctrlKey) return;
      if (e.key === "w") {
        e.preventDefault();
        if (activeKey) closeTab(activeKey);
      } else if (e.key === "Tab") {
        e.preventDefault();
        cycleNext();
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [activeKey, closeTab, cycleNext]);

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex flex-none items-center gap-3.5 border-b border-(--edge) bg-(--slate) px-4 py-2.5">
        <div className="grid h-[30px] w-[30px] flex-none place-items-center rounded-[9px] bg-linear-to-br from-(--mint) to-(--mint-dim) text-[14px] font-black text-(--on-mint) shadow-[var(--gl-mint)]">
          ق
        </div>
        <b className="text-[14.5px] font-bold text-(--ice)">دفتر اقساط</b>
        <span className="flex-1" />
        <span className="text-[11.5px] text-(--ice-3)">نمایندگی ۲۴۹۱ — شیراز</span>
        <ThemeToggle />
      </div>

      <div className="flex min-h-0 flex-1">
        <Sidebar />
        <div className="flex min-w-0 flex-1 flex-col">
          <TabBar />
          <Stage />
        </div>
      </div>

      <ConfirmCloseDialog />
      <Toast />
    </div>
  );
}
