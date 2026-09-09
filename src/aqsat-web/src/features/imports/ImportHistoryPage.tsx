import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

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
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">هر ورود اطلاعاتی که تاکنون انجام شده، جدیدترین در بالا</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && batches === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && batches !== null && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(batches.length)} ورود</div>
          {batches.length === 0 ? (
            <EmptyState
              icon="🗂️"
              title="هنوز ورود اطلاعاتی ثبت نشده."
              description="پس از اولین بارگذاری فایل، تاریخچهٔ آن در این‌جا نمایش داده می‌شود."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["نام فایل", "جدید", "تکراری", "ناموفق", ""].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {batches.map((b) => (
                  <Tr key={b.id}>
                    <Td className="py-2.5 font-semibold">{b.fileName}</Td>
                    <Td className="py-2.5 text-(--mint)">{fa(b.newCount)}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{fa(b.duplicateCount)}</Td>
                    <Td className="py-2.5 text-(--ember)">{fa(b.failedCount)}</Td>
                    <Td className="py-2.5">
                      {b.failedCount > 0 && (
                        <button
                          type="button"
                          onClick={() => openMismatches(b)}
                          className="rounded-[8px] border border-(--edge-2) px-2 py-1 text-[10.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
                        >
                          مشاهدهٔ ناسازگارها
                        </button>
                      )}
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
