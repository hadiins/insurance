import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay, toJalaliDisplay } from "../../lib/jalali";

/// The PUBLIC customer portal (docs/CUSTOMER-PORTAL-SPEC.md §3) — standalone like /owner-setup,
/// mounted before any auth check in App.tsx; the 43-char token in the URL is the credential.
/// For a policy-verification link the page is the customer's half of the staged chain
/// (owner decision 2026-09-01): pay the inquiry fee → wait for the agency's decision → read the
/// contract and installment schedule, accept → pay the down payment. Each pay button opens a
/// simulated gateway screen first (the user-confirmed Mock semantics); only its confirm click
/// hits the POST endpoint.
interface PortalInstallmentDto {
  seqNo: number;
  dueDate: string;
  amount: number;
}

interface PortalInfoDto {
  customerDisplayName: string;
  feeToman: number;
  status: "Pending" | "Paid" | "Expired";
  expiresAtUtc: string;
  stage?: string | null;
  policyNumber?: string | null;
  downPaymentAmountToman?: number | null;
  contractText?: string | null;
  installments?: PortalInstallmentDto[] | null;
}

interface PayResultDto {
  paidAmountToman: number;
  paidAtUtc: string;
}

type Phase =
  | "loading"
  | "info"
  | "feeGateway"
  | "feePaid"
  | "contract"
  | "downGateway"
  | "downPaid"
  | "rejected"
  | "error";

const PRIMARY_BTN =
  "w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50";
const SECONDARY_BTN =
  "flex-1 rounded-[10px] border border-(--edge-2) bg-transparent px-4 py-2.5 text-[13px] font-semibold text-(--ice-2) transition-colors hover:brightness-110";

