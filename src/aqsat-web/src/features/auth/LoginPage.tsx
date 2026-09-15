import { useState } from "react";
import { useAuthStore } from "../../app/store/authStore";
import { APP_VERSION, versionLabel } from "../../app/version";
import { todayJalaliLongDisplay } from "../../lib/jalali";
import { fa } from "../../lib/persian";
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
    <div className="grid h-full grid-rows-[auto_1fr] bg-(--void) md:grid-cols-[1fr_1.15fr] md:grid-rows-1">
      {/* Brand pane — the start (right) column in RTL. Below md it collapses to a compact
          strip: logo and date only, no marketing copy competing with the form. */}
      <aside className="flex flex-col justify-between gap-6 border-b border-(--edge) bg-(--fld) px-6 py-6 md:border-b-0 md:border-s md:px-10 md:py-10">
        <div className="flex items-center justify-between md:block">
          <div>
            <img src="/credix-logo.png" alt="Credix" className="brand-logo-light h-[30px] w-auto" />
            <img src="/credix-logo-light.png" alt="Credix" className="brand-logo-dark h-[30px] w-auto" />
          </div>
          <span className="text-[12px] text-(--ice-3) md:hidden">{todayJalaliLongDisplay()}</span>
        </div>

        <div className="hidden md:block">
          <h1 className="mb-3 text-4xl font-black leading-[1.3] text-(--ice)">دفتر اقساط</h1>
          <p className="max-w-[26ch] text-[14px] leading-7 text-(--ice-2)">
            سرسید اقساط بیمه، یادآوری پیامکی و وصول مطالبات نمایندگی، همه در یک نگاه.
          </p>
        </div>

        <div className="hidden items-center justify-between text-[12px] text-(--ice-3) md:flex">
          <span>{todayJalaliLongDisplay()}</span>
          <span dir="ltr">{fa(APP_VERSION)}</span>
        </div>
      </aside>

      {/* Form pane */}
      <main className="grid place-items-center px-5 py-8">
        <div className="w-full max-w-[380px]">
          <form
            onSubmit={handleSubmit}
            className="rounded-2xl border border-(--edge) bg-(--pane) p-6 shadow-(--sh) md:p-7"
            noValidate
          >
            <h2 className="mb-1 text-[17px] font-bold text-(--ice)">ورود به Credix</h2>
            <p className="mb-5 text-[12.5px] text-(--ice-3)">با شمارهٔ همراه و رمز عبور خود وارد شوید.</p>

            <div className="mb-3.5">
              <label htmlFor="login-mobile" className="mb-1.5 block text-[11.5px] text-(--ice-3)">
                شمارهٔ همراه
              </label>
              <input
                id="login-mobile"
                name="mobile"
                value={mobile}
                onChange={(e) => setMobile(e.target.value)}
                placeholder="09121234567"
                dir="ltr"
                inputMode="numeric"
                autoComplete="username"
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none transition-colors focus:border-(--mint)"
              />
            </div>

            <div className="mb-4">
              <label htmlFor="login-password" className="mb-1.5 block text-[11.5px] text-(--ice-3)">
                رمز عبور
              </label>
              <input
                id="login-password"
                name="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                type="password"
                dir="ltr"
                autoComplete="current-password"
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none transition-colors focus:border-(--mint)"
              />
            </div>

            {error && (
              <div role="alert" className="mb-3.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
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
              className="mt-4 block text-center text-[12px] text-(--ice-3) underline-offset-4 hover:text-(--ice-2) hover:underline"
            >
              ثبت‌نام نمایندگی جدید
            </a>
          </form>

          <p className="mt-4 text-center text-[11.5px] text-(--ice-3) md:hidden">{versionLabel()}</p>
        </div>
      </main>
    </div>
  );
}
