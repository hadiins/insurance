import { useState } from "react";
import { getTheme, toggleTheme } from "../../app/theme";

export function ThemeToggle() {
  const [theme, setThemeState] = useState(getTheme);

  return (
    <button
      type="button"
      onClick={() => setThemeState(toggleTheme())}
      aria-label="روز و شب"
      className="relative grid h-8 w-8 flex-none place-items-center overflow-hidden rounded-[9px] border border-(--edge) text-(--ice-3) transition-colors hover:bg-(--hov) hover:text-(--ice)"
    >
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        className={`absolute h-4 w-4 transition-all ${theme === "dark" ? "opacity-100" : "-translate-y-4 opacity-0"}`}
      >
        <circle cx="12" cy="12" r="4.2" />
        <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
      </svg>
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        className={`absolute h-4 w-4 transition-all ${theme === "light" ? "opacity-100" : "translate-y-4 opacity-0"}`}
      >
        <path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z" />
      </svg>
    </button>
  );
}
