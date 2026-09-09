import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

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

const timeLabel = toJalaliDateTimeDisplay;

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
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">آخرین ۲۰۰ پیامک ارسالی، جدیدترین در بالا</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && log === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && log !== null && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(log.length)} پیامک</div>
          {log.length === 0 ? (
            <EmptyState
              icon="📭"
              title="هنوز پیامکی ارسال نشده."
              description="پس از ارسال نخستین یادآوری، گزارش آن در این‌جا نمایش داده می‌شود."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["زمان", "بیمه‌نامه", "گیرنده", "شماره", "وضعیت"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {log.map((entry) => (
                  <Tr key={entry.id}>
                    <Td className="py-2.5 text-(--ice-3)">{timeLabel(entry.sentAt)}</Td>
                    <Td className="py-2.5 font-semibold">
                      {entry.policyNumber ?? "—"} {entry.seqNo ? `— قسط ${fa(entry.seqNo)}` : ""}
                    </Td>
                    <Td className="py-2.5 text-(--ice-3)">{RECIPIENT_LABEL[entry.recipientType] ?? entry.recipientType}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{fa(entry.mobile)}</Td>
                    <Td className={`py-2.5 ${entry.status === "Sent" ? "text-(--mint)" : "text-(--ember)"}`}>
                      {STATUS_LABEL[entry.status] ?? entry.status}
                    </Td>
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
