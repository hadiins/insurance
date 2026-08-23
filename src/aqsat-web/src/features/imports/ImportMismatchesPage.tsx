import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface ImportMismatchRowDto {
  id: string;
  importBatchId: string;
  fileName: string;
  rowNumber: number;
  errorMessage: string | null;
}

export function ImportMismatchesPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const batchId = (tab?.payload as { batchId?: string } | undefined)?.batchId;

  const [rows, setRows] = useState<ImportMismatchRowDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const params = batchId ? `?batchId=${batchId}` : "";
    api
      .get<ImportMismatchRowDto[]>(`/imports/mismatches${params}`)
      .then(setRows)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
  }, [batchId]);

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">رکوردهای ناسازگار</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">
        {batchId ? "ردیف‌های ناموفق همین ورود اطلاعاتی" : "ردیف‌های ناموفق در همهٔ ورودهای اطلاعاتی"}
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && rows === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && rows !== null && (
        <>
          <div className="mb-2 text-[11px] text-(--ice-3)">{fa(rows.length)} ردیف</div>
          {rows.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">رکورد ناسازگاری نیست.</div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    {["فایل", "شمارهٔ ردیف", "علت"].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.id} className="border-t border-(--edge) first:border-t-0">
                      <td className="px-3 py-2.5 text-[13px] font-semibold">{r.fileName}</td>
                      <td className="px-3 py-2.5 text-[13px]">{fa(r.rowNumber)}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ember)">{r.errorMessage ?? "—"}</td>
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
