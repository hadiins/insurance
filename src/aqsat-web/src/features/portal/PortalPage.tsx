import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";

/// The PUBLIC customer portal (docs/CUSTOMER-PORTAL-SPEC.md §3) — standalone like /owner-setup,
/// mounted before any auth check in App.tsx; the 43-char token in the URL is the credential.
/// The pay button opens a simulated gateway screen first (the user-confirmed Mock semantics);
/// only its confirm click hits POST /pay.
interface PortalInfoDto {
  customerDisplayName: string;
  feeToman: number;
  status: "Pending" | "Paid" | "Expired";
  expiresAtUtc: string;
}

interface PayResultDto {
  paidAmountToman: number;
  paidAtUtc: string;
}

type Phase = "loading" | "info" | "gateway" | "paid" | "error";

export function PortalPage() {
  const token = window.location.pathname.split("/").pop() ?? "";
  const [phase, setPhase] = useState<Phase>("loading");
  const [info, setInfo] = useState<PortalInfoDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [payResult, setPayResult] = useState<PayResultDto | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api
      .get<PortalInfoDto>(`/portal/${encodeURIComponent(token)}`)
      .then((i) => {
        setInfo(i);
        setPhase(i.status === "Paid" ? "paid" : i.status === "Expired" ? "error" : "info");
        if (i.status === "Expired") setError("اعتبار این لینک به پایان رسیده است.");
      })
      .catch((err) => {
        setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
        setPhase("error");
      });
  }, [token]);

  async function confirmPay() {
    setBusy(true);
    setError(null);
    try {
      const result = await api.post<PayResultDto>(`/portal/${encodeURIComponent(token)}/pay`);
      setPayResult(result);
      setPhase("paid");
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

  return (
    <div className="grid h-full place-items-center bg-(--void) px-6">
      <div className="w-full max-w-sm rounded-2xl border border-(--edge) bg-(--pane) p-6">
        <h1 className="mb-1 text-lg font-extrabold text-(--ice)">پورتال مشتری</h1>
        <div className="mb-4.5 text-[12px] text-(--ice-3)">پرداخت کارمزد استعلام بیمه</div>

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

        {info && phase !== "gateway" && phase !== "paid" && (
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
            <button
              type="button"
              onClick={() => setPhase("gateway")}
              className="w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
            >
              پرداخت کارمزد
            </button>
          </>
        )}

        {phase === "gateway" && info && (
          <>
            <div className="mb-3 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[11px] text-(--ice-3)">
              در حال انتقال به درگاه پرداخت…
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
                  {money(info.feeToman)} <span className="text-[10px] font-normal text-(--ice-3)">تومان</span>
                </span>
              </div>
              <div className="mb-1 text-[10px] text-(--ice-3)">شمارهٔ کارت</div>
              <div dir="ltr" className="mb-3 rounded-[8px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-center text-[13px] tabular-nums tracking-widest text-(--ice-3)">
                6037-99**-****-0000
              </div>
              <div className="text-center text-[10px] text-(--ice-3)">
                این درگاه آزمایشی است — پول واقعی جابه‌جا نمی‌شود.
              </div>
            </div>
            <div className="flex gap-2.5">
              <button
                type="button"
                onClick={() => setPhase("info")}
                className="flex-1 rounded-[10px] border border-(--edge-2) bg-transparent px-4 py-2.5 text-[13px] font-semibold text-(--ice-2) transition-colors hover:brightness-110"
              >
                انصراف
              </button>
              <button
                type="button"
                onClick={confirmPay}
                disabled={busy}
                className="flex-1 rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {busy ? "در حال پرداخت…" : "پرداخت"}
              </button>
            </div>
          </>
        )}

        {phase === "paid" && payResult && (
          <div className="rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-3 text-center">
            <div className="mb-1 text-[14px] font-bold text-(--mint)">پرداخت با موفقیت انجام شد</div>
            <div className="text-[12px] tabular-nums text-(--ice-2)">
              مبلغ {money(payResult.paidAmountToman)} تومان
            </div>
            <div className="mt-0.5 text-[11px] text-(--ice-3)">
              {fa(toJalaliDateTimeDisplay(payResult.paidAtUtc))}
            </div>
            <div className="mt-2.5 text-[11.5px] leading-relaxed text-(--ice-3)">
              استعلام‌های بیمه‌ای شما به‌زودی توسط نمایندگی انجام می‌شود و نتیجه از طریق پیامک اطلاع داده خواهد شد.
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
