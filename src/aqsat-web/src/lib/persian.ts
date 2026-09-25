const FA_DIGITS = ["۰", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹"];
const AR_DIGITS = ["٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"];

/** Persian/Arabic-Indic digits → Latin — mirrors Aqsat.Application.Common.DigitNormalizer. */
export function toLatinDigits(input: string): string {
  return input.replace(/[۰-۹٠-٩]/g, (d) => {
    const faIdx = FA_DIGITS.indexOf(d);
    if (faIdx >= 0) return String(faIdx);
    return String(AR_DIGITS.indexOf(d));
  });
}

/** Vehicle model years are entered in either Iranian or Gregorian notation. A Jalali year has no
 * month/day, so the established model-year convention is used: 1404 → 2025 (Jalali + 621).
 * Gregorian input remains unchanged. The upper bound keeps the stored Gregorian year ≤ 2100. */
export function normalizeVehicleManufactureYear(input: string | number | null | undefined): number | null {
  if (input === null || input === undefined || input === "") return null;
  const raw = String(input).trim();
  if (!/^\d{4}$/.test(raw)) return Number.NaN;
  const year = Number(raw);
  const gregorian = year >= 1300 && year <= 1479 ? year + 621 : year;
  return gregorian >= 1900 && gregorian <= 2100 ? gregorian : Number.NaN;
}

export function fa(n: number | string): string {
  return String(n).replace(/\d/g, (d) => FA_DIGITS[Number(d)]);
}

export function money(n: number): string {
  return fa(Math.round(n).toLocaleString("en-US")).replace(/,/g, "٬");
}

/** docs/TASK-25-IDENTITY-VEHICLE.md §1 — mirrors Aqsat.Application.Common.NationalIdValidator. */
export function isValidNationalId(input: string): boolean {
  const s = toLatinDigits(input).trim();
  if (s.length !== 10 || !/^\d{10}$/.test(s)) return false;
  if (new Set(s).size === 1) return false;

  let sum = 0;
  for (let i = 0; i < 9; i++) {
    sum += Number(s[i]) * (10 - i);
  }
  const rem = sum % 11;
  const check = Number(s[9]);
  return rem < 2 ? check === rem : check === 11 - rem;
}

/** Owner decision 2026-09-21 (foreign nationals) — mirrors the expanded
 * Aqsat.Application.Common.NationalIdValidator, which now CLASSIFIES identity input instead of
 * rejecting anything non-Iranian:
 *  - a regular 10-digit Iranian national ID (mod-11 checksum),
 *  - a 996-series foreign-resident ID (structural check only — no official checksum algorithm is
 *    documented for that series, and a wrong rejection of a real resident is worse than a lenient
 *    acceptance; Shahkar is the real proof),
 *  - a passport number (letter-first + alphanumerics, 5–15 chars — letter-first keeps malformed
 *    996-ish digit-leading junk in the national-ID branches where it belongs).
 * The three functions below funnel through classifyIdentityDoc exactly like the C# side funnels
 * through Classify, so the two sides can never drift apart. */
export type IdentityDocKind = "Unknown" | "IranianNationalId" | "ForeignResidentNationalId" | "Passport";

export function classifyIdentityDoc(input: string | null | undefined): IdentityDocKind {
  const s = toLatinDigits((input ?? "").trim()).toUpperCase();
  if (/^\d{10}$/.test(s)) {
    if (new Set(s).size === 1) return "Unknown"; // all-identical: checksum-valid but never real
    if (s.startsWith("996")) return "ForeignResidentNationalId";

    let sum = 0;
    for (let i = 0; i < 9; i++) {
      sum += Number(s[i]) * (10 - i);
    }
    const rem = sum % 11;
    const check = Number(s[9]);
    const ok = rem < 2 ? check === rem : check === 11 - rem;
    return ok ? "IranianNationalId" : "Unknown";
  }
  if (s.length >= 5 && s.length <= 15 && /^[A-Z][A-Z0-9]+$/.test(s)) {
    return "Passport";
  }
  return "Unknown";
}

export function isForeignResidentId(input: string | null | undefined): boolean {
  return classifyIdentityDoc(input) === "ForeignResidentNationalId";
}

export function isValidPassport(input: string | null | undefined): boolean {
  return classifyIdentityDoc(input) === "Passport";
}

export function isValidForeignNationalId(input: string | null | undefined): boolean {
  return classifyIdentityDoc(input) === "IranianNationalId" || classifyIdentityDoc(input) === "ForeignResidentNationalId";
}

/** Persian labels for the CustomerKind selector, shared by the issuance wizard and the standalone
 * new-customer form (the backend's [JsonStringEnumConverter] makes these exact wire strings). */
export const CUSTOMER_KIND_LABELS = {
  Iranian: "ایرانی",
  ForeignResident: "اتباع دارای کد ملی (سری ۹۹۶)",
  ForeignPassportOnly: "اتباع بدون کد ملی (پاسپورت)",
} as const;

export type CustomerKind = keyof typeof CUSTOMER_KIND_LABELS;
