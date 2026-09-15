import { useMemo, useState } from "react";
import { NAV } from "../../app/navConfig";
import { versionLabel } from "../../app/version";
import { useTabsStore } from "../../app/store/tabsStore";
import { useAuthStore } from "../../app/store/authStore";
import type { NavItem } from "../../app/types";
import { SidebarGroup } from "./SidebarGroup";

const DEFAULT_OPEN = new Set(NAV.filter((g) => g.defaultOpen).map((g) => g.id));

export function Sidebar() {
  const [query, setQuery] = useState("");
  const [openGroups, setOpenGroups] = useState<Set<string>>(DEFAULT_OPEN);
  const [collapsed, setCollapsed] = useState(false);

  const tabs = useTabsStore((s) => s.tabs);
  const activeKey = useTabsStore((s) => s.activeKey);
  const openTab = useTabsStore((s) => s.openTab);
  const activeNavType = tabs.find((t) => t.key === activeKey)?.navType ?? null;
  const permissions = useAuthStore((s) => s.user?.permissions) ?? [];

  const trimmedQuery = query.trim();
  const filteredGroups = useMemo(
    () =>
      NAV.map((g) => ({
        ...g,
        items: g.items
          .filter((i) => !i.requiresPermission || permissions.includes(i.requiresPermission))
          .filter((i) => !trimmedQuery || i.title.includes(trimmedQuery)),
      })).filter((g) => g.items.length > 0),
    [trimmedQuery, permissions],
  );
  const hasResults = filteredGroups.some((g) => g.items.length > 0);

  function toggleGroup(id: string) {
    setOpenGroups((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  function handleSelect(item: NavItem) {
    openTab({
      navType: item.navType,
      page: item.page,
      kind: item.kind,
      title: item.title,
      pinned: item.pinned,
      payload: item.payload,
    });
  }

  function toggleCollapsed() {
    setCollapsed((c) => {
      if (c) return false;
      setQuery("");
      return true;
    });
  }

  return (
    <aside
      className={`flex flex-none flex-col border-s border-(--edge) bg-(--slate) transition-[width] duration-200 ${
        collapsed ? "w-14" : "w-[250px]"
      }`}
    >
      {!collapsed && (
        <div className="flex-none p-2.5">
          <div className="relative">
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              className="pointer-events-none absolute start-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-(--ice-3)"
            >
              <circle cx="11" cy="11" r="7" />
              <path d="M21 21l-4.3-4.3" />
            </svg>
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="جست‌وجوی منو..."
              aria-label="جست‌وجوی منو"
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) py-2 ps-8 pe-3 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
            />
          </div>
        </div>
      )}

      <nav className="flex-1 overflow-y-auto overflow-x-hidden px-2 pb-3">
        {!hasResults ? (
          <div className="p-4 text-center text-[11.5px] text-(--ice-3)">موردی پیدا نشد</div>
        ) : (
          filteredGroups.map((g) => (
            <SidebarGroup
              key={g.id}
              group={g}
              isOpen={trimmedQuery ? true : openGroups.has(g.id)}
              collapsed={collapsed}
              query={trimmedQuery}
              activeNavType={activeNavType}
              onToggle={() => toggleGroup(g.id)}
              onSelect={handleSelect}
            />
          ))
        )}
      </nav>

      <div className="flex flex-none items-center gap-1.5 border-t border-(--edge) p-2">
        {!collapsed && <span className="flex-none pe-1 text-[10.5px] text-(--ice-3)">{versionLabel()}</span>}
        <button
          type="button"
          onClick={toggleCollapsed}
          title="جمع کردن منو"
          className="flex flex-1 items-center justify-center gap-1.5 rounded-lg border border-(--edge) py-1.5 text-[11.5px] text-(--ice-3) transition-colors hover:bg-(--hov) hover:text-(--ice)"
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            className="h-3.5 w-3.5"
          >
            <path d="M4 6h16M4 12h16M4 18h16" />
          </svg>
          {!collapsed && <span>جمع کردن</span>}
        </button>
      </div>
    </aside>
  );
}
