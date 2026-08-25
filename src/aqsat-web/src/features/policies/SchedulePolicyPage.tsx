import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money, toLatinDigits } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { MoneyInput } from "../../components/MoneyInput";
import { JalaliDateField } from "../../components/JalaliDateField";

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

interface PendingSchedulePolicyDto {
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  contractName: string;
  totalReceivable: number;
}

interface InstallmentDto {
  seqNo: number;
  dueDate: string;
  amount: number;
}

interface ScheduleResultDto {
  policyId: string;
  exceedsMaxInstallments: boolean;
  installments: InstallmentDto[];
}

/** docs/TASKS.md Task 8 — the second half of issuance: NetPremium + ServiceFee are captured at
 * creation time; down payment and installment count are a deliberately separate step here,
 * applied against whichever policies still have InstallmentCount == 0. */
export function SchedulePolicyPage() {
  const [pending, setPending] = useState<PendingSchedulePolicyDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [selected, setSelected] = useState<PendingSchedulePolicyDto | null>(null);
  const [installmentCount, setInstallmentCount] = useState("");
  const [downPayment, setDownPayment] = useState("");
  const [downPaymentTouched, setDownPaymentTouched] = useState(false);
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<ScheduleResultDto | null>(null);
  const [scheduledDownPayment, setScheduledDownPayment] = useState(0);

  const [cashBoxes, setCashBoxes] = useState<CashBoxDto[]>([]);
  const [bankAccounts, setBankAccounts] = useState<BankAccountDto[]>([]);
  const [receivePaidOn, setReceivePaidOn] = useState(new Date().toISOString().slice(0, 10));
  const [receiveMethodType, setReceiveMethodType] = useState<MethodType>("Cash");
  const [receiveCashBoxId, setReceiveCashBoxId] = useState("");
  const [receiveBankAccountId, setReceiveBankAccountId] = useState("");
  const [receiveReferenceNo, setReceiveReferenceNo] = useState("");
  const [receiving, setReceiving] = useState(false);
  const [received, setReceived] = useState(false);

  useEffect(() => {
    api
      .get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes")
      .then((list) => {
        setCashBoxes(list);
        const active = list.find((b) => b.isActive);
        if (active) setReceiveCashBoxId(active.id);
      })
      .catch(() => {});
    api
      .get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts")
      .then(setBankAccounts)
      .catch(() => {});
  }, []);

  function reload() {
    api
      .get<PendingSchedulePolicyDto[]>("/policies/pending-schedule")
      .then((data) => {
        setPending(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
  }

  useEffect(reload, []);

  function selectPolicy(p: PendingSchedulePolicyDto) {
    setSelected(p);
    setInstallmentCount("");
    setDownPayment("");
    setDownPaymentTouched(false);
    setResult(null);
    setError(null);
    setReceived(false);
    setReceiveReferenceNo("");
    setReceivePaidOn(new Date().toISOString().slice(0, 10));
  }

  useEffect(() => {
    if (!selected || downPaymentTouched) return;
    const count = Number(installmentCount);
    if (!Number.isFinite(count) || count <= 0) return;
    const handle = setTimeout(() => {
      api
        .get<number>(`/policies/${selected.policyId}/suggest-down-payment?installmentCount=${count}`)
        .then((suggested) => setDownPayment(String(suggested)))
        .catch(() => {});
    }, 300);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [installmentCount, selected, downPaymentTouched]);

  async function submitSchedule() {
    if (!selected) return;
    const count = Number(installmentCount);
    if (!Number.isFinite(count) || count <= 0) {
      setError("تعداد اقساط باید مثبت باشد.");
      return;
    }
    const down = Number(downPayment) || 0;

    setSaving(true);
    setError(null);
    try {
      const created = await api.post<ScheduleResultDto>(`/policies/${selected.policyId}/schedule`, {
        downPayment: down,
        installmentCount: count,
      });
      setResult(created);
      setScheduledDownPayment(down);
      setPending((prev) => prev?.filter((p) => p.policyId !== selected.policyId) ?? prev);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "زمان‌بندی ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  async function receiveDownPayment() {
    if (!selected) return;
    if (receiveMethodType === "Cash" && !receiveCashBoxId) {
      setError("انتخاب صندوق الزامی است.");
      return;
    }
    if (receiveMethodType !== "Cash" && !receiveBankAccountId) {
      setError("انتخاب حساب بانکی الزامی است.");
      return;
    }

    setReceiving(true);
    setError(null);
    try {
      await api.post(`/policies/${selected.policyId}/receive-down-payment`, {
        paidOn: receivePaidOn,
        referenceNo: receiveReferenceNo.trim() || null,
        methodType: receiveMethodType,
        cashBoxId: receiveMethodType === "Cash" ? receiveCashBoxId : null,
        bankAccountId: receiveMethodType !== "Cash" ? receiveBankAccountId : null,
      });
      setReceived(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت پیش‌پرداخت ناموفق بود.");
    } finally {
      setReceiving(false);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        زمان‌بندی <em className="font-extralight not-italic text-(--ice-2)">اقساط</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">
        بیمه‌نامه‌هایی که هنوز پیش‌پرداخت و تعداد اقساط برایشان تعیین نشده
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && pending === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {pending !== null && (
        <div className="grid grid-cols-2 gap-4">
          <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
            <table className="w-full border-collapse">
              <thead>
                <tr>
                  {["شمارهٔ بیمه‌نامه", "بیمه‌گذار", "مبلغ کل", ""].map((h) => (
                    <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {pending.map((p) => (
                  <tr
                    key={p.policyId}
                    onClick={() => selectPolicy(p)}
                    className={`cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov) ${
                      selected?.policyId === p.policyId ? "bg-(--mint)/9" : ""
                    }`}
                  >
                    <td className="px-3 py-2.75 text-[13px] font-semibold">{p.policyNumber}</td>
                    <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{p.customerFullName}</td>
                    <td className="px-3 py-2.75 text-[13px] font-bold">{money(p.totalReceivable)}</td>
                    <td />
                  </tr>
                ))}
                {pending.length === 0 && (
                  <tr>
                    <td colSpan={4} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                      بیمه‌نامه‌ای در انتظار زمان‌بندی نیست.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
            {!selected ? (
              <div className="text-[12.5px] text-(--ice-3)">یک بیمه‌نامه را از فهرست انتخاب کنید.</div>
            ) : (
              <>
                <div className="mb-3 text-[13px] font-semibold text-(--ice)">
                  {selected.policyNumber} — {selected.customerFullName}
                </div>
                <div className="mb-3.5 text-[12px] text-(--ice-3)">مبلغ کل: {money(selected.totalReceivable)}</div>

                <div className="mb-3.5 grid grid-cols-2 gap-3">
                  <div>
                    <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">تعداد اقساط</label>
                    <input
                      value={fa(installmentCount)}
                      onChange={(e) => setInstallmentCount(toLatinDigits(e.target.value).replace(/[^\d]/g, ""))}
                      placeholder="۹"
                      inputMode="numeric"
                      className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] tabular-nums text-(--ice) outline-none focus:border-(--mint)"
                    />
                  </div>
                  <div>
                    <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">پیش‌پرداخت (تومان)</label>
                    <MoneyInput
                      value={downPayment}
                      onChange={(v) => {
                        setDownPayment(v);
                        setDownPaymentTouched(true);
                      }}
                      placeholder="پیشنهاد خودکار"
                    />
                  </div>
                </div>
                <div className="mb-3.5 text-[11px] text-(--ice-3)">
                  پیش‌پرداخت به‌صورت خودکار پیشنهاد می‌شود تا اقساط عدد گرد شوند — قابل ویرایش است.
                </div>

                <button
                  type="button"
                  onClick={submitSchedule}
                  disabled={saving || !installmentCount.trim()}
                  className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {saving ? "در حال ساخت اقساط…" : "ساخت اقساط"}
                </button>

                {result && (
                  <div className="mt-4 rounded-[10px] border border-(--mint)/30 bg-(--mint)/8 p-3">
                    {result.exceedsMaxInstallments && (
                      <div className="mb-2 text-[12px] text-(--amber)">
                        ⚠️ تعداد اقساط از سقف تنظیم‌شدهٔ نمایندگی بیشتر است.
                      </div>
                    )}
                    <div className="mb-2 text-[12.5px] font-semibold text-(--mint)">
                      {fa(result.installments.length)} قسط ساخته شد
                    </div>
                    <div className="max-h-48 overflow-y-auto">
                      <table className="w-full border-collapse text-[12px]">
                        <tbody>
                          {result.installments.map((i) => (
                            <tr key={i.seqNo} className="border-t border-(--edge)/50 first:border-t-0">
                              <td className="py-1 text-(--ice-3)">قسط {fa(i.seqNo)}</td>
                              <td className="py-1 text-(--ice-3)">{toJalaliDisplay(i.dueDate)}</td>
                              <td className="py-1 text-end font-semibold text-(--ice)">{money(i.amount)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>

                    {scheduledDownPayment > 0 && (
                      <div className="mt-4 border-t border-(--edge)/50 pt-3">
                        {received ? (
                          <div className="text-[12.5px] font-semibold text-(--mint)">پیش‌پرداخت با موفقیت دریافت و ثبت شد.</div>
                        ) : (
                          <>
                            <div className="mb-2 text-[12.5px] font-semibold text-(--ice)">
                              دریافت پیش‌پرداخت — {money(scheduledDownPayment)} تومان
                            </div>
                            <div className="mb-2 grid grid-cols-2 gap-2">
                              <div>
                                <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">تاریخ دریافت</label>
                                <JalaliDateField value={receivePaidOn} onChange={setReceivePaidOn} />
                              </div>
                              <div>
                                <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">روش دریافت</label>
                                <select
                                  value={receiveMethodType}
                                  onChange={(e) => setReceiveMethodType(e.target.value as MethodType)}
                                  className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
                                >
                                  <option value="Cash">نقدی</option>
                                  <option value="BankTransfer">واریز بانکی</option>
                                  <option value="Cheque">چک</option>
                                </select>
                              </div>
                            </div>
                            <div className="mb-2 grid grid-cols-2 gap-2">
                              {receiveMethodType === "Cash" ? (
                                <div>
                                  <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">صندوق</label>
                                  <select
                                    value={receiveCashBoxId}
                                    onChange={(e) => setReceiveCashBoxId(e.target.value)}
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
                                <div>
                                  <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">حساب بانکی</label>
                                  <select
                                    value={receiveBankAccountId}
                                    onChange={(e) => setReceiveBankAccountId(e.target.value)}
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
                              <div>
                                <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">شمارهٔ مرجع (اختیاری)</label>
                                <input
                                  value={receiveReferenceNo}
                                  onChange={(e) => setReceiveReferenceNo(e.target.value)}
                                  className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
                                />
                              </div>
                            </div>
                            <button
                              type="button"
                              onClick={receiveDownPayment}
                              disabled={receiving}
                              className="w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                              {receiving ? "در حال ثبت…" : "ثبت دریافت پیش‌پرداخت"}
                            </button>
                          </>
                        )}
                      </div>
                    )}
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
