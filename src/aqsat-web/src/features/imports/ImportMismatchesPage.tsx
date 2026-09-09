import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

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
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
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
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(rows.length)} ردیف</div>
          {rows.length === 0 ? (
            <EmptyState
              icon="✅"
              title="رکورد ناسازگاری نیست."
              description="همهٔ ردیف‌های این ورود اطلاعاتی با موفقیت وارد شده‌اند."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["فایل", "شمارهٔ ردیف", "علت"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <Tr key={r.id}>
                    <Td className="py-2.5 font-semibold">{r.fileName}</Td>
                    <Td className="py-2.5">{fa(r.rowNumber)}</Td>
                    <Td className="py-2.5 text-(--ember)">{r.errorMessage ?? "—"}</Td>
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
