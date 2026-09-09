import { Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { ReactNode } from "react";
import { ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { useRiskDashboard } from "./riskApi";
import type { TrendPointDto } from "./riskTypes";

const TOOLTIP_STYLE = {
  background: "var(--pane)",
  border: "1px solid var(--edge)",
  borderRadius: 10,
  fontSize: 12,
  direction: "rtl" as const,
};

/** «داشبورد ریسک» (docs Phase 2A §6) — the agency-wide KPI tiles and six charts. A separate
 * management view: the pinned «امروز» tab stays the daily desk. Empty chart data renders an
 * explicit empty note, never a blank box (rule 16). */
export function RiskDashboardPage() {
  const dashboard = useRiskDashboard();

  if (dashboard.isPending) {
    return (
      <div>
        <h2 className="mb-4 text-xl font-extrabold tracking-tight text-(--ice)">داشبورد ریسک</h2>
        <div className="text-[12.5px] text-(--ice-3)">در حال محاسبهٔ شاخص‌ها…</div>
      </div>
    );
  }

  if (dashboard.isError) {
    return (
      <div>
        <h2 className="mb-4 text-xl font-extrabold tracking-tight text-(--ice)">داشبورد ریسک</h2>
        <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {dashboard.error instanceof ApiError ? dashboard.error.message : "خطا در بارگذاری داشبورد ریسک"}
          <button type="button" onClick={() => void dashboard.refetch()} className="ms-2 underline">
            تلاش مجدد
          </button>
        </div>
      </div>
    );
  }

  const d = dashboard.data!;

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">داشبورد ریسک</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        نمای مدیریتی اعتبار و ریسک نمایندگی — {fa(d.assessedCustomers)} مشتری از {fa(d.totalCustomers)} مشتری ارزیابی‌شده
      </div>

      <div className="mb-4.5 grid grid-cols-2 gap-3 lg:grid-cols-5">
        <Kpi label="کل مشتریان" value={fa(d.totalCustomers)} />
        <Kpi label="ریسک پایین" value={fa(d.veryLowCount + d.lowCount)} tone="mint" />
        <Kpi label="ریسک متوسط" value={fa(d.mediumCount)} tone="amber" />
        <Kpi label="ریسک بالا" value={fa(d.highCount)} tone="ember" />
        <Kpi label="بحرانی" value={fa(d.criticalCount)} tone="ember" />
        <Kpi label="بدهی جاری" value={money(d.currentDebtToman)} />
        <Kpi label="کل معوقات" value={money(d.totalOverdueToman)} tone={d.totalOverdueToman > 0 ? "ember" : undefined} />
        <Kpi label="نرخ پرداخت به‌موقع" value={d.onTimeRatePercent === null ? "—" : `${fa(d.onTimeRatePercent)}٪`} />
        <Kpi label="نرخ نکول" value={`${fa(d.defaultRatePercent)}٪`} tone={d.defaultRatePercent > 0 ? "ember" : undefined} />
        <Kpi
          label="نیازمند بررسی"
          value={fa(d.openReviewsCount)}
          tone={d.openReviewsCount > 0 ? "amber" : undefined}
          hint={d.unreadWarningsCount > 0 ? `${fa(d.unreadWarningsCount)} هشدار خوانده‌نشده` : undefined}
        />
      </div>

      <div className="grid grid-cols-1 gap-4.5 xl:grid-cols-2">
        <ChartCard title="توزیع مشتریان بر اساس سطح ریسک">
          <DistributionChart
            data={[
              { label: "خیلی پایین", count: d.veryLowCount, fill: "var(--mint)" },
              { label: "پایین", count: d.lowCount, fill: "var(--mint)" },
              { label: "متوسط", count: d.mediumCount, fill: "var(--amber)" },
              { label: "بالا", count: d.highCount, fill: "var(--ember)" },
              { label: "بحرانی", count: d.criticalCount, fill: "var(--ember)" },
            ]}
          />
        </ChartCard>

        <ChartCard title="روند امتیاز ریسک (میانگین روزانه)" empty={d.scoreTrend.length < 2}>
          <TrendChart points={d.scoreTrend} domain={[0, 1000]} format={(v) => fa(v)} />
        </ChartCard>

        <ChartCard title="روند معوقات (میانگین روزانه)" empty={d.overdueTrend.length < 2}>
          <TrendChart points={d.overdueTrend} format={(v) => money(Number(v))} />
        </ChartCard>

        <ChartCard title="روند پرداخت به‌موقع (میانگین روزانه)" empty={d.onTimeTrend.length < 2}>
          <TrendChart points={d.onTimeTrend} domain={[0, 100]} format={(v) => `${fa(v)}٪`} />
        </ChartCard>

        <ChartCard title="ورود مشتریان به ریسک بالا" empty={d.highRiskTrend.length < 2}>
          <TrendChart points={d.highRiskTrend} format={(v) => fa(v)} />
        </ChartCard>

        <ChartCard title="هشدارهای فعال بر اساس نوع" empty={d.warningsByType.length === 0}>
          <DistributionChart
            data={d.warningsByType.map((w) => ({
              label: w.typeFa,
              count: w.count,
              fill: "var(--amber)",
            }))}
          />
        </ChartCard>
      </div>
    </div>
  );
}

function Kpi({ label, value, tone, hint }: { label: string; value: string; tone?: "mint" | "amber" | "ember"; hint?: string }) {
  const toneClass = tone === "mint" ? "text-(--mint)" : tone === "amber" ? "text-(--amber)" : tone === "ember" ? "text-(--ember)" : "text-(--ice)";
  return (
    <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
      <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">{label}</div>
      <div className={`text-[19px] font-extrabold tabular-nums tracking-tight ${toneClass}`}>{value}</div>
      {hint && <div className="mt-0.5 text-[10.5px] text-(--ice-3)">{hint}</div>}
    </div>
  );
}

function ChartCard({ title, empty, children }: { title: string; empty?: boolean; children: ReactNode }) {
  return (
    <div className="rounded-2xl border border-(--edge) bg-(--pane) p-4">
      <div className="mb-3 text-[12.5px] font-bold text-(--ice)">{title}</div>
      {empty ? (
        <div className="grid h-56 place-items-center text-[12px] text-(--ice-3)">دادهٔ کافی برای رسم نمودار وجود ندارد.</div>
      ) : (
        <div className="h-56">{children}</div>
      )}
    </div>
  );
}

function TrendChart({
  points,
  domain,
  format,
}: {
  points: TrendPointDto[];
  domain?: [number, number];
  format: (v: number) => string;
}) {
  const data = points.map((p) => ({ label: toJalaliDisplay(p.date), value: p.value }));
  return (
    <ResponsiveContainer width="100%" height="100%">
      <AreaChart data={data} margin={{ top: 4, right: 8, left: 8, bottom: 4 }}>
        <defs>
          <linearGradient id="risk-trend-fill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--mint)" stopOpacity={0.28} />
            <stop offset="100%" stopColor="var(--mint)" stopOpacity={0.02} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--edge)" />
        <XAxis dataKey="label" tick={{ fontSize: 10, fill: "var(--ice-3)" }} minTickGap={28} />
        <YAxis
          tick={{ fontSize: 11, fill: "var(--ice-3)" }}
          tickFormatter={(v) => format(Number(v))}
          width={52}
          domain={domain ?? ["auto", "auto"]}
        />
        <Tooltip
          formatter={(value) => [format(Number(value)), "مقدار"]}
          contentStyle={TOOLTIP_STYLE}
          labelStyle={{ color: "var(--ice)" }}
        />
        <Area dataKey="value" stroke="var(--mint)" strokeWidth={2} fill="url(#risk-trend-fill)" dot={false} />
      </AreaChart>
    </ResponsiveContainer>
  );
}

function DistributionChart({ data }: { data: { label: string; count: number; fill: string }[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart data={data} margin={{ top: 4, right: 8, left: 8, bottom: 4 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--edge)" />
        <XAxis dataKey="label" tick={{ fontSize: 11, fill: "var(--ice-3)" }} interval={0} />
        <YAxis tick={{ fontSize: 11, fill: "var(--ice-3)" }} tickFormatter={(v) => fa(v)} width={40} allowDecimals={false} />
        <Tooltip
          formatter={(value) => [fa(Number(value)), "تعداد"]}
          contentStyle={TOOLTIP_STYLE}
          labelStyle={{ color: "var(--ice)" }}
        />
        <Bar dataKey="count" radius={[3, 3, 0, 0]}>
          {data.map((entry) => (
            <Cell key={entry.label} fill={entry.fill} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}
