import { lazy, Suspense } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import type { OpenTab } from "../../app/types";
import { TodayPage } from "../today/TodayPage";
import { BlankPage } from "../placeholder/BlankPage";
import { TabKeyProvider } from "./TabContext";

/// Feature #8 — code-splitting. Every page behind the MDI shell is its own dynamic chunk, loaded
/// on first open of a tab: the login shell (tab bar, sidebar, «امروز») stays small so the app
/// paints fast, and a 60-page feature like the risk suite or reports never delays a login. Tabs
/// stay mounted once loaded (the switch only swaps which tree renders), so lazy-loading costs
/// nothing on tab switches after the first. The fallback is intentionally tiny — a centered line,
/// not a skeleton — because the chunks load once and a flash of fake content would be worse than
/// a moment of honest blank.
///
/// TodayPage is NOT lazy: it is the pinned landing tab every session opens, and preloading it
/// into the entry chunk removes the flash every user would otherwise see on login.
const pages: Record<Exclude<import("../../app/types").PageKind, "today" | "blank">, React.LazyExoticComponent<React.ComponentType>> = {
  "new-policy": lazy(() => import("../policies/NewPolicyPage").then((m) => ({ default: m.NewPolicyPage }))),
  "customer-file": lazy(() => import("../customers/CustomerFilePage").then((m) => ({ default: m.CustomerFilePage }))),
  "customers-list": lazy(() => import("../customers/CustomersListPage").then((m) => ({ default: m.CustomersListPage }))),
  "policy-file": lazy(() => import("../policies/PolicyFilePage").then((m) => ({ default: m.PolicyFilePage }))),
  "import-fanavaran": lazy(() => import("../imports/FanavaranImportPage").then((m) => ({ default: m.FanavaranImportPage }))),
  "contract-templates": lazy(() => import("../imports/ContractTemplatesPage").then((m) => ({ default: m.ContractTemplatesPage }))),
  marketers: lazy(() => import("../marketers/MarketersPage").then((m) => ({ default: m.MarketersPage }))),
  "marketer-panel": lazy(() => import("../marketers/MarketerPanelPage").then((m) => ({ default: m.MarketerPanelPage }))),
  "sms-reminders": lazy(() => import("../sms/SmsReminderPage").then((m) => ({ default: m.SmsReminderPage }))),
  pnl: lazy(() => import("../reports/PnlPage").then((m) => ({ default: m.PnlPage }))),
  "missing-serials": lazy(() => import("../reports/MissingSerialsPage").then((m) => ({ default: m.MissingSerialsPage }))),
  "renewal-watches": lazy(() => import("../renewals/RenewalWatchesPage").then((m) => ({ default: m.RenewalWatchesPage }))),
  collateral: lazy(() => import("../collateral/CollateralPage").then((m) => ({ default: m.CollateralPage }))),
  "collections-report": lazy(() => import("../reports/CollectionsReportPage").then((m) => ({ default: m.CollectionsReportPage }))),
  "receipts-report": lazy(() => import("../reports/ReceiptsReportPage").then((m) => ({ default: m.ReceiptsReportPage }))),
  expenses: lazy(() => import("../reports/ExpensesPage").then((m) => ({ default: m.ExpensesPage }))),
  "expense-category-settings": lazy(() => import("../settings/ExpenseCategorySettingsPage").then((m) => ({ default: m.ExpenseCategorySettingsPage }))),
  "record-receipt": lazy(() => import("../policies/RecordReceiptPage").then((m) => ({ default: m.RecordReceiptPage }))),
  "insurer-remittance": lazy(() => import("../reports/InsurerRemittancePage").then((m) => ({ default: m.InsurerRemittancePage }))),
  "aging-report": lazy(() => import("../reports/AgingReportPage").then((m) => ({ default: m.AgingReportPage }))),
  "cash-flow": lazy(() => import("../cashflow/CashFlowPage").then((m) => ({ default: m.CashFlowPage }))),
  "platform-updates": lazy(() => import("../platform/PlatformUpdatesPage").then((m) => ({ default: m.PlatformUpdatesPage }))),
  "apiir-settings": lazy(() => import("../platform/ApiIrSettingsPage").then((m) => ({ default: m.ApiIrSettingsPage }))),
  "payment-settings": lazy(() => import("../platform/PaymentSettingsPage").then((m) => ({ default: m.PaymentSettingsPage }))),
  "agency-settings": lazy(() => import("../settings/AgencySettingsPage").then((m) => ({ default: m.AgencySettingsPage }))),
  "agency-payment-settings": lazy(() => import("../settings/AgencyPaymentSettingsPage").then((m) => ({ default: m.AgencyPaymentSettingsPage }))),
  "agency-sms-settings": lazy(() => import("../settings/AgencySmsSettingsPage").then((m) => ({ default: m.AgencySmsSettingsPage }))),
  "policy-number-settings": lazy(() => import("../settings/PolicyNumberSettingsPage").then((m) => ({ default: m.PolicyNumberSettingsPage }))),
  "cash-and-bank-settings": lazy(() => import("../settings/CashAndBankSettingsPage").then((m) => ({ default: m.CashAndBankSettingsPage }))),
  "agency-commission-settings": lazy(() => import("../settings/AgencyCommissionRateSettingsPage").then((m) => ({ default: m.AgencyCommissionRateSettingsPage }))),
  "customer-completion": lazy(() => import("../customers/CustomerCompletionPage").then((m) => ({ default: m.CustomerCompletionPage }))),
  "schedule-policy": lazy(() => import("../policies/SchedulePolicyPage").then((m) => ({ default: m.SchedulePolicyPage }))),
  "desk-feed": lazy(() => import("../desk/DeskFeedPage").then((m) => ({ default: m.DeskFeedPage }))),
  "policy-list": lazy(() => import("../policies/PoliciesListPage").then((m) => ({ default: m.PoliciesListPage }))),
  "installment-worklist": lazy(() => import("../installments/InstallmentsWorklistPage").then((m) => ({ default: m.InstallmentsWorklistPage }))),
  "cheques-list": lazy(() => import("../cheques/ChequesListPage").then((m) => ({ default: m.ChequesListPage }))),
  "customer-lookup": lazy(() => import("../customers/CustomerLookupPage").then((m) => ({ default: m.CustomerLookupPage }))),
  "new-customer": lazy(() => import("../customers/NewCustomerPage").then((m) => ({ default: m.NewCustomerPage }))),
  "import-history": lazy(() => import("../imports/ImportHistoryPage").then((m) => ({ default: m.ImportHistoryPage }))),
  "import-mismatches": lazy(() => import("../imports/ImportMismatchesPage").then((m) => ({ default: m.ImportMismatchesPage }))),
  "settings-users": lazy(() => import("../settings/UsersPage").then((m) => ({ default: m.UsersPage }))),
  "settings-audit-log": lazy(() => import("../settings/AuditLogPage").then((m) => ({ default: m.AuditLogPage }))),
  "high-risk-customers": lazy(() => import("../customers/HighRiskCustomersPage").then((m) => ({ default: m.HighRiskCustomersPage }))),
  "risk-assessment": lazy(() => import("../risk/CreditAssessmentPage").then((m) => ({ default: m.CreditAssessmentPage }))),
  "risk-network": lazy(() => import("../risk/NetworkRiskPage").then((m) => ({ default: m.NetworkRiskPage }))),
  "risk-dashboard": lazy(() => import("../risk/RiskDashboardPage").then((m) => ({ default: m.RiskDashboardPage }))),
  "risk-manual-reviews": lazy(() => import("../risk/ManualReviewsPage").then((m) => ({ default: m.ManualReviewsPage }))),
  "risk-warnings": lazy(() => import("../risk/RiskWarningsPage").then((m) => ({ default: m.RiskWarningsPage }))),
  "risk-settings": lazy(() => import("../risk/RiskSettingsPage").then((m) => ({ default: m.RiskSettingsPage }))),
  "sms-outbox": lazy(() => import("../sms/SmsOutboxPage").then((m) => ({ default: m.SmsOutboxPage }))),
  "sms-templates": lazy(() => import("../sms/SmsTemplatesPage").then((m) => ({ default: m.SmsTemplatesPage }))),
  "sms-delivery-report": lazy(() => import("../sms/SmsDeliveryReportPage").then((m) => ({ default: m.SmsDeliveryReportPage }))),
  "sms-effectiveness-report": lazy(() => import("../sms/SmsEffectivenessReportPage").then((m) => ({ default: m.SmsEffectivenessReportPage }))),
  "role-management": lazy(() => import("../platform/RoleManagementPage").then((m) => ({ default: m.RoleManagementPage }))),
  "agencies-management": lazy(() => import("../platform/AgenciesManagementPage").then((m) => ({ default: m.AgenciesManagementPage }))),
  "agency-profile": lazy(() => import("../platform/AgencyProfilePage").then((m) => ({ default: m.AgencyProfilePage }))),
  "change-password": lazy(() => import("../settings/ChangePasswordPage").then((m) => ({ default: m.ChangePasswordPage }))),
  "monitoring-dashboard": lazy(() => import("../monitoring/MonitoringDashboardPage").then((m) => ({ default: m.MonitoringDashboardPage }))),
  "monitoring-logs": lazy(() => import("../monitoring/MonitoringLogsPage").then((m) => ({ default: m.MonitoringLogsPage }))),
  "security-dashboard": lazy(() => import("../monitoring/SecurityDashboardPage").then((m) => ({ default: m.SecurityDashboardPage }))),
  "alerts-rules": lazy(() => import("../monitoring/AlertsRulesPage").then((m) => ({ default: m.AlertsRulesPage }))),
};

