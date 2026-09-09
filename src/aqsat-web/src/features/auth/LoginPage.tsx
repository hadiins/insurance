import { useState } from "react";
import { useAuthStore } from "../../app/store/authStore";
import { ApiError } from "../../lib/api";

export function LoginPage() {
  const login = useAuthStore((s) => s.login);
  const [mobile, setMobile] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(mobile.trim(), password);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ورود ناموفق بود.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="grid h-full place-items-center bg-(--void)">
      <form
        onSubmit={handleSubmit}
        className="w-[340px] rounded-2xl border border-(--edge) bg-(--pane) p-6 shadow-(--sh)"
      >
        <div className="mb-5 flex items-center gap-3">
          <img src="/credix-logo.png" alt="Credix" className="brand-logo-light h-[28px] w-auto" />
          <img src="/credix-logo-light.png" alt="Credix" className="brand-logo-dark h-[28px] w-auto" />
          <b className="text-[15px] font-bold text-(--ice)">ورود به Credix</b>
        </div>

        <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">شمارهٔ همراه</label>
        <input
          value={mobile}
          onChange={(e) => setMobile(e.target.value)}
          placeholder="۰۹۱۲۱۲۳۴۵۶۷"
          dir="ltr"
          autoComplete="username"
          className="mb-3.5 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />

        <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">رمز عبور</label>
        <input
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          type="password"
          dir="ltr"
          autoComplete="current-password"
          className="mb-4 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />

        {error && (
          <div className="mb-3.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
            {error}
          </div>
        )}

        <button
          type="submit"
          disabled={submitting || !mobile.trim() || !password}
          className="w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13.5px] font-semibold text-(--on-mint) shadow-(--gl-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting ? "در حال ورود…" : "ورود"}
        </button>

        <a
          href="/signup"
          className="mt-3 block text-center text-[12px] text-(--ice-3) underline-offset-4 hover:text-(--ice-2) hover:underline"
        >
          ثبت‌نام نمایندگی جدید
        </a>
      </form>
    </div>
  );
}
