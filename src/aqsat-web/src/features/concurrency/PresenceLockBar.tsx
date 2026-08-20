import * as signalR from "@microsoft/signalr";
import { useEffect, useRef, useState } from "react";
import { api, ApiError, getActiveOrgId, getToken } from "../../lib/api";
import { fa } from "../../lib/persian";

interface PresenceEntry {
  userId: string;
  userDisplayName: string;
  isEditing: boolean;
  lastSeenAt: string;
}

interface LockStatusDto {
  acquiredByMe: boolean;
  lockId: string;
  lockedByUserId: string;
  lockedByDisplayName: string;
  acquiredAt: string;
  expiresAt: string;
}

const LOCK_DURATION_MINUTES = 10;
const HEARTBEAT_MS = 20_000;
const RENEW_MS = 4 * 60_000;

/// docs/CONCURRENCY.md — layers 2 (presence) and 3 (exclusive lock), wired to the real PresenceHub
/// and LocksController. Never gates reading, only the "ویرایش" action.
export function PresenceLockBar({ entityType, entityId }: { entityType: string; entityId: string }) {
  const [presence, setPresence] = useState<PresenceEntry[]>([]);
  const [lock, setLock] = useState<LockStatusDto | null>(null);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [revoked, setRevoked] = useState<{ reason: string; by: string } | null>(null);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    const agencyId = getActiveOrgId();
    if (!agencyId) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/presence", { accessTokenFactory: () => getToken() ?? "" })
      .withAutomaticReconnect()
      .build();
    connectionRef.current = connection;

    connection.on("PresenceChanged", (list: PresenceEntry[]) => setPresence(list));
    connection.on("LockRevoked", (payload: { entityType: string; entityId: string; reason: string; by: string }) => {
      if (payload.entityType === entityType && payload.entityId === entityId) {
        setRevoked({ reason: payload.reason, by: payload.by });
        setLock(null);
      }
    });

    connection
      .start()
      .then(() => {
        setConnected(true);
        return connection.invoke("Enter", agencyId, entityType, entityId);
      })
      .catch(() => setConnected(false));

    const heartbeat = setInterval(() => {
      connection.invoke("Heartbeat", agencyId, entityType, entityId).catch(() => {});
    }, HEARTBEAT_MS);

    refreshLockStatus();

    return () => {
      clearInterval(heartbeat);
      connection.stop();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entityType, entityId]);

  useEffect(() => {
    if (!lock?.acquiredByMe) return;
    const renew = setInterval(() => acquire(), RENEW_MS);
    return () => clearInterval(renew);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lock?.acquiredByMe]);

  async function refreshLockStatus() {
    try {
      const status = await api.get<LockStatusDto | null>(
        `/locks/status?entityType=${entityType}&entityId=${entityId}`,
      );
      setLock(status);
    } catch {
      // best-effort — presence/lock display is not the primary content of the page
    }
  }

  async function acquire() {
    setError(null);
    try {
      const status = await api.post<LockStatusDto>("/locks/acquire", {
        entityType,
        entityId,
        durationMinutes: LOCK_DURATION_MINUTES,
      });
      setLock(status);
      setRevoked(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "دریافت قفل ناموفق بود.");
    }
  }

  async function release() {
    try {
      await api.post("/locks/release", { entityType, entityId });
    } finally {
      setLock(null);
      refreshLockStatus();
    }
  }

  async function forceRelease() {
    if (!lock) return;
    const reason = window.prompt("دلیل آزادسازی اجباری را بنویسید (حداقل ۱۰ کاراکتر):");
    if (!reason || reason.trim().length < 10) return;

    setError(null);
    try {
      await api.post(`/locks/${lock.lockId}/force-release`, { reason: reason.trim() });
      refreshLockStatus();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "آزادسازی اجباری ناموفق بود.");
    }
  }

  return (
    <div className="mb-4.5 rounded-xl border border-(--edge) bg-(--pane) p-3.5 text-[12.5px]">
      {revoked && (
        <div className="mb-3 rounded-[10px] border border-(--amber)/30 bg-(--amber)/10 px-3 py-2 text-(--amber)">
          دسترسی ویرایش شما توسط <b>{revoked.by}</b> لغو شد. دلیل: «{revoked.reason}»
        </div>
      )}
      {error && (
        <div className="mb-3 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-(--ember)">{error}</div>
      )}

      <div className="mb-2.5 flex flex-wrap items-center gap-2 text-(--ice-3)">
        <span className={`inline-block h-1.5 w-1.5 rounded-full ${connected ? "bg-(--mint)" : "bg-(--ice-3)"}`} />
        {presence.length === 0 ? (
          <span>کسی دیگری این پرونده را باز نکرده</span>
        ) : (
          presence.map((p) => (
            <span key={p.userId} className="rounded-full border border-(--edge-2) px-2 py-0.5">
              {p.isEditing ? "✏️" : "👁"} {p.userDisplayName}
            </span>
          ))
        )}
      </div>

      <div className="flex items-center gap-2">
        {lock?.acquiredByMe ? (
          <>
            <span className="text-(--mint)">در حال ویرایش شما — تا {fa(lock.expiresAt.slice(11, 16))}</span>
            <button
              type="button"
              onClick={release}
              className="rounded-[8px] border border-(--edge-2) bg-(--btn-bg) px-3 py-1 font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
            >
              پایان ویرایش
            </button>
          </>
        ) : lock ? (
          <>
            <span className="text-(--amber)">{lock.lockedByDisplayName} در حال ویرایش است</span>
            <button
              type="button"
              onClick={forceRelease}
              className="rounded-[8px] border border-(--ember)/40 bg-(--ember)/10 px-3 py-1 font-semibold text-(--ember) transition-colors hover:bg-(--ember)/20"
            >
              آزادسازی اجباری
            </button>
          </>
        ) : (
          <button
            type="button"
            onClick={acquire}
            className="rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1 font-semibold text-(--on-mint) transition-colors hover:brightness-105"
          >
            ویرایش
          </button>
        )}
      </div>
    </div>
  );
}
