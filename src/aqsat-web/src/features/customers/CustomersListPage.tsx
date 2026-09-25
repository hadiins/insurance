import { useEffect, useState } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import { useTabsStore } from "../../app/store/tabsStore";
import { useDraftState } from "../shell/useDraftState";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import { TrashSimpleIcon, UsersIcon } from "@phosphor-icons/react";

interface CustomerListItemDto {
  id: string;
  fullName: string;
  mobile: string | null;
  policyCount: number;
  canDelete: boolean;
}

export function CustomersListPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [search, setSearch] = useDraftState<string>("search", "");
  const [customers, setCustomers] = useState<CustomerListItemDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<CustomerListItemDto | null>(null);
  const [deleting, setDeleting] = useState(false);

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

  async function confirmDelete() {
    if (!pendingDelete || deleting) return;
    setDeleting(true);
    setError(null);
    try {
      await api.delete(`/customers/${pendingDelete.id}`);
      const deletedId = pendingDelete.id;
      setCustomers((prev) => prev?.filter((c) => c.id !== deletedId) ?? prev);
      setPendingDelete(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "حذف مشتری ناموفق بود.");
    } finally {
      setDeleting(false);
    }
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
        className="mb-4.5 w-full max-w-sm rounded-(--r) border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
      />

      {error && (
        <div className="mb-4.5 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && customers === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && customers !== null && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(customers.length)} مشتری</div>
          {customers.length === 0 ? (
            <EmptyState
              icon={UsersIcon}
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
                    <Td className="py-2.75">
                      {c.canDelete ? (
                        <button
                          type="button"
                          title="حذف فیزیکی؛ فقط برای مشتری کاملاً بدون سابقه"
                          onClick={(e) => {
                            e.stopPropagation();
                            setPendingDelete(c);
                          }}
                          className="inline-flex items-center gap-1 rounded-(--r) px-2 py-1 text-[11.5px] font-semibold text-(--ember) hover:bg-(--ember)/10"
                        >
                          <TrashSimpleIcon size={14} /> حذف
                        </button>
                      ) : (
                        <span className="text-[11px] text-(--ice-3)">—</span>
                      )}
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </>
      )}

      <Dialog.Root open={pendingDelete !== null} onOpenChange={(open) => !open && !deleting && setPendingDelete(null)}>
        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-[60] bg-black/55" />
          <Dialog.Content className="fixed inset-0 z-[60] grid place-items-center p-5">
            <div className="w-full max-w-[410px] rounded-(--r-lg) border border-(--edge-2) bg-(--slate) p-5.5 shadow-[var(--sh)]">
              <Dialog.Title className="mb-1.5 text-[15px] font-bold text-(--ice)">حذف فیزیکی مشتری</Dialog.Title>
              <Dialog.Description className="text-[13px] leading-relaxed text-(--ice-2)">
                آیا از حذف دائمی «{pendingDelete?.fullName}» مطمئن هستید؟ این عملیات برگشت‌پذیر نیست و فقط چون مشتری هنوز هیچ سابقه‌ای ندارد مجاز است.
              </Dialog.Description>
              {error && <div className="mt-3 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12px] text-(--ember)">{error}</div>}
              <div className="mt-5 flex justify-end gap-2">
                <button type="button" onClick={() => setPendingDelete(null)} disabled={deleting} className="rounded-(--r) border border-(--edge-2) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) disabled:opacity-50">انصراف</button>
                <button type="button" onClick={() => void confirmDelete()} disabled={deleting} className="rounded-(--r) border border-(--ember) bg-(--ember) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) disabled:opacity-50">
                  {deleting ? "در حال حذف…" : "حذف دائمی"}
                </button>
              </div>
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </div>
  );
}
