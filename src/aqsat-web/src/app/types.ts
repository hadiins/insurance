export type TabKind = "singleton" | "multi-create" | "multi-record";

export type PageKind =
  | "today"
  | "new-policy"
  | "installment-list"
  | "customer-file"
  | "policy-file"
  | "customers-list"
  | "import-fanavaran"
  | "contract-templates"
  | "marketers"
  | "marketer-panel"
  | "sms-reminders"
  | "pnl"
  | "renewal-watches"
  | "blank";

export interface NavItem {
  navType: string;
  title: string;
  kind: TabKind;
  page: PageKind;
  pinned?: boolean;
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
