import { useTabsStore } from "../../app/store/tabsStore";
import type { OpenTab } from "../../app/types";
import { TodayPage } from "../today/TodayPage";
import { NewPolicyPage } from "../policies/NewPolicyPage";
import { InstallmentListPage } from "../installments/InstallmentListPage";
import { CustomerDetailPage } from "../customers/CustomerDetailPage";
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
    case "customer-detail":
      return <CustomerDetailPage />;
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
