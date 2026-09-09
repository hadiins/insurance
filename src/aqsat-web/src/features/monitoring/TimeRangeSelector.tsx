import { fa } from "../../lib/persian";
import { TIME_RANGES, type TimeRangeKey } from "./monitoringTypes";

/** The shared range filter (۱س/۶س/۲۴س/۷ر/۳۰ر) + "به‌روزرسانی خودکار" toggle every monitoring page
 * carries. Pure presentational: state lives in the page. */
export function TimeRangeSelector({
  range,
  onRangeChange,
  autoRefresh,
  onAutoRefreshChange,
}: {
  range: TimeRangeKey;
  onRangeChange: (range: TimeRangeKey) => void;
  autoRefresh?: boolean;
  onAutoRefreshChange?: (value: boolean) => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <div className="flex overflow-hidden rounded-[10px] border border-(--edge) bg-(--pane)">
        {TIME_RANGES.map((option) => (
          <button
            key={option.key}
            type="button"
            onClick={() => onRangeChange(option.key)}
            className={`px-3 py-1.5 text-[12px] font-medium transition-colors ${
              range === option.key ? "bg-(--mint) text-(--on-mint)" : "text-(--ice-2) hover:bg-(--hov)"
            }`}
          >
            {option.label}
          </button>
        ))}
      </div>
      {autoRefresh !== undefined && onAutoRefreshChange && (
        <label className="flex cursor-pointer select-none items-center gap-2 rounded-[10px] border border-(--edge) bg-(--pane) px-3 py-1.5 text-[12px] text-(--ice-2)">
          <input
            type="checkbox"
            checked={autoRefresh}
            onChange={(e) => onAutoRefreshChange(e.target.checked)}
            className="h-3.5 w-3.5 accent-(--mint)"
          />
          به‌روزرسانی خودکار ({fa(30)} ثانیه)
        </label>
      )}
    </div>
  );
}
