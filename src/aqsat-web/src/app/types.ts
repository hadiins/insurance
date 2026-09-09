export type TabKind = "singleton" | "multi-create" | "multi-record";

export type PageKind =
  | "today"
  | "new-policy"
  | "customer-file"
  | "policy-file"
  | "customers-list"
  | "new-customer"
  | "import-fanavaran"
  | "contract-templates"
  | "marketers"
  | "marketer-panel"
  | "sms-reminders"
  | "pnl"
  | "renewal-watches"
  | "collateral"
  | "collections-report"
  | "receipts-report"
  | "expenses"
  | "expense-category-settings"
  | "record-receipt"
  | "insurer-remittance"
  | "missing-serials"
  | "policy-number-settings"
  | "cash-and-bank-settings"
  | "agency-commission-settings"
  | "customer-completion"
  | "schedule-policy"
  | "platform-updates"
  | "apiir-settings"
  | "payment-settings"
  | "agency-payment-settings"
  | "agency-sms-settings"
  | "agency-settings"
  | "desk-feed"
  | "policy-list"
  | "installment-worklist"
  | "customer-lookup"
  | "import-history"
  | "import-mismatches"
  | "settings-users"
  | "settings-audit-log"
  | "high-risk-customers"
  | "sms-outbox"
  | "sms-templates"
  | "sms-delivery-report"
  | "sms-effectiveness-report"
  | "role-management"
  | "agencies-management"
  | "agency-profile"
  | "change-password"
  | "cheques-list"
  | "cash-flow"
  | "aging-report"
  | "risk-dashboard"
  | "risk-assessment"
  | "risk-network"
  | "risk-manual-reviews"
  | "risk-warnings"
  | "risk-settings"
  | "monitoring-dashboard"
  | "monitoring-logs"
  | "security-dashboard"
  | "alerts-rules"
  | "blank";

export interface NavItem {
  navType: string;
  title: string;
  kind: TabKind;
  page: PageKind;
  pinned?: boolean;
  payload?: unknown;
  /** Hidden from the sidebar entirely unless the logged-in user's permissions include this —
   * e.g. "Platform.Owner" (docs/UPDATE-SYSTEM.md rule 1: agency users must never even see this
   * page exists, not just be blocked from using it). */
  requiresPermission?: string;
}

export interface NavGroup {
  id: string;
  label: string;
  icon: string;
  defaultOpen?: boolean;
  items: NavItem[];
}

export interface OpenTab {
  key: string;
  navType: string;
  page: PageKind;
  title: string;
  pinned: boolean;
  dirty: boolean;
  payload?: unknown;
}

export interface OpenTabRequest {
  navType: string;
  page: PageKind;
  kind: TabKind;
  title: string;
  pinned?: boolean;
  recordId?: string;
  payload?: unknown;
}
