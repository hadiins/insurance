import { useCallback, useEffect, useRef, useState } from "react";
import { Area, AreaChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { MetricCard } from "../../components/MetricCard";
import { Table, Td, Th, Tr } from "../../components/Table";
import { SeverityBadge } from "./SeverityBadge";
import { TimeRangeSelector } from "./TimeRangeSelector";
import { HealthBanner } from "./HealthBanner";
import type {
  AlertOccurrenceDto,
  EndpointStatDto,
  HangfireStatsDto,
  MonitoringOverviewDto,
  RecurringJobStatusDto,
  ServerStatusDto,
  TimeRangeKey,
  TimeseriesPointDto,
} from "./monitoringTypes";

const POLL_MS = 30_000;

function chartTimestamp(iso: string): string {
  return toJalaliDateTimeDisplay(iso).split("،")[0] ?? "";
}

export function MonitoringDashboardPage() {
  const [range, setRange] = useState<TimeRangeKey>("24h");
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [overview, setOverview] = useState<MonitoringOverviewDto | null>(null);
  const [overviewError, setOverviewError] = useState<string | null>(null);
  const [series, setSeries] = useState<TimeseriesPointDto[] | null>(null);
  const [seriesError, setSeriesError] = useState<string | null>(null);
  const [endpoints, setEndpoints] = useState<EndpointStatDto[] | null>(null);
  const [server, setServer] = useState<ServerStatusDto | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);
  const [jobs, setJobs] = useState<HangfireStatsDto | null>(null);
  const [jobsError, setJobsError] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(() => {
    api
      .get<MonitoringOverviewDto>(`/monitoring/overview?range=${range}`)
      .then((data) => {
        setOverview(data);
        setOverviewError(null);
      })
      .catch((err) => setOverviewError(err instanceof ApiError ? err.message : "خطا در بارگذاری وضعیت کلی"))
      .finally(() => setLoaded(true));
    api
      .get<TimeseriesPointDto[]>(`/monitoring/timeseries?range=${range}`)
      .then((data) => {
        setSeries(data.map((p) => ({ ...p, label: chartTimestamp(p.bucketUtc) })));
        setSeriesError(null);
      })
      .catch(() => setSeriesError("خطا در بارگذاری نمودارها"));
    api
      .get<EndpointStatDto[]>(`/monitoring/endpoints?range=${range}`)
      .then(setEndpoints)
      .catch(() => setEndpoints(null));
    api
      .get<ServerStatusDto>("/monitoring/server")
      .then((data) => {
        setServer(data);
        setServerError(null);
      })
      .catch((err) => setServerError(err instanceof ApiError ? err.message : "خطا در بارگذاری منابع سرور"));
    api
      .get<HangfireStatsDto>("/monitoring/jobs")
      .then((data) => {
        setJobs(data);
        setJobsError(null);
      })
      .catch((err) => setJobsError(err instanceof ApiError ? err.message : "خطا در بارگذاری کارهای پس‌زمینه"));
  }, [range]);

  useEffect(load, [load]);

  useEffect(() => {
    if (!autoRefresh) {
      return;
    }
    timerRef.current = setInterval(load, POLL_MS);
    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
    };
  }, [autoRefresh, load]);

  if (!loaded) {
    return <div className="grid h-full place-items-center text-(--ice-3)">در حال بارگذاری…</div>;
  }

  if (overviewError && !overview) {
    return (
      <div className="mx-auto max-w-3xl py-10">
        <EmptyState
          icon="⚠️"
          title="بارگذاری داشبورد پایش ناموفق بود"
          description={overviewError}
          action={{ label: "تلاش دوباره", onClick: load }}
        />
      </div>
    );
  }

  const errorRateSeries = (series ?? []).map((p) => ({
    ...p,
    errorRate: p.requests > 0 ? (p.errors * 100) / p.requests : 0,
  }));

  return (
    <div className="mx-auto max-w-[1400px] space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-[17px] font-bold text-(--ice)">پایش سامانه</h1>
          <p className="mt-0.5 text-[12px] text-(--ice-3)">
            داده‌های واقعی از خود فرایند — با انتخاب بازه، و به‌روزرسانی خودکار هر ۳۰ ثانیه
          </p>
        </div>
        <TimeRangeSelector
          range={range}
          onRangeChange={setRange}
          autoRefresh={autoRefresh}
          onAutoRefreshChange={setAutoRefresh}
        />
      </div>

      {overview && <HealthBanner overview={overview} />}

      {overview && (
        <div className="grid grid-cols-2 gap-3.5 lg:grid-cols-4">
          <MetricCard icon="📥" label="درخواست‌ها در بازه" value={overview.totalRequests} />
          <MetricCard
            icon="🔥"
            label="نرخ خطای سرور (۵xx)"
            value={overview.errorRatePercent}
            tone={overview.errorRatePercent > 5 ? "ember" : "mint"}
          />
          <MetricCard
            icon="⚡"
            label="بیشترین تأخیر P95 (میلی‌ثانیه)"
            value={overview.latencyP95Ms}
            tone={overview.latencyP95Ms > 3000 ? "amber" : "neutral"}
          />
          <MetricCard
            icon="🧠"
            label="حافظهٔ فرایند (مگابایت)"
            value={Math.round(server?.processMemoryMb ?? 0)}
            tone="neutral"
          />
        </div>
      )}

      {seriesError && !series ? (
        <EmptyState
          icon="⚠️"
          title="بارگذاری نمودارها ناموفق بود"
          description={seriesError}
          action={{ label: "تلاش دوباره", onClick: load }}
        />
      ) : series === null ? (
        <EmptyState
          icon="📈"
          title="در حال بارگذاری نمودارها…"
          description="نمونه‌بردار هر دقیقه یک ردیف متریک ثبت می‌کند؛ نمودارها پس از اولین نمونه پُر می‌شوند."
        />
      ) : series.length === 0 ? (
        <EmptyState
          icon="📈"
          title="در این بازه داده‌ای ثبت نشده است"
          description="بازهٔ بزرگ‌تری را انتخاب کنید یا مطمئن شوید کار پس‌زمینهٔ نمونه‌بردار فعال است."
        />
      ) : (
        <div className="grid gap-4 xl:grid-cols-2">
          <ChartCard title="حجم درخواست‌ها (بر دقیقه/بازه)">
            <AreaChart data={series} margin={{ top: 8, right: 8, left: 8, bottom: 4 }}>
              <defs>
                <linearGradient id="reqFill" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="var(--mint)" stopOpacity={0.35} />
                  <stop offset="100%" stopColor="var(--mint)" stopOpacity={0.02} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--edge)" />
              <XAxis dataKey="label" tick={{ fontSize: 10, fill: "var(--ice-3)" }} minTickGap={40} />
              <YAxis tick={{ fontSize: 10, fill: "var(--ice-3)" }} tickFormatter={(v) => fa(v)} width={44} />
              <Tooltip
                formatter={(value) => [fa(Number(value)), "درخواست"]}
                labelStyle={{ color: "var(--ice)" }}
                contentStyle={{ background: "var(--pane)", border: "1px solid var(--edge)", borderRadius: 10, fontSize: 12, direction: "rtl" }}
              />
              <Area dataKey="requests" stroke="var(--mint)" strokeWidth={2} fill="url(#reqFill)" />
            </AreaChart>
          </ChartCard>

          <ChartCard title="تأخیر پاسخ — P95 و P99 (میلی‌ثانیه)">
            <LineChart data={series} margin={{ top: 8, right: 8, left: 8, bottom: 4 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--edge)" />
              <XAxis dataKey="label" tick={{ fontSize: 10, fill: "var(--ice-3)" }} minTickGap={40} />
              <YAxis tick={{ fontSize: 10, fill: "var(--ice-3)" }} tickFormatter={(v) => fa(v)} width={44} />
              <Tooltip
                formatter={(value, key) => [fa(Number(value)), key === "latencyP95Ms" ? "P95" : "P99"]}
                labelStyle={{ color: "var(--ice)" }}
                contentStyle={{ background: "var(--pane)", border: "1px solid var(--edge)", borderRadius: 10, fontSize: 12, direction: "rtl" }}
              />
              <Line dataKey="latencyP95Ms" stroke="var(--amber)" strokeWidth={2} dot={false} />
              <Line dataKey="latencyP99Ms" stroke="var(--ember)" strokeWidth={2} dot={false} />
            </LineChart>
          </ChartCard>

          <ChartCard title="نرخ خطای سرور (٪)">
            <AreaChart data={errorRateSeries} margin={{ top: 8, right: 8, left: 8, bottom: 4 }}>
              <defs>
                <linearGradient id="errFill" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="var(--ember)" stopOpacity={0.35} />
                  <stop offset="100%" stopColor="var(--ember)" stopOpacity={0.02} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--edge)" />
              <XAxis dataKey="label" tick={{ fontSize: 10, fill: "var(--ice-3)" }} minTickGap={40} />
              <YAxis tick={{ fontSize: 10, fill: "var(--ice-3)" }} tickFormatter={(v) => fa(v)} width={44} />
              <Tooltip
                formatter={(value) => [`${fa(Number(value))}٪`, "خطا"]}
                labelStyle={{ color: "var(--ice)" }}
                contentStyle={{ background: "var(--pane)", border: "1px solid var(--edge)", borderRadius: 10, fontSize: 12, direction: "rtl" }}
              />
              <Area dataKey="errorRate" stroke="var(--ember)" strokeWidth={2} fill="url(#errFill)" />
            </AreaChart>
          </ChartCard>

          <ResourcePanel server={server} error={serverError} onRetry={load} />
        </div>
      )}

      <JobsPanel jobs={jobs} error={jobsError} onRetry={load} />

      <section className="space-y-3">
        <h2 className="text-[14px] font-bold text-(--ice)">پرترددترین مسیرهای API</h2>
        {endpoints === null ? (
          <EmptyState icon="🧭" title="داده‌ای برای مسیرها ثبت نشده است" description="جدول پس از اولین دقیقهٔ ترافیک پُر می‌شود." />
        ) : endpoints.length === 0 ? (
          <EmptyState icon="🧭" title="در این بازه درخواستی ثبت نشده است" />
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>مسیر</Th>
                <Th>درخواست</Th>
                <Th>خطا</Th>
                <Th>میانگین (میلی‌ثانیه)</Th>
                <Th>کند (&gt;۱ ثانیه)</Th>
              </tr>
            </thead>
            <tbody>
              {endpoints.map((e) => (
                <Tr key={e.path}>
                  <Td ltr className="text-(--ice-2)">{e.path}</Td>
                  <Td className="tabular-nums">{fa(e.count)}</Td>
                  <Td className={`tabular-nums ${e.errors > 0 ? "font-bold text-(--ember)" : "text-(--ice-3)"}`}>
                    {fa(e.errors)}
                  </Td>
                  <Td className="tabular-nums">{fa(Math.round(e.avgMs))}</Td>
                  <Td className={`tabular-nums ${e.slowCount > 0 ? "text-(--amber)" : "text-(--ice-3)"}`}>
                    {fa(e.slowCount)}
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        )}
      </section>

      {overview && overview.latestAlerts.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-[14px] font-bold text-(--ice)">آخرین هشدارها</h2>
          <AlertList alerts={overview.latestAlerts} onChanged={load} />
        </section>
      )}
    </div>
  );
}

