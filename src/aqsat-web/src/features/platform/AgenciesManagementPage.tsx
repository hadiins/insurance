import { useCallback, useEffect, useMemo, useState } from "react";
import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { useTabsStore } from "../../app/store/tabsStore";

interface AgencyListRowDto {
  id: string;
  code: string;
  name: string;
  province: string | null;
  city: string | null;
  insurerName: string | null;
  isActive: boolean;
  userCount: number;
  policiesTotal: number;
  smsSentTotal: number;
  inquiryPaymentsTotal: number;
  inquiryCallsTotal: number;
  inquiryRevenueToman: number;
}

interface AgencyListPageDto {
  totalCount: number;
  rows: AgencyListRowDto[];
}

interface AgencyFilterOptionsDto {
  provinces: string[];
  cities: string[];
  insurers: string[];
}

interface AgencyProvinceStatDto {
  province: string | null;
  agencyCount: number;
  policiesTotal: number;
  smsSentTotal: number;
  inquiryPaymentsTotal: number;
  inquiryRevenueToman: number;
}

interface AgencyPlatformSummaryDto {
  totalAgencies: number;
  activeAgencies: number;
  policiesTotal: number;
  smsSentTotal: number;
  smsCostToman: number;
  inquiryPaymentsTotal: number;
  inquiryRevenueToman: number;
  inquiryCallsTotal: number;
  byProvince: AgencyProvinceStatDto[];
}

interface CreateAgencyResultDto {
  agency: { id: string; code: string; name: string };
  managerMobile: string;
  roleName: string;
}

const PAGE_SIZE = 25;
const CHART_COLOR = "var(--mint)";

type SortKey = "name" | "code" | "users" | "policies" | "sms" | "inquiries" | "inquirycalls" | "revenue";

