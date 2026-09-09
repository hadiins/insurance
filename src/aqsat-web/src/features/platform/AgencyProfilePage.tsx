import { useEffect, useState } from "react";
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";

interface AgencyMonthlyTrendDto {
  year: number;
  month: number;
  policies: number;
  sms: number;
  inquiryPayments: number;
  inquiryCalls: number;
  inquiryRevenueToman: number;
}

interface AgencyProfileDto {
  id: string;
  code: string;
  name: string;
  province: string | null;
  city: string | null;
  insurerName: string | null;
  isActive: boolean;
  agencyCode: string | null;
  userCount: number;
  policiesIssuedTotal: number;
  policiesThisMonth: number;
  smsSentTotal: number;
  smsCostToman: number;
  inquiryPaymentsTotal: number;
  inquiryRevenueToman: number;
  inquiryCallsTotal: number;
  inquiryCallCostToman: number;
  monthlyTrend: AgencyMonthlyTrendDto[];
}

interface AgencyProfilePayload {
  agencyId: string;
}

const JALALI_MONTHS = [
  "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
  "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
];

export function AgencyProfilePage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const setDirty = useTabsStore((s) => s.setDirty);
  const payload = tab?.payload as AgencyProfilePayload | undefined;

  const [profile, setProfile] = useState<AgencyProfileDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);

  const [name, setName] = useState("");
  const [province, setProvince] = useState("");
  const [city, setCity] = useState("");
  const [insurerName, setInsurerName] = useState("");
  const [isActive, setIsActive] = useState(false);
  const [provinces, setProvinces] = useState<string[]>([]);
  const [formChanged, setFormChanged] = useState(false);

  useEffect(() => {
    if (!payload?.agencyId) return;
    setProfile(null);
    setError(null);
    api
      .get<AgencyProfileDto>(`/platform/agencies/${payload.agencyId}`)
      .then((p) => {
        setProfile(p);
        setName(p.name);
        setProvince(p.province ?? "");
        setCity(p.city ?? "");
        setInsurerName(p.insurerName ?? "");
        setIsActive(p.isActive);
        setFormChanged(false);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری پروندهٔ نمایندگی"));
    api
      .get<{ provinces: string[] }>("/platform/agencies/filters")
      .then((f) => setProvinces(f.provinces))
      .catch(() => setProvinces([]));
  }, [payload?.agencyId]);

  useEffect(() => {
    setDirty(tabKey, formChanged);
  }, [formChanged, tabKey, setDirty]);

  async function save() {
    if (!profile) return;
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      await api.put(`/platform/agencies/${profile.id}`, {
        name: name.trim(),
        province: province || null,
        city: city.trim() || null,
        insurerName: insurerName.trim() || null,
        isActive,
      });
      const refreshed = await api.get<AgencyProfileDto>(`/platform/agencies/${profile.id}`);
      setProfile(refreshed);
      setFormChanged(false);
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیرهٔ تغییرات ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  if (!payload?.agencyId) {
    return <div className="text-[12.5px] text-(--ice-3)">پرونده‌ای انتخاب نشده است.</div>;
  }

  if (profile === null) {
    return (
      <div>
        <h2 className="mb-4 text-xl font-extrabold tracking-tight text-(--ice)">پروندهٔ نمایندگی</h2>
        {error ? (
          <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{error}</div>
        ) : (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        )}
      </div>
    );
  }

  const chartData = profile.monthlyTrend.map((m) => ({
    label: JALALI_MONTHS[m.month - 1] ?? fa(m.month),
    year: fa(m.year),
    policies: m.policies,
    sms: m.sms,
    inquiryPayments: m.inquiryPayments,
  }));

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">پروندهٔ {profile.name}</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">کد {fa(profile.code)} — {profile.province ?? "بدون استان"}{profile.city ? ` / ${profile.city}` : ""}</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{error}</div>
      )}
      {saved && (
        <div className="mb-4.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">تغییرات ذخیره شد.</div>
      )}

      <div className="mb-4.5 grid grid-cols-2 gap-3 lg:grid-cols-4 xl:grid-cols-7">
        <Fig label="بیمه‌نامهٔ صادرشده" value={fa(profile.policiesIssuedTotal)} />
        <Fig label="بیمه‌نامهٔ این ماه" value={fa(profile.policiesThisMonth)} />
        <Fig label="پیامک ارسال‌شده" value={fa(profile.smsSentTotal)} />
        <Fig label="هزینهٔ پیامک (تومان)" value={money(profile.smsCostToman)} />
        <Fig label="پرداخت کارمزد استعلام" value={fa(profile.inquiryPaymentsTotal)} />
        <Fig label="اجرای استعلام" value={fa(profile.inquiryCallsTotal)} />
        <Fig label="درآمد استعلام (تومان)" value={money(profile.inquiryRevenueToman)} tone="mint" />
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">روند ۱۲ ماه اخیر</div>
        {chartData.length === 0 ? (
          <div className="py-6 text-center text-[12.5px] text-(--ice-3)">داده‌ای برای نمایش روند وجود ندارد.</div>
        ) : (
          <div className="h-72" dir="ltr">
            <ResponsiveContainer width="100%" height="100%">
              <ComposedChart data={chartData} margin={{ top: 4, right: 12, left: 12, bottom: 4 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--edge)" />
                <XAxis dataKey="label" tick={{ fontSize: 11, fill: "var(--ice-3)" }} interval={0} angle={-28} textAnchor="end" height={52} />
                <YAxis tick={{ fontSize: 11, fill: "var(--ice-3)" }} tickFormatter={(v) => fa(v)} width={44} />
                <Tooltip
                  formatter={(value, key) => {
                    const labels: Record<string, string> = {
                      policies: "بیمه‌نامه",
                      sms: "پیامک",
                      inquiryPayments: "پرداخت استعلام",
                    };
                    return [fa(Number(value)), labels[String(key)] ?? String(key)];
                  }}
                  labelFormatter={(label, payload) =>
                    payload?.[0] ? `${label} ${payload[0].payload.year}` : String(label)
                  }
                  contentStyle={{ background: "var(--pane)", border: "1px solid var(--edge)", borderRadius: 10, fontSize: 12, direction: "rtl" }}
                  labelStyle={{ color: "var(--ice)" }}
                />
                <Legend formatter={(value) => (value === "policies" ? "بیمه‌نامه" : value === "sms" ? "پیامک" : "پرداخت استعلام")} wrapperStyle={{ fontSize: 12 }} />
                <Bar dataKey="policies" fill="var(--mint)" radius={[3, 3, 0, 0]} />
                <Bar dataKey="sms" fill="var(--ice-3)" radius={[3, 3, 0, 0]} />
                <Line dataKey="inquiryPayments" stroke="var(--ember)" strokeWidth={2} dot={false} />
              </ComposedChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">مشخصات نمایندگی</div>
        <div className="mb-3 grid grid-cols-2 gap-3 lg:grid-cols-4">
          <Field label="نام نمایندگی">
            <input
              value={name}
              onChange={(e) => { setName(e.target.value); setFormChanged(true); }}
              className={inputClass}
            />
          </Field>
          <Field label="استان">
            <select
              value={province}
              onChange={(e) => { setProvince(e.target.value); setFormChanged(true); }}
              className={inputClass}
            >
              <option value="">انتخاب نشده</option>
              {provinces.map((p) => (
                <option key={p} value={p}>{p}</option>
              ))}
            </select>
          </Field>
          <Field label="شهر">
            <input
              value={city}
              onChange={(e) => { setCity(e.target.value); setFormChanged(true); }}
              className={inputClass}
            />
          </Field>
          <Field label="شرکت بیمهٔ طرف قرارداد">
            <input
              value={insurerName}
              onChange={(e) => { setInsurerName(e.target.value); setFormChanged(true); }}
              className={inputClass}
            />
          </Field>
        </div>
        <label className="mb-4 flex cursor-pointer items-center gap-2.5">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => { setIsActive(e.target.checked); setFormChanged(true); }}
            className="h-4 w-4 accent-(--mint)"
          />
          <span className="text-[13.5px] text-(--ice)">نمایندگی فعال است</span>
        </label>
        <div className="mb-4 grid grid-cols-2 gap-3 text-[12.5px] text-(--ice-3) lg:grid-cols-3">
          <div>کد نمایندگی: <span className="text-(--ice-2)">{fa(profile.code)}</span></div>
          <div>کد شمارهٔ بیمه‌نامه: <span className="text-(--ice-2)">{profile.agencyCode ? fa(profile.agencyCode) : "—"}</span></div>
          <div>تعداد کاربران: <span className="text-(--ice-2)">{fa(profile.userCount)}</span></div>
        </div>
        <button
          type="button"
          onClick={save}
          disabled={busy || !formChanged}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-5 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال ذخیره…" : "ذخیرهٔ تغییرات"}
        </button>
      </div>
    </div>
  );
}

function Fig({ label, value, tone }: { label: string; value: string; tone?: "mint" }) {
  return (
    <div className="relative overflow-hidden rounded-2xl border border-(--edge) bg-(--pane) px-3.5 pt-3 pb-2.5">
      <span className={`absolute start-0 top-0 h-0.5 w-7.5 ${tone === "mint" ? "bg-(--mint)" : "bg-(--ice-3)/40"}`} />
      <div className="text-[10.5px] tracking-wider text-(--ice-3)">{label}</div>
      <div className="mt-1 text-[15px] font-bold text-(--ice)">{value}</div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">{label}</label>
      {children}
    </div>
  );
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)";
