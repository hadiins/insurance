import { useEffect, useState } from "react";
import { useDraftState } from "../shell/useDraftState";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";
import { MoneyInput } from "../../components/MoneyInput";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

interface ExpenseCategoryDto {
  id: string;
  name: string;
  isActive: boolean;
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

interface ExpenseDto {
  id: string;
  title: string;
  amount: number;
  date: string;
  categoryId: string;
  categoryName: string;
  methodType: string;
  cashBoxName: string | null;
  bankAccountLabel: string | null;
}

interface ExpenseByCategoryRow {
  categoryName: string;
  count: number;
  amount: number;
}

interface ExpensesReportDto {
  totalAmount: number;
  count: number;
  byCategory: ExpenseByCategoryRow[];
  rows: ExpenseDto[];
}

type MethodType = "Cash" | "BankTransfer";

const TODAY = new Date().toISOString().slice(0, 10);
const MONTH_AGO = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);

/** «ثبت و گزارش هزینه‌ها» — Stage 7/7: the agency's own operating costs, feeding the P&L as a real
 * third expense line (never a discount, never a derived figure like default write-off). */
export function ExpensesPage() {
  const [categories, setCategories] = useState<ExpenseCategoryDto[]>([]);
  const [cashBoxes, setCashBoxes] = useState<CashBoxDto[]>([]);
  const [bankAccounts, setBankAccounts] = useState<BankAccountDto[]>([]);

  const [form, setForm] = useDraftState("expense-form", {
    title: "",
    amount: "",
    date: TODAY,
    categoryId: "",
    methodType: "Cash" as MethodType,
    cashBoxId: "",
    bankAccountId: "",
  });
  const title = form.title;
  const amount = form.amount;
  const date = form.date;
  const categoryId = form.categoryId;
  const methodType = form.methodType;
  const cashBoxId = form.cashBoxId;
  const bankAccountId = form.bankAccountId;
  const [saving, setSaving] = useState(false);

  const [from, setFrom] = useState(MONTH_AGO);
  const [to, setTo] = useState(TODAY);
  const [report, setReport] = useState<ExpensesReportDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.get<ExpenseCategoryDto[]>("/settings/expense-categories").then(setCategories).catch(() => {});
    api
      .get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes")
      .then((list) => {
        setCashBoxes(list);
        const active = list.find((b) => b.isActive);
        if (active) setForm({ ...form, cashBoxId: active.id });
      })
      .catch(() => {});
    api.get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts").then(setBankAccounts).catch(() => {});
    loadReport();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function loadReport() {
    setBusy(true);
    setError(null);
    try {
      const result = await api.get<ExpensesReportDto>(`/expenses?from=${from}&to=${to}`);
      setReport(result);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "دریافت گزارش ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function submit() {
    const numericAmount = Number(amount);
    if (!title.trim()) {
      setError("عنوان هزینه الزامی است.");
      return;
    }
    if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError("مبلغ هزینه باید مثبت باشد.");
      return;
    }
    if (!categoryId) {
      setError("انتخاب دستهٔ هزینه الزامی است.");
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
      await api.post("/expenses", {
        title: title.trim(),
        amount: numericAmount,
        date,
        categoryId,
        methodType,
        cashBoxId: methodType === "Cash" ? cashBoxId : null,
        bankAccountId: methodType === "BankTransfer" ? bankAccountId : null,
      });
      setForm({ ...form, title: "", amount: "" });
      loadReport();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت هزینه ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        هزینه‌های <em className="font-extralight not-italic text-(--ice-2)">نمایندگی</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">ثبت هزینه و گزارش به‌تفکیک دسته</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3.5 text-[13.5px] font-semibold text-(--ice)">ثبت هزینهٔ جدید</div>
        <div className="mb-3.5 grid grid-cols-4 gap-3">
          <div className="col-span-2">
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">عنوان</label>
            <input
              value={title}
              onChange={(e) => setForm({ ...form, title: e.target.value })}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
            />
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">مبلغ (تومان)</label>
            <MoneyInput value={amount} onChange={(v) => setForm({ ...form, amount: v })} />
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">تاریخ</label>
            <JalaliDateField value={date} onChange={(iso) => setForm({ ...form, date: iso })} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)" />
          </div>
        </div>
        <div className="mb-3.5 grid grid-cols-4 gap-3">
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">دسته</label>
            <select
              value={categoryId}
              onChange={(e) => setForm({ ...form, categoryId: e.target.value })}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
            >
              <option value="">انتخاب کنید…</option>
              {categories.filter((c) => c.isActive).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">روش پرداخت</label>
            <select
              value={methodType}
              onChange={(e) => setForm({ ...form, methodType: e.target.value as MethodType })}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
            >
              <option value="Cash">نقدی</option>
              <option value="BankTransfer">واریز بانکی</option>
            </select>
          </div>
          {methodType === "Cash" ? (
            <div className="col-span-2">
              <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">صندوق</label>
              <select
                value={cashBoxId}
                onChange={(e) => setForm({ ...form, cashBoxId: e.target.value })}
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
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
            <div className="col-span-2">
              <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">حساب بانکی</label>
              <select
                value={bankAccountId}
                onChange={(e) => setForm({ ...form, bankAccountId: e.target.value })}
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
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
        </div>
        <button
          type="button"
          onClick={submit}
          disabled={saving}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {saving ? "در حال ثبت…" : "ثبت هزینه"}
        </button>
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3.5 grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">از تاریخ</label>
            <JalaliDateField value={from} onChange={setFrom} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)" />
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">تا تاریخ</label>
            <JalaliDateField value={to} onChange={setTo} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)" />
          </div>
        </div>
        <button
          type="button"
          onClick={loadReport}
          disabled={busy}
          className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice) disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال محاسبه…" : "دریافت گزارش"}
        </button>
      </div>

      {report && (
        <>
          <div className="mb-4.5 grid grid-cols-2 gap-3">
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">جمع هزینه</div>
              <div className="text-[20px] font-extrabold tracking-tight text-(--ember)">{money(report.totalAmount)}</div>
            </div>
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">تعداد هزینه</div>
              <div className="text-[20px] font-extrabold tracking-tight text-(--ice)">{fa(report.count)}</div>
            </div>
          </div>

          {report.byCategory.length === 0 ? (
            <div className="mb-4.5">
              <EmptyState
                icon="🧾"
                title="هزینه‌ای در این بازه نیست"
                description="در بازهٔ انتخابی هزینه‌ای ثبت نشده است؛ بازهٔ زمانی را تغییر دهید."
              />
            </div>
          ) : (
            <div className="mb-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">به‌تفکیک دسته</div>
              <table className="w-full border-collapse">
                <tbody>
                  {report.byCategory.map((c) => (
                    <Tr key={c.categoryName}>
                      <Td className="py-2.5 text-(--ice-3)">{c.categoryName}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-3)">{fa(c.count)} مورد</Td>
                      <Td className="py-2.5 text-end font-bold text-(--ice)">{money(c.amount)}</Td>
                    </Tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {report.rows.length === 0 ? (
            <EmptyState
              icon="🧾"
              title="هزینه‌ای در این بازه نیست"
              description="در بازهٔ انتخابی هزینه‌ای ثبت نشده است؛ بازهٔ زمانی را تغییر دهید."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["تاریخ", "عنوان", "دسته", "مبلغ", "روش", "محل پرداخت"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {report.rows.map((r) => (
                  <Tr key={r.id}>
                    <Td className="py-2.5 tabular-nums">{toJalaliDisplay(r.date)}</Td>
                    <Td className="py-2.5 font-semibold">{r.title}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{r.categoryName}</Td>
                    <Td className="py-2.5 font-bold">{money(r.amount)}</Td>
                    <Td className="py-2.5 !text-[12.5px] text-(--ice-3)">{r.methodType === "Cash" ? "نقدی" : "واریز بانکی"}</Td>
                    <Td className="py-2.5 !text-[12.5px] text-(--ice-3)">{r.cashBoxName ?? r.bankAccountLabel ?? "—"}</Td>
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
