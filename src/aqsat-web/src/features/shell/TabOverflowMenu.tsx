import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import { useTabsStore } from "../../app/store/tabsStore";

export function TabOverflowMenu() {
  const tabs = useTabsStore((s) => s.tabs);
  const activeKey = useTabsStore((s) => s.activeKey);
  const setActive = useTabsStore((s) => s.setActive);

  if (tabs.length === 0) return null;

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger asChild>
        <button
          type="button"
          aria-label="فهرست همهٔ تب‌ها"
          className="mx-1.5 my-1 grid h-7 w-7 flex-none place-items-center self-center rounded-lg border border-(--edge) text-(--ice-3) transition-colors hover:bg-(--hov) hover:text-(--ice)"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="h-3.5 w-3.5">
            <path d="M6 9l6 6 6-6" />
          </svg>
        </button>
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content
          align="end"
          sideOffset={6}
          className="z-50 max-h-80 min-w-[220px] overflow-y-auto rounded-xl border border-(--edge-2) bg-(--slate) p-1.5 shadow-[var(--sh)]"
        >
          {tabs.map((tab) => (
            <DropdownMenu.Item
              key={tab.key}
              onSelect={() => setActive(tab.key)}
              className={`flex cursor-pointer items-center gap-2 rounded-lg px-2.5 py-2 text-[12.5px] outline-none ${
                tab.key === activeKey ? "font-semibold text-(--mint)" : "text-(--ice-2)"
              } hover:bg-(--hov)`}
            >
              {tab.dirty && <span className="h-[6px] w-[6px] flex-none rounded-full bg-(--amber)" />}
              <span className="overflow-hidden text-ellipsis whitespace-nowrap">{tab.title}</span>
            </DropdownMenu.Item>
          ))}
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}
