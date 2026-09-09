import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useDraftState } from "../shell/useDraftState";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

interface CustomerListItemDto {
  id: string;
  fullName: string;
  mobile: string | null;
  policyCount: number;
}

export function CustomersListPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [search, setSearch] = useDraftState<string>("search", "");
  const [customers, setCustomers] = useState<CustomerListItemDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = () => {
    const query = search.trim() ? `?search=${encodeURIComponent(search.trim())}` : "";
    api
      .get<CustomerListItemDto[]>(`/customers${query}`)
      .then((data) => {
        setCustomers(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست مشتریان"));
  };

  useEffect(() => {
    const handle = setTimeout(reload, 250);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  useLiveReload(reload);

  function openCustomer(customer: CustomerListItemDto) {
    openTab({
      navType: "customer-file",
      page: "customer-file",
      kind: "multi-record",
      recordId: customer.id,
      title: customer.fullName,
      payload: { customerId: customer.id },
    });
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        فهرست <em className="font-extralight not-italic text-(--ice-2)">مشتریان</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">جست‌وجو بر اساس نام، کد ملی، شمارهٔ همراه یا پلاک خودرو</div>

      <input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="نام، کد ملی، موبایل، پلاک…"
        className="mb-4.5 w-full max-w-sm rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
      />

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && customers === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && customers !== null && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(customers.length)} مشتری</div>
          {customers.length === 0 ? (
            <EmptyState
              icon="👥"
              title="مشتری‌ای یافت نشد."
              description={
                search.trim()
                  ? "هیچ مشتری‌ای با این عبارت مطابقت ندارد؛ جستجو را تغییر دهید یا پاک کنید."
                  : "هنوز مشتری‌ای در دفتر شما ثبت نشده است."
              }
              action={search.trim() ? { label: "پاک کردن جستجو", onClick: () => setSearch("") } : undefined}
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["نام", "شمارهٔ همراه", "تعداد بیمه‌نامه", ""].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {customers.map((c) => (
                  <Tr key={c.id} onClick={() => openCustomer(c)}>
                    <Td className="py-2.75 font-semibold">{c.fullName}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{c.mobile ? fa(c.mobile) : "—"}</Td>
                    <Td className="py-2.75">{fa(c.policyCount)}</Td>
                    <Td />
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
