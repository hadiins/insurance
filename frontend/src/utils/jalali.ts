import { DateObject } from "react-multi-date-picker";
import persian from "react-date-object/calendars/persian";
import persian_fa from "react-date-object/locales/persian_fa";
import gregorian from "react-date-object/calendars/gregorian";

export function toJalaliDisplay(isoDate: string | null | undefined): string {
  if (!isoDate) return "-";
  const jalali = new DateObject({
    date: isoDate,
    format: "YYYY-MM-DD",
    calendar: gregorian,
  }).convert(persian, persian_fa);
  return jalali.format("YYYY/MM/DD");
}
