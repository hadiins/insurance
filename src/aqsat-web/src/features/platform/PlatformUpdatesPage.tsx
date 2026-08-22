import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { api, ApiError, getToken } from "../../lib/api";
import { fa } from "../../lib/persian";

interface PlatformStatusDto {
  currentVersion: string;
  maintenanceModeActive: boolean;
  maintenanceEstimatedEndsAt: string | null;
}

interface UpdatePackageDto {
  id: string;
  version: string;
  releaseNotesFa: string;
  hasDbMigration: boolean;
  isSecurityUpdate: boolean;
  publishedAt: string;
}

interface UpdateRunDto {
  id: string;
  fromVersion: string;
  toVersion: string;
  startedByFullName: string;
  startedAt: string;
  completedAt: string | null;
  status: "Running" | "Success" | "Failed" | "RolledBack";
  currentStage: string;
  progressPercent: number;
  errorMessage: string | null;
  errorDetail: string | null;
}

interface LiveProgress {
  stage: string;
  status: string;
  percentComplete: number;
  errorMessage: string | null;
}

const STAGE_LABEL: Record<string, string> = {
  VerifyingSignature: "بررسی امضا",
  BackingUpDatabase: "پشتیبان‌گیری دیتابیس",
  PullingImage: "دانلود بسته",
  RecreatingContainer: "جابه‌جایی کانتینر",
  HealthChecking: "بررسی سلامت",
  Done: "پایان",
  RolledBack: "بازگشت داده‌شد",
  PreparingMaintenanceWindow: "آماده‌سازی پنجرهٔ تعمیر",
};

const STATUS_LABEL: Record<string, string> = {
  Running: "در حال اجرا",
  Success: "موفق",
  Failed: "ناموفق",
  RolledBack: "بازگشت‌داده‌شد",
};

