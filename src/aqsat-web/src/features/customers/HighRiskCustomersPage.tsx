import { useTabsStore } from "../../app/store/tabsStore";
import { ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import { useHighRiskCustomers } from "../risk/riskApi";
import { RISK_DECISION_STYLES, RISK_LEVEL_STYLES } from "../risk/riskTypes";

/** «مشتریان پرریسک» — the operational list (overdue installments, bounced cheques) merged with
 * each customer's latest risk assessment (docs Phase 2A §6): risk-level first, then operational
 * severity. Customers assessed High/Critical appear even before anything is overdue today. */
export function HighRiskCustomersPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const query = useHighRiskCustomers();

  function openCustomer(customerId: string, fullName: string) {
    openTab({
      navType: "customer-file",
      page: "customer-file",
      kind: "multi-record",
      recordId: customerId,
      title: fullName,
      payload: { customerId },
    });
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">مشتریان پرریسک</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        دارای قسط معوق یا چک برگشتی، یا سطح ریسک ارزیابی‌شدهٔ بالا — بر اساس سطح ریسک، بدترین در بالا
      </div>

      {query.isPending && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {query.isError && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {query.error instanceof ApiError ? query.error.message : "خطا در بارگذاری فهرست"}
          <button type="button" onClick={() => void query.refetch()} className="ms-2 underline">
            تلاش مجدد
          </button>
        </div>
      )}

      {query.data && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(query.data.length)} مشتری</div>
          {query.data.length === 0 ? (
            <EmptyState
              icon="🛡️"
              title="مشتری پرریسکی نیست."
              description="هیچ مشتری‌ای با قسط معوق، چک برگشتی یا سطح ریسک بالا در دفتر شما ثبت نشده است."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["نام", "سطح ریسک", "امتیاز", "تصمیم", "اقساط معوق", "مبلغ معوق", "بیشترین تأخیر (روز)", "چک برگشتی"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {query.data.map((r) => (
                  <Tr key={r.customerId} onClick={() => openCustomer(r.customerId, r.fullName)}>
                    <Td className="py-2.75 font-semibold">{r.fullName}</Td>
                    <Td className="py-2.75">
                      {r.riskLevelKey ? (
                        <span className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${RISK_LEVEL_STYLES[r.riskLevelKey].chip}`}>
                          {RISK_LEVEL_STYLES[r.riskLevelKey].label}
                        </span>
                      ) : (
                        <span className="text-[11.5px] text-(--ice-3)">ارزیابی‌نشده</span>
                      )}
                    </Td>
                    <Td className="py-2.75 font-bold tabular-nums">{r.score === null ? "—" : fa(r.score)}</Td>
                    <Td className="py-2.75">
                      {r.decisionKey ? (
                        <span className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${RISK_DECISION_STYLES[r.decisionKey].chip}`}>
                          {RISK_DECISION_STYLES[r.decisionKey].label}
                        </span>
                      ) : (
                        "—"
                      )}
                    </Td>
                    <Td className="py-2.75 text-(--ember) font-bold">{fa(r.overdueInstallmentCount)}</Td>
                    <Td className="py-2.75 tabular-nums text-(--ember)">{money(r.overdueAmountToman)}</Td>
                    <Td className="py-2.75">{fa(r.maxDaysOverdue)}</Td>
                    <Td className="py-2.75 text-(--ember)">{fa(r.bouncedChequeCount)}</Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </>
      )}
    </div>
  );
}
