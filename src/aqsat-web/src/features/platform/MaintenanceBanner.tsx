import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { getToken } from "../../lib/api";
import { fa } from "../../lib/persian";

/// docs/UPDATE-SYSTEM.md §7 — every active user gets a 60-second warning before maintenance mode
/// starts, not just the Platform.Owner who triggered it. Mounted once in Shell.tsx so it's visible
/// regardless of which tab is open.
export function MaintenanceBanner() {
  const [secondsRemaining, setSecondsRemaining] = useState<number | null>(null);
  const [maintenanceActive, setMaintenanceActive] = useState(false);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/platform", { accessTokenFactory: () => getToken() ?? "" })
      .withAutomaticReconnect()
      .build();

    connection.on("MaintenanceWarning", (payload: { secondsRemaining: number }) => {
      setSecondsRemaining(payload.secondsRemaining);
    });
    connection.on("MaintenanceModeChanged", (payload: { active: boolean }) => {
      setMaintenanceActive(payload.active);
      if (!payload.active) setSecondsRemaining(null);
    });

    connection.start().catch(() => {});
    return () => {
      connection.stop().catch(() => {});
    };
  }, []);

  useEffect(() => {
    if (secondsRemaining === null || secondsRemaining <= 0) return;
    const timer = setTimeout(() => setSecondsRemaining((s) => (s !== null ? s - 1 : null)), 1000);
    return () => clearTimeout(timer);
  }, [secondsRemaining]);

  if (maintenanceActive) {
    return (
      <div className="fixed inset-0 z-100 grid place-items-center bg-(--void)/95 px-6 text-center backdrop-blur-sm">
        <div>
          <div className="mb-2 text-lg font-extrabold text-(--ice)">سامانه در حال به‌روزرسانی است</div>
          <div className="text-[13px] text-(--ice-3)">لطفاً چند دقیقهٔ دیگر دوباره تلاش کنید. اطلاعات شما حفظ مانده است.</div>
        </div>
      </div>
    );
  }

  if (secondsRemaining !== null && secondsRemaining > 0) {
    return (
      <div className="fixed inset-x-0 top-0 z-100 flex items-center justify-center gap-2 bg-(--amber) px-4 py-2 text-[12.5px] font-semibold text-(--void)">
        به‌روزرسانی سامانه تا {fa(secondsRemaining)} ثانیهٔ دیگر آغاز می‌شود — کارتان را ذخیره کنید
      </div>
    );
  }

  return null;
}
