import type { NavGroup, NavItem } from "../../app/types";
import { fa } from "../../lib/persian";
import { highlightMatch } from "./highlightMatch";

interface SidebarGroupProps {
  group: NavGroup;
  isOpen: boolean;
  collapsed: boolean;
  query: string;
  activeNavType: string | null;
  onToggle: () => void;
  onSelect: (item: NavItem) => void;
}

export function SidebarGroup({
  group,
  isOpen,
  collapsed,
  query,
  activeNavType,
  onToggle,
  onSelect,
}: SidebarGroupProps) {
  const filteredItems = group.items;
  if (filteredItems.length === 0) return null;

  return (
    <div className="group/nav relative mb-0.5">
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={isOpen}
        className={`flex w-full items-center gap-2.5 rounded-[10px] px-2.5 py-2.5 text-right text-[12.5px] font-semibold transition-colors ${
          collapsed ? "justify-center" : ""
        } ${isOpen ? "text-(--ice)" : "text-(--ice-2)"} hover:bg-(--hov) hover:text-(--ice)`}
      >
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className={`h-4 w-4 flex-none ${isOpen ? "text-(--mint)" : "text-(--ice-3)"}`}
        >
          <path d={group.icon} />
        </svg>
        {!collapsed && (
          <>
            <span className="flex-1 overflow-hidden text-ellipsis whitespace-nowrap">{group.label}</span>
            <span className="flex-none rounded-full bg-(--btn-bg) px-1.5 text-[10px] leading-[15px] text-(--ice-3)">
              {fa(filteredItems.length)}
            </span>
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
              strokeLinecap="round"
              strokeLinejoin="round"
              className={`h-3 w-3 flex-none text-(--ice-3) transition-transform ${isOpen ? "-rotate-90" : ""}`}
            >
              <path d="M15 18l-6-6 6-6" />
            </svg>
          </>
        )}
      </button>

      <div
        className={`overflow-hidden transition-[max-height] duration-200 ${
          collapsed
            ? "hidden group-hover/nav:absolute group-hover/nav:end-full group-hover/nav:top-0 group-hover/nav:z-40 group-hover/nav:block group-hover/nav:min-w-[190px] group-hover/nav:rounded-xl group-hover/nav:border group-hover/nav:border-(--edge-2) group-hover/nav:bg-(--slate) group-hover/nav:p-1.5 group-hover/nav:shadow-[var(--sh)]"
            : isOpen
              ? "max-h-[640px]"
              : "max-h-0"
        }`}
      >
        {filteredItems.map((item) => (
          <a
            key={item.navType}
            role="button"
            tabIndex={0}
            onClick={() => onSelect(item)}
            onKeyDown={(e) => {
              if (e.key === "Enter") onSelect(item);
            }}
            className={`relative flex cursor-pointer items-center gap-2 py-1.5 text-[12.5px] transition-colors ${
              collapsed
                ? "px-2"
                : "ms-3.5 border-s border-(--edge) px-2.5"
            } ${
              item.navType === activeNavType
                ? "font-semibold text-(--mint)"
                : "text-(--ice-3) hover:bg-(--hov) hover:text-(--ice)"
            }`}
          >
            {item.navType === activeNavType && !collapsed && (
              <span className="absolute inset-y-1.5 -start-px w-0.5 rounded-full bg-(--mint)" />
            )}
            <span className="flex-1 overflow-hidden text-ellipsis whitespace-nowrap">
              {highlightMatch(item.title, query)}
            </span>
          </a>
        ))}
      </div>
    </div>
  );
}
