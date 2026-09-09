import { useState } from "react";
import { api, ApiError, getActiveOrgId, getToken } from "../../lib/api";
import { fa } from "../../lib/persian";

interface MissingSerialsReportDto {
  year: number;
  rangeStart: string;
  rangeEnd: string | null;
  registeredCount: number;
  missingCount: number;
  missing: string[];
}

const CURRENT_JALALI_YEAR_GUESS = 1405;

/** docs/TASK-24-POLICY-NUMBER.md §3 — "این یک ابزار تطبیق رایگان با فناوران است، بدون هیچ API":
 * a gap in this agency's serial sequence usually means a policy was issued in Fanavaran but never
 * entered here. */
export function MissingSerialsPage() {
  const [year, setYear] = useState(String(CURRENT_JALALI_YEAR_GUESS));
  const [result, setResult] = useState<MissingSerialsReportDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run() {
    const y = Number(year);
    if (!Number.isFinite(y) || y < 1300 || y > 1500) {
      setError("سال شمسی نامعتبر است.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const r = await api.get<MissingSerialsReportDto>(`/reports/missing-serials?year=${y}`);
      setResult(r);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "دریافت گزارش ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function exportXlsx() {
    const token = getToken();
    const orgId = getActiveOrgId();
    const headers = new Headers();
    if (token) headers.set("Authorization", `Bearer ${token}`);
    if (orgId) headers.set("X-Organization-Id", orgId);

    const response = await fetch(`/api/reports/missing-serials/export?year=${year}`, { headers });
    if (!response.ok) {
      setError("خروجی اکسل ناموفق بود.");
      return;
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `missing-serials-${year}.xlsx`;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        شماره‌های <em className="font-extralight not-italic text-(--ice-2)">جا افتاده</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        فهرست سریال‌های غایب در سال جاری — تطبیق رایگان با فناوران، بدون هیچ API
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4 flex items-end gap-2.5 rounded-2xl border border-(--edge) bg-(--pane) p-4">
        <div>
          <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">سال (شمسی)</label>
          <input
            value={year}
            onChange={(e) => setYear(e.target.value.replace(/\D/g, ""))}
            dir="ltr"
            className="w-28 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-center text-[13.5px] tabular-nums text-(--ice) outline-none focus:border-(--mint)"
          />
        </div>
        <button
          type="button"
          onClick={run}
          disabled={busy}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال بررسی…" : "بررسی"}
        </button>
        {result && result.missingCount > 0 && (
          <button
            type="button"
            onClick={exportXlsx}
            className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
          >
            خروجی اکسل
          </button>
        )}
      </div>

      {result && (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          {result.registeredCount === 0 ? (
            <div className="text-[13.5px] text-(--ice-3)">هیچ بیمه‌نامهٔ تجزیه‌شده‌ای در سال {fa(result.year)} یافت نشد.</div>
          ) : (
            <>
              <div className="mb-4 text-[13.5px] text-(--ice-2)" dir="ltr">
                محدوده: {fa(result.rangeStart)} تا {fa(result.rangeEnd!)} · ثبت‌شده: {fa(result.registeredCount)} · غایب:{" "}
                <span className={result.missingCount > 0 ? "font-bold text-(--ember)" : "font-bold text-(--mint)"}>
                  {fa(result.missingCount)}
                </span>
              </div>
              {result.missingCount === 0 ? (
                <div className="text-[13.5px] text-(--mint)">✅ هیچ شماره‌ای جا نیفتاده است.</div>
              ) : (
                <div className="grid grid-cols-6 gap-2 tabular-nums" dir="ltr">
                  {result.missing.map((s) => (
                    <div
                      key={s}
                      className="rounded-[8px] border border-(--ember)/30 bg-(--ember)/10 px-2 py-1.5 text-center text-[12.5px] text-(--ember)"
                    >
                      {fa(s)}
                    </div>
                  ))}
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}
