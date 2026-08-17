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
          <div className="grid h-[34px] w-[34px] flex-none place-items-center rounded-[10px] bg-linear-to-br from-(--mint) to-(--mint-dim) text-[15px] font-black text-(--on-mint) shadow-(--gl-mint)">
            ق
          </div>
          <b className="text-[15px] font-bold text-(--ice)">ورود به دفتر اقساط</b>
        </div>

        <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">شمارهٔ همراه</label>
        <input
          value={mobile}
          onChange={(e) => setMobile(e.target.value)}
          placeholder="۰۹۱۲۱۲۳۴۵۶۷"
          dir="ltr"
          autoComplete="username"
          className="mb-3.5 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />

        <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">رمز عبور</label>
        <input
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          type="password"
          dir="ltr"
          autoComplete="current-password"
          className="mb-4 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />

        {error && (
          <div className="mb-3.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12px] text-(--ember)">
            {error}
          </div>
        )}

        <button
          type="submit"
          disabled={submitting || !mobile.trim() || !password}
          className="w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13px] font-semibold text-(--on-mint) shadow-(--gl-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting ? "در حال ورود…" : "ورود"}
        </button>
      </form>
    </div>
  );
}
