import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay, toJalaliDisplay } from "../../lib/jalali";

/// The PUBLIC installment-payment page (/pay/{token}) — standalone like /portal/, mounted before
/// any auth check in App.tsx; the 43-char token in the URL is the credential. The SMS reminder
/// carries this link; the customer sees every open installment across their policies and pays one
/// at its full remaining balance through the agency's gateway. The Mock gateway's simulated PSP
/// screen is the same contract as PortalPage's: only the confirm click hits the POST endpoint.
interface PaymentLinkInstallmentDto {
  id: string;
  seqNo: number;
  policyNumber: string;
  dueDate: string;
  balanceToman: number;
  isOverdue: boolean;
}

interface PaymentLinkInfoDto {
  customerDisplayName: string;
  agencyName: string;
  installments: PaymentLinkInstallmentDto[];
}

interface PayResultDto {
  paidAmountToman?: number;
  paidAtUtc?: string;
  policyNumber: string;
  seqNo: number;
  /// Set when the agency's gateway is a real PSP: the browser must go to the PSP's hosted
  /// payment page; nothing is settled until its callback returns us here with ?pay=ok|error.
  redirectUrl?: string | null;
}

/// The PSP's browser callback lands back on this page as ?pay=ok|error&detail=… — rendered as a
/// banner and then stripped from the URL so a refresh / share never replays it.
const callbackParams = new URLSearchParams(window.location.search);
const callbackState: "ok" | "error" | null = callbackParams.get("pay") === "ok"
  ? "ok"
  : callbackParams.get("pay") === "error"
    ? "error"
    : null;
const callbackDetail = callbackParams.get("detail");
if (callbackParams.get("pay") !== null) {
  window.history.replaceState(null, "", window.location.pathname);
}

const PRIMARY_BTN =
  "w-full rounded-(--r) border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50";
const SECONDARY_BTN =
  "flex-1 rounded-(--r) border border-(--edge-2) bg-transparent px-4 py-2.5 text-[13.5px] font-semibold text-(--ice-2) transition-colors hover:brightness-110";

