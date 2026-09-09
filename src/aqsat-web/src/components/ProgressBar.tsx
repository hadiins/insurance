import { fa, money } from "../lib/persian";

/** Progress toward a target (TailAdmin's MonthlyTarget pattern). The natural target in this
 * domain is "collected against owed" — no stored goal needed, the data is already true. */
export function ProgressBar({
  label,
  value,
  target,
  valueLabel = "وصول‌شده",
  targetLabel = "کل بدهی",
}: {
  label: string;
  value: number;
  target: number;
  valueLabel?: string;
  targetLabel?: string;
}) {
  const percent = target > 0 ? Math.min(100, Math.round((value / target) * 100)) : 0;

  return (
    <div className="rounded-2xl border border-(--edge) bg-(--pane) p-4.5">
      <div className="mb-1 text-[10.5px] tracking-[0.14em] text-(--ice-3)">{label}</div>
      <div className="mb-3 flex items-baseline justify-between">
        <div className="text-[20px] font-extrabold tabular-nums text-(--mint)">{fa(percent)}٪</div>
        <div className="text-[11.5px] tabular-nums text-(--ice-3)">
          {valueLabel}: {money(value)} از {targetLabel}: {money(target)}
        </div>
      </div>
      <div className="h-2.5 overflow-hidden rounded-full bg-(--fld)">
        <div
          className="h-full rounded-full bg-(--mint) transition-[width] duration-500"
          style={{ width: `${percent}%` }}
        />
      </div>
    </div>
  );
}
