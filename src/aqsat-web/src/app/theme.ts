export type Theme = "light" | "dark";

const STORAGE_KEY = "aqsat-theme";

export function getTheme(): Theme {
  return (document.documentElement.getAttribute("data-theme") as Theme | null) ?? "dark";
}

export function setTheme(theme: Theme): void {
  document.documentElement.setAttribute("data-theme", theme);
  try {
    localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // localStorage unavailable (private mode) — theme just won't persist across reloads.
  }
}

export function toggleTheme(): Theme {
  const next: Theme = getTheme() === "dark" ? "light" : "dark";
  setTheme(next);
  return next;
}
