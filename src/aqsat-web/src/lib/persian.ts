const FA_DIGITS = ["۰", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹"];
const AR_DIGITS = ["٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"];

export function fa(n: number | string): string {
  return String(n).replace(/\d/g, (d) => FA_DIGITS[Number(d)]);
}

export function money(n: number): string {
  return fa(Math.round(n).toLocaleString("en-US")).replace(/,/g, "٬");
}

/** Persian/Arabic-Indic digits → Latin — mirrors Aqsat.Application.Common.DigitNormalizer. */
export function toLatinDigits(input: string): string {
  return input.replace(/[۰-۹٠-٩]/g, (d) => {
    const faIdx = FA_DIGITS.indexOf(d);
    if (faIdx >= 0) return String(faIdx);
    return String(AR_DIGITS.indexOf(d));
  });
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
