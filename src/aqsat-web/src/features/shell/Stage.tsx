import { useTabsStore } from "../../app/store/tabsStore";
import type { OpenTab } from "../../app/types";
import { TodayPage } from "../today/TodayPage";
import { NewPolicyPage } from "../policies/NewPolicyPage";
import { CustomerFilePage } from "../customers/CustomerFilePage";
import { CustomersListPage } from "../customers/CustomersListPage";
import { PolicyFilePage } from "../policies/PolicyFilePage";
import { FanavaranImportPage } from "../imports/FanavaranImportPage";
import { ContractTemplatesPage } from "../imports/ContractTemplatesPage";
import { MarketersPage } from "../marketers/MarketersPage";
import { MarketerPanelPage } from "../marketers/MarketerPanelPage";
import { SmsReminderPage } from "../sms/SmsReminderPage";
import { PnlPage } from "../reports/PnlPage";
import { MissingSerialsPage } from "../reports/MissingSerialsPage";
import { RenewalWatchesPage } from "../renewals/RenewalWatchesPage";
import { CollateralPage } from "../collateral/CollateralPage";
import { CollectionsReportPage } from "../reports/CollectionsReportPage";
import { ReceiptsReportPage } from "../reports/ReceiptsReportPage";
import { ExpensesPage } from "../reports/ExpensesPage";
import { ExpenseCategorySettingsPage } from "../settings/ExpenseCategorySettingsPage";
import { RecordReceiptPage } from "../policies/RecordReceiptPage";
import { InsurerRemittancePage } from "../reports/InsurerRemittancePage";
import { AgingReportPage } from "../reports/AgingReportPage";
import { CashFlowPage } from "../cashflow/CashFlowPage";
import { PlatformUpdatesPage } from "../platform/PlatformUpdatesPage";
import { ApiIrSettingsPage } from "../platform/ApiIrSettingsPage";
import { PaymentSettingsPage } from "../platform/PaymentSettingsPage";
import { AgencySettingsPage } from "../settings/AgencySettingsPage";
import { AgencyPaymentSettingsPage } from "../settings/AgencyPaymentSettingsPage";
import { AgencySmsSettingsPage } from "../settings/AgencySmsSettingsPage";
import { PolicyNumberSettingsPage } from "../settings/PolicyNumberSettingsPage";
import { CashAndBankSettingsPage } from "../settings/CashAndBankSettingsPage";
import { AgencyCommissionRateSettingsPage } from "../settings/AgencyCommissionRateSettingsPage";
import { CustomerCompletionPage } from "../customers/CustomerCompletionPage";
import { SchedulePolicyPage } from "../policies/SchedulePolicyPage";
import { DeskFeedPage } from "../desk/DeskFeedPage";
import { PoliciesListPage } from "../policies/PoliciesListPage";
import { InstallmentsWorklistPage } from "../installments/InstallmentsWorklistPage";
import { ChequesListPage } from "../cheques/ChequesListPage";
import { CustomerLookupPage } from "../customers/CustomerLookupPage";
import { ImportHistoryPage } from "../imports/ImportHistoryPage";
import { ImportMismatchesPage } from "../imports/ImportMismatchesPage";
import { UsersPage } from "../settings/UsersPage";
import { AuditLogPage } from "../settings/AuditLogPage";
import { HighRiskCustomersPage } from "../customers/HighRiskCustomersPage";
import { SmsOutboxPage } from "../sms/SmsOutboxPage";
import { SmsTemplatesPage } from "../sms/SmsTemplatesPage";
import { SmsDeliveryReportPage } from "../sms/SmsDeliveryReportPage";
import { RoleManagementPage } from "../platform/RoleManagementPage";
import { AgenciesManagementPage } from "../platform/AgenciesManagementPage";
import { AgencyProfilePage } from "../platform/AgencyProfilePage";
import { ChangePasswordPage } from "../settings/ChangePasswordPage";
import { BlankPage } from "../placeholder/BlankPage";
import { TabKeyProvider } from "./TabContext";

function renderPage(tab: OpenTab) {
  switch (tab.page) {
    case "today":
      return <TodayPage />;
    case "new-policy":
      return <NewPolicyPage />;
    case "customer-file":
      return <CustomerFilePage />;
    case "policy-file":
      return <PolicyFilePage />;
    case "customers-list":
      return <CustomersListPage />;
    case "import-fanavaran":
      return <FanavaranImportPage />;
    case "contract-templates":
      return <ContractTemplatesPage />;
    case "marketers":
      return <MarketersPage />;
    case "marketer-panel":
      return <MarketerPanelPage />;
    case "sms-reminders":
      return <SmsReminderPage />;
    case "pnl":
      return <PnlPage />;
    case "missing-serials":
      return <MissingSerialsPage />;
    case "renewal-watches":
      return <RenewalWatchesPage />;
    case "collateral":
      return <CollateralPage />;
    case "collections-report":
      return <CollectionsReportPage />;
    case "receipts-report":
      return <ReceiptsReportPage />;
    case "expenses":
      return <ExpensesPage />;
    case "expense-category-settings":
      return <ExpenseCategorySettingsPage />;
    case "record-receipt":
      return <RecordReceiptPage />;
    case "insurer-remittance":
      return <InsurerRemittancePage />;
    case "aging-report":
      return <AgingReportPage />;
    case "cash-flow":
      return <CashFlowPage />;
    case "platform-updates":
      return <PlatformUpdatesPage />;
    case "apiir-settings":
      return <ApiIrSettingsPage />;
    case "payment-settings":
      return <PaymentSettingsPage />;
    case "agency-settings":
      return <AgencySettingsPage />;
    case "agency-payment-settings":
      return <AgencyPaymentSettingsPage />;
    case "agency-sms-settings":
      return <AgencySmsSettingsPage />;
    case "policy-number-settings":
      return <PolicyNumberSettingsPage />;
    case "cash-and-bank-settings":
      return <CashAndBankSettingsPage />;
    case "agency-commission-settings":
      return <AgencyCommissionRateSettingsPage />;
    case "customer-completion":
      return <CustomerCompletionPage />;
    case "schedule-policy":
      return <SchedulePolicyPage />;
    case "desk-feed":
      return <DeskFeedPage />;
    case "policy-list":
      return <PoliciesListPage />;
    case "installment-worklist":
      return <InstallmentsWorklistPage />;
    case "cheques-list":
      return <ChequesListPage />;
    case "customer-lookup":
      return <CustomerLookupPage />;
    case "import-history":
      return <ImportHistoryPage />;
    case "import-mismatches":
      return <ImportMismatchesPage />;
    case "settings-users":
      return <UsersPage />;
    case "settings-audit-log":
      return <AuditLogPage />;
    case "high-risk-customers":
      return <HighRiskCustomersPage />;
    case "sms-outbox":
      return <SmsOutboxPage />;
    case "sms-templates":
      return <SmsTemplatesPage />;
    case "sms-delivery-report":
      return <SmsDeliveryReportPage />;
    case "role-management":
      return <RoleManagementPage />;
    case "agencies-management":
      return <AgenciesManagementPage />;
    case "agency-profile":
      return <AgencyProfilePage />;
    case "change-password":
      return <ChangePasswordPage />;
    case "blank":
    default:
      return <BlankPage title={tab.title} />;
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
