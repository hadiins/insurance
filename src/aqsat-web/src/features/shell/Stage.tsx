import { useTabsStore } from "../../app/store/tabsStore";
import type { OpenTab } from "../../app/types";
import { TodayPage } from "../today/TodayPage";
import { NewPolicyPage } from "../policies/NewPolicyPage";
import { InstallmentListPage } from "../installments/InstallmentListPage";
import { CustomerFilePage } from "../customers/CustomerFilePage";
import { CustomersListPage } from "../customers/CustomersListPage";
import { PolicyFilePage } from "../policies/PolicyFilePage";
import { FanavaranImportPage } from "../imports/FanavaranImportPage";
import { ContractTemplatesPage } from "../imports/ContractTemplatesPage";
import { MarketersPage } from "../marketers/MarketersPage";
import { MarketerPanelPage } from "../marketers/MarketerPanelPage";
import { SmsReminderPage } from "../sms/SmsReminderPage";
import { PnlPage } from "../reports/PnlPage";
import { RenewalWatchesPage } from "../renewals/RenewalWatchesPage";
import { BlankPage } from "../placeholder/BlankPage";
import { TabKeyProvider } from "./TabContext";

function renderPage(tab: OpenTab) {
  switch (tab.page) {
    case "today":
      return <TodayPage />;
    case "new-policy":
      return <NewPolicyPage />;
    case "installment-list":
      return <InstallmentListPage />;
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
    case "renewal-watches":
      return <RenewalWatchesPage />;
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
          <TabKeyProvider tabKey={tab.key}>{renderPage(tab)}</TabKeyProvider>
        </div>
      ))}
    </div>
  );
}
