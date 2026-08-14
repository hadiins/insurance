const FA_DIGITS = ["۰", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹"];

export function fa(n: number | string): string {
  return String(n).replace(/\d/g, (d) => FA_DIGITS[Number(d)]);
}

export function money(n: number): string {
  return fa(Math.round(n).toLocaleString("en-US")).replace(/,/g, "٬");
}
