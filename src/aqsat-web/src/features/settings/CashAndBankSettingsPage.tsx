import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";

interface CashBoxDto {
  id: string;
  name: string;
  isActive: boolean;
}

interface BankAccountDto {
  id: string;
  bankName: string;
  accountNumber: string;
  accountHolderName: string | null;
  isActive: boolean;
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)";

/** «صندوق و بانک‌ها» — agency-defined receipt destinations shown as a dropdown on every cash-receipt
 * form (down payment, installment payment, non-installment full payment). */
export function CashAndBankSettingsPage() {
  const [boxes, setBoxes] = useState<CashBoxDto[] | null>(null);
  const [accounts, setAccounts] = useState<BankAccountDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [newBoxName, setNewBoxName] = useState("");
  const [newBankName, setNewBankName] = useState("");
  const [newAccountNumber, setNewAccountNumber] = useState("");
  const [newHolderName, setNewHolderName] = useState("");

  function reload() {
    api
      .get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes")
      .then(setBoxes)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری صندوق‌ها"));
    api
      .get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts")
      .then(setAccounts)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری حساب‌های بانکی"));
  }

  useEffect(reload, []);

  async function addBox() {
    if (!newBoxName.trim()) return;
    setError(null);
    try {
      await api.post("/settings/cash-and-bank/cash-boxes", { name: newBoxName.trim() });
      setNewBoxName("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "افزودن صندوق ناموفق بود.");
    }
  }

  async function toggleBox(b: CashBoxDto) {
    setError(null);
    try {
      await api.put(`/settings/cash-and-bank/cash-boxes/${b.id}`, { name: b.name, isActive: !b.isActive });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی ناموفق بود.");
    }
  }

  async function removeBox(id: string) {
    setError(null);
    try {
      await api.delete(`/settings/cash-and-bank/cash-boxes/${id}`);
      setBoxes((prev) => prev?.filter((b) => b.id !== id) ?? prev);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "حذف ناموفق بود.");
    }
  }

  async function addAccount() {
    if (!newBankName.trim() || !newAccountNumber.trim()) return;
    setError(null);
    try {
      await api.post("/settings/cash-and-bank/bank-accounts", {
        bankName: newBankName.trim(),
        accountNumber: newAccountNumber.trim(),
        accountHolderName: newHolderName.trim() || null,
      });
      setNewBankName("");
      setNewAccountNumber("");
      setNewHolderName("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "افزودن حساب ناموفق بود.");
    }
  }

  async function toggleAccount(a: BankAccountDto) {
    setError(null);
    try {
      await api.put(`/settings/cash-and-bank/bank-accounts/${a.id}`, {
        bankName: a.bankName,
        accountNumber: a.accountNumber,
        accountHolderName: a.accountHolderName,
        isActive: !a.isActive,
      });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی ناموفق بود.");
    }
  }

  async function removeAccount(id: string) {
    setError(null);
    try {
      await api.delete(`/settings/cash-and-bank/bank-accounts/${id}`);
      setAccounts((prev) => prev?.filter((a) => a.id !== id) ?? prev);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "حذف ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-4.5 text-xl font-extrabold tracking-tight text-(--ice)">
        صندوق و <em className="font-extralight not-italic text-(--ice-2)">بانک‌ها</em>
      </h2>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">صندوق‌های نقدی</div>
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["نام", "وضعیت", ""].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {boxes?.map((b) => (
              <tr key={b.id} className="border-t border-(--edge) first:border-t-0">
                <td className="px-3 py-2.5 text-[13px] font-semibold">{b.name}</td>
                <td className="px-3 py-2.5 text-[13px]">
                  <span
                    className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${b.isActive ? "bg-(--mint)/12 text-(--mint)" : "bg-(--ice-3)/12 text-(--ice-3)"}`}
                  >
                    {b.isActive ? "فعال" : "غیرفعال"}
                  </span>
                </td>
                <td className="px-3 py-2.5 text-[13px]">
                  <button type="button" onClick={() => toggleBox(b)} className="ms-2 text-[11px] text-(--ice-3) hover:text-(--ice)">
                    {b.isActive ? "غیرفعال کردن" : "فعال کردن"}
                  </button>
                  <button type="button" onClick={() => removeBox(b.id)} className="ms-2 text-[11px] text-(--ember) hover:brightness-110">
                    حذف
                  </button>
                </td>
              </tr>
            ))}
            {boxes?.length === 0 && (
              <tr>
                <td colSpan={3} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  هنوز صندوقی ثبت نشده است.
                </td>
              </tr>
            )}
          </tbody>
        </table>
        <div className="flex items-end gap-2 border-t border-(--edge) p-3">
          <div className="flex-1">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">نام صندوق</label>
            <input value={newBoxName} onChange={(e) => setNewBoxName(e.target.value)} className={inputClass} />
          </div>
          <button
            type="button"
            onClick={addBox}
            disabled={!newBoxName.trim()}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            + افزودن
          </button>
        </div>
      </div>

      <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">حساب‌های بانکی</div>
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["بانک", "شمارهٔ حساب", "صاحب حساب", "وضعیت", ""].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {accounts?.map((a) => (
              <tr key={a.id} className="border-t border-(--edge) first:border-t-0">
                <td className="px-3 py-2.5 text-[13px] font-semibold">{a.bankName}</td>
                <td className="px-3 py-2.5 text-[13px] tabular-nums" dir="ltr">
                  {a.accountNumber}
                </td>
                <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{a.accountHolderName ?? "—"}</td>
                <td className="px-3 py-2.5 text-[13px]">
                  <span
                    className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${a.isActive ? "bg-(--mint)/12 text-(--mint)" : "bg-(--ice-3)/12 text-(--ice-3)"}`}
                  >
                    {a.isActive ? "فعال" : "غیرفعال"}
                  </span>
                </td>
                <td className="px-3 py-2.5 text-[13px]">
                  <button type="button" onClick={() => toggleAccount(a)} className="ms-2 text-[11px] text-(--ice-3) hover:text-(--ice)">
                    {a.isActive ? "غیرفعال کردن" : "فعال کردن"}
                  </button>
                  <button type="button" onClick={() => removeAccount(a.id)} className="ms-2 text-[11px] text-(--ember) hover:brightness-110">
                    حذف
                  </button>
                </td>
              </tr>
            ))}
            {accounts?.length === 0 && (
              <tr>
                <td colSpan={5} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  هنوز حساب بانکی ثبت نشده است.
                </td>
              </tr>
            )}
          </tbody>
        </table>
        <div className="flex items-end gap-2 border-t border-(--edge) p-3">
          <div className="w-40">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">نام بانک</label>
            <input value={newBankName} onChange={(e) => setNewBankName(e.target.value)} className={inputClass} />
          </div>
          <div className="w-40">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">شمارهٔ حساب</label>
            <input value={newAccountNumber} onChange={(e) => setNewAccountNumber(e.target.value)} dir="ltr" className={inputClass} />
          </div>
          <div className="flex-1">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">صاحب حساب (اختیاری)</label>
            <input value={newHolderName} onChange={(e) => setNewHolderName(e.target.value)} className={inputClass} />
          </div>
          <button
            type="button"
            onClick={addAccount}
            disabled={!newBankName.trim() || !newAccountNumber.trim()}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            + افزودن
          </button>
        </div>
      </div>
    </div>
  );
}
