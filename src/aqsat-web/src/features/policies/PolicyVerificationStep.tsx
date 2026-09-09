import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { CreditReportCard, type CreditReportDto } from "../../components/CreditReportCard";

/// Wizard step 3.5 (owner decision 2026-09-01) — the operator side of the issuance-verification
/// chain: start it (the customer gets the portal link by SMS), watch the stage, read the api.ir
/// credit report, approve or reject (reject cancels the policy), then hand off to the down-payment
/// step. Stages advance on the customer's phone, so the view polls until it reaches a terminal
/// stage or one that needs an operator decision. When the customer already has a fresh report
/// (≤30 days, standalone or from a previous policy — owner decision 2026-09-03), starting the
/// chain reuses it: the stage begins at ReportReady with a zero fee and no new inquiry.
interface PolicyVerificationDto {
  invitationId: string | null;
  token: string | null;
  stage: string | null;
  status: string | null;
  expiresAtUtc: string | null;
  inquiryFeeToman: number | null;
  downPaymentAmountToman: number | null;
  agencyDecisionAtUtc: string | null;
  customerApprovedAtUtc: string | null;
  downPaymentPaidAtUtc: string | null;
  creditReport: CreditReportDto | null;
  smsSent: boolean | null;
}

const STAGE_LABELS: Record<string, string> = {
  FeePending: "در انتظار پرداخت کارمزد استعلام توسط مشتری",
  FeePaid: "کارمزد پرداخت شد — استعلام ناموفق",
  ReportReady: "گزارش اعتباری آماده است — تصمیم شما لازم است",
  AgencyApproved: "تأیید شد — در انتظار تأیید قرارداد توسط مشتری",
  CustomerApproved: "مشتری قرارداد را تأیید کرد — نوبت پیشپرداخت است",
  Completed: "زنجیرهٔ اعتبارسنجی کامل شد",
  Rejected: "اعتبارسنجی رد شد — بیمه‌نامه لغو شد",
};

const POLL_MS = 4000;

