import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import type { LogPageDto } from "./monitoringTypes";

const LEVELS = ["Fatal", "Error", "Warning", "Information", "Debug"] as const;
const LEVEL_LABELS: Record<string, string> = {
  Fatal: "بحرانی",
  Error: "خطا",
  Warning: "هشدار",
  Information: "اطلاع",
  Debug: "اشکال‌زدایی",
};
const MINUTES_OPTIONS = [
  { value: 60, label: "۱ ساعت اخیر" },
  { value: 360, label: "۶ ساعت اخیر" },
  { value: 1440, label: "۲۴ ساعت اخیر" },
  { value: 4320, label: "۳ روز اخیر" },
  { value: 10080, label: "۷ روز اخیر" },
];
const PAGE_SIZE = 100;

function levelTone(level: string): string {
  return level === "Fatal" || level === "Error"
    ? "bg-(--ember)/13 text-(--ember)"
    : level === "Warning"
      ? "bg-(--amber)/14 text-(--amber)"
      : "bg-(--fld) text-(--ice-2)";
}

/** minimum-level semantics: the API returns that level and above (more severe). */
const MIN_LEVEL_INDEX: Record<string, number> = {
  Debug: 0,
  Information: 1,
  Warning: 2,
  Error: 3,
  Fatal: 4,
};

export function MonitoringLogsPage() {
  const [level, setLevel] = useState("Warning");
  const [search, setSearch] = useState("");
  const [minutes, setMinutes] = useState(1440);
  const [page, setPage] = useState<LogPageDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const loadFirst = useCallback(() => {
    setLoading(true);
    const params = new URLSearchParams({
      level: MIN_LEVEL_INDEX[level] >= MIN_LEVEL_INDEX.Information ? level : "Information",
      minutes: String(minutes),
      limit: String(PAGE_SIZE),
    });
    if (search.trim()) params.set("search", search.trim());
    api
      .get<LogPageDto>(`/monitoring/logs?${params}`)
      .then((data) => {
        setPage(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در خواندن لاگ‌ها"))
      .finally(() => setLoading(false));
  }, [level, search, minutes]);

  useEffect(loadFirst, [loadFirst]);

  function loadMore() {
    if (!page?.nextBefore) return;
    setLoading(true);
    const params = new URLSearchParams({
      level: MIN_LEVEL_INDEX[level] >= MIN_LEVEL_INDEX.Information ? level : "Information",
      minutes: String(minutes),
      limit: String(PAGE_SIZE),
      before: page.nextBefore,
    });
    if (search.trim()) params.set("search", search.trim());
    api
      .get<LogPageDto>(`/monitoring/logs?${params}`)
      .then((data) => {
        if (page) setPage({ entries: [...page.entries, ...data.entries], nextBefore: data.nextBefore, hasMore: data.hasMore });
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در خواندن لاگ‌ها"))
      .finally(() => setLoading(false));
  }

  return (
    <div className="mx-auto max-w-[1200px] space-y-4">
      <div>
        <h1 className="text-[17px] font-bold text-(--ice)">لاگ‌های سامانه</h1>
        <p className="mt-0.5 text-[12px] text-(--ice-3)">
          فایل‌های Serilog سرور، از جدید به قدیم — فیلتر سطح از حد انتخاب‌شده به بالا
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2.5">
        <select
          value={level}
          onChange={(e) => setLevel(e.target.value)}
          className="rounded-[10px] border border-(--edge) bg-(--pane) px-3 py-1.5 text-[12px] text-(--ice-2)"
        >
          {LEVELS.map((l) => (
            <option key={l} value={l}>
              {LEVEL_LABELS[l]} و بالاتر
            </option>
          ))}
        </select>
        <select
          value={minutes}
          onChange={(e) => setMinutes(Number(e.target.value))}
          className="rounded-[10px] border border-(--edge) bg-(--pane) px-3 py-1.5 text-[12px] text-(--ice-2)"
        >
          {MINUTES_OPTIONS.map((m) => (
            <option key={m.value} value={m.value}>
              {m.label}
            </option>
          ))}
        </select>
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="جستجو در متن پیام…"
          className="min-w-52 flex-1 rounded-[10px] border border-(--edge) bg-(--pane) px-3 py-1.5 text-[12px] text-(--ice) placeholder:text-(--ice-3)"
        />
        <button
          type="button"
          onClick={loadFirst}
          className="rounded-[10px] bg-(--mint) px-3.5 py-1.5 text-[12px] font-semibold text-(--on-mint) transition-opacity hover:opacity-90"
        >
          اعمال فیلتر
        </button>
      </div>

      {error && !page ? (
        <EmptyState
          icon="⚠️"
          title="خواندن لاگ‌ها ناموفق بود"
          description={error}
          action={{ label: "تلاش دوباره", onClick: loadFirst }}
        />
      ) : !page ? (
        <div className="grid h-40 place-items-center text-(--ice-3)">در حال بارگذاری…</div>
      ) : page.entries.length === 0 ? (
        <EmptyState
          icon="📄"
          title="لاگی با این فیلترها یافت نشد"
          description="فیلتر سطح را کاهش دهید یا بازهٔ بزرگ‌تری را انتخاب کنید."
          action={{ label: "نمایش همهٔ سطوح", onClick: () => setLevel("Debug") }}
        />
      ) : (
        <div className="space-y-3">
          <div className="text-[11.5px] text-(--ice-3)">{fa(page.entries.length)} ردیف نمایش داده شده</div>
          <Table>
            <thead>
              <tr>
                <Th>زمان</Th>
                <Th>سطح</Th>
                <Th>پیام</Th>
              </tr>
            </thead>
            <tbody>
              {page.entries.map((entry, i) => (
                <Tr key={`${entry.timestampUtc}-${i}`}>
                  <Td className="whitespace-nowrap text-(--ice-3)">{toJalaliDateTimeDisplay(entry.timestampUtc)}</Td>
                  <Td>
                    <span className={`inline-block rounded-full px-2.5 py-0.5 text-[10.5px] font-bold ${levelTone(entry.level)}`}>
                      {LEVEL_LABELS[entry.level] ?? entry.level}
                    </span>
                  </Td>
                  <Td ltr className="max-w-[720px] break-words text-left text-[11.5px] leading-relaxed text-(--ice-2)">
                    {entry.message}
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          {page.hasMore && page.nextBefore && (
            <div className="flex justify-center">
              <button
                type="button"
                onClick={loadMore}
                disabled={loading}
                className="rounded-[10px] border border-(--edge) bg-(--pane) px-4 py-1.5 text-[12px] text-(--ice-2) transition-colors hover:bg-(--hov) disabled:opacity-50"
              >
                {loading ? "در حال بارگذاری…" : "بیشتر"}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
