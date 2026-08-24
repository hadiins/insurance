import * as Dialog from "@radix-ui/react-dialog";
import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { MoneyInput } from "../../components/MoneyInput";

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

type MethodType = "Cash" | "BankTransfer" | "Cheque";

interface AllocationLineDto {
  installmentId: string;
  seqNo: number;
  policyNumber: string;
  amount: number;
}

interface PaymentResultDto {
  paymentId: string;
  amount: number;
  allocations: AllocationLineDto[];
  unallocatedAmount: number;
}

export function RecordPaymentDialog({
  installmentId,
  customerFullName,
  suggestedAmount,
  onClose,
  onRecorded,
}: {
  installmentId: string;
  customerFullName: string;
  suggestedAmount: number;
  onClose: () => void;
  onRecorded: () => void;
}) {
  const [amount, setAmount] = useState(String(Math.round(suggestedAmount)));
  const [methodType, setMethodType] = useState<MethodType>("Cash");
  const [cashBoxId, setCashBoxId] = useState("");
  const [bankAccountId, setBankAccountId] = useState("");
  const [cashBoxes, setCashBoxes] = useState<CashBoxDto[]>([]);
  const [bankAccounts, setBankAccounts] = useState<BankAccountDto[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<PaymentResultDto | null>(null);

  useEffect(() => {
    api
      .get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes")
      .then((list) => {
        setCashBoxes(list);
        const active = list.find((b) => b.isActive);
        if (active) setCashBoxId(active.id);
      })
      .catch(() => {});
    api
      .get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts")
      .then(setBankAccounts)
      .catch(() => {});
  }, []);

  const methodLabel: Record<MethodType, string> = { Cash: "نقدی", BankTransfer: "واریز بانکی", Cheque: "چک" };

  async function submit() {
    const numericAmount = Number(amount);
    if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError("مبلغ پرداخت باید مثبت باشد.");
      return;
    }
    if (methodType === "Cash" && !cashBoxId) {
      setError("انتخاب صندوق الزامی است.");
      return;
    }
    if (methodType !== "Cash" && !bankAccountId) {
      setError("انتخاب حساب بانکی الزامی است.");
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      const paidOn = new Date().toISOString().slice(0, 10);
      const recorded = await api.post<PaymentResultDto>("/payments", {
        installmentIdHint: installmentId,
        amount: numericAmount,
        paidOn,
        method: methodLabel[methodType],
        methodType,
        cashBoxId: methodType === "Cash" ? cashBoxId : null,
        bankAccountId: methodType !== "Cash" ? bankAccountId : null,
      });
      setResult(recorded);
      onRecorded();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت پرداخت ناموفق بود.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog.Root open onOpenChange={(open) => !open && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[60] bg-black/55" />
        <Dialog.Content className="fixed inset-0 z-[60] grid place-items-center p-5">
          <div className="w-full max-w-[380px] rounded-2xl border border-(--edge-2) bg-(--slate) p-5.5 shadow-[var(--sh)]">
            <Dialog.Title className="mb-1.5 text-[15px] font-bold text-(--ice)">ثبت پرداخت</Dialog.Title>
            <Dialog.Description className="mb-4.5 text-[13px] text-(--ice-2)">{customerFullName}</Dialog.Description>

            {result ? (
              <>
                <div className="mb-4 space-y-2">
                  {result.allocations.map((line) => (
                    <div
                      key={line.installmentId}
                      className="flex items-center justify-between rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px]"
                    >
                      <span className="text-(--ice-2)">قسط شمارهٔ {fa(line.seqNo)}</span>
                      <span className="font-bold text-(--ice)">{money(line.amount)}</span>
                    </div>
                  ))}
                  {result.unallocatedAmount > 0 && (
                    <div className="rounded-[10px] border border-(--amber)/30 bg-(--amber)/10 px-3 py-2 text-[12px] text-(--amber)">
                      {money(result.unallocatedAmount)} تومان مازاد — به‌عنوان اعتبار مشتری باقی ماند.
                    </div>
                  )}
                </div>
                <button
                  type="button"
                  onClick={onClose}
                  className="w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105"
                >
                  بستن
                </button>
              </>
            ) : (
              <>
                {error && (
                  <div className="mb-3 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12px] text-(--ember)">
                    {error}
                  </div>
                )}

                <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">مبلغ (تومان)</label>
                <MoneyInput
                  value={amount}
                  onChange={setAmount}
                  className="mb-3.5 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] tabular-nums text-(--ice) outline-none focus:border-(--mint)"
                />

                <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">روش پرداخت</label>
                <select
                  value={methodType}
                  onChange={(e) => setMethodType(e.target.value as MethodType)}
                  className="mb-3.5 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
                >
                  <option value="Cash">نقدی</option>
                  <option value="BankTransfer">واریز بانکی</option>
                  <option value="Cheque">چک</option>
                </select>

                {methodType === "Cash" ? (
                  <>
                    <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">صندوق</label>
                    <select
                      value={cashBoxId}
                      onChange={(e) => setCashBoxId(e.target.value)}
                      className="mb-4.5 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
                    >
                      <option value="">انتخاب کنید…</option>
                      {cashBoxes.filter((b) => b.isActive).map((b) => (
                        <option key={b.id} value={b.id}>
                          {b.name}
                        </option>
                      ))}
                    </select>
                  </>
                ) : (
                  <>
                    <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">حساب بانکی</label>
                    <select
                      value={bankAccountId}
                      onChange={(e) => setBankAccountId(e.target.value)}
                      className="mb-4.5 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
                    >
                      <option value="">انتخاب کنید…</option>
                      {bankAccounts.filter((a) => a.isActive).map((a) => (
                        <option key={a.id} value={a.id}>
                          {a.bankName} — {a.accountNumber}
                        </option>
                      ))}
                    </select>
                  </>
                )}

                <div className="flex gap-2">
                  <button
                    type="button"
                    onClick={submit}
                    disabled={submitting}
                    className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {submitting ? "در حال ثبت…" : "ثبت پرداخت"}
                  </button>
                  <button
                    type="button"
                    onClick={onClose}
                    className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
                  >
                    انصراف
                  </button>
                </div>
              </>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
