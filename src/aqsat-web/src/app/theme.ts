export type Theme = "light" | "dark";
export type Palette = "indigo" | "mint" | "teal" | "violet" | "zinc";

const STORAGE_KEY = "aqsat-theme";
const PALETTE_KEY = "aqsat-palette";

export function getTheme(): Theme {
  return (document.documentElement.getAttribute("data-theme") as Theme | null) ?? "dark";
}

export function getPalette(): Palette {
  return (document.documentElement.getAttribute("data-palette") as Palette | null) ?? "indigo";
}

export function applyTheme(theme: Theme, palette: Palette): void {
  document.documentElement.setAttribute("data-theme", theme);
  document.documentElement.setAttribute("data-palette", palette);
  try {
    localStorage.setItem(STORAGE_KEY, theme);
    localStorage.setItem(PALETTE_KEY, palette);
  } catch {
    // localStorage unavailable (private mode) — theme just won't persist across reloads.
  }
}

export function setTheme(theme: Theme): void {
  applyTheme(theme, getPalette());
}

export function setPalette(palette: Palette): void {
  applyTheme(getTheme(), palette);
}

export function toggleTheme(): Theme {
  const next: Theme = getTheme() === "dark" ? "light" : "dark";
  setTheme(next);
  return next;
}
