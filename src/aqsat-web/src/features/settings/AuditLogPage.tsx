import { useEffect, useState } from "react";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

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
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">همهٔ تغییرات ثبت‌شده در این نمایندگی، جدیدترین در بالا</div>

      <input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="جست‌وجو بر اساس کاربر یا شرح تغییر…"
        className="mb-4.5 w-full max-w-sm rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
      />

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && rows === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && rows !== null && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(rows.length)} رویداد</div>
          {rows.length === 0 ? (
            <EmptyState
              icon="📜"
              title="رویدادی ثبت نشده"
              description={
                search.trim()
                  ? "هیچ رویدادی با این جست‌وجو مطابقت ندارد؛ عبارت دیگری را امتحان کنید."
                  : "هنوز فعالیتی در این نمایندگی ثبت نشده است."
              }
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["زمان", "کاربر", "نوع تغییر", "شرح"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <Tr key={r.id}>
                    <Td className="py-2.5 text-(--ice-3)">{timeLabel(r.occurredAt)}</Td>
                    <Td className="py-2.5 font-semibold">{r.userDisplayName}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{ACTION_LABEL[r.action] ?? r.action}</Td>
                    <Td className="py-2.5">{r.description}</Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </>
      )}
    </div>
  );
}
