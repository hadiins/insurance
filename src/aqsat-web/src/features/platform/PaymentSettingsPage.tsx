import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { MoneyInput } from "../../components/MoneyInput";

/** Mirrors PlatformPaymentSettingsDto — the owner-side gateway that collects the customer's
 * inquiry fee (کارمزد استعلام), docs/CUSTOMER-PORTAL-SPEC.md §3. The merchant ID is write-only:
 * GET only ever returns a mask. */
interface PaymentSettingsDto {
  provider: "Mock" | "ZarinPal";
  enabled: boolean;
  hasOwnerMerchantId: boolean;
  ownerMerchantIdMasked: string | null;
  callbackBaseUrl: string | null;
  inquiryFeeToman: number;
  updatedAt: string | null;
}

const PROVIDERS: Array<{ value: PaymentSettingsDto["provider"]; label: string; note: string }> = [
  { value: "Mock", label: "آزمایشی (Mock)", note: "درگاه شبیهسازیشده برای توسعه و تست — پول واقعی جابهجا نمیشود و به شناسهٔ پذیرنده نیاز ندارد." },
  { value: "ZarinPal", label: "زرینپال", note: "درگاه واقعی — برای فعالسازی، شناسهٔ پذیرنده (Merchant ID) صادرشده از پنل زرینپال الزامی است." },
];

