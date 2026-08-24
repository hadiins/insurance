import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { isoToJalaliText, jalaliTextToIso, toJalaliDisplay } from "../../lib/jalali";

interface InsuranceLineDto {
  id: string;
  nameFa: string;
}

interface AgencyCommissionRateDto {
  id: string;
  insuranceLineId: string;
  insuranceLineNameFa: string;
  ratePercent: number;
  effectiveFrom: string;
  effectiveTo: string | null;
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)";

/** «کارمزد از بیمه‌گر» — the agency's own commission rate from the insurer, per insurance line
 * (including sub-lines). Locked into Policy.AgencyCommissionPercent at issuance; a rate change
 * here never touches past policies (docs/PHASE-1-SPEC.md-style rate history). */
export function AgencyCommissionRateSettingsPage() {
  const [lines, setLines] = useState<InsuranceLineDto[] | null>(null);
  const [rates, setRates] = useState<AgencyCommissionRateDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [newLineId, setNewLineId] = useState("");
  const [newRatePercent, setNewRatePercent] = useState("");
  const [newEffectiveFrom, setNewEffectiveFrom] = useState(isoToJalaliText(new Date().toISOString().slice(0, 10)));

  function reload() {
    api
      .get<AgencyCommissionRateDto[]>("/settings/agency-commission-rates")
      .then(setRates)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری نرخ‌ها"));
  }

  useEffect(() => {
    api.get<InsuranceLineDto[]>("/insurance-lines").then(setLines).catch(() => {});
    reload();
  }, []);

  async function addRate() {
    const iso = jalaliTextToIso(newEffectiveFrom);
    if (!newLineId || !newRatePercent || !iso) return;
    setError(null);
    try {
      await api.post("/settings/agency-commission-rates", {
        insuranceLineId: newLineId,
        ratePercent: Number(newRatePercent),
        effectiveFrom: iso,
      });
      setNewLineId("");
      setNewRatePercent("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت نرخ ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-4.5 text-xl font-extrabold tracking-tight text-(--ice)">
        کارمزد از <em className="font-extralight not-italic text-(--ice-2)">بیمه‌گر</em>
      </h2>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["رشته", "درصد", "از تاریخ", "تا تاریخ", ""].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rates?.map((r) => (
              <tr key={r.id} className="border-t border-(--edge) first:border-t-0">
                <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{r.insuranceLineNameFa}</td>
                <td className="px-3 py-2.5 text-[13px] font-semibold tabular-nums">{fa(r.ratePercent)}٪</td>
                <td className="px-3 py-2.5 text-[13px] tabular-nums">{toJalaliDisplay(r.effectiveFrom)}</td>
                <td className="px-3 py-2.5 text-[13px] tabular-nums text-(--ice-3)">
                  {r.effectiveTo ? toJalaliDisplay(r.effectiveTo) : "—"}
                </td>
                <td className="px-3 py-2.5 text-[13px]">
                  {!r.effectiveTo && <span className="rounded-full bg-(--mint)/12 px-2.5 py-0.5 text-[11px] font-semibold text-(--mint)">فعال</span>}
                </td>
              </tr>
            ))}
            {rates?.length === 0 && (
              <tr>
                <td colSpan={5} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  هنوز نرخی ثبت نشده است.
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
          <div className="w-24">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">درصد</label>
            <input value={newRatePercent} onChange={(e) => setNewRatePercent(e.target.value)} dir="ltr" className={inputClass} />
          </div>
          <div className="w-36">
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">از تاریخ</label>
            <input value={newEffectiveFrom} onChange={(e) => setNewEffectiveFrom(e.target.value)} dir="ltr" className={inputClass} />
          </div>
          <button
            type="button"
            onClick={addRate}
            disabled={!newLineId || !newRatePercent}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            + افزودن
          </button>
        </div>
      </div>
    </div>
  );
}
