import { useTabsStore } from "../../app/store/tabsStore";
import { TabOverflowMenu } from "./TabOverflowMenu";

export function TabBar() {
  const tabs = useTabsStore((s) => s.tabs);
  const activeKey = useTabsStore((s) => s.activeKey);
  const setActive = useTabsStore((s) => s.setActive);
  const closeTab = useTabsStore((s) => s.closeTab);

  return (
    <div className="flex flex-none items-stretch gap-0.5 border-b border-(--edge) bg-(--tabbar) ps-3 pt-1.5">
      <div className="flex flex-1 items-stretch gap-0.5 overflow-x-auto" style={{ scrollbarWidth: "thin" }}>
        {tabs.map((tab) => {
          const isActive = tab.key === activeKey;
          return (
            <button
              key={tab.key}
              type="button"
              data-testid={`tab-${tab.key}`}
              onClick={() => setActive(tab.key)}
              className={`relative flex max-w-[230px] flex-none items-center gap-2 rounded-t-[10px] border border-transparent px-3 py-2 text-[12.5px] whitespace-nowrap transition-colors ${
                isActive
                  ? "border-(--edge) border-b-0 bg-(--void) font-semibold text-(--ice)"
                  : "text-(--ice-3) hover:bg-(--hov) hover:text-(--ice-2)"
              }`}
            >
              {isActive && (
                <span className="absolute inset-x-0 top-0 h-0.5 rounded-full bg-(--mint) shadow-[var(--gl-mint)]" />
              )}
              {tab.dirty && (
                <span
                  title="ذخیره نشده"
                  className="h-[7px] w-[7px] flex-none rounded-full bg-(--amber) shadow-[0_0_8px_var(--amber)]"
                />
              )}
              <span className="overflow-hidden text-ellipsis">{tab.title}</span>
              {!tab.pinned && (
                <span
                  role="button"
                  aria-label="بستن تب"
                  onClick={(e) => {
                    e.stopPropagation();
                    closeTab(tab.key);
                  }}
                  className="grid h-[17px] w-[17px] flex-none place-items-center rounded-[5px] text-[14px] leading-none opacity-50 transition-opacity hover:bg-(--btn-hov) hover:text-(--ember) hover:opacity-100"
                >
                  ×
                </span>
              )}
            </button>
          );
        })}
      </div>
      <TabOverflowMenu />
    </div>
  );
}
