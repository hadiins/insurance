import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { MetricCard } from "../../components/MetricCard";
import { Table, Td, Th, Tr } from "../../components/Table";
import { SeverityBadge } from "./SeverityBadge";
import { TimeRangeSelector } from "./TimeRangeSelector";
import {
  SECURITY_TYPE_LABELS,
  type MonitoringOverviewDto,
  type SecurityEventPageDto,
  type SecurityScoreDto,
  type ServerStatusDto,
  type TimeRangeKey,
} from "./monitoringTypes";

const POLL_MS = 30_000;
const FEED_PAGE_SIZE = 50;

export function SecurityDashboardPage() {
  const [range, setRange] = useState<TimeRangeKey>("24h");
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [score, setScore] = useState<SecurityScoreDto | null>(null);
  const [scoreError, setScoreError] = useState<string | null>(null);
  const [overview, setOverview] = useState<MonitoringOverviewDto | null>(null);
  const [events, setEvents] = useState<SecurityEventPageDto | null>(null);
  const [eventsError, setEventsError] = useState<string | null>(null);
  const [server, setServer] = useState<ServerStatusDto | null>(null);
  const [page, setPage] = useState(1);
  const [loaded, setLoaded] = useState(false);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(() => {
    api
      .get<SecurityScoreDto>("/monitoring/security/score")
      .then((data) => {
        setScore(data);
        setScoreError(null);
      })
      .catch((err) => setScoreError(err instanceof ApiError ? err.message : "خطا در محاسبهٔ امتیاز امنیتی"))
      .finally(() => setLoaded(true));
    api
      .get<MonitoringOverviewDto>(`/monitoring/overview?range=${range}`)
      .then(setOverview)
      .catch(() => setOverview(null));
    api
      .get<SecurityEventPageDto>(`/monitoring/security/events?range=${range}&page=${page}`)
      .then((data) => {
        setEvents(data);
        setEventsError(null);
      })
      .catch((err) => setEventsError(err instanceof ApiError ? err.message : "خطا در بارگذاری رویدادها"));
    api
      .get<ServerStatusDto>("/monitoring/server")
      .then(setServer)
      .catch(() => setServer(null));
  }, [range, page]);

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

  const summary = overview?.security;
  const failedLoginIps = useMemo(() => {
    const byIp = new Map<string, number>();
    for (const e of events?.items ?? []) {
      if (e.type === "FailedLogin") byIp.set(e.ipAddress ?? "نامشخص", (byIp.get(e.ipAddress ?? "نامشخص") ?? 0) + 1);
    }
    return [...byIp.entries()].sort((a, b) => b[1] - a[1]).slice(0, 5);
  }, [events]);

  if (!loaded) {
    return <div className="grid h-full place-items-center text-(--ice-3)">در حال بارگذاری…</div>;
  }

  if (scoreError && !score) {
    return (
      <div className="mx-auto max-w-3xl py-10">
        <EmptyState
          icon="⚠️"
          title="بارگذاری داشبورد امنیت ناموفق بود"
          description={scoreError}
          action={{ label: "تلاش دوباره", onClick: load }}
        />
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-[1300px] space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-[17px] font-bold text-(--ice)">امنیت سامانه</h1>
          <p className="mt-0.5 text-[12px] text-(--ice-3)">
            ورودهای ناموفق، فعالیت‌های مشکوک و دسترسی‌های غیرمجاز — بر پایهٔ رویدادهای واقعی ثبت‌شده
          </p>
        </div>
        <TimeRangeSelector
          range={range}
          onRangeChange={(r) => {
            setRange(r);
            setPage(1);
          }}
          autoRefresh={autoRefresh}
          onAutoRefreshChange={setAutoRefresh}
        />
      </div>

      {score && <ScoreCard score={score} />}

      <div className="grid grid-cols-2 gap-3.5 lg:grid-cols-4">
        <MetricCard
          icon="🚪"
          label={`ورود ناموفق در بازه`}
          value={summary?.failedLogins ?? 0}
          tone={(summary?.failedLogins ?? 0) > 10 ? "ember" : "neutral"}
        />
        <MetricCard icon="🕵️" label={`فعالیت مشکوک در بازه`} value={summary?.suspiciousActivities ?? 0} tone="amber" />
        <MetricCard icon="⛔" label={`دسترسی غیرمجاز در بازه`} value={summary?.permissionDenied ?? 0} tone="neutral" />
        <MetricCard icon="🛡️" label={`رد سقف نرخ در بازه`} value={summary?.rateLimitRejections ?? 0} tone="neutral" />
      </div>

      <div className="grid gap-4 xl:grid-cols-[2fr_1fr]">
        <section className="space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-[14px] font-bold text-(--ice)">آخرین رویدادهای امنیتی</h2>
            {events && (
              <span className="text-[11.5px] text-(--ice-3)">
                مجموع: {fa(events.totalCount)} — صفحهٔ {fa(events.page)}
              </span>
            )}
          </div>
          {eventsError && !events ? (
            <EmptyState
              icon="⚠️"
              title="بارگذاری رویدادها ناموفق بود"
              description={eventsError}
              action={{ label: "تلاش دوباره", onClick: load }}
            />
          ) : !events ? (
            <div className="grid h-40 place-items-center text-(--ice-3)">در حال بارگذاری…</div>
          ) : events.items.length === 0 ? (
            <EmptyState
              icon="🕊️"
              title="در این بازه رویداد امنیتی ثبت نشده است"
              description="ورودهای ناموفق، تلاش‌های دسترسی غیرمجاز و فعالیت‌های مشکوک اینجا فهرست می‌شوند."
            />
          ) : (
            <>
              <Table>
                <thead>
                  <tr>
                    <Th>زمان</Th>
                    <Th>نوع</Th>
                    <Th>شدت</Th>
                    <Th>IP</Th>
                    <Th>شرح</Th>
                  </tr>
                </thead>
                <tbody>
                  {events.items.map((e) => (
                    <Tr key={e.id}>
                      <Td className="whitespace-nowrap text-(--ice-3)">{toJalaliDateTimeDisplay(e.occurredAt)}</Td>
                      <Td className="whitespace-nowrap text-(--ice-2)">{SECURITY_TYPE_LABELS[e.type] ?? e.type}</Td>
                      <Td><SeverityBadge severity={e.severity} /></Td>
                      <Td ltr className="text-(--ice-3)">{e.ipAddress ?? "—"}</Td>
                      <Td className="max-w-[380px]">{e.detail}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
              <div className="flex items-center justify-center gap-3">
                <button
                  type="button"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  className="rounded-md border border-(--edge) bg-(--pane) px-3 py-1 text-[12px] text-(--ice-2) transition-colors hover:bg-(--hov) disabled:opacity-40"
                >
                  صفحهٔ قبل
                </button>
                <span className="text-[11.5px] text-(--ice-3) tabular-nums">{fa(events.page)}</span>
                <button
                  type="button"
                  disabled={page * FEED_PAGE_SIZE >= events.totalCount}
                  onClick={() => setPage((p) => p + 1)}
                  className="rounded-md border border-(--edge) bg-(--pane) px-3 py-1 text-[12px] text-(--ice-2) transition-colors hover:bg-(--hov) disabled:opacity-40"
                >
                  صفحهٔ بعد
                </button>
              </div>
            </>
          )}
        </section>

        <div className="space-y-4">
          <section className="rounded-2xl border border-(--edge) bg-(--pane) p-4">
            <h3 className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">وضعیت HTTPS</h3>
            {server ? (
              server.https.enabled ? (
                <div className="flex items-center gap-2.5">
                  <span className="text-[20px]">🔒</span>
                  <div className="text-[12px] text-(--ice-2)">
                    فعال
                    <div className="text-[11px] text-(--ice-3) ltr" dir="ltr">{server.https.url}</div>
                  </div>
                </div>
              ) : (
                <div className="flex items-start gap-2.5">
                  <span className="text-[20px]">🔓</span>
                  <div className="text-[12px] leading-relaxed text-(--ice-2)">
                    <b className="text-(--ember)">بدون HTTPS</b>
                    <div className="mt-1 text-[11px] text-(--ice-3)">
                      نشانی عمومی سامانه روی HTTP پاسخ می‌دهد؛ توصیه می‌شود گواهی SSL فعال شود.
                    </div>
                    <div className="mt-1 text-[11px] text-(--ice-3) ltr" dir="ltr">{server.https.url}</div>
                  </div>
                </div>
              )
            ) : (
              <div className="text-[12px] text-(--ice-3)">در حال بررسی…</div>
            )}
          </section>

          <section className="rounded-2xl border border-(--edge) bg-(--pane) p-4">
            <h3 className="mb-1 text-[12.5px] font-semibold text-(--ice-2)">پرتکرارترین IPهای ورود ناموفق</h3>
            <div className="mb-3 text-[10.5px] text-(--ice-3)">بر اساس آخرین رویدادهای نمایش‌داده‌شده در این صفحه</div>
            {failedLoginIps.length === 0 ? (
              <div className="text-[11.5px] text-(--ice-3)">در این بازه ورود ناموفقی ثبت نشده است.</div>
            ) : (
              <div className="space-y-2">
                {failedLoginIps.map(([ip, count]) => (
                  <div key={ip} className="flex items-center justify-between text-[11.5px]">
                    <span className="ltr text-(--ice-2)" dir="ltr">{ip}</span>
                    <span className={`font-bold tabular-nums ${count >= 10 ? "text-(--ember)" : count >= 5 ? "text-(--amber)" : "text-(--ice-3)"}`}>
                      {fa(count)} بار
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>
        </div>
      </div>
    </div>
  );
}

function ScoreCard({ score }: { score: SecurityScoreDto }) {
  const tone = score.score >= 80 ? "mint" : score.score >= 50 ? "amber" : "ember";
  const stroke = tone === "mint" ? "var(--mint)" : tone === "amber" ? "var(--amber)" : "var(--ember)";
  const radius = 46;
  const circumference = 2 * Math.PI * radius;
  const filled = (score.score / 100) * circumference;

  return (
    <div className="flex flex-col gap-6 rounded-2xl border border-(--edge) bg-(--pane) p-5 sm:flex-row sm:items-center">
      <div className="relative h-32 w-32 shrink-0 self-center">
        <svg viewBox="0 0 110 110" className="h-full w-full -rotate-90">
          <circle cx="55" cy="55" r={radius} fill="none" stroke="var(--fld)" strokeWidth="9" />
          <circle
            cx="55"
            cy="55"
            r={radius}
            fill="none"
            stroke={stroke}
            strokeWidth="9"
            strokeLinecap="round"
            strokeDasharray={`${filled} ${circumference}`}
          />
        </svg>
        <div className="absolute inset-0 grid place-items-center">
          <div className="text-center">
            <div className="text-[26px] font-extrabold text-(--ice) tabular-nums">{fa(score.score)}</div>
            <div className="text-[10px] text-(--ice-3)">از ۱۰۰</div>
          </div>
        </div>
      </div>
      <div className="min-w-0 flex-1 space-y-2">
        <div className="text-[13px] font-bold text-(--ice)">امتیاز امنیتی (۲۴ ساعت اخیر)</div>
        {score.factors.map((f) => (
          <div key={f.label} className="flex items-center justify-between gap-3 text-[12px]">
            <span className="flex items-center gap-2 text-(--ice-2)">
              <span className={f.ok ? "text-(--mint)" : "text-(--ember)"}>{f.ok ? "✔" : "✖"}</span>
              {f.label}
            </span>
            {!f.ok && f.impact > 0 && (
              <span className="shrink-0 text-(--ember) tabular-nums">−{fa(f.impact)}</span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
