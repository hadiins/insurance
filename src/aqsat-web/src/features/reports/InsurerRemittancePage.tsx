import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";

interface PendingRemittanceRow {
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  insuranceLineNameFa: string;
  installmentId: string | null;
  seqNo: number | null;
  amount: number;
  collectedOn: string;
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

interface InsurerRemittanceLineDto {
  policyId: string;
  policyNumber: string;
  seqNo: number | null;
  amount: number;
}

interface InsurerRemittanceDto {
  id: string;
  date: string;
  amount: number;
  methodType: string;
  cashBoxName: string | null;
  bankAccountLabel: string | null;
  referenceNo: string | null;
  lines: InsurerRemittanceLineDto[];
}

type MethodType = "Cash" | "BankTransfer";

const METHOD_LABEL: Record<string, string> = { Cash: "نقدی", BankTransfer: "واریز بانکی" };

/** «پرداخت به بیمه‌گر» — remitting already-collected installment/premium money to the insurer,
 * linked to specific installments so a per-policy remittance history stays queryable. */
export function InsurerRemittancePage() {
  const [pending, setPending] = useState<PendingRemittanceRow[] | null>(null);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [past, setPast] = useState<InsurerRemittanceDto[] | null>(null);
  const [cashBoxes, setCashBoxes] = useState<CashBoxDto[]>([]);
  const [bankAccounts, setBankAccounts] = useState<BankAccountDto[]>([]);

  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const [methodType, setMethodType] = useState<MethodType>("BankTransfer");
  const [cashBoxId, setCashBoxId] = useState("");
  const [bankAccountId, setBankAccountId] = useState("");
  const [referenceNo, setReferenceNo] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function reload() {
    api.get<PendingRemittanceRow[]>("/insurer-remittances/pending").then(setPending).catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری"));
    api.get<InsurerRemittanceDto[]>("/insurer-remittances").then(setPast).catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری"));
  }

  useEffect(() => {
    reload();
    api.get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes").then(setCashBoxes).catch(() => {});
    api.get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts").then(setBankAccounts).catch(() => {});
  }, []);

  function rowKey(r: PendingRemittanceRow) {
    return `${r.policyId}:${r.installmentId ?? "bare"}`;
  }

  function toggle(r: PendingRemittanceRow) {
    const key = rowKey(r);
    setSelectedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  const selectedRows = pending?.filter((r) => selectedKeys.has(rowKey(r))) ?? [];
  const selectedTotal = selectedRows.reduce((sum, r) => sum + r.amount, 0);

  async function submit() {
    if (selectedRows.length === 0) {
      setError("حداقل یک مورد را انتخاب کنید.");
      return;
    }
    if (methodType === "Cash" && !cashBoxId) {
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
      await api.post("/insurer-remittances", {
        date,
        methodType,
        cashBoxId: methodType === "Cash" ? cashBoxId : null,
        bankAccountId: methodType === "BankTransfer" ? bankAccountId : null,
        referenceNo: referenceNo.trim() || null,
        lines: selectedRows.map((r) => ({ policyId: r.policyId, installmentId: r.installmentId, amount: r.amount })),
      });
      setSelectedKeys(new Set());
      setReferenceNo("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت پرداخت ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        پرداخت به <em className="font-extralight not-italic text-(--ice-2)">بیمه‌گر</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">واریز اقساط و حق بیمهٔ جمع‌آوری‌شده به حساب بیمه‌گر</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">دریافتی‌های واریزنشده به بیمه‌گر</div>
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["", "بیمه‌نامه", "بیمه‌گذار", "رشته", "قسط", "تاریخ دریافت", "مبلغ"].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {pending?.map((r) => (
              <tr key={rowKey(r)} className="border-t border-(--edge) first:border-t-0">
                <td className="px-3 py-2">
                  <input type="checkbox" checked={selectedKeys.has(rowKey(r))} onChange={() => toggle(r)} />
                </td>
                <td className="px-3 py-2 text-[13px] font-semibold">{r.policyNumber}</td>
                <td className="px-3 py-2 text-[13px] text-(--ice-3)">{r.customerFullName}</td>
                <td className="px-3 py-2 text-[12px] text-(--ice-3)">{r.insuranceLineNameFa}</td>
                <td className="px-3 py-2 text-[12px] text-(--ice-3)">{r.seqNo ? `قسط ${fa(r.seqNo)}` : "پیش‌پرداخت/کامل"}</td>
                <td className="px-3 py-2 text-[12px] tabular-nums">{toJalaliDisplay(r.collectedOn)}</td>
                <td className="px-3 py-2 text-[13px] font-bold">{money(r.amount)}</td>
              </tr>
            ))}
            {pending?.length === 0 && (
              <tr>
                <td colSpan={7} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  همه چیز به بیمه‌گر واریز شده است.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {selectedRows.length > 0 && (
        <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-3.5 text-[13px] font-semibold text-(--ice)">
            ثبت پرداخت — {fa(selectedRows.length)} مورد — جمع {money(selectedTotal)} تومان
          </div>
          <div className="mb-3 grid grid-cols-3 gap-3">
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">تاریخ پرداخت</label>
              <JalaliDateField value={date} onChange={setDate} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice)" />
            </div>
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">روش پرداخت</label>
              <select
                value={methodType}
                onChange={(e) => setMethodType(e.target.value as MethodType)}
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
              >
                <option value="BankTransfer">واریز بانکی</option>
                <option value="Cash">نقدی</option>
              </select>
            </div>
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">شمارهٔ پیگیری (اختیاری)</label>
              <input
                value={referenceNo}
                onChange={(e) => setReferenceNo(e.target.value)}
                dir="ltr"
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
              />
            </div>
          </div>
          {methodType === "Cash" ? (
            <div className="mb-3">
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">صندوق</label>
              <select
                value={cashBoxId}
                onChange={(e) => setCashBoxId(e.target.value)}
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
              >
                <option value="">انتخاب کنید…</option>
                {cashBoxes.filter((b) => b.isActive).map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <div className="mb-3">
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">حساب بانکی</label>
              <select
                value={bankAccountId}
                onChange={(e) => setBankAccountId(e.target.value)}
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
              >
                <option value="">انتخاب کنید…</option>
                {bankAccounts.filter((a) => a.isActive).map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.bankName} — {a.accountNumber}
                  </option>
                ))}
              </select>
            </div>
          )}
          <button
            type="button"
            onClick={submit}
            disabled={saving}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {saving ? "در حال ثبت…" : "ثبت پرداخت به بیمه‌گر"}
          </button>
        </div>
      )}

      <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">تاریخچهٔ پرداخت‌ها</div>
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["تاریخ", "مبلغ", "روش", "محل پرداخت", "شمارهٔ پیگیری", "تعداد ردیف"].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {past?.map((r) => (
              <tr key={r.id} className="border-t border-(--edge) first:border-t-0">
                <td className="px-3 py-2.5 text-[13px] tabular-nums">{toJalaliDisplay(r.date)}</td>
                <td className="px-3 py-2.5 text-[13px] font-bold">{money(r.amount)}</td>
                <td className="px-3 py-2.5 text-[12px] text-(--ice-3)">{METHOD_LABEL[r.methodType] ?? r.methodType}</td>
                <td className="px-3 py-2.5 text-[12px] text-(--ice-3)">{r.cashBoxName ?? r.bankAccountLabel ?? "—"}</td>
                <td className="px-3 py-2.5 text-[12px] text-(--ice-3)">{r.referenceNo ?? "—"}</td>
                <td className="px-3 py-2.5 text-[12px] text-(--ice-3)">{fa(r.lines.length)}</td>
              </tr>
            ))}
            {past?.length === 0 && (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  هنوز پرداختی ثبت نشده است.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
