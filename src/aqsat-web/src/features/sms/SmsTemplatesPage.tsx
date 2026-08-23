import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";

interface SmsTemplateDto {
  key: string;
  label: string;
  placeholders: string;
  body: string;
  isCustomized: boolean;
}

export function SmsTemplatesPage() {
  const [templates, setTemplates] = useState<SmsTemplateDto[] | null>(null);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  function reload() {
    api
      .get<SmsTemplateDto[]>("/sms/templates")
      .then((data) => {
        setTemplates(data);
        setDrafts(Object.fromEntries(data.map((t) => [t.key, t.body])));
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری قالب‌ها"));
  }

  useEffect(reload, []);

  async function save(key: string) {
    setBusyKey(key);
    setError(null);
    try {
      await api.put(`/sms/templates/${key}`, { body: drafts[key] });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیره ناموفق بود.");
    } finally {
      setBusyKey(null);
    }
  }

  async function reset(key: string) {
    setBusyKey(key);
    setError(null);
    try {
      await api.delete(`/sms/templates/${key}`);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "بازنشانی ناموفق بود.");
    } finally {
      setBusyKey(null);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">قالب پیامک‌ها</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">متن هر پیامک یادآوری را می‌توانید سفارشی کنید</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {templates === null ? (
        <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
      ) : (
        <div className="space-y-3.5">
          {templates.map((t) => (
            <div key={t.key} className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
              <div className="mb-2 flex items-center justify-between">
                <div className="text-[12.5px] font-semibold text-(--ice-2)">{t.label}</div>
                {t.isCustomized && <span className="rounded-full bg-(--mint)/12 px-2.5 py-0.5 text-[10.5px] font-semibold text-(--mint)">سفارشی‌شده</span>}
              </div>
              <div className="mb-2 text-[10.5px] text-(--ice-3)">جایگزین‌های مجاز: {t.placeholders}</div>
              <textarea
                value={drafts[t.key] ?? ""}
                onChange={(e) => setDrafts((d) => ({ ...d, [t.key]: e.target.value }))}
                rows={3}
                className="mb-3 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
              />
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => save(t.key)}
                  disabled={busyKey === t.key}
                  className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  ذخیره
                </button>
                {t.isCustomized && (
                  <button
                    type="button"
                    onClick={() => reset(t.key)}
                    disabled={busyKey === t.key}
                    className="rounded-[10px] border border-(--edge-2) px-4 py-2 text-[12.5px] text-(--ice-3) transition-colors hover:bg-(--hov) disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    بازگشت به پیش‌فرض
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
