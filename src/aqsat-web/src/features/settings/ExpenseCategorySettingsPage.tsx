import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { StatusBadge } from "../../components/StatusBadge";
import { EmptyState } from "../../components/EmptyState";

interface ExpenseCategoryDto {
  id: string;
  name: string;
  isActive: boolean;
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)";

/** «دسته‌بندی هزینه‌ها» — agency-definable, never a fixed system list. */
export function ExpenseCategorySettingsPage() {
  const [categories, setCategories] = useState<ExpenseCategoryDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [newName, setNewName] = useState("");

  function reload() {
    api
      .get<ExpenseCategoryDto[]>("/settings/expense-categories")
      .then(setCategories)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری دسته‌ها"));
  }

  useEffect(reload, []);

  async function addCategory() {
    if (!newName.trim()) return;
    setError(null);
    try {
      await api.post("/settings/expense-categories", { name: newName.trim() });
      setNewName("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "افزودن دسته ناموفق بود.");
    }
  }

  async function toggleActive(c: ExpenseCategoryDto) {
    setError(null);
    try {
      await api.put(`/settings/expense-categories/${c.id}`, { name: c.name, isActive: !c.isActive });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی ناموفق بود.");
    }
  }

  async function removeCategory(id: string) {
    setError(null);
    try {
      await api.delete(`/settings/expense-categories/${id}`);
      setCategories((prev) => prev?.filter((c) => c.id !== id) ?? prev);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "حذف ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-4.5 text-xl font-extrabold tracking-tight text-(--ice)">
        دسته‌بندی <em className="font-extralight not-italic text-(--ice-2)">هزینه‌ها</em>
      </h2>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        {categories !== null && categories.length === 0 ? (
          <EmptyState
            icon="🗂"
            title="هنوز دسته‌ای ثبت نشده است"
            description="دسته‌بندی هزینه‌ها را نمایندگی خودش تعریف می‌کند؛ اولین دسته را از فرم پایین اضافه کنید."
          />
        ) : (
          <table className="w-full border-collapse">
            <thead>
              <tr>
                {["نام", "وضعیت", ""].map((h) => (
                  <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {categories?.map((c) => (
                <tr key={c.id} className="border-t border-(--edge) first:border-t-0">
                  <td className="px-3 py-2.5 text-[13.5px] font-semibold">{c.name}</td>
                  <td className="px-3 py-2.5 text-[13.5px]">
                    <StatusBadge tone={c.isActive ? "mint" : "neutral"}>{c.isActive ? "فعال" : "غیرفعال"}</StatusBadge>
                  </td>
                  <td className="px-3 py-2.5 text-[13.5px]">
                    <button type="button" onClick={() => toggleActive(c)} className="ms-2 text-[11.5px] text-(--ice-3) hover:text-(--ice)">
                      {c.isActive ? "غیرفعال کردن" : "فعال کردن"}
                    </button>
                    <button type="button" onClick={() => removeCategory(c.id)} className="ms-2 text-[11.5px] text-(--ember) hover:brightness-110">
                      حذف
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <div className="flex items-end gap-2 border-t border-(--edge) p-3">
          <div className="flex-1">
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">نام دسته</label>
            <input value={newName} onChange={(e) => setNewName(e.target.value)} className={inputClass} />
          </div>
          <button
            type="button"
            onClick={addCategory}
            disabled={!newName.trim()}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            + افزودن
          </button>
        </div>
      </div>
    </div>
  );
}
