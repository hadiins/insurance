import { useEffect } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useAuthStore } from "../../app/store/authStore";
import { Sidebar } from "./Sidebar";
import { TabBar } from "./TabBar";
import { Stage } from "./Stage";
import { ConfirmCloseDialog } from "./ConfirmCloseDialog";
import { ThemeToggle } from "./ThemeToggle";
import { Toast } from "./Toast";
import { MaintenanceBanner } from "../platform/MaintenanceBanner";

export function Shell() {
  const tabs = useTabsStore((s) => s.tabs);
  const activeKey = useTabsStore((s) => s.activeKey);
  const openTab = useTabsStore((s) => s.openTab);
  const closeTab = useTabsStore((s) => s.closeTab);
  const cycleNext = useTabsStore((s) => s.cycleNext);

  const user = useAuthStore((s) => s.user);
  const logout = useAuthStore((s) => s.logout);
  const switchOrganization = useAuthStore((s) => s.switchOrganization);

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

  const activeOrg = user?.organizations.find((o) => o.organizationId === user.activeOrganizationId);

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <MaintenanceBanner />
      <div className="flex flex-none items-center gap-3.5 border-b border-(--edge) bg-(--slate) px-4 py-2.5">
        <div className="grid h-[30px] w-[30px] flex-none place-items-center rounded-[9px] bg-linear-to-br from-(--mint) to-(--mint-dim) text-[14px] font-black text-(--on-mint) shadow-[var(--gl-mint)]">
          ق
        </div>
        <b className="text-[14.5px] font-bold text-(--ice)">دفتر اقساط</b>
        <span className="flex-1" />

        {user && user.organizations.length > 1 ? (
          <select
            value={user.activeOrganizationId}
            onChange={(e) => switchOrganization(e.target.value)}
            className="rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1 text-[11.5px] text-(--ice-2) outline-none focus:border-(--mint)"
          >
            {user.organizations.map((o) => (
              <option key={o.organizationId} value={o.organizationId}>
                {o.organizationName}
              </option>
            ))}
          </select>
        ) : (
          <span className="text-[11.5px] text-(--ice-3)">{activeOrg?.organizationName ?? ""}</span>
        )}

        <span className="text-[11.5px] text-(--ice-3)">{user?.displayName}</span>

        <button
          type="button"
          onClick={logout}
          className="rounded-[9px] border border-(--edge) px-2.5 py-1.5 text-[11.5px] text-(--ice-3) transition-colors hover:bg-(--hov) hover:text-(--ice)"
        >
          خروج
        </button>

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
