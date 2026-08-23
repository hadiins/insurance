import { useEffect, useState } from "react";
import { api, ApiError, setActiveOrgId, setToken } from "../../lib/api";

interface BootstrapStatusDto {
  available: boolean;
}

interface LoginResponseDto {
  token: string;
}

/// Standalone page, outside the MDI shell — reached only by direct URL (/owner-setup), never
/// linked from the sidebar. Renders "already used" once an owner exists so this can be left
/// reachable without leaking a way to create a second owner account.
export function OwnerSetupPage() {
  const [status, setStatus] = useState<BootstrapStatusDto | null>(null);
  const [secret, setSecret] = useState("");
  const [fullName, setFullName] = useState("");
  const [mobile, setMobile] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  useEffect(() => {
    api
      .get<BootstrapStatusDto>("/platform/bootstrap-owner/status")
      .then(setStatus)
      .catch(() => setStatus({ available: false }));
  }, []);

  async function submit() {
    if (!secret.trim() || !fullName.trim() || !mobile.trim() || !password) {
      setError("همهٔ فیلدها الزامی است.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const response = await api.post<LoginResponseDto>("/platform/bootstrap-owner", {
        secret: secret.trim(),
        fullName: fullName.trim(),
        mobile: mobile.trim(),
        password,
      });
      setToken(response.token);
      setActiveOrgId(null);
      setDone(true);
      window.location.href = "/";
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "راه‌اندازی ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  if (status === null) {
    return <div className="grid h-full place-items-center bg-(--void) text-(--ice-3)">در حال بررسی…</div>;
  }

  if (!status.available && !done) {
    return (
      <div className="grid h-full place-items-center bg-(--void) px-6 text-center">
        <div>
          <b className="mb-1 block text-base font-bold text-(--ice-2)">این صفحه غیرفعال است</b>
          <span className="text-[13px] text-(--ice-3)">
            یا حساب مالک از قبل ساخته شده، یا کد راه‌اندازی روی سرور پیکربندی نشده است.
          </span>
        </div>
      </div>
    );
  }

  return (
    <div className="grid h-full place-items-center bg-(--void) px-6">
      <div className="w-full max-w-sm rounded-2xl border border-(--edge) bg-(--pane) p-6">
        <h1 className="mb-1 text-lg font-extrabold text-(--ice)">راه‌اندازی حساب مالک</h1>
        <div className="mb-4.5 text-[12px] text-(--ice-3)">این فرم فقط یک‌بار قابل استفاده است</div>

        {error && (
          <div className="mb-3.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
            {error}
          </div>
        )}

        <div className="space-y-2.5">
          <input
            value={secret}
            onChange={(e) => setSecret(e.target.value)}
            type="password"
            placeholder="کد راه‌اندازی"
            className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            placeholder="نام کامل"
            className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            value={mobile}
            onChange={(e) => setMobile(e.target.value)}
            placeholder="شمارهٔ همراه"
            className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <input
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            type="password"
            placeholder="رمز عبور"
            className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
          />
        </div>

        <button
          type="button"
          onClick={submit}
          disabled={busy}
          className="mt-4 w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال ساخت…" : "ساخت حساب مالک"}
        </button>
      </div>
    </div>
  );
}
