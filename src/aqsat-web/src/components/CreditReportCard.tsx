import { fa, money } from "../lib/persian";
import { toJalaliDateTimeDisplay } from "../lib/jalali";

/// Structured api.ir credit data (UnpaidCheque + ActiveLoans) shared by the wizard's
/// verification step and the customer file's «گزارش اعتباری» card. Mirrors the backend's
/// CreditReportDto — every field nullable, rawSuccess=false means sandboxed/failed.
export interface CreditReportDto {
  chequeCount: number | null;
  chequeSumAmountToman: number | null;
  chequeSumBouncedAmountToman: number | null;
  activeLoansCount: number | null;
  loanTotalAmountToman: number | null;
  loanDebtTotalAmountToman: number | null;
  loanPastExpiredTotalAmountToman: number | null;
  loanDeferredTotalAmountToman: number | null;
  loanSuspiciousTotalAmountToman: number | null;
  loanDishonoredAmountToman: number | null;
  rawSuccess: boolean;
  retrievedAtUtc: string;
}

/// The api.ir credit report as a card: bounced-cheque and active-loan figures with the
/// sandbox warning when the inquiry had no real data.
export function CreditReportCard({ report }: { report: CreditReportDto }) {
  return (
    <div className="rounded-[12px] border border-(--edge-2) bg-(--fld) p-4">
      <div className="mb-2.5 flex items-center justify-between gap-2">
        <div className="text-[12.5px] font-bold text-(--ice)">گزارش اعتباری</div>
        <div className="text-[11.5px] text-(--ice-3)">
          {fa(toJalaliDateTimeDisplay(report.retrievedAtUtc))}
        </div>
      </div>
      {!report.rawSuccess && (
        <div className="mb-2.5 rounded-[8px] border border-(--ember)/30 bg-(--ember)/10 px-2.5 py-1.5 text-[11.5px] leading-relaxed text-(--ember)">
          استعلام در حالت آزمایشی (sandbox) اجرا شده و دادهٔ واقعی ندارد.
        </div>
      )}
      <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">چک برگشتی</div>
      <div className="mb-3 grid grid-cols-3 gap-2 text-[12.5px]">
        <ReportCell label="تعداد" value={report.chequeCount} />
        <ReportCell label="مجموع مبلغ" value={report.chequeSumAmountToman} isMoney />
        <ReportCell label="مجموع برگشتی" value={report.chequeSumBouncedAmountToman} isMoney />
      </div>
      <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">تسهیلات فعال بانکی</div>
      <div className="grid grid-cols-3 gap-2 text-[12.5px]">
        <ReportCell label="تعداد" value={report.activeLoansCount} />
        <ReportCell label="مبلغ کل" value={report.loanTotalAmountToman} isMoney />
        <ReportCell label="بدهی جاری" value={report.loanDebtTotalAmountToman} isMoney />
        <ReportCell label="سررسید گذشته" value={report.loanPastExpiredTotalAmountToman} isMoney />
        <ReportCell label="معوق" value={report.loanDeferredTotalAmountToman} isMoney />
        <ReportCell label="مشکوک" value={report.loanSuspiciousTotalAmountToman} isMoney />
      </div>
    </div>
  );
}

function ReportCell({ label, value, isMoney = false }: { label: string; value: number | null; isMoney?: boolean }) {
  const display = value === null ? "—" : isMoney ? money(value) : fa(value);
  return (
    <div className="rounded-[8px] bg-(--pane) px-2.5 py-2">
      <div className="mb-0.5 text-[10.5px] tracking-wider text-(--ice-3)">{label}</div>
      <div className="tabular-nums font-semibold text-(--ice)">{display}</div>
    </div>
  );
}
