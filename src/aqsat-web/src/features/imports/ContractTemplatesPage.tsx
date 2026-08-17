import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";

interface ContractTemplateDto {
  id: string;
  contractNamePattern: string;
  isInstallment: boolean;
  defaultInstallmentCount: number;
  suggestedDownPaymentPercent: number | null;
}

interface PendingSchedulePolicyDto {
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  contractName: string;
  totalPremium: number;
}

interface InstallmentDto {
  seqNo: number;
  dueDate: string;
  amount: number;
}

interface ScheduleResultDto {
  policyId: string;
  exceedsMaxInstallments: boolean;
  installments: InstallmentDto[];
}

const EMPTY_TEMPLATE = { contractNamePattern: "", isInstallment: true, defaultInstallmentCount: 9, suggestedDownPaymentPercent: "" };

export function ContractTemplatesPage() {
  const [templates, setTemplates] = useState<ContractTemplateDto[] | null>(null);
  const [newTemplate, setNewTemplate] = useState(EMPTY_TEMPLATE);
  const [error, setError] = useState<string | null>(null);

  const [pending, setPending] = useState<PendingSchedulePolicyDto[] | null>(null);
  const [rowInputs, setRowInputs] = useState<Record<string, { downPayment: string; count: string }>>({});
  const [rowResults, setRowResults] = useState<Record<string, string>>({});

  function loadTemplates() {
    api
      .get<ContractTemplateDto[]>("/contract-templates")
      .then(setTemplates)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری قراردادها"));
  }

  function loadPending() {
    api
      .get<PendingSchedulePolicyDto[]>("/policies/pending-schedule")
      .then((list) => {
        setPending(list);
        setRowInputs((prev) => {
          const next = { ...prev };
          for (const p of list) {
            next[p.policyId] ??= { downPayment: "0", count: "9" };
          }
          return next;
        });
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
  }

  useEffect(() => {
    loadTemplates();
    loadPending();
  }, []);

  async function addTemplate() {
    setError(null);
    try {
      await api.post("/contract-templates", {
        contractNamePattern: newTemplate.contractNamePattern,
        isInstallment: newTemplate.isInstallment,
        defaultInstallmentCount: Number(newTemplate.defaultInstallmentCount) || 0,
        suggestedDownPaymentPercent: newTemplate.suggestedDownPaymentPercent
          ? Number(newTemplate.suggestedDownPaymentPercent)
          : null,
      });
      setNewTemplate(EMPTY_TEMPLATE);
      loadTemplates();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت قرارداد ناموفق بود.");
    }
  }

  async function suggestDownPayment(policyId: string) {
    const count = Number(rowInputs[policyId]?.count) || 9;
    try {
      const suggestion = await api.get<number>(`/policies/${policyId}/suggest-down-payment?installmentCount=${count}`);
      setRowInputs((prev) => ({ ...prev, [policyId]: { ...prev[policyId], downPayment: String(suggestion) } }));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "پیشنهاد پیش‌پرداخت ناموفق بود.");
    }
  }

  async function scheduleOne(policyId: string) {
    const input = rowInputs[policyId];
    try {
      const result = await api.post<ScheduleResultDto>(`/policies/${policyId}/schedule`, {
        downPayment: Number(input?.downPayment) || 0,
        installmentCount: Number(input?.count) || 0,
      });
      setRowResults((prev) => ({
        ...prev,
        [policyId]: result.exceedsMaxInstallments
          ? `ثبت شد (${fa(result.installments.length)} قسط) — بیش از سقف مجاز`
          : `ثبت شد (${fa(result.installments.length)} قسط)`,
      }));
      loadPending();
    } catch (err) {
      setRowResults((prev) => ({
        ...prev,
        [policyId]: err instanceof ApiError ? err.message : "خطا در ثبت زمان‌بندی",
      }));
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        تنظیم <em className="font-extralight not-italic text-(--ice-2)">قراردادهای اقساطی</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">
        نگاشت نام قرارداد به «اقساطی بودن» — هیچ‌وقت فقط دنبال «اقساطی» نگردید
      </div>

      {error && (
        <div className="mb-4 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <b className="mb-3 block text-[13.5px] text-(--ice)">قراردادهای تعریف‌شده</b>

        {templates === null ? (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        ) : (
          <table className="mb-4 w-full text-[12.5px]">
            <thead>
              <tr className="text-(--ice-3)">
                <th className="pb-2 text-start font-normal">الگوی نام قرارداد</th>
                <th className="pb-2 text-start font-normal">اقساطی</th>
                <th className="pb-2 text-start font-normal">تعداد پیش‌فرض</th>
              </tr>
            </thead>
            <tbody>
              {templates.map((t) => (
                <tr key={t.id} className="border-t border-(--edge)">
                  <td className="py-2 text-(--ice)">{t.contractNamePattern}</td>
                  <td className="py-2 text-(--ice-2)">{t.isInstallment ? "بله" : "خیر"}</td>
                  <td className="py-2 text-(--ice-2)">{fa(t.defaultInstallmentCount)}</td>
                </tr>
              ))}
              {templates.length === 0 && (
                <tr>
                  <td colSpan={3} className="py-4 text-center text-(--ice-3)">
                    هنوز قراردادی تعریف نشده
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}

        <div className="grid grid-cols-4 gap-3">
          <input
            value={newTemplate.contractNamePattern}
            onChange={(e) => setNewTemplate((t) => ({ ...t, contractNamePattern: e.target.value }))}
            placeholder="مثلاً تجارت آفرینان تسنیم"
            className="col-span-2 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            value={newTemplate.defaultInstallmentCount}
            onChange={(e) => setNewTemplate((t) => ({ ...t, defaultInstallmentCount: Number(e.target.value) }))}
            type="number"
            placeholder="تعداد اقساط"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <button
            type="button"
            onClick={addTemplate}
            disabled={!newTemplate.contractNamePattern.trim()}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-3 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            افزودن
          </button>
        </div>
      </div>

      <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <b className="mb-3 block text-[13.5px] text-(--ice)">زمان‌بندی بیمه‌نامه‌های وارد‌شده</b>
        <div className="mb-3.5 text-[11.5px] text-(--ice-3)">
          بیمه‌نامه‌های اقساطیِ ثبت‌شده از ورود اطلاعات که هنوز اقساطشان ساخته نشده
        </div>

        {pending === null ? (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        ) : pending.length === 0 ? (
          <div className="text-[12.5px] text-(--ice-3)">بیمه‌نامهٔ در انتظار زمان‌بندی وجود ندارد</div>
        ) : (
          <div className="space-y-2.5">
            {pending.map((p) => (
              <div key={p.policyId} className="rounded-[10px] border border-(--edge-2) bg-(--fld) p-3">
                <div className="mb-2 flex items-center justify-between text-[12.5px]">
                  <b className="text-(--ice)">{p.policyNumber}</b>
                  <span className="text-(--ice-3)">
                    {p.customerFullName} — {money(p.totalPremium)} تومان
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <input
                    value={rowInputs[p.policyId]?.downPayment ?? "0"}
                    onChange={(e) =>
                      setRowInputs((prev) => ({ ...prev, [p.policyId]: { ...prev[p.policyId], downPayment: e.target.value } }))
                    }
                    type="number"
                    placeholder="پیش‌پرداخت"
                    className="w-32 rounded-[8px] border border-(--edge-2) bg-(--pane) px-2 py-1.5 text-[12px] text-(--ice) outline-none focus:border-(--mint)"
                  />
                  <input
                    value={rowInputs[p.policyId]?.count ?? "9"}
                    onChange={(e) =>
                      setRowInputs((prev) => ({ ...prev, [p.policyId]: { ...prev[p.policyId], count: e.target.value } }))
                    }
                    type="number"
                    placeholder="تعداد اقساط"
                    className="w-24 rounded-[8px] border border-(--edge-2) bg-(--pane) px-2 py-1.5 text-[12px] text-(--ice) outline-none focus:border-(--mint)"
                  />
                  <button
                    type="button"
                    onClick={() => suggestDownPayment(p.policyId)}
                    className="rounded-[8px] border border-(--edge-2) bg-(--btn-bg) px-2.5 py-1.5 text-[11.5px] text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
                  >
                    پیشنهاد پیش‌پرداخت
                  </button>
                  <button
                    type="button"
                    onClick={() => scheduleOne(p.policyId)}
                    className="rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1.5 text-[11.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
                  >
                    ثبت زمان‌بندی
                  </button>
                  {rowResults[p.policyId] && (
                    <span className="text-[11.5px] text-(--ice-3)">{rowResults[p.policyId]}</span>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
