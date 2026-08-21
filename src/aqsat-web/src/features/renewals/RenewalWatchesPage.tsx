import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface InsuranceLineDto {
  id: string;
  nameFa: string;
}

interface RenewalWatchDto {
  id: string;
  customerId: string | null;
  customerFullName: string | null;
  prospectName: string | null;
  prospectMobile: string | null;
  insuranceLineId: string;
  insuranceLineNameFa: string;
  currentInsurer: string | null;
  currentExpiryDate: string;
  notifyDaysBefore: number;
  marketerId: string | null;
  marketerFullName: string | null;
  status: "Watching" | "Notified" | "Converted" | "Lost";
  policyId: string | null;
}

const STATUS_LABEL: Record<RenewalWatchDto["status"], string> = {
  Watching: "در حال پایش",
  Notified: "یادآوری ارسال شد",
  Converted: "تمدید شد",
  Lost: "از دست رفت",
};

const STATUS_PILL_CLASS: Record<RenewalWatchDto["status"], string> = {
  Watching: "bg-(--mint)/12 text-(--mint)",
  Notified: "bg-(--amber)/13 text-(--amber)",
  Converted: "bg-(--mint)/12 text-(--mint)",
  Lost: "bg-(--ember)/13 text-(--ember)",
};

export function RenewalWatchesPage() {
  const [watches, setWatches] = useState<RenewalWatchDto[] | null>(null);
  const [lines, setLines] = useState<InsuranceLineDto[] | null>(null);
  const [statusFilter, setStatusFilter] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [convertingId, setConvertingId] = useState<string | null>(null);
  const [convertPolicyId, setConvertPolicyId] = useState("");

  const [prospectName, setProspectName] = useState("");
  const [prospectMobile, setProspectMobile] = useState("");
  const [lineId, setLineId] = useState("");
  const [currentInsurer, setCurrentInsurer] = useState("");
  const [expiryDate, setExpiryDate] = useState("");
  const [notifyDaysBefore, setNotifyDaysBefore] = useState("2");

  function reload() {
    const query = statusFilter ? `?status=${statusFilter}` : "";
    api.get<RenewalWatchDto[]>(`/renewal-watches${query}`).then(setWatches).catch(() => {});
  }

  useEffect(() => {
    reload();
    api.get<InsuranceLineDto[]>("/insurance-lines").then(setLines).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter]);

  async function registerProspect() {
    if (!prospectName.trim() || !prospectMobile.trim() || !lineId || !expiryDate) {
      setError("نام، شمارهٔ همراه، رشتهٔ بیمه و تاریخ سررسید الزامی است.");
      return;
    }
    setError(null);
    try {
      await api.post("/renewal-watches", {
        customerId: null,
        prospectName: prospectName.trim(),
        prospectMobile: prospectMobile.trim(),
        insuranceLineId: lineId,
        currentInsurer: currentInsurer.trim() || null,
        currentExpiryDate: expiryDate,
        notifyDaysBefore: Number(notifyDaysBefore) || 2,
        marketerId: null,
      });
      setProspectName("");
      setProspectMobile("");
      setCurrentInsurer("");
      setExpiryDate("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت سررسید ناموفق بود.");
    }
  }

  async function convert(id: string) {
    if (!convertPolicyId.trim()) {
      setError("شناسهٔ بیمه‌نامهٔ جدید را وارد کنید.");
      return;
    }
    setError(null);
    try {
      await api.post(`/renewal-watches/${id}/convert`, { policyId: convertPolicyId.trim() });
      setConvertingId(null);
      setConvertPolicyId("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت تمدید ناموفق بود.");
    }
  }

  async function markLost(id: string) {
    setError(null);
    try {
      await api.post(`/renewal-watches/${id}/lost`, {});
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "عملیات ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        سررسید <em className="font-extralight not-italic text-(--ice-2)">تمدید</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">
        مشتریان احتمالی و بیمه‌نامه‌های در حال انقضا — یادآوری خودکار به مشتری و بازاریاب
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">ثبت مشتری احتمالی (واکینگ)</div>
        <div className="mb-3 grid grid-cols-5 gap-3">
          <input
            value={prospectName}
            onChange={(e) => setProspectName(e.target.value)}
            placeholder="نام مشتری احتمالی"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            value={prospectMobile}
            onChange={(e) => setProspectMobile(e.target.value)}
            placeholder="۰۹۱۲۳۴۵۶۷۸۹"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <select
            value={lineId}
            onChange={(e) => setLineId(e.target.value)}
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice)"
          >
            <option value="">رشتهٔ بیمه…</option>
            {lines?.map((l) => (
              <option key={l.id} value={l.id}>
                {l.nameFa}
              </option>
            ))}
          </select>
          <input
            value={currentInsurer}
            onChange={(e) => setCurrentInsurer(e.target.value)}
            placeholder="بیمه‌گر فعلی"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            type="date"
            value={expiryDate}
            onChange={(e) => setExpiryDate(e.target.value)}
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice)"
          />
        </div>
        <div className="flex items-center gap-3">
          <label className="text-[11.5px] text-(--ice-3)">یادآوری چند روز قبل از سررسید</label>
          <input
            value={notifyDaysBefore}
            onChange={(e) => setNotifyDaysBefore(e.target.value)}
            className="w-16 rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12px] text-(--ice)"
          />
          <button
            type="button"
            onClick={registerProspect}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
          >
            ثبت سررسید
          </button>
        </div>
      </div>

      <div className="mb-3 flex gap-2">
        {(["", "Watching", "Notified", "Converted", "Lost"] as const).map((s) => (
          <button
            key={s}
            type="button"
            onClick={() => setStatusFilter(s)}
            className={`rounded-full px-3 py-1 text-[11.5px] font-semibold transition-colors ${
              statusFilter === s ? "bg-(--mint) text-(--on-mint)" : "border border-(--edge-2) text-(--ice-3) hover:bg-(--hov)"
            }`}
          >
            {s === "" ? "همه" : STATUS_LABEL[s]}
          </button>
        ))}
      </div>

      {watches !== null && <div className="mb-2 text-[11px] text-(--ice-3)">{fa(watches.length)} مورد</div>}

      {watches === null ? (
        <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
      ) : watches.length === 0 ? (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">
          موردی یافت نشد.
        </div>
      ) : (
        <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
          <table className="w-full border-collapse">
            <thead>
              <tr>
                {["نام", "رشته", "بیمه‌گر فعلی", "سررسید", "بازاریاب", "وضعیت", ""].map((h) => (
                  <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {watches.map((w) => (
                <tr key={w.id} className="border-t border-(--edge) first:border-t-0">
                  <td className="px-3 py-2.75 text-[13px] font-semibold">{w.customerFullName ?? w.prospectName}</td>
                  <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{w.insuranceLineNameFa}</td>
                  <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{w.currentInsurer ?? "—"}</td>
                  <td className="px-3 py-2.75 text-[13px]">{fa(w.currentExpiryDate)}</td>
                  <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{w.marketerFullName ?? "—"}</td>
                  <td className="px-3 py-2.75 text-[13px]">
                    <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${STATUS_PILL_CLASS[w.status]}`}>
                      {STATUS_LABEL[w.status]}
                    </span>
                  </td>
                  <td className="px-3 py-2.75 text-[13px]">
                    {(w.status === "Watching" || w.status === "Notified") && (
                      <div className="flex items-center gap-1.5">
                        {convertingId === w.id ? (
                          <>
                            <input
                              value={convertPolicyId}
                              onChange={(e) => setConvertPolicyId(e.target.value)}
                              placeholder="شناسهٔ بیمه‌نامهٔ جدید"
                              className="w-40 rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1 text-[11px] text-(--ice)"
                            />
                            <button
                              type="button"
                              onClick={() => convert(w.id)}
                              className="rounded-[8px] border border-(--mint) bg-(--mint) px-2.5 py-1 text-[11px] font-semibold text-(--on-mint)"
                            >
                              تأیید
                            </button>
                          </>
                        ) : (
                          <button
                            type="button"
                            onClick={() => {
                              setConvertingId(w.id);
                              setConvertPolicyId("");
                            }}
                            className="rounded-[8px] border border-(--mint) bg-(--mint) px-2.5 py-1 text-[11px] font-semibold text-(--on-mint)"
                          >
                            تمدید شد
                          </button>
                        )}
                        <button
                          type="button"
                          onClick={() => markLost(w.id)}
                          className="rounded-[8px] border border-(--edge-2) px-2.5 py-1 text-[11px] font-semibold text-(--ice-3) transition-colors hover:bg-(--hov)"
                        >
                          از دست رفت
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
