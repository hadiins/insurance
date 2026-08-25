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

export const NAV: NavGroup[] = [
  {
    id: "desk",
    label: "میز کار",
    icon: ICONS.today,
    defaultOpen: true,
    items: [
      { navType: "today", title: "امروز", kind: "singleton", page: "today", pinned: true },
      {
        navType: "today-reminders", title: "یادآوری‌های امروز", kind: "singleton", page: "desk-feed",
        payload: { mode: "reminders" },
      },
      {
        navType: "overdue-tasks", title: "کارهای معوق", kind: "singleton", page: "desk-feed",
        payload: { mode: "overdue" },
      },
      {
        navType: "notifications", title: "اعلان‌ها", kind: "singleton", page: "desk-feed",
        payload: { mode: "notifications" },
      },
    ],
  },
  {
    id: "policies",
    label: "بیمه‌نامه‌ها",
    icon: ICONS.doc,
    items: [
      { navType: "policy-new", title: "ثبت بیمه‌نامه", kind: "multi-create", page: "new-policy" },
      { navType: "policies-list", title: "فهرست بیمه‌نامه‌ها", kind: "singleton", page: "policy-list" },
      {
        navType: "policies-installment", title: "بیمه‌نامه‌های اقساطی", kind: "singleton", page: "policy-list",
        payload: { isInstallment: true },
      },
      {
        navType: "policies-pending", title: "در انتظار تأیید مشتری", kind: "singleton", page: "policy-list",
        payload: { status: "PendingConfirmation" },
      },
      {
        navType: "policies-cancelled", title: "باطل‌شده‌ها", kind: "singleton", page: "policy-list",
        payload: { status: "Cancelled" },
      },
      { navType: "policies-renewal", title: "سررسید تمدید", kind: "singleton", page: "renewal-watches" },
    ],
  },
  {
    id: "collections",
    label: "اقساط و وصول",
    icon: ICONS.coin,
    items: [
      { navType: "installments-list", title: "فهرست اقساط", kind: "singleton", page: "installment-worklist" },
      { navType: "installments-schedule", title: "در انتظار زمان‌بندی", kind: "singleton", page: "schedule-policy" },
      {
        navType: "installments-overdue", title: "اقساط معوق", kind: "singleton", page: "installment-worklist",
        payload: { overdueOnly: true },
      },
      { navType: "payment-record", title: "ثبت پرداخت", kind: "singleton", page: "payment-record" },
      { navType: "payment-online", title: "پرداخت‌های آنلاین", kind: "singleton", page: "blank" },
      {
        navType: "settlement-partial", title: "تسویه‌های جزئی", kind: "singleton", page: "installment-worklist",
        payload: { status: "Partial" },
      },
      {
        navType: "customer-statement", title: "صورت‌حساب مشتری", kind: "singleton", page: "customer-lookup",
        payload: { mode: "statement" },
      },
    ],
  },
  {
    id: "collateral",
    label: "وثیقه و چک",
    icon: ICONS.card,
    items: [
      {
        navType: "checks-upcoming", title: "چک‌های پیشِ رو", kind: "singleton", page: "collateral",
        payload: { type: "ChequeSayadi", upcomingDays: "30" },
      },
      {
        navType: "checks-bounced", title: "چک‌های برگشتی", kind: "singleton", page: "collateral",
        payload: { type: "ChequeSayadi", status: "Bounced" },
      },
      { navType: "check-new", title: "ثبت چک صیادی", kind: "singleton", page: "collateral", payload: { type: "ChequeSayadi" } },
      { navType: "promissory-notes", title: "سفته‌ها", kind: "singleton", page: "collateral", payload: { type: "PromissoryNote" } },
      { navType: "legal-notice", title: "اظهارنامه و پیگیری حقوقی", kind: "singleton", page: "blank" },
    ],
  },
  {
    id: "customers",
    label: "مشتریان",
    icon: ICONS.users,
    items: [
      { navType: "customers-list", title: "فهرست مشتریان", kind: "singleton", page: "customers-list" },
      {
        navType: "customer-payment-history", title: "سابقهٔ پرداخت", kind: "singleton", page: "customer-lookup",
        payload: { mode: "payments" },
      },
      { navType: "customer-consents", title: "رضایت‌نامه‌های ثبت‌شده", kind: "singleton", page: "blank" },
      { navType: "customers-high-risk", title: "مشتریان پرریسک", kind: "singleton", page: "high-risk-customers" },
      { navType: "customer-completion", title: "تکمیل پروندهٔ مشتریان", kind: "singleton", page: "customer-completion" },
    ],
  },
  {
    id: "sms",
    label: "پیامک و اطلاع‌رسانی",
    icon: ICONS.msg,
    items: [
      { navType: "sms-outbox", title: "صندوق ارسال", kind: "singleton", page: "sms-outbox" },
      { navType: "sms-templates", title: "قالب پیامک‌ها", kind: "singleton", page: "sms-templates" },
      { navType: "sms-schedule", title: "زمان‌بندی یادآوری", kind: "singleton", page: "sms-reminders" },
      { navType: "sms-delivery-report", title: "گزارش تحویل", kind: "singleton", page: "sms-delivery-report" },
      { navType: "sms-credit", title: "اعتبار پنل", kind: "singleton", page: "blank" },
    ],
  },
  {
    id: "marketers",
    label: "بازاریاب‌ها",
    icon: ICONS.users,
    items: [
      { navType: "marketers-manage", title: "بازاریاب‌ها و پورسانت", kind: "singleton", page: "marketers" },
      { navType: "marketer-panel", title: "پنل بازاریاب", kind: "singleton", page: "marketer-panel" },
    ],
  },
  {
    id: "reports",
    label: "گزارش‌ها",
    icon: ICONS.chart,
    items: [
      { navType: "reports-pnl", title: "سود و زیان", kind: "singleton", page: "pnl" },
      { navType: "reports-receipts", title: "گزارش دریافتی‌ها", kind: "singleton", page: "receipts-report" },
      { navType: "reports-expenses", title: "گزارش هزینه‌ها", kind: "singleton", page: "expenses" },
      { navType: "reports-insurer-remittance", title: "گزارش پرداخت به بیمه‌گر", kind: "singleton", page: "insurer-remittance" },
      { navType: "reports-missing-serials", title: "شماره‌های جا افتاده", kind: "singleton", page: "missing-serials" },
      { navType: "reports-builder", title: "گزارش‌گیری", kind: "singleton", page: "blank" },
      { navType: "reports-period-collection", title: "وصولی‌های دوره", kind: "singleton", page: "collections-report" },
      { navType: "reports-ontime-rate", title: "نرخ وصول به‌موقع", kind: "singleton", page: "collections-report" },
      { navType: "reports-default-analysis", title: "تحلیل نکول", kind: "singleton", page: "collections-report" },
      { navType: "reports-network-compare", title: "مقایسه با میانگین شبکه", kind: "singleton", page: "blank" },
      { navType: "reports-export", title: "خروجی اکسل", kind: "singleton", page: "collections-report" },
    ],
  },
  {
    id: "cash-flow",
    label: "دریافت و پرداخت",
    icon: ICONS.coin,
    items: [
      { navType: "cash-flow-record-receipt", title: "ثبت دریافتی‌ها", kind: "singleton", page: "record-receipt" },
      { navType: "cash-flow-expenses", title: "ثبت هزینه‌ها", kind: "singleton", page: "expenses" },
      { navType: "cash-flow-insurer-remittance", title: "پرداخت به بیمه‌گر", kind: "singleton", page: "insurer-remittance" },
    ],
  },
  {
    id: "import",
    label: "ورود اطلاعات",
    icon: ICONS.up,
    items: [
      { navType: "import-fanavaran", title: "آپلود فایل فناوران", kind: "singleton", page: "import-fanavaran" },
      { navType: "import-contract-templates", title: "تنظیم قراردادهای اقساطی", kind: "singleton", page: "contract-templates" },
      { navType: "import-history", title: "تاریخچهٔ ورود داده", kind: "singleton", page: "import-history" },
      { navType: "import-mismatches", title: "رکوردهای ناسازگار", kind: "singleton", page: "import-mismatches" },
    ],
  },
  {
    id: "settings",
    label: "تنظیمات",
    icon: ICONS.gear,
    items: [
      { navType: "settings-agency", title: "مشخصات نمایندگی", kind: "singleton", page: "agency-settings" },
      { navType: "settings-policy-number", title: "کدهای بیمه‌نامه", kind: "singleton", page: "policy-number-settings" },
      { navType: "settings-cash-and-bank", title: "صندوق و بانک‌ها", kind: "singleton", page: "cash-and-bank-settings" },
      { navType: "settings-agency-commission", title: "کارمزد از بیمه‌گر", kind: "singleton", page: "agency-commission-settings" },
      { navType: "settings-expense-categories", title: "دسته‌بندی هزینه‌ها", kind: "singleton", page: "expense-category-settings" },
      { navType: "settings-users", title: "کاربران و دسترسی‌ها", kind: "singleton", page: "settings-users" },
      { navType: "change-password", title: "تغییر رمز عبور", kind: "singleton", page: "change-password" },
      { navType: "settings-payment-gateway", title: "درگاه پرداخت", kind: "singleton", page: "blank" },
      { navType: "settings-sms-panel", title: "پنل پیامک", kind: "singleton", page: "blank" },
      { navType: "settings-activity-log", title: "لاگ فعالیت", kind: "singleton", page: "settings-audit-log" },
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
    ],
  },
];

export const MENU = NAV.flatMap((g) => g.items);
