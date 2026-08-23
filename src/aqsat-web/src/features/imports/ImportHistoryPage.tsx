import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface ImportBatchDto {
  id: string;
  fileName: string;
  newCount: number;
  duplicateCount: number;
  failedCount: number;
}

export function ImportHistoryPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [batches, setBatches] = useState<ImportBatchDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<ImportBatchDto[]>("/imports/history")
      .then(setBatches)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری تاریخچه"));
  }, []);

  function openMismatches(batch: ImportBatchDto) {
    openTab({
      navType: "import-mismatches",
      page: "import-mismatches",
      kind: "singleton",
      title: "رکوردهای ناسازگار",
      payload: { batchId: batch.id },
    });
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">تاریخچهٔ ورود داده</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">هر ورود اطلاعاتی که تاکنون انجام شده، جدیدترین در بالا</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && batches === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && batches !== null && (
        <>
          <div className="mb-2 text-[11px] text-(--ice-3)">{fa(batches.length)} ورود</div>
          {batches.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">هنوز ورود اطلاعاتی ثبت نشده.</div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    {["نام فایل", "جدید", "تکراری", "ناموفق", ""].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {batches.map((b) => (
                    <tr key={b.id} className="border-t border-(--edge) first:border-t-0">
                      <td className="px-3 py-2.5 text-[13px] font-semibold">{b.fileName}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--mint)">{fa(b.newCount)}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{fa(b.duplicateCount)}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ember)">{fa(b.failedCount)}</td>
                      <td className="px-3 py-2.5 text-[13px]">
                        {b.failedCount > 0 && (
                          <button
                            type="button"
                            onClick={() => openMismatches(b)}
                            className="rounded-[8px] border border-(--edge-2) px-2 py-1 text-[10.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
                          >
                            مشاهدهٔ ناسازگارها
                          </button>
                        )}
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
