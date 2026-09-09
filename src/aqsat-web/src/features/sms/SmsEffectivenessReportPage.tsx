import { useState } from "react";
import { api, ApiError, getActiveOrgId, getToken } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

interface SmsEffectivenessSummaryDto {
  remindersSent: number;
  remindersFailed: number;
  distinctInstallments: number;
  paidWithinWindow: number;
  effectivenessRate: number;
  attributedCollectedToman: number;
  estimatedCostToman: number;
}

interface SmsEffectivenessOffsetRow {
  offsetDays: number;
  sent: number;
  paid: number;
  rate: number;
}

interface SmsEffectivenessMonthRow {
  jalaliMonthKey: string;
  monthLabel: string;
  sent: number;
  paid: number;
  rate: number;
}

interface SmsEffectivenessReportDto {
  summary: SmsEffectivenessSummaryDto;
  offsetRows: SmsEffectivenessOffsetRow[];
  monthRows: SmsEffectivenessMonthRow[];
}

interface SmsEffectivenessDetailRow {
  policyNumber: string;
  customerFullName: string;
  seqNo: number;
  dueDate: string;
  reminderCount: number;
  offsets: string;
  firstReminderAt: string;
  firstPaidOnAfterReminder: string | null;
  daysToPay: number | null;
  collectedInWindowToman: number;
  installmentStatus: string;
}

interface SmsEffectivenessDetailPageDto {
  totalCount: number;
  rows: SmsEffectivenessDetailRow[];
}

const STATUS_LABEL: Record<string, string> = {
  Unpaid: "پرداخت‌نشده",
  Partial: "پرداخت جزئی",
  Settled: "تسویه‌شده",
};

const PAGE_SIZE = 25;
const TODAY = new Date().toISOString().slice(0, 10);
const MONTH_AGO = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);

