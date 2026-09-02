import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";

interface FundBalanceDto {
  id: string;
  label: string;
  isBankAccount: boolean;
  isActive: boolean;
  openingBalance: number;
  totalIn: number;
  totalOut: number;
  balance: number;
}

interface CashFlowBalancesDto {
  cashBoxes: FundBalanceDto[];
  bankAccounts: FundBalanceDto[];
}

interface FundMovementDto {
  date: string;
  kind: string;
  label: string;
  amountIn: number | null;
  amountOut: number | null;
  referenceNo: string | null;
}

interface FundMovementsDto {
  totalIn: number;
  totalOut: number;
  rows: FundMovementDto[];
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)";

/** «موجودی و گردش صندوق و بانک» — live balances (opening + arithmetic over every recorded flow)
 * and the unified movement view behind each balance. */
export function CashFlowPage() {
  const [balances, setBalances] = useState<CashFlowBalancesDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [selected, setSelected] = useState<{ id: string; isBankAccount: boolean; label: string } | null>(null);
  const [movements, setMovements] = useState<FundMovementsDto | null>(null);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const [transferDate, setTransferDate] = useState(new Date().toISOString().slice(0, 10));
  const [transferAmount, setTransferAmount] = useState("");
  const [fromCashBoxId, setFromCashBoxId] = useState("");
  const [toCashBoxId, setToCashBoxId] = useState("");
  const [fromBankAccountId, setFromBankAccountId] = useState("");
  const [toBankAccountId, setToBankAccountId] = useState("");
  const [transferNote, setTransferNote] = useState("");
  const [saving, setSaving] = useState(false);

  function reload() {
    api
      .get<CashFlowBalancesDto>("/cash-flow/balances")
      .then(setBalances)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری موجودی‌ها"));
  }

  useEffect(reload, []);

  function loadMovements(fund: { id: string; isBankAccount: boolean; label: string }, fromDate = from, toDate = to) {
    setSelected(fund);
    setMovements(null);
    const params = new URLSearchParams(
      fund.isBankAccount ? { bankAccountId: fund.id } : { cashBoxId: fund.id },
    );
    if (fromDate) params.set("from", fromDate);
    if (toDate) params.set("to", toDate);
    api
      .get<FundMovementsDto>(`/cash-flow/movements?${params.toString()}`)
      .then(setMovements)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری گردش"));
  }

  useEffect(() => {
    if (selected) loadMovements(selected);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to]);

  async function submitTransfer() {
    const amount = Number(transferAmount.replace(/[^\d.]/g, ""));
    if (!amount || amount <= 0) {
      setError("مبلغ انتقال را وارد کنید.");
      return;
    }
    const fromCount = [fromCashBoxId, fromBankAccountId].filter(Boolean).length;
    const toCount = [toCashBoxId, toBankAccountId].filter(Boolean).length;
    if (fromCount !== 1 || toCount !== 1) {
      setError("مبدأ و مقصد هرکدام باید دقیقاً یک صندوق یا حساب بانکی باشد.");
      return;
    }
    if (fromCashBoxId && fromCashBoxId === toCashBoxId) {
      setError("مبدأ و مقصد انتقال نباید یکی باشد.");
      return;
    }
    if (fromBankAccountId && fromBankAccountId === toBankAccountId) {
      setError("مبدأ و مقصد انتقال نباید یکی باشد.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await api.post("/cash-flow/transfers", {
        date: transferDate,
        amount,
        fromCashBoxId: fromCashBoxId || null,
        fromBankAccountId: fromBankAccountId || null,
        toCashBoxId: toCashBoxId || null,
        toBankAccountId: toBankAccountId || null,
        note: transferNote.trim() || null,
      });
      setTransferAmount("");
      setTransferNote("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت انتقال ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  const boxes = balances?.cashBoxes ?? [];
  const accounts = balances?.bankAccounts ?? [];
  const allFunds = [
    ...boxes.map((b) => ({ id: b.id, isBankAccount: false, label: b.label })),
    ...accounts.map((a) => ({ id: a.id, isBankAccount: true, label: a.label })),
  ];

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        موجودی و <em className="font-extralight not-italic text-(--ice-2)">گردش صندوق و بانک</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">ماندهٔ زندهٔ هر صندوق و حساب — جمع ماندهٔ ابتدای دوره، دریافتی‌ها و پرداخت‌ها</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">
          موجودی‌ها
          {balances && (
            <span className="ms-2 text-[11px] font-normal text-(--ice-3)">
              {allFunds.length > 0 ? `${allFunds.length} صندوق و حساب` : "هنوز صندوق یا حسابی ثبت نشده"}
            </span>
          )}
        </div>
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["عنوان", "ماندهٔ ابتدای دوره", "جمع دریافتی", "جمع پرداختی", "ماندهٔ فعلی", ""].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {allFunds.length === 0 && balances !== null && (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  صندوق یا حسابی ثبت نشده است — از «تنظیمات ← صندوق و بانک‌ها» اضافه کنید.
                </td>
              </tr>
            )}
            {boxes.map((b) => (
              <BalanceRow key={b.id} fund={b} selectedId={selected?.id} onOpen={() => loadMovements({ id: b.id, isBankAccount: false, label: b.label })} />
            ))}
            {accounts.map((a) => (
              <BalanceRow key={a.id} fund={a} selectedId={selected?.id} onOpen={() => loadMovements({ id: a.id, isBankAccount: true, label: a.label })} />
            ))}
          </tbody>
        </table>
        {balances === null && !error && (
          <div className="p-4 text-center text-[12px] text-(--ice-3)">در حال بارگذاری…</div>
        )}
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3.5 text-[13px] font-semibold text-(--ice)">انتقال وجه بین صندوق و حساب‌ها</div>
        <div className="mb-3 grid grid-cols-4 gap-3">
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">تاریخ</label>
            <JalaliDateField value={transferDate} onChange={setTransferDate} className={inputClass} />
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">مبلغ (تومان)</label>
            <input value={transferAmount} onChange={(e) => setTransferAmount(e.target.value)} dir="ltr" className={inputClass} />
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">مبدأ — صندوق</label>
            <select value={fromCashBoxId} onChange={(e) => { setFromCashBoxId(e.target.value); setFromBankAccountId(""); }} className={inputClass}>
              <option value="">—</option>
              {boxes.map((b) => (
                <option key={b.id} value={b.id}>{b.label}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">مبدأ — حساب بانکی</label>
            <select value={fromBankAccountId} onChange={(e) => { setFromBankAccountId(e.target.value); setFromCashBoxId(""); }} className={inputClass}>
              <option value="">—</option>
              {accounts.map((a) => (
                <option key={a.id} value={a.id}>{a.label}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">مقصد — صندوق</label>
            <select value={toCashBoxId} onChange={(e) => { setToCashBoxId(e.target.value); setToBankAccountId(""); }} className={inputClass}>
              <option value="">—</option>
              {boxes.map((b) => (
                <option key={b.id} value={b.id}>{b.label}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">مقصد — حساب بانکی</label>
            <select value={toBankAccountId} onChange={(e) => { setToBankAccountId(e.target.value); setToCashBoxId(""); }} className={inputClass}>
              <option value="">—</option>
              {accounts.map((a) => (
                <option key={a.id} value={a.id}>{a.label}</option>
              ))}
            </select>
          </div>
          <div className="col-span-2">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">توضیح (اختیاری)</label>
            <input value={transferNote} onChange={(e) => setTransferNote(e.target.value)} className={inputClass} />
          </div>
        </div>
        <button
          type="button"
          onClick={submitTransfer}
          disabled={saving}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {saving ? "در حال ثبت…" : "ثبت انتقال"}
        </button>
      </div>

      {selected && (
        <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-(--edge) px-3 py-2.5">
            <div className="text-[12.5px] font-semibold text-(--ice-2)">
              گردش {selected.label}
              {movements && (
                <span className="ms-2 text-[11px] font-normal text-(--ice-3)">
                  جمع دریافتی {money(movements.totalIn)} — جمع پرداختی {money(movements.totalOut)}
                </span>
              )}
            </div>
            <div className="flex items-center gap-2">
              <JalaliDateField value={from} onChange={setFrom} placeholder="از تاریخ" className="w-36 rounded-[10px] border border-(--edge-2) bg-(--fld) px-2.5 py-1.5 text-[12px] text-(--ice)" />
              <JalaliDateField value={to} onChange={setTo} placeholder="تا تاریخ" className="w-36 rounded-[10px] border border-(--edge-2) bg-(--fld) px-2.5 py-1.5 text-[12px] text-(--ice)" />
            </div>
          </div>
          <table className="w-full border-collapse">
            <thead>
              <tr>
                {["تاریخ", "نوع", "شرح", "وارده", "خارج‌شده", "شمارهٔ پیگیری"].map((h) => (
                  <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {movements?.rows.map((r, i) => (
                <tr key={i} className="border-t border-(--edge) first:border-t-0">
                  <td className="px-3 py-2 text-[12.5px] tabular-nums">{toJalaliDisplay(r.date)}</td>
                  <td className="px-3 py-2 text-[12.5px] font-semibold text-(--ice-2)">{r.kind}</td>
                  <td className="px-3 py-2 text-[12.5px] text-(--ice-3)">{r.label}</td>
                  <td className="px-3 py-2 text-[12.5px] font-bold text-(--mint)">{r.amountIn !== null ? money(r.amountIn) : "—"}</td>
                  <td className="px-3 py-2 text-[12.5px] font-bold text-(--ember)">{r.amountOut !== null ? money(r.amountOut) : "—"}</td>
                  <td className="px-3 py-2 text-[12px] text-(--ice-3)" dir="ltr">{r.referenceNo ?? "—"}</td>
                </tr>
              ))}
              {movements?.rows.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                    در این بازه گردشی ثبت نشده است.
                  </td>
                </tr>
              )}
              {movements === null && (
                <tr>
                  <td colSpan={6} className="px-3 py-6 text-center text-[12px] text-(--ice-3)">در حال بارگذاری…</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function BalanceRow({
  fund,
  selectedId,
  onOpen,
}: {
  fund: FundBalanceDto;
  selectedId: string | undefined;
  onOpen: () => void;
}) {
  return (
    <tr className={`border-t border-(--edge) first:border-t-0 ${selectedId === fund.id ? "bg-(--mint)/6" : ""}`}>
      <td className="px-3 py-2.5 text-[13px] font-semibold">
        {fund.label}
        {!fund.isActive && <span className="ms-2 text-[11px] text-(--ice-3)">(غیرفعال)</span>}
      </td>
      <td className="px-3 py-2.5 text-[12.5px] tabular-nums text-(--ice-3)">{money(fund.openingBalance)}</td>
      <td className="px-3 py-2.5 text-[12.5px] tabular-nums text-(--mint)">{money(fund.totalIn)}</td>
      <td className="px-3 py-2.5 text-[12.5px] tabular-nums text-(--ember)">{money(fund.totalOut)}</td>
      <td className="px-3 py-2.5 text-[13px] font-bold tabular-nums">{money(fund.balance)}</td>
      <td className="px-3 py-2.5">
        <button type="button" onClick={onOpen} className="text-[11px] text-(--ice-3) hover:text-(--ice)">
          گردش
        </button>
      </td>
    </tr>
  );
}
