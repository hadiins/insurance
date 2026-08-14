import { ROWS } from "../../app/fakeData";
import { useTabsStore } from "../../app/store/tabsStore";
import { money } from "../../lib/persian";

const PILL_CLASS: Record<string, string> = {
  late: "bg-(--ember)/13 text-(--ember)",
  due: "bg-(--amber)/13 text-(--amber)",
  ok: "bg-(--mint)/12 text-(--mint)",
};

export function TodayPage() {
  const openTab = useTabsStore((s) => s.openTab);

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        ۵ پیگیری <em className="font-extralight not-italic text-(--ice-2)">در انتظار شما</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">سه‌شنبه ۱۹ مرداد ۱۴۰۵</div>

      <div className="mb-4.5 rounded-xl border border-(--mint)/22 bg-(--mint)/7 p-4 text-[12.5px] text-(--ice-2)">
        این تب <b className="font-bold text-(--mint)">سنجاق</b> شده و بسته نمی‌شود. روی هر ردیف کلیک کنید تا در تب
        جدید باز شود — این تب دست‌نخورده می‌ماند.
      </div>

      <div className="mb-4 grid grid-cols-4 gap-3">
        <Fig label="معوق" value={money(51_200_000)} caption="۷ قسط" tone="ember" />
        <Fig label="سررسید امروز" value={money(24_500_000)} caption="۳ قسط" tone="amber" />
        <Fig label="وصول مرداد" value={money(142_800_000)} caption="۱۸ قسط" />
        <Fig label="اقساطی فعال" value="۲۸" caption="از ۱۸۰" />
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
            {ROWS.map((r) => (
              <tr
                key={r.id}
                onClick={() =>
                  openTab({
                    navType: "customer-detail",
                    page: "customer-detail",
                    kind: "multi-record",
                    recordId: r.id,
                    title: `مشتری ${r.id}`,
                    payload: r,
                  })
                }
                className="cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov)"
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
    </div>
  );
}

function Fig({
  label,
  value,
  caption,
  tone,
}: {
  label: string;
  value: string;
  caption: string;
  tone?: "ember" | "amber";
}) {
  const valueColor = tone === "ember" ? "text-(--ember)" : tone === "amber" ? "text-(--amber)" : "text-(--ice)";
  const barColor = tone === "ember" ? "bg-(--ember)" : tone === "amber" ? "bg-(--amber)" : "bg-(--mint)";
  return (
    <div className="relative overflow-hidden rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
      <span className={`absolute start-0 top-0 h-0.5 w-7.5 ${barColor}`} />
      <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">{label}</div>
      <div className={`text-[23px] font-extrabold tracking-tight ${valueColor}`}>{value}</div>
      <div className="text-[11px] text-(--ice-3)">{caption}</div>
    </div>
  );
}
