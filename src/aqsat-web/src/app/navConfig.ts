import type { NavGroup } from "./types";

export const ICONS = {
  today: "M3 4h18v18H3zM16 2v4M8 2v4M3 10h18",
  doc: "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6",
  coin: "M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6",
  card: "M2 5h20v14H2zM2 10h20",
  chart: "M3 3v18h18M18 9l-5 5-3-3-4 4",
  msg: "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z",
  users:
    "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8M23 21v-2a4 4 0 0 0-3-3.87",
  gear: "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z",
  up: "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12",
} as const;

// Every item carries the permission its page's backend controller demands ([Authorize(Policy = ...)]
// on the driving endpoint) — the sidebar hides what the logged-in user cannot call, so a marketer
// whose only permission is Marketer.SelfView sees just their panel and "تغییر رمز عبور".
// "تغییر رمز عبور" stays ungated: it backs AuthController, which every logged-in user may call.
export const NAV: NavGroup[] = [
  {
    id: "desk",
    label: "میز کار",
    icon: ICONS.today,
    defaultOpen: true,
    items: [
      { navType: "today", title: "امروز", kind: "singleton", page: "today", pinned: true, requiresPermission: "Policy.Read" },
      {
        navType: "today-reminders", title: "یادآوری‌های امروز", kind: "singleton", page: "desk-feed",
        payload: { mode: "reminders" }, requiresPermission: "Policy.Read",
      },
      {
        navType: "overdue-tasks", title: "کارهای معوق", kind: "singleton", page: "desk-feed",
        payload: { mode: "overdue" }, requiresPermission: "Policy.Read",
      },
      {
        navType: "notifications", title: "اعلان‌ها", kind: "singleton", page: "desk-feed",
        payload: { mode: "notifications" }, requiresPermission: "Policy.Read",
      },
    ],
  },
  {
    id: "policies",
    label: "بیمه‌نامه‌ها",
    icon: ICONS.doc,
    items: [
      { navType: "policy-new", title: "ثبت بیمه‌نامه", kind: "multi-create", page: "new-policy", requiresPermission: "Policy.Write" },
      { navType: "policies-list", title: "فهرست بیمه‌نامه‌ها", kind: "singleton", page: "policy-list", requiresPermission: "Policy.Read" },
      {
        navType: "policies-pending", title: "در انتظار تأیید مشتری", kind: "singleton", page: "policy-list",
        payload: { status: "PendingConfirmation" }, requiresPermission: "Policy.Read",
      },
      {
        navType: "policies-cancelled", title: "باطل‌شده‌ها", kind: "singleton", page: "policy-list",
        payload: { status: "Cancelled" }, requiresPermission: "Policy.Read",
      },
      { navType: "policies-renewal", title: "سررسید تمدید", kind: "singleton", page: "renewal-watches", requiresPermission: "Policy.Read" },
    ],
  },
  {
    id: "collections",
    label: "اقساط و وصول",
    icon: ICONS.coin,
    items: [
      { navType: "installments-list", title: "فهرست اقساط", kind: "singleton", page: "installment-worklist", requiresPermission: "Policy.Read" },
      { navType: "installments-schedule", title: "در انتظار زمان‌بندی", kind: "singleton", page: "schedule-policy", requiresPermission: "Policy.Write" },
      {
        navType: "customer-statement", title: "صورت‌حساب مشتری", kind: "singleton", page: "customer-lookup",
        payload: { mode: "statement" }, requiresPermission: "Policy.Read",
      },
      {
        navType: "checks-all", title: "همهٔ چک‌ها", kind: "singleton", page: "cheques-list",
        requiresPermission: "Policy.Read",
      },
      {
        navType: "checks-upcoming", title: "چک‌های پیشِ رو", kind: "singleton", page: "cheques-list",
        payload: { upcomingDays: "30" }, requiresPermission: "Policy.Read",
      },
      {
        navType: "checks-bounced", title: "چک‌های برگشتی", kind: "singleton", page: "cheques-list",
        payload: { status: "Bounced" }, requiresPermission: "Policy.Read",
      },
    ],
  },
  {
    id: "collateral",
    label: "وثیقه و ضمانت",
    icon: ICONS.card,
    items: [
      { navType: "check-new", title: "ثبت چک صیادی", kind: "singleton", page: "collateral", payload: { type: "ChequeSayadi" }, requiresPermission: "Policy.Read" },
      { navType: "promissory-notes", title: "سفته‌ها", kind: "singleton", page: "collateral", payload: { type: "PromissoryNote" }, requiresPermission: "Policy.Read" },
    ],
  },
  {
    id: "customers",
    label: "مشتریان",
    icon: ICONS.users,
    items: [
      { navType: "customers-new", title: "مشتری جدید", kind: "multi-create", page: "new-customer", requiresPermission: "Policy.Write" },
      { navType: "customers-list", title: "فهرست مشتریان", kind: "singleton", page: "customers-list", requiresPermission: "Policy.Read" },
      {
        navType: "customer-payment-history", title: "سابقهٔ پرداخت", kind: "singleton", page: "customer-lookup",
        payload: { mode: "payments" }, requiresPermission: "Policy.Read",
      },
      { navType: "customers-high-risk", title: "مشتریان پرریسک", kind: "singleton", page: "high-risk-customers", requiresPermission: "Policy.Read" },
      { navType: "customer-completion", title: "تکمیل پروندهٔ مشتریان", kind: "singleton", page: "customer-completion", requiresPermission: "Policy.Read" },
    ],
  },
  {
    id: "cash-flow",
    label: "دریافت و پرداخت",
    icon: ICONS.coin,
    items: [
      { navType: "cash-flow-record-receipt", title: "ثبت دریافتی‌ها", kind: "singleton", page: "record-receipt", requiresPermission: "Payment.Write" },
      { navType: "cash-flow-expenses", title: "ثبت هزینه‌ها", kind: "singleton", page: "expenses", requiresPermission: "Finance.Read" },
      { navType: "cash-flow-balances", title: "موجودی و گردش صندوق و بانک", kind: "singleton", page: "cash-flow", requiresPermission: "Finance.Read" },
      { navType: "cash-flow-insurer-remittance", title: "پرداخت به بیمه‌گر", kind: "singleton", page: "insurer-remittance", requiresPermission: "Finance.Read" },
    ],
  },
  {
    id: "sms",
    label: "پیامک و اطلاع‌رسانی",
    icon: ICONS.msg,
    items: [
      { navType: "sms-outbox", title: "صندوق ارسال", kind: "singleton", page: "sms-outbox", requiresPermission: "Policy.Write" },
      { navType: "sms-templates", title: "قالب پیامک‌ها", kind: "singleton", page: "sms-templates", requiresPermission: "Settings.Write" },
      { navType: "sms-schedule", title: "زمان‌بندی یادآوری", kind: "singleton", page: "sms-reminders", requiresPermission: "Policy.Write" },
      { navType: "sms-delivery-report", title: "گزارش تحویل", kind: "singleton", page: "sms-delivery-report", requiresPermission: "Policy.Write" },
      { navType: "sms-effectiveness", title: "اثربخشی پیامک", kind: "singleton", page: "sms-effectiveness-report", requiresPermission: "Policy.Write" },
    ],
  },
  {
    id: "marketers",
    label: "بازاریاب‌ها",
    icon: ICONS.users,
    items: [
      { navType: "marketers-manage", title: "بازاریاب‌ها و پورسانت", kind: "singleton", page: "marketers", requiresPermission: "Marketer.Manage" },
      { navType: "marketer-panel", title: "پنل بازاریاب", kind: "singleton", page: "marketer-panel", requiresPermission: "Marketer.SelfView" },
    ],
  },
  {
    id: "risk",
    label: "اعتبار و ریسک",
    icon: ICONS.chart,
    items: [
      { navType: "risk-dashboard", title: "داشبورد ریسک", kind: "singleton", page: "risk-dashboard", requiresPermission: "Policy.Read" },
      { navType: "risk-assessment", title: "ارزیابی اعتبار", kind: "singleton", page: "risk-assessment", requiresPermission: "Policy.Read" },
      { navType: "risk-network", title: "استعلام شبکه‌ای", kind: "singleton", page: "risk-network", requiresPermission: "Risk.NetworkRead" },
      { navType: "risk-manual-reviews", title: "بررسی‌های دستی", kind: "singleton", page: "risk-manual-reviews", requiresPermission: "Policy.Read" },
      { navType: "risk-warnings", title: "هشدارها", kind: "singleton", page: "risk-warnings", requiresPermission: "Policy.Read" },
      { navType: "risk-settings", title: "قوانین اعتبارسنجی", kind: "singleton", page: "risk-settings", requiresPermission: "Settings.Write" },
    ],
  },
  {
    id: "reports",
    label: "گزارش‌ها",
    icon: ICONS.chart,
    items: [
      { navType: "reports-pnl", title: "سود و زیان", kind: "singleton", page: "pnl", requiresPermission: "Finance.Read" },
      { navType: "reports-aging", title: "سنی معوقات", kind: "singleton", page: "aging-report", requiresPermission: "Finance.Read" },
      { navType: "reports-receipts", title: "گزارش دریافتی‌ها", kind: "singleton", page: "receipts-report", requiresPermission: "Report.Read" },
      { navType: "reports-expenses", title: "گزارش هزینه‌ها", kind: "singleton", page: "expenses", requiresPermission: "Finance.Read" },
      { navType: "reports-insurer-remittance", title: "گزارش پرداخت به بیمه‌گر", kind: "singleton", page: "insurer-remittance", requiresPermission: "Finance.Read" },
      { navType: "reports-missing-serials", title: "شماره‌های جا افتاده", kind: "singleton", page: "missing-serials", requiresPermission: "Policy.Read" },
      { navType: "reports-period-collection", title: "وصولی‌های دوره", kind: "singleton", page: "collections-report", requiresPermission: "Report.Read" },
      { navType: "reports-ontime-rate", title: "نرخ وصول به‌موقع", kind: "singleton", page: "collections-report", requiresPermission: "Report.Read" },
      { navType: "reports-default-analysis", title: "تحلیل نکول", kind: "singleton", page: "collections-report", requiresPermission: "Report.Read" },
      { navType: "reports-export", title: "خروجی اکسل", kind: "singleton", page: "collections-report", requiresPermission: "Report.Read" },
    ],
  },
  {
    id: "import",
    label: "ورود اطلاعات",
    icon: ICONS.up,
    items: [
      { navType: "import-fanavaran", title: "آپلود فایل فناوران", kind: "singleton", page: "import-fanavaran", requiresPermission: "Import.Run" },
      { navType: "import-contract-templates", title: "تنظیم قراردادهای اقساطی", kind: "singleton", page: "contract-templates", requiresPermission: "Policy.Write" },
      { navType: "import-history", title: "تاریخچهٔ ورود داده", kind: "singleton", page: "import-history", requiresPermission: "Import.Run" },
      { navType: "import-mismatches", title: "رکوردهای ناسازگار", kind: "singleton", page: "import-mismatches", requiresPermission: "Import.Run" },
    ],
  },
  {
    id: "monitoring",
    label: "پایش و امنیت",
    icon: ICONS.chart,
    items: [
      {
        navType: "monitoring-dashboard", title: "پایش سامانه", kind: "singleton", page: "monitoring-dashboard",
        requiresPermission: "Platform.Owner",
      },
      {
        navType: "security-dashboard", title: "امنیت سامانه", kind: "singleton", page: "security-dashboard",
        requiresPermission: "Platform.Owner",
      },
      {
        navType: "alerts-rules", title: "هشدارها و قوانین", kind: "singleton", page: "alerts-rules",
        requiresPermission: "Platform.Owner",
      },
      {
        navType: "monitoring-logs", title: "لاگ‌های سامانه", kind: "singleton", page: "monitoring-logs",
        requiresPermission: "Platform.Owner",
      },
    ],
  },
  {
    id: "settings",
    label: "تنظیمات",
    icon: ICONS.gear,
    items: [
      { navType: "settings-agency", title: "مشخصات نمایندگی", kind: "singleton", page: "agency-settings", requiresPermission: "Settings.Write" },
      { navType: "settings-payment", title: "تنظیمات درگاه پرداخت", kind: "singleton", page: "agency-payment-settings", requiresPermission: "Settings.Write" },
      { navType: "settings-sms-panel", title: "تنظیمات پنل پیامکی", kind: "singleton", page: "agency-sms-settings", requiresPermission: "Settings.Write" },
      { navType: "settings-policy-number", title: "کدهای بیمه‌نامه", kind: "singleton", page: "policy-number-settings", requiresPermission: "Settings.Write" },
      { navType: "settings-cash-and-bank", title: "صندوق و بانک‌ها", kind: "singleton", page: "cash-and-bank-settings", requiresPermission: "Settings.Write" },
      { navType: "settings-agency-commission", title: "کارمزد از بیمه‌گر", kind: "singleton", page: "agency-commission-settings", requiresPermission: "Settings.Write" },
      { navType: "settings-expense-categories", title: "دسته‌بندی هزینه‌ها", kind: "singleton", page: "expense-category-settings", requiresPermission: "Settings.Write" },
      { navType: "settings-users", title: "کاربران و دسترسی‌ها", kind: "singleton", page: "settings-users", requiresPermission: "Settings.Write" },
      { navType: "change-password", title: "تغییر رمز عبور", kind: "singleton", page: "change-password" },
      { navType: "settings-activity-log", title: "لاگ فعالیت", kind: "singleton", page: "settings-audit-log", requiresPermission: "Settings.Write" },
      {
        navType: "agencies-management", title: "نمایندگی‌ها", kind: "singleton", page: "agencies-management",
        requiresPermission: "Platform.Owner",
      },
      {
        navType: "role-management", title: "مدیریت نقش‌ها", kind: "singleton", page: "role-management",
        requiresPermission: "Platform.Owner",
      },
      {
        navType: "platform-updates", title: "به‌روزرسانی سیستم", kind: "singleton", page: "platform-updates",
        requiresPermission: "Platform.Owner",
      },
      {
        navType: "apiir-settings", title: "تنظیمات پنل پیامکی (api.ir)", kind: "singleton", page: "apiir-settings",
        requiresPermission: "Platform.Owner",
      },
      {
        navType: "payment-settings", title: "درگاه پرداخت مالک", kind: "singleton", page: "payment-settings",
        requiresPermission: "Platform.Owner",
      },
    ],
  },
];

export const MENU = NAV.flatMap((g) => g.items);
