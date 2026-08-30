import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";

interface ApiIrSettingsDto {
  allowPaidEndpoints: boolean;
  hasApiKey: boolean;
  apiKeyMasked: string | null;
  updatedAt: string | null;
}

/** The Phase-1 services and their per-call prices (تومان) — mirrors ApiIrClient's cost constants. */
const ENDPOINTS: Array<{ name: string; cost: string; note: string }> = [
  { name: "ShahkarLite", cost: "۵۵۰", note: "تطبیق کد ملی و شمارهٔ همراه" },
  { name: "SendSms", cost: "۱۱۵", note: "ارسال پیامک (یادآوری اقساط، اطلاعرسانی)" },
  { name: "SmsOTP / CallOTP", cost: "۱۱۵ / ۹۵", note: "کد یکبارمصرف پیامکی و تماسی" },
  { name: "IsHoliday", cost: "۱۵۰", note: "تشخیص تعطیلی رسمی برای سررسید اقساط" },
  { name: "ChequeColor", cost: "۱٬۱۰۰", note: "وضعیت چک صیادی" },
];

export function ApiIrSettingsPage() {
  const [settings, setSettings] = useState<ApiIrSettingsDto | null>(null);
  const [allowPaid, setAllowPaid] = useState(false);
  const [apiKey, setApiKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<ApiIrSettingsDto>("/platform/apiir/settings")
      .then((s) => {
        setSettings(s);
        setAllowPaid(s.allowPaidEndpoints);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره"));
  }, []);

  async function save() {
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      // An untouched key field is sent as null — the server then keeps the stored key, so an
      // ordinary save can never wipe a working credential.
      const updated = await api.put<ApiIrSettingsDto>("/platform/apiir/settings", {
        allowPaidEndpoints: allowPaid,
        apiKey: apiKey.trim() === "" ? null : apiKey.trim(),
      });
      setSettings(updated);
      setApiKey("");
      setMessage(
        updated.allowPaidEndpoints
          ? "سرویسهای پرداختی فعال شد — تماسها به endpointهای واقعی api.ir ارسال و طبق تعرفه حساب میشود."
          : "حالت آزمایشی (Sandbox) فعال است — تماسهای پرداختی به api.ir ارسال نمیشود.",
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mx-auto max-w-3xl">
      <h1 className="mb-4.5 text-[17px] font-extrabold text-(--ice)">تنظیمات api.ir</h1>

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

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">وضعیت فعلی</div>
        {settings === null ? (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری...</div>
        ) : (
          <>
            <div className="text-[15px] font-bold text-(--ice)">
              {settings.allowPaidEndpoints ? "سرویسهای پرداختی فعال" : "حالت آزمایشی (Sandbox)"}
            </div>
            <div className="mt-1 text-[12px] text-(--ice-3)">
              کلید api.ir: {settings.hasApiKey ? settings.apiKeyMasked : "تنظیم نشده — از پیکربندی سرور استفاده میشود"}
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

        <label className="mb-4 flex cursor-pointer items-center gap-2.5">
          <input
            type="checkbox"
            checked={allowPaid}
            onChange={(e) => setAllowPaid(e.target.checked)}
            className="h-4 w-4 accent-(--mint)"
          />
          <span className="text-[13px] text-(--ice)">اجازهٔ تماس با سرویسهای پرداختی api.ir</span>
        </label>
        <div className="mb-4 rounded-[10px] border border-(--amber)/30 bg-(--amber)/8 px-3 py-2 text-[11.5px] leading-relaxed text-(--amber)">
          تا زمانی که این گزینه خاموش است، هر تماس پرداختی به endpoint آزمایشی (Sandbox/Echo) هدایت
          میشود و هیچ هزینهای ثبت نمیشود — اما هیچ نتیجهٔ واقعی هم دریافت نمیکند.
        </div>

        <div className="mb-1 text-[12px] font-semibold text-(--ice-2)">کلید api.ir</div>
        <div className="mb-2 text-[11.5px] text-(--ice-3)">
          برای حفظ کلید فعلی خالی بگذارید. مقدار جدید جایگزین کلید ذخیرهشده میشود.
        </div>
        <input
          value={apiKey}
          onChange={(e) => setApiKey(e.target.value)}
          placeholder={settings?.hasApiKey ? settings.apiKeyMasked ?? "" : "کلید api.ir"}
          className="mb-4 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          autoComplete="off"
        />

        <button
          type="button"
          disabled={busy || settings === null}
          onClick={save}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال ذخیره..." : "ذخیره"}
        </button>
        <div className="mt-2 text-[11px] text-(--ice-3)">تغییرات چند ثانیه پس از ذخیره روی همهٔ تماسهای api.ir اعمال میشود.</div>
      </div>

      <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">سرویسها و تعرفه هر تماس (تومان)</div>
        <div className="space-y-2">
          {ENDPOINTS.map((endpoint) => (
            <div key={endpoint.name} className="flex items-center justify-between rounded-[10px] bg-(--fld) px-3 py-2">
              <div>
                <div className="text-[12.5px] font-semibold text-(--ice)">{endpoint.name}</div>
                <div className="text-[11px] text-(--ice-3)">{endpoint.note}</div>
              </div>
              <div className="text-[13px] font-bold text-(--ice-2)">{endpoint.cost}</div>
            </div>
          ))}
        </div>
        <div className="mt-3 text-[11px] text-(--ice-3)">هر تماس (واقعی یا آزمایشی) در لاگ هزینه ثبت میشود.</div>
      </div>
    </div>
  );
}