function renderPage(tab: OpenTab) {
  switch (tab.page) {
    case "today":
      return <TodayPage />;
    case "blank":
      return <BlankPage title={tab.title} />;
    default: {
      const Page = pages[tab.page];
      return (
        <Suspense fallback={<div className="grid h-full place-items-center text-(--ice-3)">در حال بارگذاری…</div>}>
          <Page />
        </Suspense>
      );
    }
  }
}

export function Stage() {
  const tabs = useTabsStore((s) => s.tabs);
  const activeKey = useTabsStore((s) => s.activeKey);

  if (tabs.length === 0) {
    return (
      <div className="relative grid flex-1 place-items-center bg-(--void) text-center text-(--ice-3)">
        <div>
          <b className="mb-1 block text-base font-bold text-(--ice-2)">تبی باز نیست</b>
          از نوار بالا یکی را باز کنید
        </div>
      </div>
    );
  }

  return (
    <div className="relative flex-1 overflow-hidden bg-(--void)">
      {tabs.map((tab) => (
        <div
          key={tab.key}
          hidden={tab.key !== activeKey}
          className="absolute inset-0 overflow-auto px-6 pt-5.5 pb-10"
        >
          <TabKeyProvider tabKey={tab.key} active={tab.key === activeKey}>
            {renderPage(tab)}
          </TabKeyProvider>
        </div>
      ))}
    </div>
  );
}