export function AgenciesManagementPage() {
  const [summary, setSummary] = useState<AgencyPlatformSummaryDto | null>(null);
  const [filters, setFilters] = useState<AgencyFilterOptionsDto | null>(null);
  const [pageData, setPageData] = useState<AgencyListPageDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [listError, setListError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [justCreated, setJustCreated] = useState<CreateAgencyResultDto | null>(null);

  const [province, setProvince] = useState("");
  const [city, setCity] = useState("");
  const [insurer, setInsurer] = useState("");
  const [status, setStatus] = useState("");
  const [search, setSearch] = useState("");
  const [sortBy, setSortBy] = useState<SortKey>("name");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [page, setPage] = useState(1);

  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [newProvince, setNewProvince] = useState("");
  const [newCity, setNewCity] = useState("");
  const [newInsurer, setNewInsurer] = useState("");
  const [managerFullName, setManagerFullName] = useState("");
  const [managerMobile, setManagerMobile] = useState("");
  const [managerPassword, setManagerPassword] = useState("");

  const hasActiveFilter = province !== "" || city !== "" || insurer !== "" || status !== "" || search.trim() !== "";
  const totalPages = pageData ? Math.max(1, Math.ceil(pageData.totalCount / PAGE_SIZE)) : 1;

  const loadList = useCallback(() => {
    const params = new URLSearchParams();
    if (province) params.set("province", province);
    if (city) params.set("city", city);
    if (insurer) params.set("insurer", insurer);
    if (status) params.set("isActive", status);
    if (search.trim()) params.set("search", search.trim());
    params.set("sortBy", sortBy);
    params.set("sortDir", sortDir);
    params.set("page", String(page));
    params.set("pageSize", String(PAGE_SIZE));

    setListError(null);
    api
      .get<AgencyListPageDto>(`/platform/agencies?${params.toString()}`)
      .then(setPageData)
      .catch((err) => setListError(err instanceof ApiError ? err.message : "خطا در بارگذاری نمایندگی‌ها"));
  }, [province, city, insurer, status, search, sortBy, sortDir, page]);

  useEffect(() => {
    api
      .get<AgencyPlatformSummaryDto>("/platform/agencies/summary")
      .then(setSummary)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری خلاصه"));
    api
      .get<AgencyFilterOptionsDto>("/platform/agencies/filters")
      .then(setFilters)
      .catch(() => setFilters({ provinces: [], cities: [], insurers: [] }));
  }, []);

  useEffect(loadList, [loadList]);

  function clearFilters() {
    setProvince("");
    setCity("");
    setInsurer("");
    setStatus("");
    setSearch("");
    setPage(1);
  }

  function applyFilter(setter: (v: string) => void, value: string) {
    setter(value);
    setPage(1);
  }

  function toggleSort(key: SortKey) {
    if (sortBy === key) {
      setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    } else {
      setSortBy(key);
      setSortDir("desc");
    }
    setPage(1);
  }

  async function create() {
    setBusy(true);
    setError(null);
    setJustCreated(null);
    try {
      const result = await api.post<CreateAgencyResultDto>("/platform/agencies", {
        code: code.trim(),
        name: name.trim(),
        province: newProvince || null,
        city: newCity.trim() || null,
        insurerName: newInsurer.trim() || null,
        managerFullName: managerFullName.trim(),
        managerMobile: managerMobile.trim(),
        managerPassword,
        roleId: null,
      });
      setJustCreated(result);
      setCode("");
      setName("");
      setNewProvince("");
      setNewCity("");
      setNewInsurer("");
      setManagerFullName("");
      setManagerMobile("");
      setManagerPassword("");
      loadList();
      api.get<AgencyPlatformSummaryDto>("/platform/agencies/summary").then(setSummary).catch(() => {});
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ساخت نمایندگی ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  const chartData = useMemo(
    () =>
      (summary?.byProvince ?? [])
        .filter((p) => p.policiesTotal > 0)
        .sort((a, b) => b.policiesTotal - a.policiesTotal)
        .slice(0, 10)
        .map((p) => ({
          name: p.province ?? "بدون استان",
          policies: p.policiesTotal,
          agencies: p.agencyCount,
        })),
    [summary],
  );

  const openTab = useTabsStore((s) => s.openTab);

  function openProfile(row: AgencyListRowDto) {
    openTab({
      navType: "agency-profile",
      page: "agency-profile",
      kind: "multi-record",
      recordId: row.id,
      title: `پروندهٔ ${row.name}`,
      payload: { agencyId: row.id },
    });
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">نمایندگی‌ها</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">مدیریت و پروندهٔ تمام نمایندگی‌های فعال روی پلتفرم</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {summary === null && !error && (
        <div className="mb-4.5 text-[12.5px] text-(--ice-3)">در حال بارگذاری خلاصه…</div>
      )}

      {summary && (
        <div className="mb-4.5 grid grid-cols-2 gap-3 lg:grid-cols-4 xl:grid-cols-8">
          <Fig label="نمایندگی" value={fa(summary.totalAgencies)} />
          <Fig label="فعال" value={fa(summary.activeAgencies)} />
          <Fig label="بیمه‌نامه" value={fa(summary.policiesTotal)} />
          <Fig label="پیامک" value={fa(summary.smsSentTotal)} />
          <Fig label="هزینهٔ پیامک (تومان)" value={money(summary.smsCostToman)} />
          <Fig label="پرداخت استعلام" value={fa(summary.inquiryPaymentsTotal)} />
          <Fig label="اجرای استعلام" value={fa(summary.inquiryCallsTotal)} />
          <Fig label="درآمد استعلام (تومان)" value={money(summary.inquiryRevenueToman)} tone="mint" />
        </div>
      )}

      {chartData.length > 0 && (
        <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">بیمه‌نامه‌های صادرشده به تفکیک استان — ۱۰ استان برتر</div>
          <div className="h-64" dir="ltr">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={chartData} margin={{ top: 4, right: 12, left: 12, bottom: 4 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--edge)" />
                <XAxis
                  dataKey="name"
                  tick={{ fontSize: 11, fill: "var(--ice-3)" }}
                  interval={0}
                  angle={-28}
                  textAnchor="end"
                  height={56}
                />
                <YAxis tick={{ fontSize: 11, fill: "var(--ice-3)" }} tickFormatter={(v) => fa(v)} width={48} />
                <Tooltip
                  formatter={(value, key) => [fa(Number(value)), key === "policies" ? "بیمه‌نامه" : "نمایندگی"]}
                  labelStyle={{ color: "var(--ice)" }}
                  contentStyle={{ background: "var(--pane)", border: "1px solid var(--edge)", borderRadius: 10, fontSize: 12, direction: "rtl" }}
                />
                <Bar dataKey="policies" radius={[4, 4, 0, 0]}>
                  {chartData.map((entry) => (
                    <Cell key={entry.name} fill={CHART_COLOR} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <input
            value={search}
            onChange={(e) => applyFilter(setSearch, e.target.value)}
            placeholder="جستجوی نام یا کد نمایندگی…"
            className={`${inputClass} w-64`}
          />
          <select value={province} onChange={(e) => applyFilter(setProvince, e.target.value)} className={`${inputClass} w-44`}>
            <option value="">همهٔ استان‌ها</option>
            {(filters?.provinces ?? []).map((p) => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
          <select value={city} onChange={(e) => applyFilter(setCity, e.target.value)} className={`${inputClass} w-40`}>
            <option value="">همهٔ شهرها</option>
            {(filters?.cities ?? []).map((c) => (
              <option key={c} value={c}>{c}</option>
            ))}
          </select>
          <select value={insurer} onChange={(e) => applyFilter(setInsurer, e.target.value)} className={`${inputClass} w-44`}>
            <option value="">همهٔ شرکت‌های بیمه</option>
            {(filters?.insurers ?? []).map((i) => (
              <option key={i} value={i}>{i}</option>
            ))}
          </select>
          <select value={status} onChange={(e) => applyFilter(setStatus, e.target.value)} className={`${inputClass} w-32`}>
            <option value="">همهٔ وضعیت‌ها</option>
            <option value="true">فعال</option>
            <option value="false">غیرفعال</option>
          </select>
          {hasActiveFilter && (
            <button
              type="button"
              onClick={clearFilters}
              className="rounded-[10px] border border-(--edge-2) px-3 py-2 text-[11.5px] text-(--ice-2) transition-colors hover:border-(--ember) hover:text-(--ember)"
            >
              حذف فیلترها
            </button>
          )}
        </div>

        {listError ? (
          <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{listError}</div>
        ) : pageData === null ? (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        ) : pageData.rows.length === 0 ? (
          <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">
            {hasActiveFilter ? "نمایندگی‌ای با این فیلترها یافت نشد." : "هنوز نمایندگی‌ای ساخته نشده."}
          </div>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    <SortableTh label="کد" active={sortBy === "code"} dir={sortDir} onClick={() => toggleSort("code")} />
                    <SortableTh label="نام" active={sortBy === "name"} dir={sortDir} onClick={() => toggleSort("name")} />
                    <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">استان / شهر</th>
                    <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">شرکت بیمه</th>
                    <SortableTh label="کاربران" active={sortBy === "users"} dir={sortDir} onClick={() => toggleSort("users")} />
                    <SortableTh label="بیمه‌نامه" active={sortBy === "policies"} dir={sortDir} onClick={() => toggleSort("policies")} />
                    <SortableTh label="پیامک" active={sortBy === "sms"} dir={sortDir} onClick={() => toggleSort("sms")} />
                    <SortableTh label="پرداخت استعلام" active={sortBy === "inquiries"} dir={sortDir} onClick={() => toggleSort("inquiries")} />
                    <SortableTh label="اجرای استعلام" active={sortBy === "inquirycalls"} dir={sortDir} onClick={() => toggleSort("inquirycalls")} />
                    <SortableTh label="درآمد (تومان)" active={sortBy === "revenue"} dir={sortDir} onClick={() => toggleSort("revenue")} />
                    <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">وضعیت</th>
                  </tr>
                </thead>
                <tbody>
                  {pageData.rows.map((a) => (
                    <tr
                      key={a.id}
                      onClick={() => openProfile(a)}
                      className="cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--fld)"
                      title={`پروندهٔ ${a.name}`}
                    >
                      <td className="px-3 py-2.5 text-[13px] font-semibold text-(--ice)">{fa(a.code)}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ice)">{a.name}</td>
                      <td className="px-3 py-2.5 text-[12.5px] text-(--ice-3)">
                        {a.province ?? "—"}{a.city ? ` / ${a.city}` : ""}
                      </td>
                      <td className="px-3 py-2.5 text-[12.5px] text-(--ice-3)">{a.insurerName ?? "—"}</td>
                      <td className="px-3 py-2.5 text-[13px]">{fa(a.userCount)}</td>
                      <td className="px-3 py-2.5 text-[13px]">{fa(a.policiesTotal)}</td>
                      <td className="px-3 py-2.5 text-[13px]">{fa(a.smsSentTotal)}</td>
                      <td className="px-3 py-2.5 text-[13px]">{fa(a.inquiryPaymentsTotal)}</td>
                      <td className="px-3 py-2.5 text-[13px]">{fa(a.inquiryCallsTotal)}</td>
                      <td className="px-3 py-2.5 text-[13px]">{money(a.inquiryRevenueToman)}</td>
                      <td className="px-3 py-2.5">
                        <span className={`rounded-full px-2 py-0.5 text-[10.5px] font-semibold ${a.isActive ? "bg-(--mint)/15 text-(--mint)" : "bg-(--ember)/15 text-(--ember)"}`}>
                          {a.isActive ? "فعال" : "غیرفعال"}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="mt-3 flex items-center justify-between text-[12px] text-(--ice-3)">
              <div>مجموع: {fa(pageData.totalCount)} نمایندگی</div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  className="rounded-[10px] border border-(--edge-2) px-3 py-1.5 transition-colors hover:border-(--mint) disabled:cursor-not-allowed disabled:opacity-40"
                >
                  قبلی
                </button>
                <span>صفحهٔ {fa(page)} از {fa(totalPages)}</span>
                <button
                  type="button"
                  disabled={page >= totalPages}
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  className="rounded-[10px] border border-(--edge-2) px-3 py-1.5 transition-colors hover:border-(--mint) disabled:cursor-not-allowed disabled:opacity-40"
                >
                  بعدی
                </button>
              </div>
            </div>
          </>
        )}
      </div>

      {justCreated && (
        <div className="mb-4.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">
          نمایندگی «{justCreated.agency.name}» ساخته شد. اطلاعات ورود اولین کاربر: شمارهٔ همراه {fa(justCreated.managerMobile)} با نقش «{justCreated.roleName}» — رمز عبوری که وارد کردید.
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">نمایندگی جدید</div>
        <div className="mb-3 grid grid-cols-2 gap-3 lg:grid-cols-4">
          <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="کد نمایندگی" className={inputClass} />
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="نام نمایندگی" className={inputClass} />
          <select value={newProvince} onChange={(e) => setNewProvince(e.target.value)} className={inputClass}>
            <option value="">استان (اختیاری)</option>
            {(filters?.provinces ?? []).map((p) => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
          <input value={newCity} onChange={(e) => setNewCity(e.target.value)} placeholder="شهر (اختیاری)" className={inputClass} />
          <input value={newInsurer} onChange={(e) => setNewInsurer(e.target.value)} placeholder="شرکت بیمهٔ طرف قرارداد (اختیاری)" className={inputClass} />
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
    </div>
  );
}

function SortableTh({ label, active, dir, onClick }: { label: string; active: boolean; dir: "asc" | "desc"; onClick: () => void }) {
  return (
    <th
      onClick={onClick}
      className="cursor-pointer border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3) transition-colors select-none hover:text-(--ice-2)"
    >
      {label}
      {active && <span className="ms-1 text-[9px]">{dir === "asc" ? "▲" : "▼"}</span>}
    </th>
  );
}

function Fig({ label, value, tone }: { label: string; value: string; tone?: "mint" }) {
  return (
    <div className="relative overflow-hidden rounded-2xl border border-(--edge) bg-(--pane) px-3.5 pt-3 pb-2.5">
      <span className={`absolute start-0 top-0 h-0.5 w-7.5 ${tone === "mint" ? "bg-(--mint)" : "bg-(--ice-3)/40"}`} />
      <div className="text-[10px] tracking-wider text-(--ice-3)">{label}</div>
      <div className="mt-1 text-[15px] font-bold text-(--ice)">{value}</div>
    </div>
  );
}

const inputClass =
  "rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)";
