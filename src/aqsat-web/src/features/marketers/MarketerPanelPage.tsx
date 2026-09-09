import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { money } from "../../lib/persian";
import { StatusBadge } from "../../components/StatusBadge";
import { EmptyState } from "../../components/EmptyState";

interface MarketerCustomerDto {
  customerId: string;
  fullName: string;
  isOverdue: boolean;
}

interface CommissionEntryDto {
  id: string;
  policyNumber: string;
  installmentSeqNo: number | null;
  amount: number;
  status: string;
}

interface CommissionSummaryDto {
  pending: number;
  payable: number;
  paid: number;
  entries: CommissionEntryDto[];
}

const STATUS_LABEL: Record<string, string> = {
  Pending: "در انتظار تسویه",
  Payable: "قابل پرداخت",
  Paid: "پرداخت‌شده",
};

/// docs/PHASE-1-SPEC.md §2.2 — restricted marketer self-view. Only their own customers (name +
/// overdue status, never amounts) and their own commission entries. No export control exists on
/// this page by design.
export function MarketerPanelPage() {
  const [customers, setCustomers] = useState<MarketerCustomerDto[] | null>(null);
  const [commissions, setCommissions] = useState<CommissionSummaryDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<MarketerCustomerDto[]>("/marketer-panel/customers")
      .then(setCustomers)
      .catch((err) => setError(err instanceof ApiError ? err.message : "این کاربر به‌عنوان بازاریاب ثبت نشده است."));
    api.get<CommissionSummaryDto>("/marketer-panel/commissions").then(setCommissions).catch(() => {});
  }, []);

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        پنل <em className="font-extralight not-italic text-(--ice-2)">بازاریاب</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">فقط مشتریانی که خودتان معرفی کرده‌اید</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && commissions && (
        <div className="mb-4.5 grid grid-cols-3 gap-3">
          <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5 text-center">
            <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">در انتظار تسویه</div>
            <div className="text-[20px] font-extrabold text-(--ice)">{money(commissions.pending)}</div>
          </div>
          <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5 text-center">
            <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">قابل پرداخت</div>
            <div className="text-[20px] font-extrabold text-(--amber)">{money(commissions.payable)}</div>
          </div>
          <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5 text-center">
            <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">پرداخت‌شده</div>
            <div className="text-[20px] font-extrabold text-(--mint)">{money(commissions.paid)}</div>
          </div>
        </div>
      )}

      {!error && (
        <div className="mb-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
          <table className="w-full border-collapse">
            <thead>
              <tr>
                <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  مشتری
                </th>
                <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  وضعیت
                </th>
              </tr>
            </thead>
            <tbody>
              {customers?.map((c) => (
                <tr key={c.customerId} className="border-t border-(--edge)">
                  <td className="px-3 py-2.5 text-[13.5px] font-semibold">{c.fullName}</td>
                  <td className="px-3 py-2.5 text-[13.5px]">
                    <StatusBadge tone={c.isOverdue ? "ember" : "mint"}>{c.isOverdue ? "معوق" : "به‌روز"}</StatusBadge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {customers?.length === 0 && (
            <EmptyState
              icon="👥"
              title="هنوز مشتری‌ای معرفی نکرده‌اید"
              description="با معرفی مشتریان جدید، آن‌ها و وضعیت اقساطشان از همین پنل قابل پیگیری خواهد بود."
            />
          )}
        </div>
      )}

      {!error && commissions && (
        <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
          {commissions.entries.map((e) => (
            <div
              key={e.id}
              className="flex items-center justify-between border-t border-(--edge) px-3 py-2.5 text-[12.5px] first:border-t-0"
            >
              <span>
                {e.policyNumber} {e.installmentSeqNo ? `— قسط ${e.installmentSeqNo}` : "— پیش‌پرداخت"}
              </span>
              <span className="text-(--ice-3)">{STATUS_LABEL[e.status] ?? e.status}</span>
              <span className="font-bold">{money(e.amount)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
