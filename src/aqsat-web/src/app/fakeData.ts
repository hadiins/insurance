export interface FakeInstallmentRow {
  id: string;
  status: "late" | "due" | "ok";
  label: string;
  amount: number;
  progress: string;
}

export const ROWS: FakeInstallmentRow[] = [
  { id: "۸۰۳۰۹۸۷", status: "late", label: "۳۸ روز تأخیر", amount: 8_200_000, progress: "۳ از ۴" },
  { id: "۱۰۱۳۸۸۹۳", status: "late", label: "۱۱ روز تأخیر", amount: 6_500_000, progress: "۲ از ۴" },
  { id: "۹۱۸۵۳۵۶", status: "due", label: "سررسید امروز", amount: 9_100_000, progress: "۱ از ۳" },
  { id: "۷۷۴۲۰۱۵", status: "due", label: "سررسید امروز", amount: 7_300_000, progress: "۲ از ۴" },
  { id: "۵۵۹۰۳۳۴", status: "ok", label: "۴ روز مانده", amount: 8_100_000, progress: "۴ از ۴" },
];
