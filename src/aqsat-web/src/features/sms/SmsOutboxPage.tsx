import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface ReminderLogDto {
  id: string;
  policyNumber: string | null;
  seqNo: number | null;
  recipientType: string;
  mobile: string;
  offsetDays: number;
  status: string;
  sentAt: string;
}

const STATUS_LABEL: Record<string, string> = { Sent: "ارسال‌شده", Failed: "ناموفق" };
const RECIPIENT_LABEL: Record<string, string> = { Customer: "مشتری", Marketer: "بازاریاب" };

function timeLabel(iso: string): string {
  const d = new Date(iso);
  return fa(`${d.toLocaleDateString("en-CA")} ${d.getHours().toString().padStart(2, "0")}:${d.getMinutes().toString().padStart(2, "0")}`);
}

export function SmsOutboxPage() {
  const [log, setLog] = useState<ReminderLogDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<ReminderLogDto[]>("/sms/log?take=200")
      .then(setLog)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری صندوق ارسال"));
  }, []);

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">صندوق ارسال</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">آخرین ۲۰۰ پیامک ارسالی، جدیدترین در بالا</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && log === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && log !== null && (
        <>
          <div className="mb-2 text-[11px] text-(--ice-3)">{fa(log.length)} پیامک</div>
          {log.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">هنوز پیامکی ارسال نشده.</div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    {["زمان", "بیمه‌نامه", "گیرنده", "شماره", "وضعیت"].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {log.map((entry) => (
                    <tr key={entry.id} className="border-t border-(--edge) first:border-t-0">
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{timeLabel(entry.sentAt)}</td>
                      <td className="px-3 py-2.5 text-[13px] font-semibold">
                        {entry.policyNumber ?? "—"} {entry.seqNo ? `— قسط ${fa(entry.seqNo)}` : ""}
                      </td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{RECIPIENT_LABEL[entry.recipientType] ?? entry.recipientType}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{fa(entry.mobile)}</td>
                      <td className={`px-3 py-2.5 text-[13px] ${entry.status === "Sent" ? "text-(--mint)" : "text-(--ember)"}`}>
                        {STATUS_LABEL[entry.status] ?? entry.status}
                      </td>
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
