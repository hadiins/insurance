import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

interface PermissionCatalogItemDto {
  key: string;
  label: string;
}

interface RoleDto {
  id: string;
  name: string;
  permissions: string[];
  isSystemRole: boolean;
  memberCount: number;
}

export function RoleManagementPage() {
  const [roles, setRoles] = useState<RoleDto[] | null>(null);
  const [catalog, setCatalog] = useState<PermissionCatalogItemDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [selected, setSelected] = useState<Set<string>>(new Set());

  function reload() {
    api.get<RoleDto[]>("/platform/roles").then(setRoles).catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری نقش‌ها"));
  }

  useEffect(() => {
    reload();
    api.get<PermissionCatalogItemDto[]>("/platform/roles/permissions-catalog").then(setCatalog).catch(() => setCatalog([]));
  }, []);

  function startCreate() {
    setEditingId("new");
    setName("");
    setSelected(new Set());
    setError(null);
  }

  function startEdit(role: RoleDto) {
    setEditingId(role.id);
    setName(role.name);
    setSelected(new Set(role.permissions));
    setError(null);
  }

  function cancel() {
    setEditingId(null);
  }

  function togglePermission(key: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  async function save() {
    if (!name.trim()) {
      setError("نام نقش الزامی است.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const body = { name: name.trim(), permissions: Array.from(selected) };
      if (editingId === "new") {
        await api.post("/platform/roles", body);
      } else {
        await api.put(`/platform/roles/${editingId}`, body);
      }
      setEditingId(null);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیرهٔ نقش ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function remove(role: RoleDto) {
    setError(null);
    try {
      await api.delete(`/platform/roles/${role.id}`);
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "حذف نقش ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">مدیریت نقش‌ها</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        نقش‌ها بین تمام نمایندگی‌ها مشترک‌اند — تغییر اینجا روی هر کاربری که آن نقش را دارد بلافاصله اثر می‌گذارد
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {editingId ? (
        <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">{editingId === "new" ? "نقش جدید" : "ویرایش نقش"}</div>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="نام نقش"
            className="mb-3 w-full max-w-sm rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <div className="mb-3 grid grid-cols-2 gap-2">
            {catalog?.map((p) => (
              <label key={p.key} className="flex items-center gap-2 text-[12.5px] text-(--ice-2)">
                <input type="checkbox" checked={selected.has(p.key)} onChange={() => togglePermission(p.key)} />
                {p.label}
              </label>
            ))}
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={save}
              disabled={busy}
              className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
            >
              ذخیره
            </button>
            <button
              type="button"
              onClick={cancel}
              className="rounded-[10px] border border-(--edge-2) px-4 py-2 text-[12.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
            >
              انصراف
            </button>
          </div>
        </div>
      ) : (
        <button
          type="button"
          onClick={startCreate}
          className="mb-4.5 rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
        >
          نقش جدید
        </button>
      )}

      {roles === null ? (
        <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
      ) : (
        <div className="space-y-2">
          {roles.map((r) => (
            <div key={r.id} className="flex items-center justify-between rounded-2xl border border-(--edge) bg-(--pane) p-4">
              <div>
                <div className="flex items-center gap-2">
                  <b className="text-[13.5px] text-(--ice)">{r.name}</b>
                  {r.isSystemRole && (
                    <span className="rounded-full bg-(--amber)/13 px-2 py-0.5 text-[10.5px] font-semibold text-(--amber)">سامانه‌ای</span>
                  )}
                  <span className="text-[11.5px] text-(--ice-3)">{fa(r.memberCount)} کاربر</span>
                </div>
                <div className="mt-1 text-[11.5px] text-(--ice-3)">
                  {r.permissions.length === 0 ? "بدون مجوز" : r.permissions.map((p) => catalog?.find((c) => c.key === p)?.label ?? p).join(" · ")}
                </div>
              </div>
              {!r.isSystemRole && (
                <div className="flex gap-2">
                  <button
                    type="button"
                    onClick={() => startEdit(r)}
                    className="rounded-[8px] border border-(--edge-2) px-2.5 py-1 text-[11.5px] text-(--ice-2) transition-colors hover:bg-(--hov)"
                  >
                    ویرایش
                  </button>
                  <button
                    type="button"
                    onClick={() => remove(r)}
                    className="rounded-[8px] border border-(--ember) px-2.5 py-1 text-[11.5px] text-(--ember) transition-colors hover:bg-(--ember)/10"
                  >
                    حذف
                  </button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
