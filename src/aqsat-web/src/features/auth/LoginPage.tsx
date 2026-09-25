import { useState } from "react";
import {
  AlarmIcon,
  ArrowLeftIcon,
  BellRingingIcon,
  EyeIcon,
  EyeSlashIcon,
  LockIcon,
  PhoneIcon,
  SignInIcon,
  SpinnerGapIcon,
  TrendUpIcon,
  UserPlusIcon,
  WarningOctagonIcon,
} from "@phosphor-icons/react";
import { useAuthStore } from "../../app/store/authStore";
import { versionLabel } from "../../app/version";
import { todayJalaliLongDisplay } from "../../lib/jalali";
import { ApiError } from "../../lib/api";

/* Decorative demo data for the hero card — shows the product at a glance:
 * three installments in the three states the agent lives in (settled /
 * in-window countdown / overdue). Persian digits are display literals. */
type Tone = "moss" | "mint" | "ember";

const DEMO_INSTALLMENTS: { policy: string; label: string; amount: string; tone: Tone; chip: string }[] = [
  { policy: "۷۴ب۳۲۱", label: "قسط ۳ از ۶", amount: "۴٬۲۵۰٬۰۰۰", tone: "moss", chip: "تسویه شد" },
  { policy: "۸۳ک۱۸۴", label: "قسط ۱ از ۴", amount: "۹٬۸۰۰٬۰۰۰", tone: "mint", chip: "۲ روز مانده" },
  { policy: "۶۲د۴۵۹", label: "قسط ۲ از ۵", amount: "۶٬۴۰۰٬۰۰۰", tone: "ember", chip: "۱ روز معوق" },
];

const CHIP_STYLE: Record<Tone, string> = {
  moss: "border-(--moss)/25 bg-(--moss)/12 text-(--moss)",
  mint: "border-(--mint)/25 bg-(--mint)/12 text-(--mint)",
  ember: "border-(--ember)/25 bg-(--ember)/12 text-(--ember)",
};

const FEATURES: { icon: typeof BellRingingIcon; title: string; sub: string }[] = [
  { icon: BellRingingIcon, title: "یادآوری پیامکی هوشمند", sub: "به مشتری و بازاریاب، فقط وقتی که لازم است" },
  { icon: AlarmIcon, title: "شمارش معکوس مهلت تسویه", sub: "پنجرهٔ ۳ روزهٔ هر قسط، همیشه جلوی چشم" },
  { icon: TrendUpIcon, title: "سود و زیان لحظه‌ای", sub: "کمیسیون‌ها، وصولی‌ها و نکول در یک نگاه" },
];

