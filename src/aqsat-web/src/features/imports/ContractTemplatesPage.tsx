import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface ContractTemplateDto {
  id: string;
  contractNamePattern: string;
  isInstallment: boolean;
  defaultInstallmentCount: number;
  suggestedDownPaymentPercent: number | null;
}

const EMPTY_TEMPLATE = { contractNamePattern: "", isInstallment: true, defaultInstallmentCount: 9, suggestedDownPaymentPercent: "" };

export function ContractTemplatesPage() {
  const [templates, setTemplates] = useState<ContractTemplateDto[] | null>(null);
  const [newTemplate, setNewTemplate] = useState(EMPTY_TEMPLATE);
  const [error, setError] = useState<string | null>(null);

  function loadTemplates() {
    api
      .get<ContractTemplateDto[]>("/contract-templates")
      .then(setTemplates)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری قراردادها"));
  }

  useEffect(() => {
    loadTemplates();
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

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        تنظیم <em className="font-extralight not-italic text-(--ice-2)">قراردادهای اقساطی</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
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
            className="col-span-2 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            value={newTemplate.defaultInstallmentCount}
            onChange={(e) => setNewTemplate((t) => ({ ...t, defaultInstallmentCount: Number(e.target.value) }))}
            type="number"
            placeholder="تعداد اقساط"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
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
    </div>
  );
}
