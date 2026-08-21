import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";

interface InsuranceLineDto {
  id: string;
  nameFa: string;
}

interface MarketerDto {
  id: string;
  fullName: string;
  mobile: string;
  type: string;
  isActive: boolean;
  appUserId: string | null;
}

interface MarketerRateDto {
  id: string;
  insuranceLineId: string;
  insuranceLineName: string;
  ratePercent: number;
  effectiveFrom: string;
  effectiveTo: string | null;
}

interface CommissionEntryDto {
  id: string;
  policyId: string;
  policyNumber: string;
  installmentSeqNo: number | null;
  amount: number;
  status: string;
  eligibleAt: string | null;
  paidAt: string | null;
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

export function MarketersPage() {
  const [marketers, setMarketers] = useState<MarketerDto[] | null>(null);
  const [lines, setLines] = useState<InsuranceLineDto[] | null>(null);
  const [selected, setSelected] = useState<MarketerDto | null>(null);
  const [rates, setRates] = useState<MarketerRateDto[] | null>(null);
  const [commissions, setCommissions] = useState<CommissionSummaryDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [newName, setNewName] = useState("");
  const [newMobile, setNewMobile] = useState("");
  const [newRateLine, setNewRateLine] = useState("");
  const [newRatePercent, setNewRatePercent] = useState("");

  function reload() {
    api.get<MarketerDto[]>("/marketers").then(setMarketers).catch(() => {});
  }

  useEffect(() => {
    reload();
    api.get<InsuranceLineDto[]>("/insurance-lines").then(setLines).catch(() => {});
  }, []);

  useEffect(() => {
    if (!selected) {
      setRates(null);
      setCommissions(null);
      return;
    }
    api.get<MarketerRateDto[]>(`/marketers/${selected.id}/rates`).then(setRates).catch(() => {});
    api.get<CommissionSummaryDto>(`/marketers/${selected.id}/commissions`).then(setCommissions).catch(() => {});
  }, [selected]);

  async function createMarketer() {
    if (!newName.trim() || !newMobile.trim()) {
      setError("نام و شمارهٔ همراه الزامی است.");
      return;
    }
    setError(null);
    try {
      await api.post("/marketers", { fullName: newName.trim(), mobile: newMobile.trim(), nationalId: null, type: "Independent", appUserId: null });
      setNewName("");
      setNewMobile("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت بازاریاب ناموفق بود.");
    }
  }

  async function addRate() {
    if (!selected || !newRateLine || !newRatePercent) {
      setError("رشتهٔ بیمه و درصد پورسانت را انتخاب کنید.");
      return;
    }
    setError(null);
    try {
      await api.post(`/marketers/${selected.id}/rates`, {
        insuranceLineId: newRateLine,
        ratePercent: Number(newRatePercent),
        effectiveFrom: new Date().toISOString().slice(0, 10),
      });
      setNewRatePercent("");
      api.get<MarketerRateDto[]>(`/marketers/${selected.id}/rates`).then(setRates).catch(() => {});
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت نرخ ناموفق بود.");
    }
  }

  async function payCommissions() {
    if (!selected || !commissions) return;
    const payableIds = commissions.entries.filter((e) => e.status === "Payable").map((e) => e.id);
    if (payableIds.length === 0) return;
    setError(null);
    try {
      await api.post(`/marketers/${selected.id}/commissions/pay`, { commissionEntryIds: payableIds });
      api.get<CommissionSummaryDto>(`/marketers/${selected.id}/commissions`).then(setCommissions).catch(() => {});
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "پرداخت پورسانت ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        بازاریاب‌ها <em className="font-extralight not-italic text-(--ice-2)">و پورسانت</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">هر بازاریاب برای هر رشتهٔ بیمه یک نرخ پورسانت جداگانه دارد</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 grid grid-cols-[1fr_2fr] gap-4">
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-4">
          <div className="mb-3 flex gap-2">
            <input
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="نام بازاریاب"
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
            />
          </div>
          <div className="mb-3 flex gap-2">
            <input
              value={newMobile}
              onChange={(e) => setNewMobile(e.target.value)}
              placeholder="۰۹۱۲۳۴۵۶۷۸۹"
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
            />
          </div>
          <button
            type="button"
            onClick={createMarketer}
            className="mb-4 w-full rounded-[10px] border border-(--mint) bg-(--mint) px-3 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
          >
            افزودن بازاریاب
          </button>

          {marketers === null ? (
            <div className="text-[12px] text-(--ice-3)">در حال بارگذاری…</div>
          ) : marketers.length === 0 ? (
            <div className="text-[12px] text-(--ice-3)">هیچ بازاریابی ثبت نشده.</div>
          ) : (
            <div className="space-y-1.5">
              {marketers.map((m) => (
                <button
                  key={m.id}
                  type="button"
                  onClick={() => setSelected(m)}
                  className={`w-full rounded-[10px] border px-3 py-2 text-right text-[12.5px] transition-colors ${
                    selected?.id === m.id
                      ? "border-(--mint) bg-(--mint)/10 text-(--ice)"
                      : "border-(--edge-2) text-(--ice-2) hover:bg-(--hov)"
                  }`}
                >
                  {m.fullName} <span className="text-(--ice-3)">— {m.mobile}</span>
                </button>
              ))}
            </div>
          )}
        </div>

        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-4">
          {!selected ? (
            <div className="text-[12.5px] text-(--ice-3)">یک بازاریاب را از فهرست انتخاب کنید.</div>
          ) : (
            <>
              <b className="mb-3 block text-[13.5px] text-(--ice)">{selected.fullName}</b>

              <div className="mb-4">
                <div className="mb-2 text-[11px] tracking-wider text-(--ice-3)">نرخ‌های پورسانت</div>
                {rates?.map((r) => (
                  <div
                    key={r.id}
                    className="mb-1.5 flex items-center justify-between rounded-[8px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12px]"
                  >
                    <span>{r.insuranceLineName}</span>
                    <span className="font-bold text-(--ice)">
                      {fa(r.ratePercent)}٪ {r.effectiveTo && <span className="text-(--ice-3)">(پایان‌یافته)</span>}
                    </span>
                  </div>
                ))}
                <div className="mt-2 flex gap-2">
                  <select
                    value={newRateLine}
                    onChange={(e) => setNewRateLine(e.target.value)}
                    className="rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)"
                  >
                    <option value="">رشته…</option>
                    {lines?.map((l) => (
                      <option key={l.id} value={l.id}>
                        {l.nameFa}
                      </option>
                    ))}
                  </select>
                  <input
                    value={newRatePercent}
                    onChange={(e) => setNewRatePercent(e.target.value)}
                    placeholder="درصد"
                    className="w-20 rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)"
                  />
                  <button
                    type="button"
                    onClick={addRate}
                    className="rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1.5 text-[12px] font-semibold text-(--on-mint)"
                  >
                    ثبت نرخ
                  </button>
                </div>
              </div>

              <div>
                <div className="mb-2 flex items-center justify-between">
                  <span className="text-[11px] tracking-wider text-(--ice-3)">پورسانت‌ها</span>
                  {commissions && commissions.payable > 0 && (
                    <button
                      type="button"
                      onClick={payCommissions}
                      className="rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1 text-[11px] font-semibold text-(--on-mint)"
                    >
                      پرداخت {money(commissions.payable)} تومان
                    </button>
                  )}
                </div>
                {commissions && (
                  <div className="mb-2 grid grid-cols-3 gap-2 text-center text-[11px]">
                    <div className="rounded-[8px] border border-(--edge-2) p-2">
                      <div className="text-(--ice-3)">در انتظار</div>
                      <div className="font-bold text-(--ice)">{money(commissions.pending)}</div>
                    </div>
                    <div className="rounded-[8px] border border-(--edge-2) p-2">
                      <div className="text-(--ice-3)">قابل پرداخت</div>
                      <div className="font-bold text-(--amber)">{money(commissions.payable)}</div>
                    </div>
                    <div className="rounded-[8px] border border-(--edge-2) p-2">
                      <div className="text-(--ice-3)">پرداخت‌شده</div>
                      <div className="font-bold text-(--mint)">{money(commissions.paid)}</div>
                    </div>
                  </div>
                )}
                {commissions?.entries.map((e) => (
                  <div key={e.id} className="mb-1 flex items-center justify-between rounded-[8px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[11.5px]">
                    <span>
                      {e.policyNumber} {e.installmentSeqNo ? `— قسط ${fa(e.installmentSeqNo)}` : "— پیش‌پرداخت"}
                    </span>
                    <span className="text-(--ice-3)">{STATUS_LABEL[e.status] ?? e.status}</span>
                    <span className="font-bold">{money(e.amount)}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
