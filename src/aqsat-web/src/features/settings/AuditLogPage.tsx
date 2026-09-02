import { useEffect, useState } from "react";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";

interface AuditLogRowDto {
  id: number;
  userDisplayName: string;
  entityType: string;
  action: string;
  description: string;
  occurredAt: string;
}

const ACTION_LABEL: Record<string, string> = {
  Created: "ایجاد",
  Updated: "ویرایش",
  PaymentRecorded: "ثبت پرداخت",
  LockForceReleased: "رفع قفل اجباری",
};

const timeLabel = toJalaliDateTimeDisplay;

export function AuditLogPage() {
  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<AuditLogRowDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = () => {
    const params = search.trim() ? `?search=${encodeURIComponent(search.trim())}` : "";
    api
      .get<AuditLogRowDto[]>(`/settings/audit-log${params}`)
      .then((data) => {
        setRows(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری لاگ فعالیت"));
  };

  useEffect(() => {
    const handle = setTimeout(reload, 250);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  useLiveReload(reload);

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">لاگ فعالیت</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">همهٔ تغییرات ثبت‌شده در این نمایندگی، جدیدترین در بالا</div>

      <input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="جست‌وجو بر اساس کاربر یا شرح تغییر…"
        className="mb-4.5 w-full max-w-sm rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
      />

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && rows === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && rows !== null && (
        <>
          <div className="mb-2 text-[11px] text-(--ice-3)">{fa(rows.length)} رویداد</div>
          {rows.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">رویدادی ثبت نشده.</div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    {["زمان", "کاربر", "نوع تغییر", "شرح"].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.id} className="border-t border-(--edge) first:border-t-0">
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{timeLabel(r.occurredAt)}</td>
                      <td className="px-3 py-2.5 text-[13px] font-semibold">{r.userDisplayName}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{ACTION_LABEL[r.action] ?? r.action}</td>
                      <td className="px-3 py-2.5 text-[13px]">{r.description}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}
