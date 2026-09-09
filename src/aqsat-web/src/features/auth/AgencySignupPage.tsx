import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { toLatinDigits } from "../../lib/persian";

interface SignupStatusDto {
  isOpen: boolean;
}

interface AgencySignupRequest {
  agencyName: string;
  managerFullName: string;
  managerMobile: string;
  otpCode: string;
  password: string;
  province: string | null;
  city: string | null;
  insurerName: string | null;
}

// The backend's IranProvinces.All, hardcoded here because the filters endpoint that would
// provide them is Platform.Owner-only — unreachable before any login exists. Validation stays
// server-side against the same canonical list.
const PROVINCES = [
  "آذربایجان شرقی", "آذربایجان غربی", "اردبیل", "اصفهان", "البرز", "ایلام", "بوشهر",
  "تهران", "چهارمحال و بختیاری", "خراسان جنوبی", "خراسان رضوی", "خراسان شمالی",
  "خوزستان", "زنجان", "سمنان", "سیستان و بلوچستان", "فارس", "قزوین", "قم", "کردستان",
  "کرمان", "کرمانشاه", "کهگیلویه و بویراحمد", "گلستان", "گیلان", "لرستان", "مازندران",
  "مرکزی", "هرمزگان", "همدان", "یزد",
];

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)";

/// Standalone public page (feature 5 — self-serve agency signup), outside the MDI shell and
/// reached only by direct URL (/signup). The agency is created PENDING: the manager logs in only
/// after the platform owner activates it, so the final step is a message, never a session.
export function AgencySignupPage() {
  const [status, setStatus] = useState<SignupStatusDto | null>(null);
  const [mobile, setMobile] = useState("");
  const [otpSent, setOtpSent] = useState(false);
  const [otpCode, setOtpCode] = useState("");
  const [agencyName, setAgencyName] = useState("");
  const [managerFullName, setManagerFullName] = useState("");
  const [password, setPassword] = useState("");
  const [province, setProvince] = useState("");
  const [city, setCity] = useState("");
  const [insurerName, setInsurerName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  useEffect(() => {
    api
      .get<SignupStatusDto>("/signup/status")
      .then(setStatus)
      .catch(() => setStatus({ isOpen: false }));
  }, []);

  async function sendOtp() {
    const normalized = toLatinDigits(mobile.trim());
    if (!normalized) {
      setError("شمارهٔ همراه الزامی است.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.post("/signup/request-otp", { mobile: normalized });
      setOtpSent(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ارسال کد ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function submit() {
    const normalized = toLatinDigits(mobile.trim());
    if (!agencyName.trim() || !managerFullName.trim() || !otpCode.trim() || !password) {
      setError("نام نمایندگی، نام مدیر، کد تأیید و رمز عبور الزامی است.");
      return;
    }
    if (password.length < 8) {
      setError("رمز عبور باید حداقل ۸ کاراکتر باشد.");
      return;
    }
    const request: AgencySignupRequest = {
      agencyName: agencyName.trim(),
      managerFullName: managerFullName.trim(),
      managerMobile: normalized,
      otpCode: toLatinDigits(otpCode.trim()),
      password,
      province: province || null,
      city: city.trim() || null,
      insurerName: insurerName.trim() || null,
    };
    setBusy(true);
    setError(null);
    try {
      await api.post("/signup", request);
      setDone(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت‌نام ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  if (status === null) {
    return <div className="grid h-full place-items-center bg-(--void) text-(--ice-3)">در حال بررسی…</div>;
  }

  if (!status.isOpen && !done) {
    return (
      <div className="grid h-full place-items-center bg-(--void) px-6 text-center">
        <div>
          <b className="mb-1 block text-base font-bold text-(--ice-2)">ثبت‌نام نمایندگی</b>
          <span className="text-[13.5px] text-(--ice-3)">
            ثبت‌نام نمایندگی جدید در حال حاضر فعال نیست.
          </span>
        </div>
      </div>
    );
  }

  if (done) {
    return (
      <div className="grid h-full place-items-center bg-(--void) px-6">
        <div className="w-full max-w-sm rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center">
          <b className="mb-2 block text-[15px] font-bold text-(--ice)">ثبت‌نام شما ثبت شد</b>
          <span className="mb-4 block text-[13px] leading-relaxed text-(--ice-3)">
            پس از تأیید مالک سامانه می‌توانید با همین شمارهٔ همراه وارد شوید.
          </span>
          <a
            href="/"
            className="inline-block rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
          >
            بازگشت به صفحهٔ ورود
          </a>
        </div>
      </div>
    );
  }

  return (
    <div className="grid h-full place-items-center overflow-auto bg-(--void) px-6 py-10">
      <div className="w-full max-w-sm rounded-2xl border border-(--edge) bg-(--pane) p-6">
        <h1 className="mb-1 text-xl font-extrabold text-(--ice)">ثبت‌نام نمایندگی</h1>
        <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
          نمایندگی شما پس از تأیید مالک سامانه فعال می‌شود
        </div>

        {error && (
          <div className="mb-3.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
            {error}
          </div>
        )}

        {!otpSent ? (
          <div className="space-y-2.5">
            <input
              value={mobile}
              onChange={(e) => setMobile(e.target.value)}
              placeholder="شمارهٔ همراه مدیر…"
              dir="ltr"
              className={inputClass}
            />
            <button
              type="button"
              onClick={sendOtp}
              disabled={busy}
              className="mt-1 w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {busy ? "در حال ارسال…" : "ارسال کد تأیید"}
            </button>
          </div>
        ) : (
          <div className="space-y-2.5">
            <div className="mb-1 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12px] text-(--ice-2)">
              کد تأیید به {mobile || "شمارهٔ شما"} ارسال شد (تا ۵ دقیقه معتبر است).
            </div>
            <input
              value={otpCode}
              onChange={(e) => setOtpCode(e.target.value)}
              placeholder="کد تأیید ۶ رقمی"
              dir="ltr"
              className={inputClass}
            />
            <input
              value={agencyName}
              onChange={(e) => setAgencyName(e.target.value)}
              placeholder="نام نمایندگی"
              className={inputClass}
            />
            <input
              value={managerFullName}
              onChange={(e) => setManagerFullName(e.target.value)}
              placeholder="نام کامل مدیر"
              className={inputClass}
            />
            <input
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              type="password"
              placeholder="رمز عبور (حداقل ۸ کاراکتر)"
              dir="ltr"
              className={inputClass}
            />
            <select
              value={province}
              onChange={(e) => setProvince(e.target.value)}
              className={inputClass}
            >
              <option value="">استان (اختیاری)</option>
              {PROVINCES.map((p) => (
                <option key={p} value={p}>{p}</option>
              ))}
            </select>
            <input
              value={city}
              onChange={(e) => setCity(e.target.value)}
              placeholder="شهر (اختیاری)"
              className={inputClass}
            />
            <input
              value={insurerName}
              onChange={(e) => setInsurerName(e.target.value)}
              placeholder="شرکت بیمهٔ طرف قرارداد (اختیاری)"
              className={inputClass}
            />
            <button
              type="button"
              onClick={submit}
              disabled={busy}
              className="mt-1 w-full rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2.5 text-[13.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {busy ? "در حال ثبت…" : "ثبت‌نام نمایندگی"}
            </button>
            <button
              type="button"
              onClick={() => {
                setOtpSent(false);
                setOtpCode("");
                setError(null);
              }}
              className="w-full text-[12px] text-(--ice-3) underline-offset-4 hover:text-(--ice-2) hover:underline"
            >
              تغییر شمارهٔ همراه
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
