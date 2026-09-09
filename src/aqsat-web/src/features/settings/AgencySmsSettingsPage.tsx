import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

/** Mirrors AgencySmsPanelDto — the agency's own api.ir key for sending SMS. An empty key falls
 * back to the platform-level key, so inheriting and owning are distinct states. */
interface AgencySmsPanelDto {
  name: string;
  code: string;
  hasSmsApiKey: boolean;
  smsApiKeyMasked: string | null;
}

export function AgencySmsSettingsPage() {
  const [form, setForm] = useState<AgencySmsPanelDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [apiKeyInput, setApiKeyInput] = useState("");

  useEffect(() => {
    api
      .get<AgencySmsPanelDto>("/settings/agency/sms-panel")
      .then(setForm)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری تنظیمات"));
  }, []);

  async function save() {
    if (!form) return;
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api.put<AgencySmsPanelDto>("/settings/agency/sms-panel", {
        smsApiKey: apiKeyInput.trim() || null,
      });
      setForm(updated);
      setSaved(true);
      setApiKeyInput("");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیرهٔ تنظیمات ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  if (!form) {
    return (
      <div>
        <h2 className="mb-4 text-xl font-extrabold tracking-tight text-(--ice)">تنظیمات پنل پیامکی</h2>
        {error ? (
          <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{error}</div>
        ) : (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        )}
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-3xl">
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        تنظیمات <em className="font-extralight not-italic text-(--ice-2)">پنل پیامکی</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">{fa(form.name)} — کد {fa(form.code)}</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{error}</div>
      )}
      {saved && (
        <div className="mb-4.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">تنظیمات ذخیره شد.</div>
      )}

      <div className="mb-4.5 rounded-[10px] border border-(--edge) bg-(--pane) px-3 py-2 text-[11.5px] leading-relaxed text-(--ice-3)">
        پیامک‌های این نمایندگی (یادآوری اقساط و اطلاع‌رسانی) با کلید api.ir خودِ نمایندگی ارسال می‌شود و هزینهٔ آن به حساب پیامکی نمایندگی تعلق می‌گیرد.
        تا زمانی که کلید ثبت نشود، ارسال با کلید پلتفرم انجام می‌شود.
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-1 text-[12.5px] font-semibold text-(--ice-2)">کلید API پنل پیامکی (api.ir)</div>
        <div className="mb-2 text-[11.5px] text-(--ice-3)">
          کلید صادرشده از پنل api.ir برای حساب پیامکی این نمایندگی. برای حفظ مقدار فعلی خالی بگذارید؛ پس از ذخیره هرگز کامل نمایش داده نمی‌شود.
        </div>
        {form.hasSmsApiKey ? (
          <div className="space-y-1.5">
            <div className="rounded-[10px] border border-(--edge-2) bg-(--fld)/50 px-3 py-2 text-[13.5px] text-(--ice-3)" dir="ltr">
              {form.smsApiKeyMasked ?? "—"}
            </div>
            <input
              value={apiKeyInput}
              onChange={(e) => setApiKeyInput(e.target.value)}
              placeholder="برای تغییر، کلید جدید را وارد کنید (خالی = بدون تغییر)"
              dir="ltr"
              className={inputClass}
            />
          </div>
        ) : (
          <input
            value={apiKeyInput}
            onChange={(e) => setApiKeyInput(e.target.value)}
            placeholder="هنوز کلید اختصاصی ثبت نشده — ارسال با کلید پلتفرم"
            dir="ltr"
            className={inputClass}
          />
        )}
      </div>

      <button
        type="button"
        disabled={busy}
        onClick={save}
        className="rounded-[10px] border border-(--mint) bg-(--mint) px-5 py-2.5 text-[13.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {busy ? "در حال ذخیره…" : "ذخیرهٔ تنظیمات"}
      </button>
    </div>
  );
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)";