function ChartCard({ title, children }: { title: string; children: React.ReactElement }) {
  return (
    <div className="rounded-2xl border border-(--edge) bg-(--pane) p-4">
      <div className="mb-2 text-[12.5px] font-semibold text-(--ice-2)">{title}</div>
      <div className="h-56">
        <ResponsiveContainer width="100%" height="100%">
          {children}
        </ResponsiveContainer>
      </div>
    </div>
  );
}

function ResourcePanel({ server, error, onRetry }: { server: ServerStatusDto | null; error: string | null; onRetry: () => void }) {
  return (
    <div className="rounded-2xl border border-(--edge) bg-(--pane) p-4">
      <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">منابع فرایند (لحظه‌ای)</div>
      {error && !server ? (
        <EmptyState icon="⚠️" title="بارگذاری منابع ناموفق بود" description={error} action={{ label: "تلاش دوباره", onClick: onRetry }} />
      ) : !server ? (
        <div className="grid h-56 place-items-center text-(--ice-3)">در حال بارگذاری…</div>
      ) : (
        <div className="space-y-4">
          <ResourceBar
            label="پردازش (CPU فرایند)"
            display={server.cpuPercent === null ? "—" : `${fa(server.cpuPercent)}٪`}
            percent={server.cpuPercent ?? 0}
            tone={(server.cpuPercent ?? 0) > 80 ? "ember" : "mint"}
          />
          <ResourceBar
            label="حافظهٔ فرایند"
            display={`${fa(Math.round(server.processMemoryMb))} مگابایت`}
            percent={Math.min((server.processMemoryMb / 2048) * 100, 100)}
            tone={server.processMemoryMb > 1536 ? "amber" : "mint"}
          />
          <ResourceBar
            label="فضای آزاد دیسک"
            display={`${fa(Math.round(server.diskFreeGb))} گیگابایت`}
            percent={Math.min((server.diskFreeGb / 50) * 100, 100)}
            tone={server.diskFreeGb < 5 ? "ember" : "mint"}
          />
          <div className="grid grid-cols-2 gap-3 text-[12px] text-(--ice-3)">
            <div>
              زمان پاسخ دیتابیس:{" "}
              <b className="text-(--ice) tabular-nums">
                {server.dbProbeMs === null ? "پاسخ نداد" : `${fa(server.dbProbeMs)} میلی‌ثانیه`}
              </b>
            </div>
            <div>
              نخ‌های فعال: <b className="text-(--ice) tabular-nums">{fa(server.threadCount)}</b>
            </div>
            <div>
              این دقیقه: <b className="text-(--ice) tabular-nums">{fa(server.liveMinuteRequests)} درخواست</b>
              {server.liveMinuteErrors > 0 && (
                <span className="text-(--ember)"> ({fa(server.liveMinuteErrors)} خطا)</span>
              )}
            </div>
            <div>
              بالا بودن فرایند: <b className="text-(--ice) tabular-nums">{fa(Math.round(server.processUpMinutes))} دقیقه</b>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function ResourceBar({ label, display, percent, tone }: { label: string; display: string; percent: number; tone: "mint" | "amber" | "ember" }) {
  const width = Math.max(Math.min(percent, 100), 2);
  return (
    <div>
      <div className="mb-1 flex items-center justify-between text-[11.5px]">
        <span className="text-(--ice-3)">{label}</span>
        <span className="font-semibold text-(--ice) tabular-nums">{display}</span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-(--fld)">
        <div
          className={`h-full rounded-full ${tone === "ember" ? "bg-(--ember)" : tone === "amber" ? "bg-(--amber)" : "bg-(--mint)"}`}
          style={{ width: `${width}%` }}
        />
      </div>
    </div>
  );
}

function JobsPanel({ jobs, error, onRetry }: { jobs: HangfireStatsDto | null; error: string | null; onRetry: () => void }) {
  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-[14px] font-bold text-(--ice)">کارهای پس‌زمینه (Hangfire)</h2>
        {jobs && (
          <div className="flex gap-2 text-[11px]">
            <span className="rounded-full bg-(--ember)/13 px-2.5 py-0.5 font-bold text-(--ember)">
              ناموفق: {fa(jobs.failedCount)}
            </span>
            <span className="rounded-full bg-(--fld) px-2.5 py-0.5 text-(--ice-2)">
              در اجرا: {fa(jobs.processingCount)}
            </span>
            <span className="rounded-full bg-(--fld) px-2.5 py-0.5 text-(--ice-2)">
              زمان‌بندی‌شده: {fa(jobs.scheduledCount)}
            </span>
          </div>
        )}
      </div>
      {error && !jobs ? (
        <EmptyState icon="⚠️" title="بارگذاری کارها ناموفق بود" description={error} action={{ label: "تلاش دوباره", onClick: onRetry }} />
      ) : !jobs ? (
        <div className="grid h-24 place-items-center text-(--ice-3)">در حال بارگذاری…</div>
      ) : jobs.jobs.length === 0 ? (
        <EmptyState icon="⏱️" title="کاری ثبت نشده است" />
      ) : (
        <Table>
          <thead>
            <tr>
              <Th>کار</Th>
              <Th>زمان‌بندی (Cron)</Th>
              <Th>آخرین اجرا</Th>
              <Th>اجرای بعدی</Th>
            </tr>
          </thead>
          <tbody>
            {jobs.jobs.map((job: RecurringJobStatusDto) => (
              <Tr key={job.id}>
                <Td ltr className="text-(--ice-2)">{job.id}</Td>
                <Td ltr className="text-(--ice-3)">{job.cron}</Td>
                <Td className="text-(--ice-3)">{job.lastExecutionUtc ? toJalaliDateTimeDisplay(job.lastExecutionUtc) : "—"}</Td>
                <Td className="text-(--ice-3)">{job.nextExecutionUtc ? toJalaliDateTimeDisplay(job.nextExecutionUtc) : "—"}</Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
    </section>
  );
}

export function AlertList({ alerts, onChanged }: { alerts: AlertOccurrenceDto[]; onChanged: () => void }) {
  // B17 — ack/resolve are user-initiated clicks, not polls: swallowing the error made the button
  // look dead. Rule 15 — the failure must be visible with a retry path (the button itself).
  const [actionError, setActionError] = useState<string | null>(null);

  async function ack(id: string) {
    setActionError(null);
    try {
      await api.put(`/monitoring/alerts/${id}/ack`);
      onChanged();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "ثبت «دیده شد» ناموفق بود؛ دوباره تلاش کنید.");
    }
  }

  async function resolve(id: string) {
    setActionError(null);
    try {
      await api.put(`/monitoring/alerts/${id}/resolve`);
      onChanged();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "رسیدگی به هشدار ناموفق بود؛ دوباره تلاش کنید.");
    }
  }

  return (
    <Table>
      {actionError && (
        <caption className="mb-2 text-right text-[12px] text-(--ember)">{actionError}</caption>
      )}
      <thead>
        <tr>
          <Th>هشدار</Th>
          <Th>شدت</Th>
          <Th>وضعیت</Th>
          <Th>آخرین مشاهده</Th>
          <Th>اقدام</Th>
        </tr>
      </thead>
      <tbody>
        {alerts.map((a) => (
          <Tr key={a.id}>
            <Td className="max-w-[420px]">{a.message}</Td>
            <Td><SeverityBadge severity={a.severity} /></Td>
            <Td><SeverityBadge severity={a.status} /></Td>
            <Td className="whitespace-nowrap text-(--ice-3)">{toJalaliDateTimeDisplay(a.lastSeenAt)}</Td>
            <Td>
              {a.status === "Active" && (
                <button
                  type="button"
                  onClick={() => ack(a.id)}
                  className="ml-2 rounded-md border border-(--edge) px-2 py-1 text-[11px] text-(--ice-2) transition-colors hover:bg-(--hov)"
                >
                  دیده شد
                </button>
              )}
              {a.status !== "Resolved" && (
                <button
                  type="button"
                  onClick={() => resolve(a.id)}
                  className="rounded-md border border-(--mint) px-2 py-1 text-[11px] font-semibold text-(--mint) transition-colors hover:bg-(--mint)/10"
                >
                  برطرف شد
                </button>
              )}
            </Td>
          </Tr>
        ))}
      </tbody>
    </Table>
  );
}
