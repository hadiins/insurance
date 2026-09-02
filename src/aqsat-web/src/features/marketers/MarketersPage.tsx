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
  appUserFullName: string | null;
  appUserMobile: string | null;
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

interface CashBoxDto {
  id: string;
  name: string;
  isActive: boolean;
}

interface BankAccountDto {
  id: string;
  bankName: string;
  accountNumber: string;
  isActive: boolean;
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
  const [cashBoxes, setCashBoxes] = useState<CashBoxDto[]>([]);
  const [bankAccounts, setBankAccounts] = useState<BankAccountDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [payMethod, setPayMethod] = useState<"Cash" | "BankTransfer">("Cash");
  const [payCashBoxId, setPayCashBoxId] = useState("");
  const [payBankAccountId, setPayBankAccountId] = useState("");

  const [newName, setNewName] = useState("");
  const [newMobile, setNewMobile] = useState("");
  const [newRateLine, setNewRateLine] = useState("");
  const [newRatePercent, setNewRatePercent] = useState("");

  const [panelMobile, setPanelMobile] = useState("");
  const [panelName, setPanelName] = useState("");
  const [panelPassword, setPanelPassword] = useState("");

  useEffect(() => {
    setPanelMobile(selected?.mobile ?? "");
    setPanelName(selected?.fullName ?? "");
    setPanelPassword("");
    // Defaults come from the marketer record; re-defaulting on every keystroke elsewhere would
    // wipe what the manager is typing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected?.id]);

  function reload() {
    api.get<MarketerDto[]>("/marketers").then(setMarketers).catch(() => {});
  }

  useEffect(() => {
    reload();
    api.get<InsuranceLineDto[]>("/insurance-lines").then(setLines).catch(() => {});
    api.get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes").then(setCashBoxes).catch(() => {});
    api.get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts").then(setBankAccounts).catch(() => {});
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

  async function grantPanelAccess() {
    if (!selected || !panelMobile.trim()) {
      setError("شمارهٔ همراه حساب پنل الزامی است.");
      return;
    }
    setError(null);
    try {
      const updated = await api.post<MarketerDto>(`/marketers/${selected.id}/panel-access`, {
        mobile: panelMobile.trim(),
        password: panelPassword || null,
        fullName: panelName.trim() || null,
      });
      setSelected(updated);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ایجاد دسترسی پنل ناموفق بود.");
    }
  }

  async function revokePanelAccess() {
    if (!selected) return;
    setError(null);
    try {
      const updated = await api.delete<MarketerDto>(`/marketers/${selected.id}/panel-access`);
      setSelected(updated);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "قطع دسترسی پنل ناموفق بود.");
    }
  }

  async function payCommissions() {
    if (!selected || !commissions) return;
    const payableIds = commissions.entries.filter((e) => e.status === "Payable").map((e) => e.id);
    if (payableIds.length === 0) return;
    if (payMethod === "Cash" && !payCashBoxId) {
      setError("برای پرداخت نقدی، انتخاب صندوق الزامی است.");
      return;
    }
    if (payMethod === "BankTransfer" && !payBankAccountId) {
      setError("برای واریز بانکی، انتخاب حساب بانکی الزامی است.");
      return;
    }
    setError(null);
    try {
      await api.post(`/marketers/${selected.id}/commissions/pay`, {
        commissionEntryIds: payableIds,
        methodType: payMethod,
        cashBoxId: payMethod === "Cash" ? payCashBoxId : null,
        bankAccountId: payMethod === "BankTransfer" ? payBankAccountId : null,
      });
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

              <div className="mb-4 rounded-[10px] border border-(--edge-2) bg-(--fld) p-3">
                <div className="mb-2 text-[11px] tracking-wider text-(--ice-3)">دسترسی پنل بازاریاب</div>
                {selected.appUserId ? (
                  <div className="flex items-center justify-between gap-2">
                    <div className="text-[12.5px] text-(--ice-2)">
                      متصل: <b className="text-(--ice)">{selected.appUserFullName}</b>{" "}
                      <span className="tabular-nums">({fa(selected.appUserMobile ?? "")})</span>
                    </div>
                    <button
                      type="button"
                      onClick={revokePanelAccess}
                      className="rounded-[8px] border border-(--ember) px-2.5 py-1.5 text-[11px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/10"
                    >
                      قطع دسترسی
                    </button>
                  </div>
                ) : (
                  <>
                    <div className="mb-2 grid grid-cols-3 gap-2">
                      <input
                        value={panelMobile}
                        onChange={(e) => setPanelMobile(e.target.value)}
                        placeholder="موبایل حساب"
                        className="rounded-[8px] border border-(--edge-2) bg-(--fld) px-2.5 py-1.5 text-[12px] tabular-nums text-(--ice) outline-none focus:border-(--mint)"
                      />
                      <input
                        value={panelName}
                        onChange={(e) => setPanelName(e.target.value)}
                        placeholder="نام کاربر"
                        className="rounded-[8px] border border-(--edge-2) bg-(--fld) px-2.5 py-1.5 text-[12px] text-(--ice) outline-none focus:border-(--mint)"
                      />
                      <input
                        type="password"
                        value={panelPassword}
                        onChange={(e) => setPanelPassword(e.target.value)}
                        placeholder="رمز عبور (کاربر جدید)"
                        className="rounded-[8px] border border-(--edge-2) bg-(--fld) px-2.5 py-1.5 text-[12px] text-(--ice) outline-none focus:border-(--mint)"
                      />
                    </div>
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-[11px] text-(--ice-3)">
                        اگر این موبایل قبلاً ثبت شده باشد، فقط متصل می‌شود و رمز جدید لازم نیست.
                      </span>
                      <button
                        type="button"
                        onClick={grantPanelAccess}
                        className="shrink-0 rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1.5 text-[11.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
                      >
                        ایجاد دسترسی پنل
                      </button>
                    </div>
                  </>
                )}
              </div>

              <div>
                <div className="mb-2 flex items-center justify-between">
                  <span className="text-[11px] tracking-wider text-(--ice-3)">پورسانت‌ها</span>
                </div>
                {commissions && commissions.payable > 0 && (
                  <div className="mb-2 rounded-[10px] border border-(--edge-2) bg-(--fld) p-3">
                    <div className="mb-2 text-[11px] tracking-wider text-(--ice-3)">
                      پرداخت {money(commissions.payable)} تومان از محل
                    </div>
                    <div className="mb-2 grid grid-cols-2 gap-2">
                      <select
                        value={payMethod}
                        onChange={(e) => setPayMethod(e.target.value as "Cash" | "BankTransfer")}
                        className="rounded-[8px] border border-(--edge-2) bg-(--pane) px-2 py-1.5 text-[12px] text-(--ice)"
                      >
                        <option value="Cash">نقدی (صندوق)</option>
                        <option value="BankTransfer">واریز بانکی</option>
                      </select>
                      {payMethod === "Cash" ? (
                        <select
                          value={payCashBoxId}
                          onChange={(e) => setPayCashBoxId(e.target.value)}
                          className="rounded-[8px] border border-(--edge-2) bg-(--pane) px-2 py-1.5 text-[12px] text-(--ice)"
                        >
                          <option value="">صندوق…</option>
                          {cashBoxes.filter((b) => b.isActive).map((b) => (
                            <option key={b.id} value={b.id}>{b.name}</option>
                          ))}
                        </select>
                      ) : (
                        <select
                          value={payBankAccountId}
                          onChange={(e) => setPayBankAccountId(e.target.value)}
                          className="rounded-[8px] border border-(--edge-2) bg-(--pane) px-2 py-1.5 text-[12px] text-(--ice)"
                        >
                          <option value="">حساب بانکی…</option>
                          {bankAccounts.filter((a) => a.isActive).map((a) => (
                            <option key={a.id} value={a.id}>{a.bankName} — {a.accountNumber}</option>
                          ))}
                        </select>
                      )}
                    </div>
                    <button
                      type="button"
                      onClick={payCommissions}
                      className="rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1 text-[11px] font-semibold text-(--on-mint)"
                    >
                      ثبت پرداخت پورسانت
                    </button>
                  </div>
                )}
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
