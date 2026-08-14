import { useMemo, useState } from "react";
import { ROWS } from "../../app/fakeData";
import { fa, money } from "../../lib/persian";

const PILL_CLASS: Record<string, string> = {
  late: "bg-(--ember)/13 text-(--ember)",
  due: "bg-(--amber)/13 text-(--amber)",
  ok: "bg-(--mint)/12 text-(--mint)",
};

const LIST = Array.from({ length: 22 }, (_, i) => ({ ...ROWS[i % ROWS.length], rowIndex: i }));

export function InstallmentListPage() {
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<number | null>(null);

  const filtered = useMemo(
    () => LIST.filter((r) => r.id.includes(query.trim())),
    [query],
  );

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        فهرست <em className="font-extralight not-italic text-(--ice-2)">اقساط</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">جست‌وجو، اسکرول و ردیف انتخاب‌شده حفظ می‌شوند</div>

      <div className="mb-4.5 rounded-xl border border-(--mint)/22 bg-(--mint)/7 p-4 text-[12.5px] text-(--ice-2)">
        یک ردیف را انتخاب کنید و کمی اسکرول کنید. تب دیگری باز کنید و برگردید —{" "}
        <b className="font-bold text-(--mint)">همان‌جا هستید</b>.
      </div>

      <div className="mb-3.5 max-w-80">
        <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">جست‌وجو</label>
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="کد مشتری..."
          className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />
      </div>

      <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <table className="w-full border-collapse">
          <thead>
            <tr>
              <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                بیمه‌گذار
              </th>
              <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                وضعیت
              </th>
              <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                قسط
              </th>
              <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                مبلغ
              </th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((r) => (
              <tr
                key={r.rowIndex}
                onClick={() => setSelected(r.rowIndex)}
                className={`cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov) ${
                  selected === r.rowIndex ? "bg-(--mint)/9" : ""
                }`}
              >
                <td className="px-3 py-2.75 text-[13px] font-semibold">مشتری {r.id}</td>
                <td className="px-3 py-2.75 text-[13px]">
                  <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${PILL_CLASS[r.status]}`}>
                    {r.label}
                  </span>
                </td>
                <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{r.progress}</td>
                <td className="px-3 py-2.75 text-[13px] font-bold">{money(r.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="mt-2 text-[11px] text-(--ice-3)">{fa(filtered.length)} ردیف</div>
    </div>
  );
}