export function InstallmentPayPage() {
  const token = window.location.pathname.split("/").pop() ?? "";
  const [info, setInfo] = useState<PaymentLinkInfoDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [paying, setPaying] = useState<PaymentLinkInstallmentDto | null>(null);
  const [payResult, setPayResult] = useState<PayResultDto | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    return api
      .get<PaymentLinkInfoDto>(`/portal/pay/${encodeURIComponent(token)}`)
      .then((i) => {
        setInfo(i);
        setNotFound(false);
        setError(null);
        return i;
      })
      .catch((err) => {
        if (err instanceof ApiError && err.status === 404) {
          setNotFound(true);
          setError(err.message);
        } else {
          setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
        }
      });
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  // A PSP callback just landed (?pay=…) — reload so the settled installment disappears from the
  // open list, and let the banner below say why.
  useEffect(() => {
    if (callbackState !== null) void load();
  }, [callbackState, load]);

  async function pay() {
    if (!paying) return;
    setBusy(true);
    setError(null);
    try {
      const result = await api.post<PayResultDto>(
        `/portal/pay/${encodeURIComponent(token)}/installments/${paying.id}`,
      );
      if (result.redirectUrl) {
        // A real PSP opened a hosted charge — the browser leaves this page now and the settle
        // comes back through the gateway's callback (?pay=ok|error). Keep the row visible in
        // case the customer comes back via Back.
        window.location.href = result.redirectUrl;
        return;
      }
      setPayResult(result);
      setPaying(null);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
      setPaying(null);
    } finally {
      setBusy(false);
    }
  }

  if (!info) {
    if (notFound || error) {
      return (
        <div className="grid h-full place-items-center bg-(--void) px-6">
          <div className="w-full max-w-md rounded-(--r-lg) border border-(--edge) bg-(--pane) p-6 text-center">
            <div className="mb-2 text-[13.5px] leading-relaxed text-(--ember)">{error}</div>
            <div className="text-[12.5px] text-(--ice-3)">لطفاً با نمایندگی خود تماس بگیرید.</div>
          </div>
        </div>
      );
    }
    return <div className="grid h-full place-items-center bg-(--void) text-(--ice-3)">در حال بارگذاری…</div>;
  }

  return (
    <div className="grid h-full place-items-center overflow-y-auto bg-(--void) px-6 py-8">
      <div className="w-full max-w-md rounded-(--r-lg) border border-(--edge) bg-(--pane) p-6">
        <h1 className="mb-1 text-xl font-extrabold text-(--ice)">پرداخت آنلاین قسط</h1>
        <div className="mb-4.5 text-[12.5px] text-(--ice-3)">{info.agencyName}</div>

        {error && (
          <div className="mb-3.5 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] leading-relaxed text-(--ember)">
            {error}
          </div>
        )}

        {callbackState === "ok" && (
          <div className="mb-4.5 rounded-(--r) border border-(--mint)/30 bg-(--mint)/10 px-3 py-3 text-center text-[13px] font-semibold text-(--mint)">
            پرداخت با موفقیت تأیید شد. جدول اقساط به‌روزرسانی شده است.
          </div>
        )}
        {callbackState === "error" && (
          <div className="mb-4.5 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-3 py-2.5 text-[12.5px] leading-relaxed text-(--ember)">
            پرداخت تأیید نشد{callbackDetail ? ` — ${callbackDetail}` : ""}. در صورت کسر مبلغ از
            حساب، طی چند دقیقه به‌صورت خودکار برگردانده می‌شود؛ دوباره تلاش کنید.
          </div>
        )}

        {payResult && (
          <div className="mb-4.5 rounded-(--r) border border-(--mint)/30 bg-(--mint)/10 px-3 py-3 text-center">
            <div className="mb-1 text-[14px] font-bold text-(--mint)">پرداخت با موفقیت انجام شد</div>
            <div className="text-[12.5px] tabular-nums text-(--ice-2)">
              قسط {fa(payResult.seqNo)} بیمه‌نامهٔ {payResult.policyNumber} — مبلغ{" "}
              {money(payResult.paidAmountToman ?? 0)} تومان
            </div>
            <div className="mt-0.5 text-[11.5px] text-(--ice-3)">
              {fa(toJalaliDateTimeDisplay(payResult.paidAtUtc))}
            </div>
          </div>
        )}

        {paying ? (
          renderGateway(paying, () => setPaying(null), pay, busy)
        ) : info.installments.length === 0 ? (
          <div className="rounded-(--r) border border-(--edge-2) bg-(--fld) px-3 py-4 text-center text-[13px] text-(--ice-2)">
            {payResult
              ? "همهٔ اقساط شما تسویه شده است. ممنون از همراهی شما."
              : "قسط بازی برای پرداخت ندارید."}
          </div>
        ) : (
          <>
            <div className="mb-3 rounded-(--r) bg-(--fld) px-3 py-2.5">
              <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">مشتری</div>
              <div className="text-[14px] font-semibold text-(--ice)">{info.customerDisplayName}</div>
            </div>
            <div className="mb-2 text-[12.5px] text-(--ice-3)">اقساط باز شما، به ترتیب سررسید:</div>
            <div className="space-y-2">
              {info.installments.map((i) => (
                <div
                  key={i.id}
                  className="rounded-(--r) border border-(--edge-2) bg-(--fld) px-3 py-2.5"
                >
                  <div className="flex items-center justify-between gap-2">
                    <div className="text-[12.5px] text-(--ice-2)">
                      قسط {fa(i.seqNo)} — بیمه‌نامهٔ{" "}
                      <span className="tabular-nums">{i.policyNumber}</span>
                    </div>
                    {i.isOverdue && (
                      <span className="rounded-full border border-(--ember)/40 bg-(--ember)/15 px-2 py-0.5 text-[10.5px] font-semibold text-(--ember)">
                        معوق
                      </span>
                    )}
                  </div>
                  <div className="mt-1 flex items-center justify-between gap-2">
                    <span className="text-[11.5px] text-(--ice-3)">سررسید {toJalaliDisplay(i.dueDate)}</span>
                    <span className="text-[14px] font-bold tabular-nums text-(--ice)">
                      {money(i.balanceToman)}{" "}
                      <span className="text-[10.5px] font-normal text-(--ice-3)">تومان</span>
                    </span>
                  </div>
                  <button type="button" onClick={() => setPaying(i)} className={`${PRIMARY_BTN} mt-2`}>
                    پرداخت این قسط
                  </button>
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function renderGateway(
  installment: PaymentLinkInstallmentDto,
  onCancel: () => void,
  onConfirm: () => void,
  busy: boolean,
) {
  return (
    <>
      <div className="mb-3 rounded-(--r) border border-(--edge-2) bg-(--fld) px-3 py-2 text-[11.5px] text-(--ice-3)">
        در حال انتقال به درگاه پرداخت… — قسط {fa(installment.seqNo)} بیمه‌نامهٔ {installment.policyNumber}
      </div>
      {/* Simulated PSP screen — the Mock gateway's "redirect". In production this is replaced
          by the real provider's hosted page; only the confirm click charges. */}
      <div className="mb-4.5 rounded-(--r) border border-(--edge-2) bg-(--void) p-4">
        <div className="mb-2 text-center text-[12.5px] font-bold tracking-[0.12em] text-(--ice-2)">
          درگاه پرداخت آزمایشی
        </div>
        <div className="mb-3 flex items-baseline justify-between border-b border-(--edge) pb-3">
          <span className="text-[11.5px] text-(--ice-3)">مبلغ قابل پرداخت</span>
          <span className="text-[14px] font-bold tabular-nums text-(--ice)">
            {money(installment.balanceToman)}{" "}
            <span className="text-[10.5px] font-normal text-(--ice-3)">تومان</span>
          </span>
        </div>
        <div className="mb-1 text-[10.5px] text-(--ice-3)">شمارهٔ کارت</div>
        <div
          dir="ltr"
          className="mb-3 rounded-(--r) border border-(--edge-2) bg-(--fld) px-3 py-2 text-center text-[13.5px] tabular-nums tracking-widest text-(--ice-3)"
        >
          6037-99**-****-0000
        </div>
        <div className="text-center text-[10.5px] text-(--ice-3)">
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
          className="flex-1 rounded-(--r) border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال پرداخت…" : "پرداخت"}
        </button>
      </div>
    </>
  );
}
