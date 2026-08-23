import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface HighRiskCustomerDto {
  customerId: string;
  fullName: string;
  mobile: string | null;
  overdueInstallmentCount: number;
  maxDaysOverdue: number;
  bouncedChequeCount: number;
}

export function HighRiskCustomersPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [rows, setRows] = useState<HighRiskCustomerDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<HighRiskCustomerDto[]>("/customers/high-risk")
      .then(setRows)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
  }, []);

  function openCustomer(customer: HighRiskCustomerDto) {
    openTab({
      navType: "customer-file",
      page: "customer-file",
      kind: "multi-record",
      recordId: customer.customerId,
      title: customer.fullName,
      payload: { customerId: customer.customerId },
    });
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">مشتریان پرریسک</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">دارای قسط معوق فعلی یا چک برگشتی، بدترین در بالا</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && rows === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && rows !== null && (
        <>
          <div className="mb-2 text-[11px] text-(--ice-3)">{fa(rows.length)} مشتری</div>
          {rows.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">مشتری پرریسکی نیست.</div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    {["نام", "شمارهٔ همراه", "اقساط معوق", "بیشترین تأخیر (روز)", "چک برگشتی"].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr
                      key={r.customerId}
                      onClick={() => openCustomer(r)}
                      className="cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov)"
                    >
                      <td className="px-3 py-2.75 text-[13px] font-semibold">{r.fullName}</td>
                      <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{r.mobile ? fa(r.mobile) : "—"}</td>
                      <td className="px-3 py-2.75 text-[13px] text-(--ember) font-bold">{fa(r.overdueInstallmentCount)}</td>
                      <td className="px-3 py-2.75 text-[13px]">{fa(r.maxDaysOverdue)}</td>
                      <td className="px-3 py-2.75 text-[13px] text-(--ember)">{fa(r.bouncedChequeCount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}
