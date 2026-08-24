import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";

interface InsuranceLineDto {
  id: string;
  nameFa: string;
}

interface InsuranceLineCodeDto {
  id: string;
  insuranceLineId: string;
  insuranceLineNameFa: string;
  code: string;
  isActive: boolean;
}

interface PolicyNumberFormatDto {
  id: string;
  insurerName: string;
  pattern: string;
  separator: string;
  lineCodeLength: number;
  agencyCodeLength: number;
  yearDigits: number;
  serialLength: number;
  isStrict: boolean;
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)";

/** docs/TASK-24-POLICY-NUMBER.md §7 — settings for the two things the issuance form's locked
 * segments depend on: which numeric code maps to which line, and the tunable parts of the format
 * itself, with a live composed example. */
export function PolicyNumberSettingsPage() {
  const [lines, setLines] = useState<InsuranceLineDto[] | null>(null);
  const [codes, setCodes] = useState<InsuranceLineCodeDto[] | null>(null);
  const [format, setFormat] = useState<PolicyNumberFormatDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [newLineId, setNewLineId] = useState("");
  const [newCode, setNewCode] = useState("");

  function reload() {
    api.get<InsuranceLineCodeDto[]>("/settings/policy-number/line-codes").then(setCodes).catch(() => {});
    api.get<PolicyNumberFormatDto>("/settings/policy-number/format").then(setFormat).catch(() => {});
  }

  useEffect(() => {
    api.get<InsuranceLineDto[]>("/insurance-lines").then(setLines).catch(() => {});
    reload();
  }, []);

  async function addCode() {
    if (!newLineId || !newCode.trim()) return;
    setError(null);
    try {
      await api.post("/settings/policy-number/line-codes", { insuranceLineId: newLineId, code: newCode.trim() });
      setNewLineId("");
      setNewCode("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "افزودن کد ناموفق بود.");
    }
  }

  async function toggleActive(c: InsuranceLineCodeDto) {
    setError(null);
    try {
      await api.put(`/settings/policy-number/line-codes/${c.id}`, { code: c.code, isActive: !c.isActive });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی ناموفق بود.");
    }
  }

  async function removeCode(id: string) {
    setError(null);
    try {
      await api.delete(`/settings/policy-number/line-codes/${id}`);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "حذف ناموفق بود.");
    }
  }

  async function saveFormat(next: PolicyNumberFormatDto) {
    setError(null);
    try {
      const updated = await api.put<PolicyNumberFormatDto>("/settings/policy-number/format", {
        separator: next.separator,
        lineCodeLength: next.lineCodeLength,
        agencyCodeLength: next.agencyCodeLength,
        yearDigits: next.yearDigits,
        serialLength: next.serialLength,
        isStrict: next.isStrict,
      });
      setFormat(updated);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیرهٔ الگو ناموفق بود.");
    }
  }

  const example = format
    ? [
        "1".padStart(format.lineCodeLength, "1").slice(0, format.lineCodeLength),
        "5".repeat(format.agencyCodeLength),
        format.yearDigits === 3 ? "405" : "1405",
        "1".padStart(format.serialLength, "0"),
      ].join(format.separator)
    : "";

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        کدهای <em className="font-extralight not-italic text-(--ice-2)">بیمه‌نامه</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">{format ? `شرکت بیمه: ${format.insurerName}` : "…"}</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["کد", "رشته", "وضعیت", ""].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {codes?.map((c) => (
              <tr key={c.id} className="border-t border-(--edge) first:border-t-0">
                <td className="px-3 py-2.5 text-[13px] font-semibold tabular-nums" dir="ltr">
                  {c.code}
                </td>
                <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{c.insuranceLineNameFa}</td>
                <td className="px-3 py-2.5 text-[13px]">
                  <span
                    className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${c.isActive ? "bg-(--mint)/12 text-(--mint)" : "bg-(--ice-3)/12 text-(--ice-3)"}`}
                  >
                    {c.isActive ? "فعال" : "غیرفعال"}
                  </span>
                </td>
                <td className="px-3 py-2.5 text-[13px]">
                  <button type="button" onClick={() => toggleActive(c)} className="ms-2 text-[11px] text-(--ice-3) hover:text-(--ice)">
                    {c.isActive ? "غیرفعال کردن" : "فعال کردن"}
                  </button>
                  <button type="button" onClick={() => removeCode(c.id)} className="ms-2 text-[11px] text-(--ember) hover:brightness-110">
                    حذف
                  </button>
                </td>
              </tr>
            ))}
            {codes?.length === 0 && (
              <tr>
                <td colSpan={4} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  هنوز کدی ثبت نشده است.
                </td>
              </tr>
            )}
          </tbody>
        </table>
        <div className="flex items-end gap-2 border-t border-(--edge) p-3">
          <div className="w-56">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">رشته</label>
            <select value={newLineId} onChange={(e) => setNewLineId(e.target.value)} className={inputClass}>
              <option value="">انتخاب کنید…</option>
              {lines?.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.nameFa}
                </option>
              ))}
            </select>
          </div>
          <div className="w-28">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">کد</label>
            <input value={newCode} onChange={(e) => setNewCode(e.target.value)} dir="ltr" className={inputClass} />
          </div>
          <button
            type="button"
            onClick={addCode}
            disabled={!newLineId || !newCode.trim()}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            + افزودن
          </button>
        </div>
      </div>

      {format && (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">الگوی شماره</div>
          <div className="grid grid-cols-3 gap-3">
            <FormatField label="جداکننده">
              <input
                value={format.separator}
                onChange={(e) => setFormat({ ...format, separator: e.target.value })}
                onBlur={() => saveFormat(format)}
                dir="ltr"
                className={inputClass}
              />
            </FormatField>
            <FormatField label="طول کد رشته">
              <input
                value={format.lineCodeLength}
                onChange={(e) => setFormat({ ...format, lineCodeLength: Number(e.target.value) || 0 })}
                onBlur={() => saveFormat(format)}
                className={inputClass}
              />
            </FormatField>
            <FormatField label="طول کد نمایندگی">
              <input
                value={format.agencyCodeLength}
                onChange={(e) => setFormat({ ...format, agencyCodeLength: Number(e.target.value) || 0 })}
                onBlur={() => saveFormat(format)}
                className={inputClass}
              />
            </FormatField>
            <FormatField label="طول سریال">
              <input
                value={format.serialLength}
                onChange={(e) => setFormat({ ...format, serialLength: Number(e.target.value) || 0 })}
                onBlur={() => saveFormat(format)}
                className={inputClass}
              />
            </FormatField>
            <FormatField label="رقم سال">
              <select
                value={format.yearDigits}
                onChange={(e) => {
                  const next = { ...format, yearDigits: Number(e.target.value) };
                  setFormat(next);
                  saveFormat(next);
                }}
                className={inputClass}
              >
                <option value={3}>سه رقم (۴۰۵)</option>
                <option value={4}>چهار رقم (۱۴۰۵)</option>
              </select>
            </FormatField>
            <FormatField label="اعتبارسنجی سخت‌گیرانهٔ طول">
              <select
                value={format.isStrict ? "yes" : "no"}
                onChange={(e) => {
                  const next = { ...format, isStrict: e.target.value === "yes" };
                  setFormat(next);
                  saveFormat(next);
                }}
                className={inputClass}
              >
                <option value="no">غیرفعال</option>
                <option value="yes">فعال</option>
              </select>
            </FormatField>
          </div>
          <div className="mt-4 text-[12px] text-(--ice-3)">
            نمونهٔ زنده: <span className="tabular-nums text-(--mint)" dir="ltr">{example}</span>
          </div>
        </div>
      )}
    </div>
  );
}

function FormatField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">{label}</label>
      {children}
    </div>
  );
}
