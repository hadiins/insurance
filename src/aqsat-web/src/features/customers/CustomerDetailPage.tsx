import { useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { fa, money } from "../../lib/persian";
import { PresenceLockBar } from "../concurrency/PresenceLockBar";

interface CustomerDetailPayload {
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  seqNo: number;
  balance: number;
  status: string;
  urgency: string;
}

export function CustomerDetailPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const setDirty = useTabsStore((s) => s.setDirty);
  const [note, setNote] = useState("");

  const row = tab?.payload as CustomerDetailPayload | undefined;
  if (!row) return null;

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        پروندهٔ <em className="font-extralight not-italic text-(--ice-2)">{row.customerFullName}</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">بیمه‌نامهٔ {row.policyNumber} — در تب جدید باز شد</div>

      <PresenceLockBar entityType="Policy" entityId={row.policyId} />

      <div className="mb-4 grid grid-cols-3 gap-3">
        <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
          <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">مانده</div>
          <div className="text-[23px] font-extrabold tracking-tight text-(--ember)">{money(row.balance)}</div>
          <div className="text-[11px] text-(--ice-3)">قسط شمارهٔ {fa(row.seqNo)}</div>
        </div>
        <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
          <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">وضعیت</div>
          <div className="text-[23px] font-extrabold tracking-tight text-(--ice)">{row.status}</div>
          <div className="text-[11px] text-(--ice-3)">این قسط</div>
        </div>
        <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
          <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">فوریت</div>
          <div className="pt-1 text-[16px] font-extrabold tracking-tight text-(--amber)">{row.urgency}</div>
          <div className="text-[11px] text-(--ice-3)">شمارش‌معکوس تسویه</div>
        </div>
      </div>

      <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">
          یادداشت پیگیری — دربارهٔ بیمه‌نامه
        </label>
        <textarea
          value={note}
          onChange={(e) => {
            setNote(e.target.value);
            setDirty(tabKey, true);
          }}
          rows={3}
          placeholder="مثلاً: قرار شد تا پایان هفته واریز کند"
          className="mb-3.5 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => setDirty(tabKey, false)}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105"
          >
            ثبت پیگیری
          </button>
          <button
            type="button"
            className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
          >
            ارسال یادآوری
          </button>
        </div>
      </div>
    </div>
  );
}