export function PolicyVerificationStep({
  policyId,
  onChainCompleted,
  onRejected,
}: {
  policyId: string;
  /** paidOnline=false → the operator will record the down payment manually in the next step. */
  onChainCompleted: (paidOnline: boolean) => void;
  onRejected: () => void;
}) {
  const [dto, setDto] = useState<PolicyVerificationDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [confirmingReject, setConfirmingReject] = useState(false);

  const load = useCallback(async () => {
    try {
      setDto(await api.get<PolicyVerificationDto>(`/policies/${policyId}/verification`));
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا در دریافت وضعیت اعتبارسنجی.");
    } finally {
      setLoading(false);
    }
  }, [policyId]);

  useEffect(() => {
    void load();
  }, [load]);

  // Poll while the chain is waiting on the customer (fee, contract approval, online payment) —
  // every reload re-schedules the next tick until a terminal stage stops it.
  const stage = dto?.stage ?? null;
  useEffect(() => {
    if (!stage || stage === "Completed" || stage === "Rejected") return;
    const h = setTimeout(() => void load(), POLL_MS);
    return () => clearTimeout(h);
  }, [stage, dto, load]);

  async function run(action: () => Promise<PolicyVerificationDto>) {
    setBusy(true);
    setError(null);
    try {
      setDto(await action());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "عملیات ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  const start = () => run(() => api.post<PolicyVerificationDto>(`/policies/${policyId}/verification`));
  const retryInquiries = () =>
    run(() => api.post<PolicyVerificationDto>(`/policies/${policyId}/verification/retry-inquiries`));
  const decide = (approve: boolean) =>
    run(() =>
      api.post<PolicyVerificationDto>(`/policies/${policyId}/verification/decision`, { approve }),
    ).then(() => {
      if (!approve) onRejected();
    });

  function copyLink() {
    if (!dto?.token) return;
    void navigator.clipboard.writeText(`${window.location.origin}/portal/${dto.token}`);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  if (loading) {
    return <div className="text-[13.5px] text-(--ice-3)">در حال بارگذاری وضعیت اعتبارسنجی…</div>;
  }

  return (
    <div>
      {error && (
        <div className="mb-3.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] leading-relaxed text-(--ember)">
          {error}
        </div>
      )}

      {/* Loading / empty / error are all distinct (rule 16): loading above, error above, and the
          "no chain yet" empty state is the start button below. */}
      {!dto?.invitationId ? (
        <div>
          <div className="mb-3 text-[12.5px] leading-relaxed text-(--ice-2)">
            برای صدور اقساطی، ابتدا مشتری باید کارمزد استعلام اعتباری را از پورتال پرداخت کند، سپس استعلام چک برگشتی
            و تسهیلات بانکی به‌صورت خودکار اجرا میشود و پس از بررسی شما و تأیید قرارداد توسط مشتری، پیشپرداخت قابل
            دریافت خواهد بود.
          </div>
          <button type="button" onClick={start} disabled={busy} className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50">
            {busy ? "در حال ارسال…" : "شروع اعتبارسنجی و ارسال لینک به مشتری"}
          </button>
        </div>
      ) : (
        <>
          <div className="mb-3.5 flex flex-wrap items-center gap-2">
            <div className="rounded-full border border-(--mint)/40 bg-(--mint)/10 px-3 py-1 text-[11.5px] font-semibold text-(--mint)">
              {STAGE_LABELS[dto.stage ?? ""] ?? "وضعیت نامشخص"}
            </div>
            {dto.inquiryFeeToman != null && (
              <div className="text-[11.5px] text-(--ice-3)">
                {dto.inquiryFeeToman === 0
                  ? "بدون کارمزد (استعلام قبلی)"
                  : <>
                      کارمزد استعلام: <span className="tabular-nums">{money(dto.inquiryFeeToman)}</span> تومان
                    </>}
              </div>
            )}
            {dto.expiresAtUtc && (
              <div className="text-[11.5px] text-(--ice-3)">
                اعتبار لینک تا {fa(toJalaliDateTimeDisplay(dto.expiresAtUtc))}
              </div>
            )}
          </div>

          {dto.stage === "Rejected" ? (
            <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-3 text-[12.5px] leading-relaxed text-(--ember)">
              این بیمه‌نامه لغو شد. برای صدور مجدد، بیمه‌نامهٔ جدیدی ثبت کنید.
            </div>
          ) : (
            <>
              <div className="mb-3.5 flex flex-wrap items-center gap-2 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2">
                <span className="text-[11.5px] text-(--ice-3)">لینک پورتال مشتری:</span>
                <span dir="ltr" className="truncate text-[11.5px] text-(--ice-2)">
                  {window.location.origin}/portal/{dto.token}
                </span>
                <button
                  type="button"
                  onClick={copyLink}
                  className="rounded-[8px] border border-(--edge-2) bg-(--btn-bg) px-2.5 py-1 text-[11.5px] font-semibold text-(--ice-2) transition-colors hover:text-(--ice)"
                >
                  {copied ? "کپی شد" : "کپی"}
                </button>
                {dto.smsSent === false && dto.inquiryFeeToman !== 0 && (
                  <span className="text-[11.5px] text-(--ember)">پیامک ارسال نشد — لینک را دستی به مشتری بدهید.</span>
                )}
                {dto.inquiryFeeToman === 0 && (
                  <span className="text-[11.5px] text-(--ice-3)">
                    لینک قرارداد پس از تأیید شما برای مشتری پیامک میشود.
                  </span>
                )}
              </div>

              {dto.stage === "FeePending" && (
                <div className="text-[12.5px] leading-relaxed text-(--ice-3)">
                  منتظر پرداخت کارمزد توسط مشتری… پس از پرداخت، استعلام‌های اعتباری به‌صورت خودکار اجرا میشوند و
                  گزارش همین‌جا نمایش داده میشود.
                </div>
              )}

              {dto.stage === "FeePaid" && (
                <div>
                  <div className="mb-2.5 text-[12.5px] leading-relaxed text-(--ember)">
                    کارمزد پرداخت شد اما استعلام اعتباری از سرویس استعلام پاسخ نداد. کارمزد دوباره گرفته نمیشود.
                  </div>
                  <button
                    type="button"
                    onClick={retryInquiries}
                    disabled={busy}
                    className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {busy ? "در حال تلاش…" : "تلاش مجدد استعلام"}
                  </button>
                </div>
              )}

              {dto.inquiryFeeToman === 0 && dto.creditReport && dto.stage !== "Completed" && (
                <div className="mb-3.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] leading-relaxed text-(--mint)">
                  استعلام قبلی مشتری ({fa(toJalaliDateTimeDisplay(dto.creditReport.retrievedAtUtc))}) بازیافت شد —
                  کارمزد و استعلام مجدد لازم نیست.
                </div>
              )}

              {dto.creditReport && <CreditReportCard report={dto.creditReport} />}

              {dto.stage === "ReportReady" && (
                <div className="flex flex-wrap gap-2">
                  <button
                    type="button"
                    onClick={() => void decide(true)}
                    disabled={busy}
                    className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {busy ? "…" : "تأیید و ادامه"}
                  </button>
                  {confirmingReject ? (
                    <button
                      type="button"
                      onClick={() => void decide(false)}
                      disabled={busy}
                      className="rounded-[10px] border border-(--ember) bg-(--ember) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                      تأیید لغو بیمه‌نامه
                    </button>
                  ) : (
                    <button
                      type="button"
                      onClick={() => setConfirmingReject(true)}
                      className="rounded-[10px] border border-(--ember)/50 bg-transparent px-4 py-2 text-[12.5px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/10"
                    >
                      رد و لغو بیمه‌نامه
                    </button>
                  )}
                  {confirmingReject && (
                    <button
                      type="button"
                      onClick={() => setConfirmingReject(false)}
                      className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2)"
                    >
                      بازگشت
                    </button>
                  )}
                </div>
              )}

              {dto.stage === "AgencyApproved" && (
                <div className="text-[12.5px] leading-relaxed text-(--ice-3)">
                  لینک قرارداد و جدول اقساط برای مشتری پیامک شد — منتظر تأیید او…
                </div>
              )}

              {dto.stage === "CustomerApproved" && (
                <div className="flex flex-wrap items-center gap-3">
                  <button
                    type="button"
                    onClick={() => onChainCompleted(false)}
                    className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
                  >
                    دریافت پیشپرداخت بهصورت دستی
                  </button>
                  <span className="text-[11.5px] leading-relaxed text-(--ice-3)">
                    یا منتظر بمانید تا مشتری پیشپرداخت را آنلاین پرداخت کند.
                  </span>
                </div>
              )}

              {dto.stage === "Completed" && (
                <div className="flex flex-wrap items-center gap-3">
                  <div className="text-[12.5px] font-semibold text-(--mint)">
                    پیشپرداخت بهصورت آنلاین پرداخت و ثبت شد.
                  </div>
                  <button
                    type="button"
                    onClick={() => onChainCompleted(true)}
                    className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
                  >
                    ادامه
                  </button>
                </div>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
