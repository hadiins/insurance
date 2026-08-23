import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface AgencyDto {
  id: string;
  code: string;
  name: string;
  city: string | null;
  insurerName: string | null;
  isActive: boolean;
  userCount: number;
}

interface CreateAgencyResultDto {
  agency: AgencyDto;
  managerMobile: string;
  roleName: string;
}

export function AgenciesManagementPage() {
  const [agencies, setAgencies] = useState<AgencyDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [justCreated, setJustCreated] = useState<CreateAgencyResultDto | null>(null);

  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [city, setCity] = useState("");
  const [insurerName, setInsurerName] = useState("");
  const [managerFullName, setManagerFullName] = useState("");
  const [managerMobile, setManagerMobile] = useState("");
  const [managerPassword, setManagerPassword] = useState("");

  function reload() {
    api.get<AgencyDto[]>("/platform/agencies").then(setAgencies).catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری نمایندگی‌ها"));
  }

  useEffect(reload, []);

  async function create() {
    setBusy(true);
    setError(null);
    setJustCreated(null);
    try {
      const result = await api.post<CreateAgencyResultDto>("/platform/agencies", {
        code: code.trim(),
        name: name.trim(),
        city: city.trim() || null,
        insurerName: insurerName.trim() || null,
        managerFullName: managerFullName.trim(),
        managerMobile: managerMobile.trim(),
        managerPassword,
        roleId: null,
      });
      setJustCreated(result);
      setCode("");
      setName("");
      setCity("");
      setInsurerName("");
      setManagerFullName("");
      setManagerMobile("");
      setManagerPassword("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ساخت نمایندگی ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">نمایندگی‌ها</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">ساخت نمایندگی جدید همراه با اولین کاربر (مدیر) آن</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {justCreated && (
        <div className="mb-4.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">
          نمایندگی «{justCreated.agency.name}» ساخته شد. اطلاعات ورود اولین کاربر: شمارهٔ همراه {fa(justCreated.managerMobile)} با نقش «{justCreated.roleName}» — رمز عبوری که وارد کردید.
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">نمایندگی جدید</div>
        <div className="mb-3 grid grid-cols-2 gap-3">
          <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="کد نمایندگی" className={inputClass} />
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="نام نمایندگی" className={inputClass} />
          <input value={city} onChange={(e) => setCity(e.target.value)} placeholder="شهر (اختیاری)" className={inputClass} />
          <input value={insurerName} onChange={(e) => setInsurerName(e.target.value)} placeholder="شرکت بیمهٔ طرف قرارداد (اختیاری)" className={inputClass} />
        </div>
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">اولین کاربر (مدیر نمایندگی)</div>
        <div className="mb-3 grid grid-cols-3 gap-3">
          <input value={managerFullName} onChange={(e) => setManagerFullName(e.target.value)} placeholder="نام کامل" className={inputClass} />
          <input value={managerMobile} onChange={(e) => setManagerMobile(e.target.value)} placeholder="شمارهٔ همراه" className={inputClass} />
          <input value={managerPassword} onChange={(e) => setManagerPassword(e.target.value)} type="password" placeholder="رمز عبور" className={inputClass} />
        </div>
        <button
          type="button"
          onClick={create}
          disabled={busy}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال ساخت…" : "ساخت نمایندگی"}
        </button>
      </div>

      {agencies === null ? (
        <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
      ) : agencies.length === 0 ? (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">هنوز نمایندگی‌ای ساخته نشده.</div>
      ) : (
        <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
          <table className="w-full border-collapse">
            <thead>
              <tr>
                {["کد", "نام", "شهر", "شرکت بیمه", "کاربران"].map((h) => (
                  <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {agencies.map((a) => (
                <tr key={a.id} className="border-t border-(--edge) first:border-t-0">
                  <td className="px-3 py-2.5 text-[13px] font-semibold">{fa(a.code)}</td>
                  <td className="px-3 py-2.5 text-[13px]">{a.name}</td>
                  <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{a.city ?? "—"}</td>
                  <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{a.insurerName ?? "—"}</td>
                  <td className="px-3 py-2.5 text-[13px]">{fa(a.userCount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

const inputClass =
  "rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)";
