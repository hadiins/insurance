import * as signalR from "@microsoft/signalr";
import { ArrowsClockwiseIcon, KeyboardIcon, PencilSimpleIcon } from "@phosphor-icons/react";
import { useEffect, useRef, useState } from "react";
import { versionLabel } from "../../app/version";
import { api, getActiveOrgId, getToken } from "../../lib/api";
import { fa } from "../../lib/persian";
import { useEditLockStore } from "../concurrency/editLockStore";

/** How many records the current user is holding an edit lock on. Derived from the record pages that
 * are actually mounted right now — there is no global lock list to ask the server for. */
function useHeldLockCount(): number {
  return useEditLockStore((s) => Object.values(s.locks).filter((l) => l.mine).length);
}

/** The shell's bottom rail: who else is in the app, what this session is holding open, and the two
 * keyboard shortcuts the shell already implements. Present on every page, so it stays one thin line
 * and never scrolls horizontally. */
export function StatusBar() {
  const [activeUsers, setActiveUsers] = useState<number | null>(null);
  const [connected, setConnected] = useState(false);
  const heldLocks = useHeldLockCount();
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    const agencyId = getActiveOrgId();
    if (!agencyId) return;

    let cancelled = false;

    // First paint from HTTP, so the count is right even before (or without) a live socket. A failed
    // call leaves it null and the rail says «—» — a silent 0 would read as "you are alone in here",
    // which is the opposite of what a failed count means.
    api
      .get<{ count: number }>("/presence/active-users")
      .then((d) => {
        if (!cancelled) setActiveUsers(d.count);
      })
      .catch(() => {
        if (!cancelled) setActiveUsers(null);
      });

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/presence", { accessTokenFactory: () => getToken() ?? "" })
      .withAutomaticReconnect()
      .build();
    connectionRef.current = connection;

    connection.on("ActiveUsersChanged", (count: number) => {
      setActiveUsers(count);
      setConnected(true);
    });

    connection
      .start()
      .then(() => {
        setConnected(true);
        return connection.invoke("EnterApp", agencyId);
      })
      .catch(() => setConnected(false));

    // A reconnect rejoins the group with a fresh connection id — EnterApp must run again, or this
    // session silently stops counting itself after the first network blip.
    connection.onreconnected(() => {
      connection.invoke("EnterApp", agencyId).catch(() => setConnected(false));
    });

    return () => {
      cancelled = true;
      void connection.stop();
      connectionRef.current = null;
    };
  }, []);

  return (
    <div className="flex flex-none items-center gap-4 overflow-hidden border-t border-(--edge) bg-(--slate) px-4 py-1.5 text-[11px] text-(--ice-3)">
      <span className="flex flex-none items-center gap-1.5" title={connected ? "اتصال زنده برقرار است" : "اتصال زنده برقرار نیست"}>
        <span
          className={`inline-block h-1.5 w-1.5 rounded-full ${
            connected ? "bg-(--moss)" : "bg-(--ice-3)"
          }`}
        />
        {activeUsers === null ? "کاربران فعال: —" : `${fa(activeUsers)} کاربر فعال`}
      </span>

      {heldLocks > 0 && (
        <span className="flex flex-none items-center gap-1.5 text-(--mint)">
          <PencilSimpleIcon size={13} />
          {fa(heldLocks)} ویرایش باز در دست شما
        </span>
      )}

      <span className="min-w-0 flex-1" />

      <span className="hidden flex-none items-center gap-1.5 sm:flex">
        <KeyboardIcon size={13} />
        <kbd className="rounded-(--r) border border-(--edge-2) bg-(--fld) px-1.5 py-0.5 font-sans" dir="ltr">
          Ctrl+W
        </kbd>
        بستن زبانه
        <kbd className="rounded-(--r) border border-(--edge-2) bg-(--fld) px-1.5 py-0.5 font-sans" dir="ltr">
          Ctrl+Tab
        </kbd>
        زبانهٔ بعد
      </span>

      <span className="flex flex-none items-center gap-1.5">
        <ArrowsClockwiseIcon size={13} />
        {versionLabel()}
      </span>
    </div>
  );
}
