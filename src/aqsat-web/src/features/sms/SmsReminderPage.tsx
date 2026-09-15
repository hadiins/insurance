import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { JalaliDateField } from "../../components/JalaliDateField";

interface InsuranceLineDto {
  id: string;
  nameFa: string;
}

interface MarketerDto {
  id: string;
  fullName: string;
}

interface SmsPreviewResultDto {
  count: number;
  estimatedCostToman: number;
}

interface SmsSendResultDto {
  sentCount: number;
  skippedCount: number;
  alreadySentTodayCount: number;
}

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

interface FilterState {
  dueFrom: string;
  dueTo: string;
  status: string;
  insuranceLineId: string;
  marketerId: string;
  minAmount: string;
  maxAmount: string;
}

const EMPTY_FILTER: FilterState = {
  dueFrom: "",
  dueTo: "",
  status: "",
  insuranceLineId: "",
  marketerId: "",
  minAmount: "",
  maxAmount: "",
};

const STATUS_LABEL: Record<string, string> = { Sent: "ارسال‌شده", Failed: "ناموفق" };

export function SmsReminderPage() {
  const [filter, setFilter] = useState<FilterState>(EMPTY_FILTER);
  const [lines, setLines] = useState<InsuranceLineDto[] | null>(null);
  const [marketers, setMarketers] = useState<MarketerDto[] | null>(null);
  const [preview, setPreview] = useState<SmsPreviewResultDto | null>(null);
  const [sendResult, setSendResult] = useState<SmsSendResultDto | null>(null);
  const [log, setLog] = useState<ReminderLogDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.get<InsuranceLineDto[]>("/insurance-lines").then(setLines).catch(() => {});
    api.get<MarketerDto[]>("/marketers").then(setMarketers).catch(() => {});
    reloadLog();
  }, []);

  function reloadLog() {
    api.get<ReminderLogDto[]>("/sms/log?take=50").then(setLog).catch(() => {});
  }

  function buildRequest() {
    return {
      dueFrom: filter.dueFrom || null,
      dueTo: filter.dueTo || null,
      status: filter.status || null,
      insuranceLineId: filter.insuranceLineId || null,
      marketerId: filter.marketerId || null,
      minAmount: filter.minAmount ? Number(filter.minAmount) : null,
      maxAmount: filter.maxAmount ? Number(filter.maxAmount) : null,
    };
  }

  async function runPreview() {
    setBusy(true);
    setError(null);
    setSendResult(null);
    try {
      const result = await api.post<SmsPreviewResultDto>("/sms/preview", buildRequest());
      setPreview(result);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "پیش‌نمایش ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function confirmSend() {
    setBusy(true);
    setError(null);
    try {
      const result = await api.post<SmsSendResultDto>("/sms/send", buildRequest());
      setSendResult(result);
      setPreview(null);
      reloadLog();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ارسال ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        یادآوری <em className="font-extralight not-italic text-(--ice-2)">پیامکی</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">فیلتر کنید، تعداد و هزینه را ببینید، بعد ارسال کنید</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3.5 grid grid-cols-3 gap-3">
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">سررسید از</label>
            <JalaliDateField
              value={filter.dueFrom}
              onChange={(v) => setFilter((f) => ({ ...f, dueFrom: v }))}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)"
            />
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">سررسید تا</label>
            <JalaliDateField
              value={filter.dueTo}
              onChange={(v) => setFilter((f) => ({ ...f, dueTo: v }))}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)"
            />
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">وضعیت</label>
            <select
              value={filter.status}
              onChange={(e) => setFilter((f) => ({ ...f, status: e.target.value }))}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)"
            >
              <option value="">همه</option>
              <option value="Unpaid">پرداخت‌نشده</option>
              <option value="Partial">جزئی</option>
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">رشتهٔ بیمه</label>
            <select
              value={filter.insuranceLineId}
              onChange={(e) => setFilter((f) => ({ ...f, insuranceLineId: e.target.value }))}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)"
            >
              <option value="">همه</option>
              {lines?.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.nameFa}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">بازاریاب</label>
            <select
              value={filter.marketerId}
              onChange={(e) => setFilter((f) => ({ ...f, marketerId: e.target.value }))}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)"
            >
              <option value="">همه</option>
              {marketers?.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.fullName}
                </option>
              ))}
            </select>
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div>
              <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">حداقل مبلغ</label>
              <input
                value={filter.minAmount}
                onChange={(e) => setFilter((f) => ({ ...f, minAmount: e.target.value }))}
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)"
              />
            </div>
            <div>
              <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">حداکثر مبلغ</label>
              <input
                value={filter.maxAmount}
                onChange={(e) => setFilter((f) => ({ ...f, maxAmount: e.target.value }))}
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)"
              />
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={runPreview}
            disabled={busy}
            className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice) disabled:cursor-not-allowed disabled:opacity-50"
          >
            پیش‌نمایش
          </button>
          {preview && (
            <>
              <span className="text-[12.5px] text-(--ice-2)">
                {fa(preview.count)} پیامک — تقریباً {money(preview.estimatedCostToman)} تومان
              </span>
              <button
                type="button"
                onClick={confirmSend}
                disabled={busy || preview.count === 0}
                className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {busy ? "در حال ارسال…" : "تأیید و ارسال"}
              </button>
            </>
          )}
        </div>

        {sendResult && (
          <div className="mt-3.5 grid grid-cols-3 gap-2 text-center text-[11.5px]">
            <div className="rounded-[8px] border border-(--edge-2) p-2">
              <div className="text-(--ice-3)">ارسال‌شده</div>
              <div className="font-bold text-(--moss)">{fa(sendResult.sentCount)}</div>
            </div>
            <div className="rounded-[8px] border border-(--edge-2) p-2">
              <div className="text-(--ice-3)">ناموفق/رد‌شده</div>
              <div className="font-bold text-(--ember)">{fa(sendResult.skippedCount)}</div>
            </div>
            <div className="rounded-[8px] border border-(--edge-2) p-2">
              <div className="text-(--ice-3)">قبلاً ارسال‌شده</div>
              <div className="font-bold text-(--ice)">{fa(sendResult.alreadySentTodayCount)}</div>
            </div>
          </div>
        )}
      </div>

      <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <div className="border-b border-(--edge) px-4 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">
          گزارش تحویل (آخرین ارسال‌ها)
        </div>
        {log?.length === 0 ? (
          <div className="p-6 text-center text-[13.5px] text-(--ice-3)">هنوز پیامکی ارسال نشده.</div>
        ) : (
          log?.map((entry) => (
            <div
              key={entry.id}
              className="flex items-center justify-between border-t border-(--edge) px-4 py-2 text-[12.5px] first:border-t-0"
            >
              <span>
                {entry.policyNumber ?? "—"} {entry.seqNo ? `— قسط ${fa(entry.seqNo)}` : ""}
              </span>
              <span className="text-(--ice-3)">{entry.mobile}</span>
              <span className={entry.status === "Sent" ? "text-(--moss)" : "text-(--ember)"}>
                {STATUS_LABEL[entry.status] ?? entry.status}
              </span>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
