import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface ImportTargetFieldDto {
  key: string;
  label: string;
  required: boolean;
  type: string;
}

interface ColumnMappingResponse {
  mapping: Record<string, string> | null;
}

interface ImportCommitResponse {
  batchId: string;
  newCount: number;
  duplicateCount: number;
  failedCount: number;
}

const IMPORT_TYPE = "FanavaranPolicyReport";

export function FanavaranImportPage() {
  const [fields, setFields] = useState<ImportTargetFieldDto[] | null>(null);
  const [mapping, setMapping] = useState<Record<string, string>>({});
  const [mappingSaved, setMappingSaved] = useState(false);
  const [savingMapping, setSavingMapping] = useState(false);

  const [file, setFile] = useState<File | null>(null);
  const [committing, setCommitting] = useState(false);
  const [report, setReport] = useState<ImportCommitResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      api.get<ImportTargetFieldDto[]>("/imports/fanavaran/fields"),
      api.get<ColumnMappingResponse>(`/imports/column-mapping?importType=${IMPORT_TYPE}`),
    ])
      .then(([targetFields, saved]) => {
        setFields(targetFields);
        setMapping(saved.mapping ?? {});
        setMappingSaved(Boolean(saved.mapping));
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری تنظیمات"));
  }, []);

  async function saveMapping() {
    setSavingMapping(true);
    setError(null);
    try {
      await api.put("/imports/column-mapping", { importType: IMPORT_TYPE, mapping });
      setMappingSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیرهٔ نگاشت ناموفق بود.");
    } finally {
      setSavingMapping(false);
    }
  }

  async function commitFile() {
    if (!file) return;
    setCommitting(true);
    setError(null);
    setReport(null);
    try {
      const form = new FormData();
      form.append("file", file);
      const result = await api.postForm<ImportCommitResponse>("/imports/fanavaran/commit", form);
      setReport(result);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت فایل ناموفق بود.");
    } finally {
      setCommitting(false);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        آپلود <em className="font-extralight not-italic text-(--ice-2)">فایل فناوران</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">
        گزارش بیمه‌نامه (شیت CarSalesBNVer) — نگاشت ستون‌ها را یک‌بار تنظیم کنید، بعد فایل را آپلود کنید
      </div>

      {error && (
        <div className="mb-4 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 flex items-center justify-between">
          <b className="text-[13.5px] text-(--ice)">نگاشت ستون‌ها</b>
          {mappingSaved && <span className="text-[11px] text-(--mint)">ذخیره شده</span>}
        </div>

        {fields === null ? (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        ) : (
          <>
            <div className="grid grid-cols-2 gap-3.5">
              {fields.map((field) => (
                <div key={field.key}>
                  <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">
                    {field.label}
                    {field.required && <span className="text-(--ember)"> *</span>}
                  </label>
                  <input
                    value={mapping[field.key] ?? ""}
                    onChange={(e) => setMapping((m) => ({ ...m, [field.key]: e.target.value }))}
                    placeholder="عنوان ستون در فایل اکسل"
                    className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
                  />
                </div>
              ))}
            </div>
            <button
              type="button"
              onClick={saveMapping}
              disabled={savingMapping}
              className="mt-4 rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {savingMapping ? "در حال ذخیره…" : "ذخیرهٔ نگاشت"}
            </button>
          </>
        )}
      </div>

      <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <b className="mb-3 block text-[13.5px] text-(--ice)">آپلود و ثبت</b>

        {!mappingSaved && (
          <div className="mb-3 rounded-[10px] border border-(--amber)/30 bg-(--amber)/10 px-3 py-2 text-[12px] text-(--amber)">
            قبل از آپلود، نگاشت ستون‌ها را ذخیره کنید.
          </div>
        )}

        <input
          type="file"
          accept=".xlsx"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          className="mb-3.5 block w-full text-[12.5px] text-(--ice-2)"
        />

        <button
          type="button"
          onClick={commitFile}
          disabled={!file || !mappingSaved || committing}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {committing ? "در حال ثبت…" : "آپلود و ثبت"}
        </button>

        {report && (
          <div className="mt-4 grid grid-cols-3 gap-3">
            <ReportStat label="جدید" value={report.newCount} color="var(--mint)" />
            <ReportStat label="تکراری" value={report.duplicateCount} color="var(--amber)" />
            <ReportStat label="ناموفق" value={report.failedCount} color="var(--ember)" />
          </div>
        )}
      </div>
    </div>
  );
}

function ReportStat({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div className="rounded-[10px] border border-(--edge-2) bg-(--fld) p-3 text-center">
      <div className="text-[11px] text-(--ice-3)">{label}</div>
      <div className="text-lg font-bold" style={{ color }}>
        {fa(value)}
      </div>
    </div>
  );
}
