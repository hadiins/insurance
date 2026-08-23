import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface SmsDeliveryReportDto {
  totalSent: number;
  totalFailed: number;
  customerRecipientCount: number;
  marketerRecipientCount: number;
  installmentReminderCount: number;
  renewalReminderCount: number;
}

export function SmsDeliveryReportPage() {
  const [report, setReport] = useState<SmsDeliveryReportDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<SmsDeliveryReportDto>("/sms/delivery-report")
      .then(setReport)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری گزارش"));
  }, []);

  const total = report ? report.totalSent + report.totalFailed : 0;
  const deliveryRate = total > 0 ? Math.round((report!.totalSent / total) * 100) : 0;

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">گزارش تحویل پیامک</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">جمع کل پیامک‌های ارسالی این نمایندگی</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && report === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && report !== null && (
        <div className="grid grid-cols-3 gap-3">
          <Fig label="ارسال‌شده" value={fa(report.totalSent)} tone="mint" />
          <Fig label="ناموفق" value={fa(report.totalFailed)} tone="ember" />
          <Fig label="نرخ تحویل" value={`${fa(deliveryRate)}٪`} tone="mint" />
          <Fig label="یادآوری قسط" value={fa(report.installmentReminderCount)} />
          <Fig label="یادآوری تمدید" value={fa(report.renewalReminderCount)} />
          <Fig label="گیرنده مشتری / بازاریاب" value={`${fa(report.customerRecipientCount)} / ${fa(report.marketerRecipientCount)}`} />
        </div>
      )}
    </div>
  );
}

function Fig({ label, value, tone }: { label: string; value: string; tone?: "mint" | "ember" }) {
  const valueColor = tone === "ember" ? "text-(--ember)" : tone === "mint" ? "text-(--mint)" : "text-(--ice)";
  return (
    <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
      <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">{label}</div>
      <div className={`text-[19px] font-extrabold tracking-tight ${valueColor}`}>{value}</div>
    </div>
  );
}
