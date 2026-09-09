import { fa } from "../../lib/persian";
import { HEALTH_LABELS, type MonitoringOverviewDto } from "./monitoringTypes";

/** The dashboard's headline strip: overall health, uptime over the selected range, and the live
 * request/error counters. Green only when the DB probe says ok; "unknown" (sampler hasn't run
 * yet) is shown as such, never silently green (rule 17's spirit applied to monitoring itself). */
export function HealthBanner({ overview }: { overview: MonitoringOverviewDto }) {
  const tone =
    overview.healthStatus === "ok"
      ? "border-(--mint)/40 bg-(--mint)/8"
      : overview.healthStatus === "down"
        ? "border-(--ember)/40 bg-(--ember)/8"
        : "border-(--amber)/40 bg-(--amber)/8";
  const dot =
    overview.healthStatus === "ok" ? "bg-(--mint)" : overview.healthStatus === "down" ? "bg-(--ember)" : "bg-(--amber)";

  return (
    <div className={`flex flex-wrap items-center gap-x-8 gap-y-3 rounded-2xl border p-4.5 ${tone}`}>
      <div className="flex items-center gap-2.5">
        <span className={`h-3 w-3 rounded-full ${dot}`} />
        <div>
          <div className="text-[10.5px] tracking-[0.14em] text-(--ice-3)">وضعیت سامانه</div>
          <div className="text-[15px] font-bold text-(--ice)">
            {HEALTH_LABELS[overview.healthStatus] ?? overview.healthStatus}
          </div>
        </div>
      </div>
      <div>
        <div className="text-[10.5px] tracking-[0.14em] text-(--ice-3)">دسترس‌پذیری در بازه</div>
        <div className="text-[15px] font-bold text-(--ice) tabular-nums">{fa(overview.uptimePercent)}٪</div>
      </div>
      <div>
        <div className="text-[10.5px] tracking-[0.14em] text-(--ice-3)">هشدارهای فعال</div>
        <div
          className={`text-[15px] font-bold tabular-nums ${overview.activeAlerts > 0 ? "text-(--ember)" : "text-(--ice)"}`}
        >
          {fa(overview.activeAlerts)}
        </div>
      </div>
      <div>
        <div className="text-[10.5px] tracking-[0.14em] text-(--ice-3)">رویدادهای بحرانی امنیتی</div>
        <div
          className={`text-[15px] font-bold tabular-nums ${overview.security.criticalEvents > 0 ? "text-(--ember)" : "text-(--ice)"}`}
        >
          {fa(overview.security.criticalEvents)}
        </div>
      </div>
      <div>
        <div className="text-[10.5px] tracking-[0.14em] text-(--ice-3)">ورودهای ناموفق در بازه</div>
        <div className="text-[15px] font-bold text-(--ice) tabular-nums">{fa(overview.security.failedLogins)}</div>
      </div>
    </div>
  );
}