export function SmsEffectivenessReportPage() {
  const [from, setFrom] = useState(MONTH_AGO);
  const [to, setTo] = useState(TODAY);
  const [attributionDays, setAttributionDays] = useState("7");
  const [page, setPage] = useState(1);
  const [report, setReport] = useState<SmsEffectivenessReportDto | null>(null);
  const [pageData, setPageData] = useState<SmsEffectivenessDetailPageDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run(targetPage = 1) {
    const days = encodeURIComponent(attributionDays);
    setBusy(true);
    setError(null);
    try {
      const [reportResult, pageResult] = await Promise.all([
        api.get<SmsEffectivenessReportDto>(
          `/sms/effectiveness-report?from=${from}&to=${to}&attributionDays=${days}`),
        api.get<SmsEffectivenessDetailPageDto>(
          `/sms/effectiveness-report/details?from=${from}&to=${to}&attributionDays=${days}&page=${targetPage}&pageSize=${PAGE_SIZE}`),
      ]);
      setReport(reportResult);
      setPageData(pageResult);
      setPage(targetPage);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "دریافت گزارش ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function exportXlsx() {
    const token = getToken();
    const orgId = getActiveOrgId();
    const headers = new Headers();
    if (token) headers.set("Authorization", `Bearer ${token}`);
    if (orgId) headers.set("X-Organization-Id", orgId);

    const response = await fetch(
      `/api/sms/effectiveness-report/export?from=${from}&to=${to}&attributionDays=${encodeURIComponent(attributionDays)}`,
      { headers });
    if (!response.ok) {
      setError("خروجی اکسل ناموفق بود.");
      return;
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `sms-effectiveness-${from}-${to}.xlsx`;
    link.click();
    URL.revokeObjectURL(url);
  }

  const totalPages = pageData ? Math.max(1, Math.ceil(pageData.totalCount / PAGE_SIZE)) : 1;

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        اثربخشی <em className="font-extralight not-italic text-(--ice-2)">پیامک</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        آیا یادآوری‌ها به پرداخت رسیده‌اند — برای ارزیابی هزینهٔ پیامک و تنظیم آفست‌های یادآوری
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3.5 grid grid-cols-3 gap-3">
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">از تاریخ</label>
            <JalaliDateField value={from} onChange={setFrom} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)" />
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">تا تاریخ</label>
            <JalaliDateField value={to} onChange={setTo} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)" />
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">روزهای انتساب</label>
            <input
              type="number"
              min={1}
              max={30}
              value={attributionDays}
              onChange={(e) => setAttributionDays(e.target.value)}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)"
            />
          </div>
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => run(1)}
            disabled={busy}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {busy ? "در حال محاسبه…" : "دریافت گزارش"}
          </button>
          {report && (
            <button
              type="button"
              onClick={exportXlsx}
              className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
            >
              خروجی اکسل
            </button>
          )}
        </div>
      </div>

      {report && (
        <>
          <div className="mb-4.5 grid grid-cols-4 gap-3">
            <Fig label="یادآوری ارسال‌شده" value={fa(report.summary.remindersSent)} />
            <Fig label="ارسال ناموفق" value={fa(report.summary.remindersFailed)} tone="ember" />
            <Fig label="اقساط یادآوری‌شده" value={fa(report.summary.distinctInstallments)} />
            <Fig label="پرداخت در پنجره" value={fa(report.summary.paidWithinWindow)} tone="mint" />
            <Fig label="نرخ اثربخشی" value={`${fa(report.summary.effectivenessRate)}٪`} tone="mint" />
            <Fig label="وصول منتسب" value={money(report.summary.attributedCollectedToman)} tone="mint" />
            <Fig label="هزینهٔ تخمینی" value={money(report.summary.estimatedCostToman)} />
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">نرخ اثربخشی یعنی چه؟</div>
              <div className="text-[11.5px] leading-relaxed text-(--ice-3)">
                سهم یادآوری‌هایی که ظرف {fa(attributionDays)} روز پس از ارسال، پرداختی برای همان قسط ثبت شد. وصولِ «منتسب» است، نه قطعاً «معلول» — مشتری شاید بدون پیامک هم می‌پرداخت.
              </div>
            </div>
          </div>

          {report.offsetRows.length > 0 && (
            <div className="mb-4.5">
              <div className="mb-2 text-[11.5px] tracking-wider text-(--ice-3)">به‌تفکیک آفست یادآوری</div>
              <Table>
                <thead>
                  <tr>
                    {["آفست (روز تا سررسید)", "ارسال", "مؤثر", "نرخ"].map((h) => (
                      <Th key={h}>{h}</Th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {report.offsetRows.map((r, idx) => (
                    <Tr key={idx}>
                      <Td className="py-2.5">{fa(r.offsetDays)}</Td>
                      <Td className="py-2.5">{fa(r.sent)}</Td>
                      <Td className="py-2.5">{fa(r.paid)}</Td>
                      <Td className="py-2.5 font-bold">{fa(r.rate)}٪</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </div>
          )}

          {report.monthRows.length > 0 && (
            <div className="mb-4.5">
              <div className="mb-2 text-[11.5px] tracking-wider text-(--ice-3)">روند ماه‌های شمسی</div>
              <Table>
                <thead>
                  <tr>
                    {["ماه", "ارسال", "مؤثر", "نرخ"].map((h) => (
                      <Th key={h}>{h}</Th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {report.monthRows.map((r, idx) => (
                    <Tr key={idx}>
                      <Td className="py-2.5">{r.monthLabel}</Td>
                      <Td className="py-2.5">{fa(r.sent)}</Td>
                      <Td className="py-2.5">{fa(r.paid)}</Td>
                      <Td className="py-2.5 font-bold">{fa(r.rate)}٪</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </div>
          )}
        </>
      )}

      {pageData && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(pageData.totalCount)} قسط</div>
          {pageData.rows.length === 0 ? (
            <EmptyState
              icon="📊"
              title="یادآوری‌ای در این بازه نیست"
              description="در بازهٔ انتخابی یادآوری قسطی ارسال نشده است؛ بازهٔ زمانی را تغییر دهید."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["بیمه‌نامه", "بیمه‌گذار", "قسط", "سررسید", "یادآوری‌ها", "آفست‌ها", "اولین یادآوری", "اولین پرداخت پس از یادآوری", "فاصله (روز)", "وصول در پنجره", "وضعیت"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {pageData.rows.map((r, idx) => (
                  <Tr key={idx}>
                    <Td className="py-2.5 font-semibold">{fa(r.policyNumber)}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{r.customerFullName}</Td>
                    <Td className="py-2.5">{fa(r.seqNo)}</Td>
                    <Td className="py-2.5">{toJalaliDisplay(r.dueDate)}</Td>
                    <Td className="py-2.5">{fa(r.reminderCount)}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{r.offsets}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{toJalaliDisplay(r.firstReminderAt.slice(0, 10))}</Td>
                    <Td className="py-2.5">
                      {r.firstPaidOnAfterReminder ? toJalaliDisplay(r.firstPaidOnAfterReminder) : "—"}
                    </Td>
                    <Td className="py-2.5">{r.daysToPay === null ? "—" : fa(r.daysToPay)}</Td>
                    <Td className="py-2.5 font-bold">{money(r.collectedInWindowToman)}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{STATUS_LABEL[r.installmentStatus] ?? r.installmentStatus}</Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
          {totalPages > 1 && (
            <div className="mt-3 flex items-center justify-center gap-2">
              <button
                type="button"
                disabled={page <= 1}
                onClick={() => run(page - 1)}
                className="rounded-[8px] border border-(--edge-2) px-3 py-1 text-[11.5px] text-(--ice-3) transition-colors hover:bg-(--hov) disabled:cursor-not-allowed disabled:opacity-40"
              >
                قبلی
              </button>
              <span className="text-[11.5px] text-(--ice-3)">
                {fa(page)} از {fa(totalPages)}
              </span>
              <button
                type="button"
                disabled={page >= totalPages}
                onClick={() => run(page + 1)}
                className="rounded-[8px] border border-(--edge-2) px-3 py-1 text-[11.5px] text-(--ice-3) transition-colors hover:bg-(--hov) disabled:cursor-not-allowed disabled:opacity-40"
              >
                بعدی
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function Fig({ label, value, tone }: { label: string; value: string; tone?: "mint" | "ember" }) {
  const color = tone === "mint" ? "text-(--mint)" : tone === "ember" ? "text-(--ember)" : "text-(--ice)";
  return (
    <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
      <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">{label}</div>
      <div className={`text-[20px] font-extrabold tracking-tight ${color}`}>{value}</div>
    </div>
  );
}
