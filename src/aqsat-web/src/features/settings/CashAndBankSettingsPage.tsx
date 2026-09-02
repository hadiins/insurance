import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { money } from "../../lib/persian";

interface CashBoxDto {
  id: string;
  name: string;
  isActive: boolean;
  openingBalance: number;
}

interface BankAccountDto {
  id: string;
  bankName: string;
  accountNumber: string;
  accountHolderName: string | null;
  isActive: boolean;
  openingBalance: number;
}

interface BankDto {
  id: string;
  name: string;
  isActive: boolean;
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)";

/** «صندوق و بانک‌ها» — agency-defined receipt destinations shown as a dropdown on every cash-receipt
 * form (down payment, installment payment, non-installment full payment). */
export function CashAndBankSettingsPage() {
  const [boxes, setBoxes] = useState<CashBoxDto[] | null>(null);
  const [accounts, setAccounts] = useState<BankAccountDto[] | null>(null);
  const [banks, setBanks] = useState<BankDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [newBoxName, setNewBoxName] = useState("");
  const [newBoxOpening, setNewBoxOpening] = useState("");
  const [newBankName, setNewBankName] = useState("");
  const [newAccountNumber, setNewAccountNumber] = useState("");
  const [newHolderName, setNewHolderName] = useState("");
  const [newAccountOpening, setNewAccountOpening] = useState("");
  const [newBankListName, setNewBankListName] = useState("");

  function reload() {
    api
      .get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes")
      .then(setBoxes)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری صندوق‌ها"));
    api
      .get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts")
      .then(setAccounts)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری حساب‌های بانکی"));
    api
      .get<BankDto[]>("/settings/cash-and-bank/banks")
      .then(setBanks)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست بانک‌ها"));
  }

  useEffect(reload, []);

  async function addBox() {
    if (!newBoxName.trim()) return;
    setError(null);
    try {
      await api.post("/settings/cash-and-bank/cash-boxes", {
        name: newBoxName.trim(),
        openingBalance: Number(newBoxOpening.replace(/[^\d.]/g, "")) || 0,
      });
      setNewBoxName("");
      setNewBoxOpening("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "افزودن صندوق ناموفق بود.");
    }
  }

  async function toggleBox(b: CashBoxDto) {
    setError(null);
    try {
      await api.put(`/settings/cash-and-bank/cash-boxes/${b.id}`, {
        name: b.name,
        isActive: !b.isActive,
        openingBalance: b.openingBalance,
      });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی ناموفق بود.");
    }
  }

  async function saveBoxOpening(b: CashBoxDto, openingBalance: number) {
    setError(null);
    try {
      await api.put(`/settings/cash-and-bank/cash-boxes/${b.id}`, {
        name: b.name,
        isActive: b.isActive,
        openingBalance,
      });
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
        openingBalance: Number(newAccountOpening.replace(/[^\d.]/g, "")) || 0,
      });
      setNewBankName("");
      setNewAccountNumber("");
      setNewHolderName("");
      setNewAccountOpening("");
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
        openingBalance: a.openingBalance,
      });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی ناموفق بود.");
    }
  }

  async function saveAccountOpening(a: BankAccountDto, openingBalance: number) {
    setError(null);
    try {
      await api.put(`/settings/cash-and-bank/bank-accounts/${a.id}`, {
        bankName: a.bankName,
        accountNumber: a.accountNumber,
        accountHolderName: a.accountHolderName,
        isActive: a.isActive,
        openingBalance,
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

  async function addBank() {
    if (!newBankListName.trim()) return;
    setError(null);
    try {
      await api.post("/settings/cash-and-bank/banks", { name: newBankListName.trim() });
      setNewBankListName("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "افزودن بانک ناموفق بود.");
    }
  }

  async function toggleBank(b: BankDto) {
    setError(null);
    try {
      await api.put(`/settings/cash-and-bank/banks/${b.id}`, { name: b.name, isActive: !b.isActive });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی ناموفق بود.");
    }
  }

  async function removeBank(id: string) {
    setError(null);
    try {
      await api.delete(`/settings/cash-and-bank/banks/${id}`);
      setBanks((prev) => prev?.filter((b) => b.id !== id) ?? prev);
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
              {["نام", "ماندهٔ ابتدای دوره (تومان)", "وضعیت", ""].map((h) => (
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
                  <OpeningBalanceInput
                    value={b.openingBalance}
                    onSave={(v) => saveBoxOpening(b, v)}
                  />
                </td>
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
                <td colSpan={4} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
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
          <div className="w-44">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">ماندهٔ ابتدای دوره</label>
            <input value={newBoxOpening} onChange={(e) => setNewBoxOpening(e.target.value)} dir="ltr" placeholder="۰" className={inputClass} />
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
              {["بانک", "شمارهٔ حساب", "صاحب حساب", "ماندهٔ ابتدای دوره (تومان)", "وضعیت", ""].map((h) => (
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
                  <OpeningBalanceInput
                    value={a.openingBalance}
                    onSave={(v) => saveAccountOpening(a, v)}
                  />
                </td>
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
                <td colSpan={6} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  هنوز حساب بانکی ثبت نشده است.
                </td>
              </tr>
            )}
          </tbody>
        </table>
        <div className="flex items-end gap-2 border-t border-(--edge) p-3">
          <div className="w-40">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">نام بانک</label>
            <select value={newBankName} onChange={(e) => setNewBankName(e.target.value)} className={inputClass}>
              <option value="">انتخاب کنید…</option>
              {banks?.filter((b) => b.isActive).map((b) => (
                <option key={b.id} value={b.name}>
                  {b.name}
                </option>
              ))}
            </select>
          </div>
          <div className="w-40">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">شمارهٔ حساب</label>
            <input value={newAccountNumber} onChange={(e) => setNewAccountNumber(e.target.value)} dir="ltr" className={inputClass} />
          </div>
          <div className="flex-1">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">صاحب حساب (اختیاری)</label>
            <input value={newHolderName} onChange={(e) => setNewHolderName(e.target.value)} className={inputClass} />
          </div>
          <div className="w-44">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">ماندهٔ ابتدای دوره</label>
            <input value={newAccountOpening} onChange={(e) => setNewAccountOpening(e.target.value)} dir="ltr" placeholder="۰" className={inputClass} />
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

      <div className="mt-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">
          فهرست بانک‌ها
          <span className="ms-2 text-[11px] font-normal text-(--ice-3)">
            نام بانک‌ها در فرم‌های ثبت چک به‌صورت کشویی نمایش داده می‌شود
          </span>
        </div>
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["نام بانک", "وضعیت", ""].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {banks?.map((b) => (
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
                  <button type="button" onClick={() => toggleBank(b)} className="ms-2 text-[11px] text-(--ice-3) hover:text-(--ice)">
                    {b.isActive ? "غیرفعال کردن" : "فعال کردن"}
                  </button>
                  <button type="button" onClick={() => removeBank(b.id)} className="ms-2 text-[11px] text-(--ember) hover:brightness-110">
                    حذف
                  </button>
                </td>
              </tr>
            ))}
            {banks?.length === 0 && (
              <tr>
                <td colSpan={3} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  هنوز بانکی ثبت نشده است.
                </td>
              </tr>
            )}
          </tbody>
        </table>
        <div className="flex items-end gap-2 border-t border-(--edge) p-3">
          <div className="flex-1">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">نام بانک (اگر در فهرست نیست)</label>
            <input value={newBankListName} onChange={(e) => setNewBankListName(e.target.value)} className={inputClass} />
          </div>
          <button
            type="button"
            onClick={addBank}
            disabled={!newBankListName.trim()}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            + افزودن
          </button>
        </div>
      </div>
    </div>
  );
}

/** Inline editable opening balance — Persian display while idle, plain digits while editing;
 * saves on blur (only when the value actually changed). */
function OpeningBalanceInput({ value, onSave }: { value: number; onSave: (v: number) => void }) {
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState("");

  function commit() {
    setEditing(false);
    const parsed = Number(text.replace(/[^\d.]/g, "")) || 0;
    if (parsed !== value) onSave(parsed);
  }

  return editing ? (
    <input
      autoFocus
      value={text}
      onChange={(e) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === "Enter") commit();
        if (e.key === "Escape") setEditing(false);
      }}
      dir="ltr"
      className="w-32 rounded-[8px] border border-(--mint) bg-(--fld) px-2 py-1 text-[12px] tabular-nums text-(--ice) outline-none"
    />
  ) : (
    <button
      type="button"
      onClick={() => {
        setText(String(value));
        setEditing(true);
      }}
      className="tabular-nums text-(--ice-2) hover:text-(--ice)"
      title="برای ویرایش کلیک کنید"
    >
      {money(value)}
    </button>
  );
}