export function PaymentSettingsPage() {
  const [settings, setSettings] = useState<PaymentSettingsDto | null>(null);
  const [provider, setProvider] = useState<PaymentSettingsDto["provider"]>("Mock");
  const [enabled, setEnabled] = useState(false);
  const [merchantId, setMerchantId] = useState("");
  const [callbackUrl, setCallbackUrl] = useState("");
  const [inquiryFee, setInquiryFee] = useState(0);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<PaymentSettingsDto>("/platform/payment/settings")
      .then((s) => {
        setSettings(s);
        setProvider(s.provider);
        setEnabled(s.enabled);
        setCallbackUrl(s.callbackBaseUrl ?? "");
        setInquiryFee(s.inquiryFeeToman);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره"));
  }, []);

  async function save() {
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      // An untouched merchant-id field is sent as null — the server then keeps the stored one,
      // so an ordinary save can never wipe a working credential.
      const updated = await api.put<PaymentSettingsDto>("/platform/payment/settings", {
        provider,
        enabled,
        ownerMerchantId: merchantId.trim() === "" ? null : merchantId.trim(),
        callbackBaseUrl: callbackUrl.trim() === "" ? null : callbackUrl.trim(),
        inquiryFeeToman: Number(inquiryFee),
      });
      setSettings(updated);
      setMerchantId("");
      setInquiryFee(updated.inquiryFeeToman);
      setMessage(
        updated.enabled
          ? `درگاه ${PROVIDERS.find((p) => p.value === updated.provider)?.label} فعال شد.`
          : "درگاه پرداخت غیرفعال است — پرداخت در پورتال مشتری غیرممکن خواهد بود.",
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mx-auto max-w-3xl">
      <h1 className="mb-4.5 text-[17px] font-extrabold text-(--ice)">درگاه پرداخت مالک</h1>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}
      {message && (
        <div className="mb-4.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">
          {message}
        </div>
      )}

      <div className="mb-4.5 rounded-[10px] border border-(--edge) bg-(--pane) px-3 py-2 text-[11.5px] leading-relaxed text-(--ice-3)">
        این درگاه فقط برای <span className="font-semibold text-(--ice-2)">دریافت کارمزد استعلام‌های مورد نیاز</span> استفاده می‌شود
        و مبالغ به حسابی که مالک در درگاه پرداخت اعمال می‌کند واریز می‌شود.
        درگاه پرداخت نمایندگی‌ها (برای پیش‌پرداخت و اقساط مشتریان) جداگانه در تنظیمات خودِ هر نمایندگی پیکربندی می‌شود و با این درگاه تداخلی ندارد.
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">وضعیت فعلی</div>
        {settings === null ? (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری...</div>
        ) : (
          <>
            <div className="text-[15px] font-bold text-(--ice)">
              {settings.enabled
                ? `فعال — ${PROVIDERS.find((p) => p.value === settings.provider)?.label}`
                : "غیرفعال"}
            </div>
            <div className="mt-1 text-[12px] text-(--ice-3)">
              شناسهٔ پذیرنده: {settings.hasOwnerMerchantId ? settings.ownerMerchantIdMasked : "تنظیم نشده"}
            </div>
            <div className="mt-0.5 text-[12px] text-(--ice-3)">
              کارمزد استعلام: {fa(settings.inquiryFeeToman)} تومان
            </div>
            <div className="mt-0.5 text-[12px] text-(--ice-3)">
              آدرس کالبک: {settings.callbackBaseUrl ?? "تنظیم نشده — از پیکربندی سرور استفاده میشود"}
            </div>
            {settings.updatedAt && (
              <div className="mt-0.5 text-[11px] text-(--ice-3)">
                آخرین تغییر: {new Date(settings.updatedAt).toLocaleString("fa-IR")}
              </div>
            )}
          </>
        )}
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">پیکربندی</div>

        <div className="mb-1 text-[12px] font-semibold text-(--ice-2)">درگاه پرداخت</div>
        <div className="mb-2 space-y-2">
          {PROVIDERS.map((p) => (
            <label key={p.value} className="flex cursor-pointer items-start gap-2.5 rounded-[10px] bg-(--fld) px-3 py-2">
              <input
                type="radio"
                name="payment-provider"
                checked={provider === p.value}
                onChange={() => setProvider(p.value)}
                className="mt-1 h-4 w-4 shrink-0 accent-(--mint)"
              />
              <span>
                <span className="block text-[13px] font-semibold text-(--ice)">{p.label}</span>
                <span className="block text-[11px] text-(--ice-3)">{p.note}</span>
              </span>
            </label>
          ))}
        </div>

        <label className="mb-4 mt-3 flex cursor-pointer items-center gap-2.5">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(e) => setEnabled(e.target.checked)}
            className="h-4 w-4 accent-(--mint)"
          />
          <span className="text-[13px] text-(--ice)">فعالسازی پرداخت آنلاین در پورتال مشتری</span>
        </label>

        <div className="mb-1 text-[12px] font-semibold text-(--ice-2)">کارمزد استعلام (تومان)</div>
        <div className="mb-2 text-[11.5px] text-(--ice-3)">
          مبلغی که هر مشتری پیش از اجرای استعلام‌ها در پورتال مشتری پرداخت می‌کند و از طریق همین درگاه به حساب مالک واریز می‌شود.
        </div>
        <div className="mb-4">
          <MoneyInput
            value={String(inquiryFee)}
            onChange={(v) => setInquiryFee(Number(v))}
            className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
          />
        </div>
        {enabled && provider === "ZarinPal" && !settings?.hasOwnerMerchantId && merchantId.trim() === "" && (
          <div className="mb-4 rounded-[10px] border border-(--amber)/30 bg-(--amber)/8 px-3 py-2 text-[11.5px] leading-relaxed text-(--amber)">
            برای فعالسازی درگاه زرینپال، شناسهٔ پذیرنده (Merchant ID) الزامی است — سرور ذخیرهٔ تنظیمات بدون آن را نمیپذیرد.
          </div>
        )}

        <div className="mb-1 text-[12px] font-semibold text-(--ice-2)">شناسهٔ پذیرنده (Merchant ID)</div>
        <div className="mb-2 text-[11.5px] text-(--ice-3)">
          برای حفظ مقدار فعلی خالی بگذارید. مقدار جدید جایگزین شناسهٔ ذخیرهشده میشود و پس از ذخیره هیچگاه کامل نمایش داده نمیشود.
        </div>
        <input
          value={merchantId}
          onChange={(e) => setMerchantId(e.target.value)}
          placeholder={settings?.hasOwnerMerchantId ? settings.ownerMerchantIdMasked ?? "" : "شناسهٔ پذیرنده"}
          className="mb-4 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          autoComplete="off"
        />

        <div className="mb-1 text-[12px] font-semibold text-(--ice-2)">آدرس کالبک (Callback URL)</div>
        <div className="mb-2 text-[11.5px] text-(--ice-3)">
          نشانی کامل سروری که درگاه، پس از پرداخت به آن برمیگرداند. خالی بگذارید تا از پیکربندی سرور استفاده شود.
        </div>
        <input
          value={callbackUrl}
          onChange={(e) => setCallbackUrl(e.target.value)}
          placeholder="https://api.example.ir"
          className="mb-4 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          autoComplete="off"
          dir="ltr"
        />

        <button
          type="button"
          disabled={busy || settings === null}
          onClick={save}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال ذخیره..." : "ذخیره"}
        </button>
      </div>
    </div>
  );
}
