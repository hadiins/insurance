import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

interface OrgUserDto {
  membershipId: string;
  userId: string;
  fullName: string;
  mobile: string;
  roleId: string;
  roleName: string;
  isActive: boolean;
}

interface RoleOptionDto {
  id: string;
  name: string;
}

export function UsersPage() {
  const [users, setUsers] = useState<OrgUserDto[] | null>(null);
  const [roles, setRoles] = useState<RoleOptionDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [fullName, setFullName] = useState("");
  const [mobile, setMobile] = useState("");
  const [password, setPassword] = useState("");
  const [roleId, setRoleId] = useState("");

  function reload() {
    api.get<OrgUserDto[]>("/settings/users").then(setUsers).catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری کاربران"));
  }

  useEffect(() => {
    reload();
    api.get<RoleOptionDto[]>("/settings/roles").then((r) => {
      setRoles(r);
      if (r.length > 0) setRoleId(r[0].id);
    }).catch(() => setRoles([]));
  }, []);

  async function create() {
    if (!fullName.trim() || !mobile.trim() || !password || !roleId) {
      setError("نام، شمارهٔ همراه، رمز عبور و نقش الزامی است.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.post("/settings/users", { fullName: fullName.trim(), mobile: mobile.trim(), password, roleId });
      setFullName("");
      setMobile("");
      setPassword("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "افزودن کاربر ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function deactivate(membershipId: string) {
    setError(null);
    try {
      await api.put(`/settings/users/${membershipId}/deactivate`, {});
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "غیرفعال‌سازی ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        کاربران <em className="font-extralight not-italic text-(--ice-2)">و دسترسی‌ها</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">کاربرانی که به این نمایندگی دسترسی دارند</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">افزودن کاربر</div>
        <div className="mb-3 grid grid-cols-4 gap-3">
          <input
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            placeholder="نام کامل"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            value={mobile}
            onChange={(e) => setMobile(e.target.value)}
            placeholder="شمارهٔ همراه"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            type="password"
            placeholder="رمز عبور"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <select
            value={roleId}
            onChange={(e) => setRoleId(e.target.value)}
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice)"
          >
            {roles?.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
          </select>
        </div>
        <button
          type="button"
          onClick={create}
          disabled={busy}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال ثبت…" : "افزودن"}
        </button>
      </div>

      {users === null ? (
        <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
      ) : users.length === 0 ? (
        <EmptyState
          icon="👥"
          title="کاربری ثبت نشده"
          description="هنوز کاربری به این نمایندگی دسترسی ندارد؛ اولین کاربر را از فرم بالا اضافه کنید."
        />
      ) : (
        <Table>
          <thead>
            <tr>
              {["نام", "شمارهٔ همراه", "نقش", ""].map((h) => (
                <Th key={h}>{h}</Th>
              ))}
            </tr>
          </thead>
          <tbody>
            {users.map((u) => (
              <Tr key={u.membershipId}>
                <Td className="py-2.5 font-semibold">{u.fullName}</Td>
                <Td className="py-2.5 text-(--ice-3)">{fa(u.mobile)}</Td>
                <Td className="py-2.5 text-(--ice-3)">{u.roleName}</Td>
                <Td className="py-2.5">
                  <button
                    type="button"
                    onClick={() => deactivate(u.membershipId)}
                    className="rounded-[8px] border border-(--ember) px-2 py-1 text-[10.5px] text-(--ember) transition-colors hover:bg-(--ember)/10"
                  >
                    حذف دسترسی
                  </button>
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
    </div>
  );
}