export function PortalPage() {
  const token = window.location.pathname.split("/").pop() ?? "";
  const [phase, setPhase] = useState<Phase>("loading");
  const [info, setInfo] = useState<PortalInfoDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [payResult, setPayResult] = useState<PayResultDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [contractAccepted, setContractAccepted] = useState(false);

  const load = useCallback(() => {
    return api
      .get<PortalInfoDto>(`/portal/${encodeURIComponent(token)}`)
      .then((i) => {
        setInfo(i);
        setPhase((prev) => {
          if (prev === "feeGateway" || prev === "downGateway") return prev; // keep the mock gateway open
          return derivePhase(i, prev);
        });
        return i;
      })
      .catch((err) => {
        setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
        setPhase("error");
      });
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  // While the agency reviews the credit report the customer has nothing to click — poll so the
  // contract appears the moment the agency approves.
  useEffect(() => {
    if (info?.status !== "Paid" || info.stage !== "ReportReady") return;
    const h = setTimeout(() => void load(), 5000);
    return () => clearTimeout(h);
  }, [info, load]);

  async function payFee() {
    setBusy(true);
    setError(null);
    try {
      setPayResult(await api.post<PayResultDto>(`/portal/${encodeURIComponent(token)}/pay`));
      await load();
      setPhase("feePaid");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
      setPhase("info");
    } finally {
      setBusy(false);
    }
  }

  async function approveContract() {
    setBusy(true);
    setError(null);
    try {
      await api.post(`/portal/${encodeURIComponent(token)}/approve-contract`);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
    } finally {
      setBusy(false);
    }
  }

  async function payDownPayment() {
    setBusy(true);
    setError(null);
    try {
      setPayResult(
        await api.post<PayResultDto>(`/portal/${encodeURIComponent(token)}/pay-down-payment`),
      );
      setPhase("downPaid");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
      setPhase("info");
    } finally {
      setBusy(false);
    }
  }

  if (phase === "loading") {
    return <div className="grid h-full place-items-center bg-(--void) text-(--ice-3)">در حال بارگذاری…</div>;
  }

  const isPolicyLink = info?.stage != null;
  const stage = info?.stage ?? null;

  return (
    <div className="grid h-full place-items-center overflow-y-auto bg-(--void) px-6 py-8">
      <div className="w-full max-w-md rounded-2xl border border-(--edge) bg-(--pane) p-6">
        <h1 className="mb-1 text-lg font-extrabold text-(--ice)">پورتال مشتری</h1>
        <div className="mb-4.5 text-[12px] text-(--ice-3)">
          {isPolicyLink ? "بیمهنامهٔ اقساطی — تکمیل مراحل" : "پرداخت کارمزد استعلام بیمه"}
        </div>

        {error && (
          <div className="mb-3.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] leading-relaxed text-(--ember)">
            {error}
          </div>
        )}

        {phase === "error" && !info && (
          <div className="text-[13px] text-(--ice-3)">
            لینک نامعتبر است یا دیگر قابل استفاده نیست. لطفاً با نمایندگی خود تماس بگیرید.
          </div>
        )}

        {info && (phase === "info" || phase === "feePaid" || phase === "error" || phase === "rejected") && (
          <>
            <div className="mb-4.5 rounded-[10px] bg-(--fld) px-3 py-2.5">
              <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">مشتری</div>
              <div className="text-[14px] font-semibold text-(--ice)">{info.customerDisplayName}</div>
            </div>
            <div className="mb-4.5 flex items-baseline justify-between rounded-[10px] bg-(--fld) px-3 py-2.5">
              <span className="text-[12px] text-(--ice-3)">کارمزد استعلام</span>
              <span className="text-[15px] font-bold tabular-nums text-(--ice)">
                {money(info.feeToman)} <span className="text-[11px] font-normal text-(--ice-3)">تومان</span>
              </span>
            </div>
            <div className="mb-4.5 text-[11px] text-(--ice-3)">
              اعتبار لینک تا {fa(toJalaliDateTimeDisplay(info.expiresAtUtc))}
            </div>

            {phase === "feePaid" ? (
              <div className="rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-3 text-center">
                <div className="mb-1 text-[14px] font-bold text-(--mint)">پرداخت کارمزد با موفقیت انجام شد</div>
                <div className="text-[12px] tabular-nums text-(--ice-2)">
                  مبلغ {money(payResult?.paidAmountToman ?? info.feeToman)} تومان
                </div>
                <div className="mt-2.5 text-[11.5px] leading-relaxed text-(--ice-3)">
                  {stage === "ReportReady"
                    ? "استعلام اعتباری شما انجام شد و پروندهٔ شما در حال بررسی نمایندگی است. نتیجه از طریق پیامک اطلاع داده میشود؛ برای ادامه همین صفحه را باز نگه دارید."
                    : stage === "FeePaid"
                      ? "پرداخت شما ثبت شد اما استعلام اعتباری موفق نبود. نمایندگی به‌زودی استعلام را مجدداً انجام میدهد و از طریق پیامک به شما اطلاع میدهد."
                      : "استعلام‌های بیمه‌ای شما به‌زودی توسط نمایندگی انجام می‌شود و نتیجه از طریق پیامک اطلاع داده خواهد شد."}
                </div>
              </div>
            ) : info.status === "Paid" && isPolicyLink ? (
              <StageNotice stage={stage} onPayDownPayment={() => setPhase("downGateway")} />
            ) : info.status === "Expired" ? (
              <div className="text-[13px] leading-relaxed text-(--ember)">
                اعتبار این لینک به پایان رسیده است. لطفاً با نمایندگی خود تماس بگیرید.
              </div>
            ) : (
              <button type="button" onClick={() => setPhase("feeGateway")} className={PRIMARY_BTN}>
                پرداخت کارمزد
              </button>
            )}
          </>
        )}

        {info && phase === "contract" && (
          <>
            <div className="mb-4.5 rounded-[10px] bg-(--fld) px-3 py-2.5">
              <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">مشتری</div>
              <div className="text-[14px] font-semibold text-(--ice)">{info.customerDisplayName}</div>
              {info.policyNumber && (
                <div className="mt-1 text-[11.5px] text-(--ice-3)">
                  بیمهنامهٔ <span className="tabular-nums text-(--ice-2)">{info.policyNumber}</span>
                </div>
              )}
            </div>

            <div className="mb-3.5 text-[12.5px] font-bold text-(--mint)">
              درخواست بیمهنامهٔ اقساطی شما تأیید شد
            </div>
            <div className="mb-3.5 text-[12px] leading-relaxed text-(--ice-2)">
              قرارداد و جدول اقساط را بررسی کنید و در صورت پذیرش، تأیید کنید.
            </div>

            <div className="mb-3.5 max-h-44 overflow-y-auto rounded-[10px] border border-(--edge-2) bg-(--fld) p-3 text-[11.5px] leading-relaxed whitespace-pre-wrap text-(--ice-2)">
              {info.contractText}
            </div>

            {info.installments && info.installments.length > 0 && (
              <div className="mb-3.5">
                <div className="mb-1.5 text-[12px] font-semibold text-(--ice-2)">جدول اقساط</div>
                <table className="w-full border-collapse text-[12px]">
                  <tbody>
                    {info.installments.map((i) => (
                      <tr key={i.seqNo} className="border-t border-(--edge)/50 first:border-t-0">
                        <td className="py-1 text-(--ice-3)">قسط {fa(i.seqNo)}</td>
                        <td className="py-1 text-(--ice-3)">{toJalaliDisplay(i.dueDate)}</td>
                        <td className="py-1 text-end font-semibold tabular-nums text-(--ice)">{money(i.amount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {info.downPaymentAmountToman != null && info.downPaymentAmountToman > 0 && (
              <div className="mb-3.5 flex items-baseline justify-between rounded-[10px] bg-(--fld) px-3 py-2.5">
                <span className="text-[12px] text-(--ice-3)">پیشپرداخت</span>
                <span className="text-[14px] font-bold tabular-nums text-(--ice)">
                  {money(info.downPaymentAmountToman)}{" "}
                  <span className="text-[10px] font-normal text-(--ice-3)">تومان</span>
                </span>
              </div>
            )}

            <label className="mb-3 flex cursor-pointer items-start gap-2 text-[11.5px] leading-relaxed text-(--ice-2)">
              <input
                type="checkbox"
                checked={contractAccepted}
                onChange={(e) => setContractAccepted(e.target.checked)}
                className="mt-0.5 accent-(--mint)"
              />
              متن قرارداد و شرایط اقساط را خواندم و می‌پذیرم.
            </label>
            <button
              type="button"
              onClick={() => void approveContract()}
              disabled={!contractAccepted || busy}
              className={PRIMARY_BTN}
            >
              {busy ? "در حال ثبت…" : "می‌پذیرم"}
            </button>
          </>
        )}

        {info &&
          (phase === "feeGateway" || phase === "downGateway") &&
          renderGateway(
            phase === "feeGateway" ? info.feeToman : (info.downPaymentAmountToman ?? 0),
            phase === "feeGateway" ? "پرداخت کارمزد استعلام" : "پرداخت پیشپرداخت",
            () => setPhase("info"),
            phase === "feeGateway" ? payFee : payDownPayment,
            busy,
          )}

        {phase === "downPaid" && info && (
          <div className="rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-3 text-center">
            <div className="mb-1 text-[14px] font-bold text-(--mint)">پیشپرداخت با موفقیت پرداخت شد</div>
            <div className="text-[12px] tabular-nums text-(--ice-2)">
              مبلغ {money(payResult?.paidAmountToman ?? info.downPaymentAmountToman ?? 0)} تومان
            </div>
            {payResult && (
              <div className="mt-0.5 text-[11px] text-(--ice-3)">
                {fa(toJalaliDateTimeDisplay(payResult.paidAtUtc))}
              </div>
            )}
            <div className="mt-2.5 text-[11.5px] leading-relaxed text-(--ice-3)">
              فرایند بیمهنامهٔ اقساطی شما کامل شد. نمایندگی با شما تماس میگیرد.
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

/// Maps the server's stage to the customer's page phase. The customer's own actions (pay,
/// approve) re-derive the phase from a fresh GET after their POST.
function derivePhase(i: PortalInfoDto, prev: Phase): Phase {
  if (i.status === "Expired") return "error";
  if (i.status === "Pending") return "info";
  if (i.stage === "Rejected") return "rejected";
  if (i.stage === "Completed") return "downPaid";
  if (i.stage === "CustomerApproved") return "info";
  if (i.stage === "AgencyApproved") return "contract";
  if (i.stage === "FeePaid" || i.stage === "ReportReady") {
    // Keep the just-paid receipt on screen while the agency reviews; the poll flips the page to
    // the contract the moment the agency approves.
    return prev === "feePaid" ? "feePaid" : "info";
  }
  // A paid plain inquiry-fee link (no policy) or a fresh poll — stay put.
  return prev === "loading" ? "info" : prev;
}

function StageNotice({ stage, onPayDownPayment }: { stage: string | null; onPayDownPayment: () => void }) {
  if (stage === "Rejected") {
    return (
      <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-3 text-[12.5px] leading-relaxed text-(--ember)">
        درخواست بیمهنامهٔ اقساطی شما از سمت نمایندگی تأیید نشد. برای اطلاعات بیشتر با نمایندگی تماس بگیرید.
      </div>
    );
  }
  if (stage === "Completed") {
    return (
      <div className="rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-3 text-[12.5px] leading-relaxed text-(--mint)">
        فرایند بیمهنامهٔ اقساطی شما کامل شد. ممنون از همراهی شما.
      </div>
    );
  }
  if (stage === "CustomerApproved") {
    return (
      <div className="rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-3 text-[12.5px] leading-relaxed text-(--ice-2)">
        قرارداد شما ثبت شد — برای تکمیل فرایند، پیشپرداخت را پرداخت کنید:
        <button
          type="button"
          onClick={onPayDownPayment}
          className="mt-2 w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
        >
          پرداخت پیشپرداخت
        </button>
      </div>
    );
  }
  return (
    <div className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-3 text-[12.5px] leading-relaxed text-(--ice-3)">
      {stage === "ReportReady"
        ? "استعلام اعتباری شما انجام شد و پروندهٔ شما در حال بررسی نمایندگی است. با تأیید نمایندگی، قرارداد و جدول اقساط همین‌جا نمایش داده میشود."
        : stage === "FeePaid"
          ? "پرداخت شما ثبت شد اما استعلام اعتباری موفق نبود. نمایندگی استعلام را مجدداً انجام میدهد و از طریق پیامک اطلاع میدهد."
          : "پروندهٔ شما در حال بررسی نمایندگی است. نتیجه از طریق پیامک اطلاع داده میشود."}
    </div>
  );
}

function renderGateway(
  amount: number,
  title: string,
  onCancel: () => void,
  onConfirm: () => void,
  busy: boolean,
) {
  return (
    <>
      <div className="mb-3 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[11px] text-(--ice-3)">
        در حال انتقال به درگاه پرداخت… — {title}
      </div>
      {/* Simulated PSP screen — the Mock gateway's "redirect". In production this is replaced
          by the real provider's hosted page; only the confirm click charges. */}
      <div className="mb-4.5 rounded-[10px] border border-(--edge-2) bg-(--void) p-4">
        <div className="mb-2 text-center text-[12px] font-bold tracking-[0.12em] text-(--ice-2)">
          درگاه پرداخت آزمایشی
        </div>
        <div className="mb-3 flex items-baseline justify-between border-b border-(--edge) pb-3">
          <span className="text-[11.5px] text-(--ice-3)">مبلغ قابل پرداخت</span>
          <span className="text-[14px] font-bold tabular-nums text-(--ice)">
            {money(amount)} <span className="text-[10px] font-normal text-(--ice-3)">تومان</span>
          </span>
        </div>
        <div className="mb-1 text-[10px] text-(--ice-3)">شمارهٔ کارت</div>
        <div
          dir="ltr"
          className="mb-3 rounded-[8px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-center text-[13px] tabular-nums tracking-widest text-(--ice-3)"
        >
          6037-99**-****-0000
        </div>
        <div className="text-center text-[10px] text-(--ice-3)">
          این درگاه آزمایشی است — پول واقعی جابه‌جا نمی‌شود.
        </div>
      </div>
      <div className="flex gap-2.5">
        <button type="button" onClick={onCancel} className={SECONDARY_BTN}>
          انصراف
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={busy}
          className="flex-1 rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال پرداخت…" : "پرداخت"}
        </button>
      </div>
    </>
  );
}
