import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface CustomerIncompleteRowDto {
  id: string;
  fullName: string;
  firstName: string | null;
  lastName: string | null;
  hasNationalId: boolean;
  mobile: string | null;
  emergencyMobile: string | null;
  address: string | null;
  postalCode: string | null;
  isProfileComplete: boolean;
}

type Filter = "all" | "no-mobile" | "no-national-id" | "no-address" | "no-postal-code" | "no-name";
const PAGE_SIZE = 50;

const FILTER_OPTIONS: { value: Filter; label: string }[] = [
  { value: "all", label: "همه" },
  { value: "no-mobile", label: "بدون موبایل" },
  { value: "no-national-id", label: "بدون کد ملی" },
  { value: "no-address", label: "بدون آدرس" },
  { value: "no-postal-code", label: "بدون کد پستی" },
  { value: "no-name", label: "بدون نام/نام خانوادگی" },
];

function isDigitsOnly(v: string): boolean {
  const s = v.trim().replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d)));
  return s.length > 0 && /^\d+$/.test(s);
}

/** docs/TASK-25-IDENTITY-VEHICLE.md §3 — "شبیه اکسل، نه ۱۳۷ فرم": one grid, editable cells,
 * auto-save per row on blur, no per-customer modal. */
export function CustomerCompletionPage() {
  const [filter, setFilter] = useState<Filter>("all");
  const [page, setPage] = useState(1);
  const [rows, setRows] = useState<CustomerIncompleteRowDto[] | null>(null);
  const [edits, setEdits] = useState<Record<string, Partial<Record<string, string>>>>({});
  const [savingRow, setSavingRow] = useState<string | null>(null);
  const [rowErrors, setRowErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  function reload() {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
    if (filter !== "all") params.set("filter", filter);
    api
      .get<CustomerIncompleteRowDto[]>(`/customers/incomplete?${params.toString()}`)
      .then((data) => {
        setRows(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
  }

  useEffect(reload, [filter, page]);

  function fieldValue(row: CustomerIncompleteRowDto, field: string, current: string | null | undefined): string {
    return edits[row.id]?.[field] ?? current ?? "";
  }

  function setField(rowId: string, field: string, value: string) {
    setEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], [field]: value } }));
  }

  async function saveRow(row: CustomerIncompleteRowDto) {
    const rowEdits = edits[row.id];
    if (!rowEdits || Object.keys(rowEdits).length === 0) return;

    if ((rowEdits.firstName !== undefined && isDigitsOnly(rowEdits.firstName)) ||
        (rowEdits.lastName !== undefined && isDigitsOnly(rowEdits.lastName))) {
      setRowErrors((prev) => ({
        ...prev,
        [row.id]: "نام/نام خانوادگی نمی‌تواند عدد باشد — کد ملی در فیلد جداگانهٔ خودش وارد می‌شود.",
      }));
      return;
    }

    setSavingRow(row.id);
    setRowErrors((prev) => ({ ...prev, [row.id]: "" }));
    try {
      const updated = await api.put<CustomerIncompleteRowDto>(`/customers/${row.id}/complete-profile`, {
        firstName: rowEdits.firstName ?? null,
        lastName: rowEdits.lastName ?? null,
        nationalId: rowEdits.nationalId ?? null,
        mobile: rowEdits.mobile ?? null,
        emergencyMobile: rowEdits.emergencyMobile ?? null,
        address: rowEdits.address ?? null,
        postalCode: rowEdits.postalCode ?? null,
      });
      setRows((prev) => prev?.map((r) => (r.id === row.id ? updated : r)) ?? prev);
      setEdits((prev) => {
        const next = { ...prev };
        delete next[row.id];
        return next;
      });
      if (updated.isProfileComplete) {
        // Fully completed rows drop out of the "ناقص" filter — remove locally rather than
        // waiting for a full reload, so the grid reflects progress immediately.
        setRows((prev) => prev?.filter((r) => r.id !== row.id) ?? prev);
      }
    } catch (err) {
      setRowErrors((prev) => ({ ...prev, [row.id]: err instanceof ApiError ? err.message : "ذخیره ناموفق بود." }));
    } finally {
      setSavingRow(null);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        تکمیل <em className="font-extralight not-italic text-(--ice-2)">پروندهٔ مشتریان</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">حرکت با Tab · ذخیرهٔ خودکار هر ردیف با خروج از سطر</div>

      <div className="mb-4 flex items-center gap-2">
        <label className="text-[11.5px] tracking-wider text-(--ice-3)">فیلتر</label>
        <select
          value={filter}
          onChange={(e) => {
            setFilter(e.target.value as Filter);
            setPage(1);
          }}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
        >
          {FILTER_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && rows === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && rows !== null && (
        <>
          {rows.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13.5px] text-(--ice-3)">
              {page === 1 ? "همهٔ پرونده‌ها کامل است." : "این صفحه رکوردی ندارد."}
            </div>
          ) : (
            <div className="overflow-x-auto rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full min-w-[900px] border-collapse">
                <thead>
                  <tr>
                    {["نام کامل", "نام", "نام خانوادگی", "کد ملی", "موبایل", "موبایل اضطراری", "آدرس", "کد پستی", ""].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-2.5 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.id} className="border-t border-(--edge) first:border-t-0">
                      <td className="px-1 py-1.5 text-[12.5px] text-(--ice-3)">{row.fullName}</td>
                      <Cell value={fieldValue(row, "firstName", row.firstName)} onChange={(v) => setField(row.id, "firstName", v)} onBlur={() => saveRow(row)} />
                      <Cell value={fieldValue(row, "lastName", row.lastName)} onChange={(v) => setField(row.id, "lastName", v)} onBlur={() => saveRow(row)} />
                      <Cell
                        value={fieldValue(row, "nationalId", null)}
                        placeholder={row.hasNationalId ? "دارد" : "۰۰۷۲۳۴۵۴۵۴"}
                        onChange={(v) => setField(row.id, "nationalId", v)}
                        onBlur={() => saveRow(row)}
                        dir="ltr"
                      />
                      <Cell value={fieldValue(row, "mobile", row.mobile)} placeholder="۰۹۱۲۳۴۵۶۷۸۹" onChange={(v) => setField(row.id, "mobile", v)} onBlur={() => saveRow(row)} dir="ltr" />
                      <Cell
                        value={fieldValue(row, "emergencyMobile", row.emergencyMobile)}
                        placeholder="اختیاری"
                        onChange={(v) => setField(row.id, "emergencyMobile", v)}
                        onBlur={() => saveRow(row)}
                        dir="ltr"
                      />
                      <Cell value={fieldValue(row, "address", row.address)} onChange={(v) => setField(row.id, "address", v)} onBlur={() => saveRow(row)} />
                      <Cell
                        value={fieldValue(row, "postalCode", row.postalCode)}
                        placeholder="۱۲۳۴۵۶۷۸۹۰"
                        onChange={(v) => setField(row.id, "postalCode", v)}
                        onBlur={() => saveRow(row)}
                        dir="ltr"
                      />
                      <td className="px-2 py-1.5 text-[11.5px] text-(--ice-3)">
                        {savingRow === row.id ? "در حال ذخیره…" : rowErrors[row.id] ? <span className="text-(--ember)">{rowErrors[row.id]}</span> : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="mt-3 flex items-center gap-2">
            <button
              type="button"
              disabled={page === 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="rounded-[8px] border border-(--edge-2) bg-(--btn-bg) px-3 py-1.5 text-[11.5px] text-(--ice-2) transition-colors hover:bg-(--btn-hov) disabled:cursor-not-allowed disabled:opacity-40"
            >
              قبلی
            </button>
            <span className="text-[11.5px] text-(--ice-3)">صفحهٔ {fa(page)}</span>
            <button
              type="button"
              disabled={rows.length < PAGE_SIZE}
              onClick={() => setPage((p) => p + 1)}
              className="rounded-[8px] border border-(--edge-2) bg-(--btn-bg) px-3 py-1.5 text-[11.5px] text-(--ice-2) transition-colors hover:bg-(--btn-hov) disabled:cursor-not-allowed disabled:opacity-40"
            >
              بعدی
            </button>
          </div>
        </>
      )}
    </div>
  );
}

function Cell({
  value,
  onChange,
  onBlur,
  placeholder,
  dir,
}: {
  value: string;
  onChange: (v: string) => void;
  onBlur: () => void;
  placeholder?: string;
  dir?: "ltr" | "rtl";
}) {
  return (
    <td className="px-1 py-1">
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
        placeholder={placeholder}
        dir={dir}
        className="w-full min-w-[110px] rounded-[8px] border border-(--edge-2) bg-(--fld) px-2 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
      />
    </td>
  );
}
