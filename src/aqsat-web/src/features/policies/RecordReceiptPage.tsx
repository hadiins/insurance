import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";
import { MoneyInput } from "../../components/MoneyInput";
import { RecordPaymentDialog } from "../today/RecordPaymentDialog";

interface PolicyListItemDto {
  id: string;
  policyNumber: string;
  customerFullName: string;
  insuranceLineNameFa: string;
  status: string;
  isInstallment: boolean;
  totalReceivable: number;
  balance: number;
  issueDate: string;
}

interface OpenInstallmentRow {
  id: string;
  seqNo: number;
  dueDate: string;
  amount: number;
  balance: number;
  status: string;
}

interface PolicyReceiptStatusDto {
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  isInstallment: boolean;
  isScheduled: boolean;
  totalReceivable: number;
  downPayment: number;
  downPaymentReceived: boolean;
  openInstallments: OpenInstallmentRow[];
  isFullyPaid: boolean;
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

type MethodType = "Cash" | "BankTransfer" | "Cheque" | "PosDirect";

/** «ثبت دریافت» — one entry point to record any receipt from a customer: search a policy, then
 * (depending on its state) receive the down payment, settle an open installment, or record the
 * one full payment of a non-installment policy. */
export function RecordReceiptPage() {
  const [search, setSearch] = useState("");
  const [results, setResults] = useState<PolicyListItemDto[] | null>(null);
  const [selected, setSelected] = useState<PolicyListItemDto | null>(null);
  const [status, setStatus] = useState<PolicyReceiptStatusDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [activeInstallmentId, setActiveInstallmentId] = useState<string | null>(null);

  const [cashBoxes, setCashBoxes] = useState<CashBoxDto[]>([]);
  const [bankAccounts, setBankAccounts] = useState<BankAccountDto[]>([]);

  useEffect(() => {
    api.get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes").then(setCashBoxes).catch(() => {});
    api.get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts").then(setBankAccounts).catch(() => {});
  }, []);

  async function runSearch() {
    if (!search.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const data = await api.get<PolicyListItemDto[]>(`/policies?search=${encodeURIComponent(search.trim())}`);
      setResults(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "جستجو ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function selectPolicy(p: PolicyListItemDto) {
    setSelected(p);
    setStatus(null);
    setError(null);
    try {
      const data = await api.get<PolicyReceiptStatusDto>(`/policies/${p.id}/receipt-status`);
      setStatus(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "دریافت وضعیت بیمه‌نامه ناموفق بود.");
    }
  }

  function reloadStatus() {
    if (selected) selectPolicy(selected);
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        ثبت <em className="font-extralight not-italic text-(--ice-2)">دریافت</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">جستجوی بیمه‌نامه یا بیمه‌گذار و ثبت دریافت وجه</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 flex gap-2">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && runSearch()}
          placeholder="شمارهٔ بیمه‌نامه یا نام بیمه‌گذار…"
          className="flex-1 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />
        <button
          type="button"
          onClick={runSearch}
          disabled={busy || !search.trim()}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال جستجو…" : "جستجو"}
        </button>
      </div>

      <div className="grid grid-cols-2 gap-4">
        {results && (
          <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
            <table className="w-full border-collapse">
              <thead>
                <tr>
                  {["شمارهٔ بیمه‌نامه", "بیمه‌گذار", ""].map((h) => (
                    <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {results.map((p) => (
                  <tr
                    key={p.id}
                    onClick={() => selectPolicy(p)}
                    className={`cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov) ${
                      selected?.id === p.id ? "bg-(--mint)/9" : ""
                    }`}
                  >
                    <td className="px-3 py-2.75 text-[13px] font-semibold">{p.policyNumber}</td>
                    <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{p.customerFullName}</td>
                    <td />
                  </tr>
                ))}
                {results.length === 0 && (
                  <tr>
                    <td colSpan={3} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                      نتیجه‌ای یافت نشد.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          {!status ? (
            <div className="text-[12.5px] text-(--ice-3)">یک بیمه‌نامه را از فهرست انتخاب کنید.</div>
          ) : (
            <>
              <div className="mb-3 text-[13px] font-semibold text-(--ice)">
                {status.policyNumber} — {status.customerFullName}
              </div>

              {!status.isScheduled && status.isInstallment && (
                <div className="rounded-[10px] border border-(--amber)/30 bg-(--amber)/10 px-3 py-2 text-[12.5px] text-(--amber)">
                  این بیمه‌نامه هنوز زمان‌بندی نشده — ابتدا از «زمان‌بندی اقساط» اقساط را بسازید.
                </div>
              )}

              {status.isScheduled && status.downPayment > 0 && !status.downPaymentReceived && (
                <DownPaymentBox policyId={status.policyId} amount={status.downPayment} onReceived={reloadStatus} cashBoxes={cashBoxes} bankAccounts={bankAccounts} />
              )}

              {status.isScheduled && status.openInstallments.length > 0 && (
                <div className="mt-3">
                  <div className="mb-2 text-[11px] tracking-wider text-(--ice-3)">اقساط باز</div>
                  {status.openInstallments.map((i) => (
                    <div
                      key={i.id}
                      className="mb-1.5 flex items-center justify-between rounded-[8px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12px]"
                    >
                      <span>
                        قسط {fa(i.seqNo)} — {toJalaliDisplay(i.dueDate)}
                      </span>
                      <span className="flex items-center gap-2">
                        <span className="font-bold text-(--ice)">{money(i.balance)}</span>
                        <button
                          type="button"
                          onClick={() => setActiveInstallmentId(i.id)}
                          className="rounded-[8px] border border-(--mint) bg-(--mint) px-2.5 py-1 text-[11px] font-semibold text-(--on-mint)"
                        >
                          ثبت دریافت
                        </button>
                      </span>
                    </div>
                  ))}
                </div>
              )}

              {status.isScheduled && status.openInstallments.length === 0 && (!status.downPayment || status.downPaymentReceived) && (
                <div className="mt-3 text-[12.5px] font-semibold text-(--mint)">این بیمه‌نامه تسویه شده است.</div>
              )}

              {!status.isInstallment && !status.isFullyPaid && (
                <FullPaymentBox policyId={status.policyId} amount={status.totalReceivable} onPaid={reloadStatus} cashBoxes={cashBoxes} bankAccounts={bankAccounts} />
              )}

              {!status.isInstallment && status.isFullyPaid && (
                <div className="mt-3 text-[12.5px] font-semibold text-(--mint)">این بیمه‌نامه تسویه شده است.</div>
              )}
            </>
          )}
        </div>
      </div>

      {activeInstallmentId && status && (
        <RecordPaymentDialog
          installmentId={activeInstallmentId}
          customerFullName={status.customerFullName}
          suggestedAmount={status.openInstallments.find((i) => i.id === activeInstallmentId)?.balance ?? 0}
          onClose={() => setActiveInstallmentId(null)}
          onRecorded={() => {
            setActiveInstallmentId(null);
            reloadStatus();
          }}
        />
      )}
    </div>
  );
}

function DownPaymentBox({
  policyId,
  amount,
  onReceived,
  cashBoxes,
  bankAccounts,
}: {
  policyId: string;
  amount: number;
  onReceived: () => void;
  cashBoxes: CashBoxDto[];
  bankAccounts: BankAccountDto[];
}) {
  const [paidOn, setPaidOn] = useState(new Date().toISOString().slice(0, 10));
  const [methodType, setMethodType] = useState<MethodType>("Cash");
  const [cashBoxId, setCashBoxId] = useState("");
  const [bankAccountId, setBankAccountId] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if ((methodType === "Cash" || methodType === "Cheque") && !cashBoxId) {
      setError("انتخاب صندوق الزامی است.");
      return;
    }
    if (methodType === "BankTransfer" && !bankAccountId) {
      setError("انتخاب حساب بانکی الزامی است.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await api.post(`/policies/${policyId}/receive-down-payment`, {
        paidOn,
        referenceNo: null,
        methodType,
        cashBoxId: methodType === "Cash" ? cashBoxId : null,
        bankAccountId: methodType === "BankTransfer" ? bankAccountId : null,
      });
      onReceived();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت پیش‌پرداخت ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mb-3 rounded-[10px] border border-(--edge-2) bg-(--fld)/50 p-3">
      <div className="mb-2 text-[12.5px] font-semibold text-(--ice)">دریافت پیش‌پرداخت — {money(amount)} تومان</div>
      {error && <div className="mb-2 text-[11.5px] text-(--ember)">{error}</div>}
      <div className="mb-2 grid grid-cols-2 gap-2">
        <JalaliDateField value={paidOn} onChange={setPaidOn} className="w-full rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)" />
        <select
          value={methodType}
          onChange={(e) => setMethodType(e.target.value as MethodType)}
          className="w-full rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)"
        >
          <option value="Cash">نقدی</option>
          <option value="BankTransfer">واریز بانکی</option>
          <option value="PosDirect">پوز مستقیم بیمه‌گر</option>
        </select>
      </div>
      {methodType === "Cash" && (
        <select value={cashBoxId} onChange={(e) => setCashBoxId(e.target.value)} className="mb-2 w-full rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)">
          <option value="">انتخاب صندوق…</option>
          {cashBoxes.filter((b) => b.isActive).map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </select>
      )}
      {methodType === "BankTransfer" && (
        <select value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)} className="mb-2 w-full rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)">
          <option value="">انتخاب حساب بانکی…</option>
          {bankAccounts.filter((a) => a.isActive).map((a) => (
            <option key={a.id} value={a.id}>
              {a.bankName} — {a.accountNumber}
            </option>
          ))}
        </select>
      )}
      <button
        type="button"
        onClick={submit}
        disabled={saving}
        className="w-full rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1.5 text-[12px] font-semibold text-(--on-mint) disabled:cursor-not-allowed disabled:opacity-50"
      >
        {saving ? "در حال ثبت…" : "ثبت دریافت پیش‌پرداخت"}
      </button>
    </div>
  );
}

function FullPaymentBox({
  policyId,
  amount,
  onPaid,
  cashBoxes,
  bankAccounts,
}: {
  policyId: string;
  amount: number;
  onPaid: () => void;
  cashBoxes: CashBoxDto[];
  bankAccounts: BankAccountDto[];
}) {
  const [paidOn, setPaidOn] = useState(new Date().toISOString().slice(0, 10));
  const [amountText, setAmountText] = useState(String(Math.round(amount)));
  const [methodType, setMethodType] = useState<MethodType>("Cash");
  const [cashBoxId, setCashBoxId] = useState("");
  const [bankAccountId, setBankAccountId] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const methodLabel: Record<MethodType, string> = { Cash: "نقدی", BankTransfer: "واریز بانکی", Cheque: "چک", PosDirect: "پوز مستقیم بیمه‌گر" };

  async function submit() {
    const numericAmount = Number(amountText);
    if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError("مبلغ باید مثبت باشد.");
      return;
    }
    if ((methodType === "Cash" || methodType === "Cheque") && !cashBoxId) {
      setError("انتخاب صندوق الزامی است.");
      return;
    }
    if (methodType === "BankTransfer" && !bankAccountId) {
      setError("انتخاب حساب بانکی الزامی است.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await api.post(`/policies/${policyId}/record-full-payment`, {
        amount: numericAmount,
        paidOn,
        method: methodLabel[methodType],
        referenceNo: null,
        methodType,
        cashBoxId: methodType === "Cash" ? cashBoxId : null,
        bankAccountId: methodType === "BankTransfer" ? bankAccountId : null,
      });
      onPaid();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت پرداخت کامل ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mt-3 rounded-[10px] border border-(--edge-2) bg-(--fld)/50 p-3">
      <div className="mb-2 text-[12.5px] font-semibold text-(--ice)">ثبت پرداخت کامل بیمه‌نامه</div>
      {error && <div className="mb-2 text-[11.5px] text-(--ember)">{error}</div>}
      <div className="mb-2 grid grid-cols-2 gap-2">
        <MoneyInput value={amountText} onChange={setAmountText} />
        <JalaliDateField value={paidOn} onChange={setPaidOn} className="w-full rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)" />
      </div>
      <select
        value={methodType}
        onChange={(e) => setMethodType(e.target.value as MethodType)}
        className="mb-2 w-full rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)"
      >
        <option value="Cash">نقدی</option>
        <option value="BankTransfer">واریز بانکی</option>
        <option value="PosDirect">پوز مستقیم بیمه‌گر</option>
      </select>
      {methodType === "Cash" && (
        <select value={cashBoxId} onChange={(e) => setCashBoxId(e.target.value)} className="mb-2 w-full rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)">
          <option value="">انتخاب صندوق…</option>
          {cashBoxes.filter((b) => b.isActive).map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </select>
      )}
      {methodType === "BankTransfer" && (
        <select value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)} className="mb-2 w-full rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)">
          <option value="">انتخاب حساب بانکی…</option>
          {bankAccounts.filter((a) => a.isActive).map((a) => (
            <option key={a.id} value={a.id}>
              {a.bankName} — {a.accountNumber}
            </option>
          ))}
        </select>
      )}
      <button
        type="button"
        onClick={submit}
        disabled={saving}
        className="w-full rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1.5 text-[12px] font-semibold text-(--on-mint) disabled:cursor-not-allowed disabled:opacity-50"
      >
        {saving ? "در حال ثبت…" : "ثبت پرداخت کامل"}
      </button>
    </div>
  );
}
