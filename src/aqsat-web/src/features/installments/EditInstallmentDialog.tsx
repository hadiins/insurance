import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { JalaliDateField } from "../../components/JalaliDateField";
import { MoneyInput } from "../../components/MoneyInput";

export interface EditInstallmentTarget {
  installmentId: string;
  seqNo: number;
  dueDate: string;
  amount: number;
  status: string;
}

/** PUT /api/installments/{id} already recalculates the settlement deadline (with the holiday
 * service) and rejects settled installments — this dialog only collects the new values. */
export function EditInstallmentDialog({
  target,
  onClose,
  onSaved,
}: {
  target: EditInstallmentTarget;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [dueDate, setDueDate] = useState(target.dueDate);
  const [amount, setAmount] = useState(String(target.amount));
  const [shiftFollowing, setShiftFollowing] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setDueDate(target.dueDate);
    setAmount(String(target.amount));
    setShiftFollowing(true);
    setError(null);
  }, [target]);

  const settled = target.status === "Settled";

  async function save() {
    const parsedAmount = amount.trim() === "" ? null : Number(amount);
    if (parsedAmount !== null && !Number.isFinite(parsedAmount)) {
      setError("مبلغ واردشده معتبر نیست.");
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await api.put(`/installments/${target.installmentId}`, {
        dueDate,
        amount: parsedAmount,
        shiftFollowing,
      });
      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیرهٔ تغییرات ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-black/45 p-4"
      onClick={settled ? onClose : undefined}
    >
      <div
        className="w-full max-w-md rounded-2xl border border-(--edge-2) bg-(--pane) p-5"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="mb-1 text-[15px] font-extrabold text-(--ice)">
          ویرایش قسط {new Intl.NumberFormat("fa-IR").format(target.seqNo)}
        </h3>
        <div className="mb-4 text-[11.5px] text-(--ice-3)">
          با تغییر سررسید، مهلت تسویه با احتساب تعطیلات رسمی بازمحاسبه می‌شود.
        </div>

        {settled && (
          <div className="mb-3 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
            قسط تسویه‌شده قابل ویرایش نیست.
          </div>
        )}

        {!settled && (
          <>
            <label className="mb-1.5 block text-[11.5px] text-(--ice-3)">سررسید</label>
            <div className="mb-3.5">
              <JalaliDateField value={dueDate} onChange={setDueDate} />
            </div>

            <label className="mb-1.5 block text-[11.5px] text-(--ice-3)">مبلغ (تومان)</label>
            <MoneyInput value={amount} onChange={setAmount} className="mb-3.5" />

            <label className="flex cursor-pointer items-center gap-2 text-[12.5px] text-(--ice-2)">
              <input
                type="checkbox"
                checked={shiftFollowing}
                onChange={(e) => setShiftFollowing(e.target.checked)}
                className="h-3.5 w-3.5 accent-(--mint)"
              />
              اقساط بعدی نیز بر اساس این تاریخ بازچین شوند (فاصلهٔ ماهانهٔ شمسی حفظ می‌شود)
            </label>
          </>
        )}

        {error && (
          <div className="mb-3 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
            {error}
          </div>
        )}

        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-[10px] border border-(--edge-2) px-4 py-2 text-[12.5px] text-(--ice-2) transition-colors hover:bg-(--hov)"
          >
            انصراف
          </button>
          <button
            type="button"
            disabled={busy || settled || dueDate === ""}
            onClick={save}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:opacity-50"
          >
            {busy ? "در حال ذخیره…" : "ذخیره"}
          </button>
        </div>
      </div>
    </div>
  );
}