export function LoginPage() {
  const login = useAuthStore((s) => s.login);
  const [mobile, setMobile] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
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
    <div className="grid h-full grid-rows-[auto_1fr] bg-(--void) md:grid-cols-[1.15fr_1fr] md:grid-rows-1">
      {/* ── Brand pane — the start (right) column in RTL ─────────────────────
          A layered scene: accent-tinted gradient base, two soft glows and a
          fading dot grid, all token-mixed so every palette x mode renders its
          own take. Below md it collapses to a compact logo/date strip. */}
      <aside
        className="relative flex flex-col justify-between gap-6 overflow-hidden border-b border-(--edge) px-6 py-6 md:border-b-0 md:border-s md:px-10 md:py-9"
        style={{
          background:
            "linear-gradient(160deg, color-mix(in oklch, var(--mint) 9%, var(--void)), var(--void) 42%, color-mix(in oklch, var(--mint-dim) 10%, var(--void)))",
        }}
      >
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0"
          style={{
            background:
              "radial-gradient(480px 340px at 82% 6%, color-mix(in oklch, var(--mint) 20%, transparent), transparent 70%)," +
              " radial-gradient(560px 420px at 8% 96%, color-mix(in oklch, var(--mint-dim) 16%, transparent), transparent 72%)",
          }}
        />
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 opacity-70"
          style={{
            backgroundImage:
              "radial-gradient(color-mix(in oklch, var(--mint) 22%, transparent) 1px, transparent 1.4px)",
            backgroundSize: "22px 22px",
            maskImage: "radial-gradient(75% 65% at 72% 22%, black, transparent)",
            WebkitMaskImage: "radial-gradient(75% 65% at 72% 22%, black, transparent)",
          }}
        />

        {/* Logo strip (mobile: also carries the date) */}
        <div className="relative flex items-center justify-between md:block">
          <div>
            <img src="/credix-logo.png" alt="Credix" className="brand-logo-light h-[30px] w-auto" />
            <img src="/credix-logo-light.png" alt="Credix" className="brand-logo-dark h-[30px] w-auto" />
          </div>
          <span className="text-[12px] text-(--ice-3) md:hidden">{todayJalaliLongDisplay()}</span>
        </div>

        {/* Hero — hidden on mobile so the form stays the focus */}
        <div className="relative hidden max-w-[46ch] md:block">
          <div className="aqsat-rise mb-6">
            <h1 className="mb-3 text-4xl font-black leading-[1.35] text-(--ice)">
              مهلت تسویه را{" "}
              <span className="bg-gradient-to-l from-(--mint) to-(--moss) bg-clip-text text-transparent">
                از دست ندهید
              </span>
            </h1>
            <p className="text-[14px] leading-7 text-(--ice-2)">
              سرسید اقساط بیمه، یادآوری پیامکی و وصول مطالبات نمایندگی — همه در یک نگاه.
            </p>
          </div>

          {/* Mini «اقساط امروز» card — the product's heartbeat as a static vignette */}
          <div className="aqsat-rise-2 mb-6 rounded-(--r-lg) border border-(--edge) bg-(--pane) p-4 shadow-(--sh)">
            <div className="mb-2 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="relative flex size-2">
                  <span aria-hidden className="absolute inline-flex h-full w-full animate-ping rounded-full bg-(--moss) opacity-60" />
                  <span className="relative inline-flex size-2 rounded-full bg-(--moss)" />
                </span>
                <h2 className="text-[13px] font-bold text-(--ice)">اقساط امروز</h2>
              </div>
              <span className="rounded-full border border-(--edge) bg-(--fld) px-2 py-0.5 text-[10.5px] text-(--ice-3)">
                ۳ بیمه‌نامهٔ فعال
              </span>
            </div>

            <ul>
              {DEMO_INSTALLMENTS.map((row) => (
                <li key={row.policy} className="flex items-center justify-between gap-3 rounded-(--r) px-2 py-2">
                  <div>
                    <p className="text-[12.5px] font-semibold text-(--ice)">{row.policy}</p>
                    <p className="text-[11px] text-(--ice-3)">{row.label}</p>
                  </div>
                  <div className="text-left">
                    <p className="text-[12px] font-semibold text-(--ice-2)">
                      {row.amount} <span className="text-[10px] font-normal text-(--ice-3)">تومان</span>
                    </p>
                    <span className={`mt-0.5 inline-block rounded-full border px-2 py-px text-[10px] font-medium ${CHIP_STYLE[row.tone]}`}>
                      {row.chip}
                    </span>
                  </div>
                </li>
              ))}
            </ul>

            <div className="mt-2 border-t border-(--edge) pt-3">
              <div className="mb-1.5 flex items-center justify-between text-[11px] text-(--ice-3)">
                <span>وصول این ماه</span>
                <span className="font-bold text-(--moss)">۸۴٪</span>
              </div>
              <div className="h-1.5 overflow-hidden rounded-full bg-(--slate-2)">
                <div className="h-full w-[84%] rounded-full bg-gradient-to-l from-(--moss) to-(--mint)" />
              </div>
            </div>
          </div>

          {/* Feature bullets */}
          <ul className="aqsat-rise-3 space-y-3">
            {FEATURES.map((f) => (
              <li key={f.title} className="flex items-center gap-3">
                <span className="grid size-9 shrink-0 place-items-center rounded-(--r) border border-(--edge) bg-(--fld) text-[17px] text-(--mint)">
                  <f.icon aria-hidden />
                </span>
                <span>
                  <span className="block text-[12.5px] font-bold text-(--ice)">{f.title}</span>
                  <span className="block text-[11.5px] text-(--ice-3)">{f.sub}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>

        <div className="relative hidden items-center text-[12px] text-(--ice-3) md:flex">
          <span>{todayJalaliLongDisplay()}</span>
        </div>
      </aside>

      {/* ── Form pane ───────────────────────────────────────────────────────── */}
      <main className="relative grid place-items-center overflow-hidden px-5 py-8">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0"
          style={{
            background:
              "radial-gradient(620px 460px at 50% 40%, color-mix(in oklch, var(--mint) 7%, transparent), transparent 70%)",
          }}
        />

        <div className="relative w-full max-w-[400px]">
          <form
            onSubmit={handleSubmit}
            className="aqsat-rise-2 overflow-hidden rounded-(--r-lg) border border-(--edge) bg-(--pane) shadow-(--sh)"
            noValidate
          >
            <div aria-hidden className="h-1 bg-gradient-to-l from-(--mint) via-(--mint-dim) to-(--moss)" />

            <div className="p-6 md:p-8">
              <div
                aria-hidden
                className="mb-4 grid size-11 place-items-center rounded-(--r-lg) bg-gradient-to-br from-(--mint) to-(--mint-dim) text-[22px] text-(--on-mint) shadow-(--gl-mint)"
              >
                <SignInIcon weight="bold" />
              </div>
              <h2 className="mb-1 text-xl font-black text-(--ice)">خوش آمدید</h2>
              <p className="mb-6 text-[12.5px] text-(--ice-3)">با شمارهٔ همراه و رمز عبور خود وارد شوید.</p>

              <div className="mb-4">
                <label htmlFor="login-mobile" className="mb-1.5 block text-[12px] font-medium text-(--ice-2)">
                  شمارهٔ همراه
                </label>
                <div className="relative">
                  <PhoneIcon
                    aria-hidden
                    className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-[15px] text-(--ice-3)"
                  />
                  <input
                    id="login-mobile"
                    name="mobile"
                    value={mobile}
                    onChange={(e) => setMobile(e.target.value)}
                    placeholder="09121234567"
                    dir="ltr"
                    inputMode="numeric"
                    autoComplete="username"
                    className="w-full rounded-(--r) border border-(--edge-2) bg-(--fld) py-2.5 pl-3 pr-9 text-[13.5px] text-(--ice) outline-none transition-[border-color] placeholder:text-(--ice-3)/60 focus:border-(--mint)"
                  />
                </div>
              </div>

              <div className="mb-5">
                <label htmlFor="login-password" className="mb-1.5 block text-[12px] font-medium text-(--ice-2)">
                  رمز عبور
                </label>
                <div className="relative">
                  <LockIcon
                    aria-hidden
                    className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-[15px] text-(--ice-3)"
                  />
                  <input
                    id="login-password"
                    name="password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    type={showPassword ? "text" : "password"}
                    dir="ltr"
                    autoComplete="current-password"
                    className="w-full rounded-(--r) border border-(--edge-2) bg-(--fld) py-2.5 pl-10 pr-9 text-[13.5px] text-(--ice) outline-none transition-[border-color] focus:border-(--mint)"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    aria-label={showPassword ? "پنهان کردن رمز" : "نمایش رمز"}
                    className="absolute left-2 top-1/2 grid size-7 -translate-y-1/2 place-items-center rounded-(--r-sharp) text-(--ice-3) transition-colors hover:bg-(--hov) hover:text-(--ice-2)"
                  >
                    {showPassword ? <EyeSlashIcon size={15} /> : <EyeIcon size={15} />}
                  </button>
                </div>
              </div>

              {error && (
                <div
                  role="alert"
                  className="mb-4 flex items-start gap-2 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-3 py-2.5 text-[12.5px] text-(--ember)"
                >
                  <WarningOctagonIcon size={15} className="mt-0.5 shrink-0" />
                  <span>{error}</span>
                </div>
              )}

              <button
                type="submit"
                disabled={submitting || !mobile.trim() || !password}
                className="flex w-full items-center justify-center gap-2 rounded-(--r) bg-gradient-to-l from-(--mint) to-(--mint-dim) px-4 py-3 text-[14px] font-bold text-(--on-mint) shadow-(--gl-mint) transition-[filter,opacity] hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:brightness-100"
              >
                {submitting ? (
                  <>
                    <SpinnerGapIcon size={16} className="animate-spin" />
                    در حال ورود…
                  </>
                ) : (
                  <>
                    ورود به دفتر
                    <ArrowLeftIcon size={16} weight="bold" />
                  </>
                )}
              </button>

              <div className="mt-5 flex items-center gap-3 text-[11px] text-(--ice-3)" aria-hidden>
                <span className="h-px flex-1 bg-(--edge)" />
                یا
                <span className="h-px flex-1 bg-(--edge)" />
              </div>

              <a
                href="/signup"
                className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-(--r) border border-(--edge-2) bg-(--btn-bg) px-4 py-2.5 text-[13px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
              >
                <UserPlusIcon size={15} />
                ثبت‌نام نمایندگی جدید
              </a>
            </div>
          </form>

          <p className="aqsat-rise-3 mt-5 text-center text-[11.5px] text-(--ice-3)">{versionLabel()}</p>
        </div>
      </main>
    </div>
  );
}