export function PlatformUpdatesPage() {
  const [status, setStatus] = useState<PlatformStatusDto | null>(null);
  const [packages, setPackages] = useState<UpdatePackageDto[] | null>(null);
  const [history, setHistory] = useState<UpdateRunDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [otpPackage, setOtpPackage] = useState<UpdatePackageDto | null>(null);
  const [otpSent, setOtpSent] = useState(false);
  const [otpCode, setOtpCode] = useState("");
  const [mobileMasked, setMobileMasked] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [activeRun, setActiveRun] = useState<UpdateRunDto | null>(null);
  const [liveProgress, setLiveProgress] = useState<LiveProgress | null>(null);

  const [rollbackImageTag, setRollbackImageTag] = useState("");

  const connectionRef = useRef<signalR.HubConnection | null>(null);

  function reload() {
    api.get<PlatformStatusDto>("/platform/updates/status").then(setStatus).catch(() => {});
    api.get<UpdatePackageDto[]>("/platform/updates/packages").then(setPackages).catch(() => {});
    api.get<UpdateRunDto[]>("/platform/updates/history").then(setHistory).catch(() => {});
    api
      .get<UpdateRunDto | null>("/platform/updates/current")
      .then((run) => setActiveRun(run))
      .catch(() => {});
  }

  useEffect(reload, []);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/platform", { accessTokenFactory: () => getToken() ?? "" })
      .withAutomaticReconnect()
      .build();
    connectionRef.current = connection;

    connection.on("UpdateProgress", (payload: LiveProgress) => {
      setLiveProgress(payload);
      if (payload.status !== "Running") {
        // Give the server a moment to finish persisting before we re-pull the authoritative record.
        setTimeout(reload, 500);
      }
    });
    connection.on("MaintenanceModeChanged", () => reload());

    connection.start().catch(() => {});
    return () => {
      connection.stop().catch(() => {});
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function requestOtp(pkg: UpdatePackageDto) {
    setError(null);
    setOtpPackage(pkg);
    setOtpSent(false);
    setOtpCode("");
    try {
      const result = await api.post<{ sent: boolean; mobileMasked: string | null }>("/platform/updates/otp", {});
      setOtpSent(result.sent);
      setMobileMasked(result.mobileMasked);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ارسال کد تأیید ناموفق بود.");
    }
  }

  async function confirmStart() {
    if (!otpPackage || !otpCode.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const run = await api.post<UpdateRunDto>(`/platform/updates/${otpPackage.id}/start`, { otpCode: otpCode.trim() });
      setActiveRun(run);
      setOtpPackage(null);
      setOtpCode("");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "شروع به‌روزرسانی ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function yankPackage(pkg: UpdatePackageDto) {
    setError(null);
    try {
      await api.post(`/platform/updates/packages/${pkg.id}/yank`, {});
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "لغو نسخه ناموفق بود.");
    }
  }

  async function rollback() {
    if (!rollbackImageTag.trim()) return;
    setBusy(true);
    setError(null);
    try {
      await api.post("/platform/updates/rollback", { toImageTag: rollbackImageTag.trim() });
      setRollbackImageTag("");
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "بازگشت ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  function copyReport(run: UpdateRunDto) {
    const report = [
      `نسخه: ${run.fromVersion} → ${run.toVersion}`,
      `وضعیت: ${STATUS_LABEL[run.status] ?? run.status}`,
      `مرحله: ${STAGE_LABEL[run.currentStage] ?? run.currentStage}`,
      `شروع: ${run.startedAt}`,
      run.completedAt ? `پایان: ${run.completedAt}` : null,
      run.errorMessage ? `خطا: ${run.errorMessage}` : null,
      run.errorDetail ? `جزئیات:\n${run.errorDetail}` : null,
    ]
      .filter(Boolean)
      .join("\n");
    navigator.clipboard.writeText(report).catch(() => {});
  }

  const displayedRun = activeRun;
  const displayedProgress = liveProgress?.percentComplete ?? displayedRun?.progressPercent ?? 0;
  const displayedStage = liveProgress?.stage ?? displayedRun?.currentStage;

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        به‌روزرسانی <em className="font-extralight not-italic text-(--ice-2)">سامانه</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">این صفحه فقط برای مالک پلتفرم است — نمایندگی‌ها آن را نمی‌بینند</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {status && (
        <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">نسخهٔ فعلی</div>
          <div className="text-[20px] font-extrabold text-(--ice)">{status.currentVersion}</div>
          {status.maintenanceModeActive && (
            <div className="mt-2 text-[12px] font-semibold text-(--amber)">🔵 سامانه در حال به‌روزرسانی است</div>
          )}
        </div>
      )}

      {displayedRun && displayedRun.status === "Running" && (
        <div className="mb-4.5 rounded-2xl border border-(--mint)/30 bg-(--mint)/6 p-5">
          <div className="mb-3 text-[13px] font-bold text-(--ice)">
            به‌روزرسانی به {displayedRun.toVersion} — {fa(displayedProgress)}٪
          </div>
          <div className="mb-3 h-2 overflow-hidden rounded-full bg-(--fld)">
            <div className="h-full bg-(--mint) transition-all" style={{ width: `${displayedProgress}%` }} />
          </div>
          <div className="text-[12px] text-(--ice-3)">
            مرحلهٔ فعلی: {STAGE_LABEL[displayedStage ?? ""] ?? displayedStage}
          </div>
          <div className="mt-3 text-[11px] text-(--ice-3)">⚠️ بستن این صفحه به‌روزرسانی را متوقف نمی‌کند</div>
        </div>
      )}

      {displayedRun && (displayedRun.status === "Failed" || displayedRun.status === "RolledBack") && (
        <div className="mb-4.5 rounded-2xl border border-(--ember)/30 bg-(--ember)/8 p-5">
          <div className="mb-1 text-[14px] font-bold text-(--ember)">❌ به‌روزرسانی ناموفق بود</div>
          <div className="mb-2 text-[12.5px] text-(--ice-2)">{displayedRun.errorMessage}</div>
          {displayedRun.status === "RolledBack" && (
            <div className="mb-3 text-[12px] text-(--mint)">🔄 بازگشت خودکار انجام شد — نسخهٔ {displayedRun.fromVersion} فعال است</div>
          )}
          <button
            type="button"
            onClick={() => copyReport(displayedRun)}
            className="rounded-[8px] border border-(--edge-2) px-3 py-1.5 text-[11.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
          >
            کپی برای پشتیبانی
          </button>
        </div>
      )}

      <div className="mb-4.5">
        <div className="mb-2 text-[12.5px] font-semibold text-(--ice-2)">نسخه‌های موجود</div>
        {packages === null ? (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        ) : packages.length === 0 ? (
          <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">نسخهٔ جدیدی موجود نیست.</div>
        ) : (
          <div className="space-y-2">
            {packages.map((pkg) => (
              <div key={pkg.id} className="rounded-2xl border border-(--edge) bg-(--pane) p-4">
                <div className="mb-1.5 flex items-center justify-between">
                  <b className="text-[13.5px] text-(--ice)">نسخهٔ {pkg.version}</b>
                  <div className="flex items-center gap-1.5">
                    {pkg.isSecurityUpdate && <span className="rounded-full bg-(--ember)/13 px-2 py-0.5 text-[10.5px] text-(--ember)">امنیتی</span>}
                    {pkg.hasDbMigration && <span className="rounded-full bg-(--amber)/13 px-2 py-0.5 text-[10.5px] text-(--amber)">تغییر ساختار دیتابیس</span>}
                  </div>
                </div>
                <div className="mb-3 whitespace-pre-line text-[12px] text-(--ice-3)">{pkg.releaseNotesFa}</div>
                <div className="flex gap-2">
                  <button
                    type="button"
                    disabled={Boolean(displayedRun && displayedRun.status === "Running")}
                    onClick={() => requestOtp(pkg)}
                    className="rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1.5 text-[12px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    اجرای به‌روزرسانی
                  </button>
                  <button
                    type="button"
                    onClick={() => yankPackage(pkg)}
                    className="rounded-[8px] border border-(--edge-2) px-3 py-1.5 text-[12px] text-(--ice-3) transition-colors hover:bg-(--hov)"
                  >
                    لغو این نسخه
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {otpPackage && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-black/50 px-4">
          <div className="w-full max-w-sm rounded-2xl border border-(--edge) bg-(--pane) p-5">
            <b className="mb-3 block text-[14px] text-(--ice)">تأیید دومرحله‌ای — به‌روزرسانی به {otpPackage.version}</b>
            {!otpSent ? (
              <div className="text-[12.5px] text-(--ice-3)">در حال ارسال کد…</div>
            ) : (
              <>
                <div className="mb-3 text-[12px] text-(--ice-3)">کدی به شمارهٔ {mobileMasked} ارسال شد.</div>
                <input
                  value={otpCode}
                  onChange={(e) => setOtpCode(e.target.value)}
                  placeholder="کد ۶ رقمی"
                  maxLength={6}
                  className="mb-3 w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-center text-[16px] tracking-[0.3em] text-(--ice) outline-none focus:border-(--mint)"
                />
              </>
            )}
            <div className="flex gap-2">
              <button
                type="button"
                disabled={busy || !otpSent || otpCode.trim().length !== 6}
                onClick={confirmStart}
                className="flex-1 rounded-[10px] border border-(--mint) bg-(--mint) px-3 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {busy ? "در حال شروع…" : "تأیید و شروع"}
              </button>
              <button
                type="button"
                onClick={() => setOtpPackage(null)}
                className="rounded-[10px] border border-(--edge-2) px-3 py-2 text-[12.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
              >
                انصراف
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-2 text-[12.5px] font-semibold text-(--ice-2)">بازگشت دستی</div>
        <div className="mb-3 text-[11.5px] text-(--ice-3)">
          بازگشت ایمیج آسان است، بازگشت مهاجرت دیتابیس نیست — اگر نسخهٔ فعلی ستونی حذف یا داده‌ای تبدیل کرده باشد، بازگشت ایمیج به‌تنهایی کافی نیست.
        </div>
        <div className="flex gap-2">
          <input
            value={rollbackImageTag}
            onChange={(e) => setRollbackImageTag(e.target.value)}
            placeholder="برچسب ایمیج برای بازگشت (مثلاً registry/aqsat-api:1.4.2)"
            className="flex-1 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <button
            type="button"
            disabled={busy}
            onClick={rollback}
            className="rounded-[10px] border border-(--ember) px-4 py-2 text-[12px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/10 disabled:cursor-not-allowed disabled:opacity-50"
          >
            بازگشت
          </button>
        </div>
      </div>

      <div>
        <div className="mb-2 text-[12.5px] font-semibold text-(--ice-2)">تاریخچهٔ به‌روزرسانی‌ها</div>
        {history === null ? (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        ) : history.length === 0 ? (
          <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">هیچ به‌روزرسانی‌ای ثبت نشده.</div>
        ) : (
          <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
            <table className="w-full border-collapse">
              <thead>
                <tr>
                  {["نسخه", "توسط", "شروع", "وضعیت", ""].map((h) => (
                    <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {history.map((run) => (
                  <tr key={run.id} className="border-t border-(--edge) first:border-t-0">
                    <td className="px-3 py-2.5 text-[13px] font-semibold">
                      {run.fromVersion} ← {run.toVersion}
                    </td>
                    <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{run.startedByFullName}</td>
                    <td className="px-3 py-2.5 text-[12.5px] text-(--ice-3)">{fa(run.startedAt.slice(0, 16).replace("T", " "))}</td>
                    <td className="px-3 py-2.5 text-[13px]">{STATUS_LABEL[run.status] ?? run.status}</td>
                    <td className="px-3 py-2.5 text-[13px]">
                      {(run.status === "Failed" || run.status === "RolledBack") && (
                        <button
                          type="button"
                          onClick={() => copyReport(run)}
                          className="rounded-[8px] border border-(--edge-2) px-2 py-1 text-[10.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
                        >
                          کپی گزارش
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
