import { fa } from "../lib/persian";

/// The app version shown to users (login page, sidebar footer). Sourced from
/// package.json at build time via vite.config.ts's __APP_VERSION__ define, so the
/// displayed version always matches the shipped release.
export const APP_VERSION = __APP_VERSION__;

/** «نسخهٔ ۱.۰.۰» — Persian digits, for display. */
export function versionLabel(): string {
  return `نسخهٔ ${fa(APP_VERSION)}`;
}
