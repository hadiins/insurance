import { toJalaali, toGregorian, isValidJalaaliDate } from "jalaali-js";
import { fa, toLatinDigits } from "./persian";

/** CLAUDE.md — "Jalali dates displayed; store DateTimeOffset UTC." One conversion point for the
 * whole frontend, per docs/TASK-25-IDENTITY-VEHICLE.md §6.1's "یک نقطهٔ تبدیل مرکزی در هر لایه." */

function isoToJalaliParts(iso: string): { jy: number; jm: number; jd: number } | null {
  const m = iso.slice(0, 10).match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!m) return null;
  const [, gy, gm, gd] = m;
  try {
    return toJalaali(Number(gy), Number(gm), Number(gd));
  } catch {
    return null;
  }
}

/** Raw Latin-digit "1405/05/23" — used as the JalaliDateField's internal editing text. */
export function isoToJalaliText(iso: string | null | undefined): string {
  if (!iso) return "";
  const parts = isoToJalaliParts(iso);
  if (!parts) return "";
  return `${parts.jy}/${String(parts.jm).padStart(2, "0")}/${String(parts.jd).padStart(2, "0")}`;
}

/** Persian-digit "۱۴۰۵/۰۵/۲۳" for read-only display (tables, file pages, reports). */
export function toJalaliDisplay(iso: string | null | undefined): string {
  const text = isoToJalaliText(iso);
  return text ? fa(text) : "—";
}

/** Persian-digit "۱۴۰۵/۰۵/۲۳ ۱۴:۰۵" for a full timestamp (audit log, timeline entries) — local time,
 * same as the Date-based formatting this replaces. */
export function toJalaliDateTimeDisplay(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const datePart = isoToJalaliText(`${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`);
  if (!datePart) return "—";
  const timePart = `${d.getHours().toString().padStart(2, "0")}:${d.getMinutes().toString().padStart(2, "0")}`;
  return fa(`${datePart} ${timePart}`);
}

/** "1405/05/23" (Persian or Latin digits, "/" or "-" separator) -> ISO "2026-08-14", or null if
 * not a real Jalali date yet (still mid-typing, or genuinely invalid). */
export function jalaliTextToIso(input: string): string | null {
  const normalized = toLatinDigits(input).trim();
  const m = normalized.match(/^(\d{3,4})[/-](\d{1,2})[/-](\d{1,2})$/);
  if (!m) return null;

  const jy = Number(m[1]);
  const jm = Number(m[2]);
  const jd = Number(m[3]);
  if (!isValidJalaaliDate(jy, jm, jd)) return null;

  const { gy, gm, gd } = toGregorian(jy, jm, jd);
  return `${gy.toString().padStart(4, "0")}-${String(gm).padStart(2, "0")}-${String(gd).padStart(2, "0")}`;
}
