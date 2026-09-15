import { useCallback, useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay, toJalaliDisplay } from "../../lib/jalali";
import { StatusBadge } from "../../components/StatusBadge";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import { EditInstallmentDialog, type EditInstallmentTarget } from "../installments/EditInstallmentDialog";

interface PolicyFilePayload {
  policyId: string;
}

interface PolicyInstallmentDto {
  id: string;
  seqNo: number;
  dueDate: string;
  settlementDeadline: string;
  amount: number;
  paidAmount: number;
  balance: number;
  status: string;
  isManuallyEdited: boolean;
}

interface PolicyEndorsementDto {
  id: string;
  endorsementNo: string;
  type: string;
  issueDate: string;
  premiumDelta: number;
  serviceFeeDelta: number;
  description: string | null;
}

interface PolicyCommissionRowDto {
  id: string;
  installmentSeqNo: number | null;
  amount: number;
  status: string;
  eligibleAt: string | null;
  paidAt: string | null;
}

interface TimelineEntryDto {
  actorDisplayName: string;
  occurredAt: string;
  description: string;
}

interface PolicyFileDto {
  policyId: string;
  policyNumber: string;
  customerId: string;
  customerFullName: string;
  insuranceLineNameFa: string;
  status: string;
  netPremium: number;
  serviceFee: number;
  totalReceivable: number;
  downPayment: number;
  marketerFullName: string | null;
  installments: PolicyInstallmentDto[];
  endorsements: PolicyEndorsementDto[];
  commissions: PolicyCommissionRowDto[];
  timeline: TimelineEntryDto[];
}

const INSTALLMENT_STATUS_LABEL: Record<string, string> = {
  Unpaid: "پرداخت‌نشده",
  Partial: "پرداخت جزئی",
  Settled: "تسویه‌شده",
};

const STATUS_LABEL: Record<string, string> = {
  Active: "فعال",
  Settled: "تسویه‌شده",
  Cancelled: "باطل‌شده",
  PendingConfirmation: "در انتظار تأیید مشتری",
};

const STATUS_TONE: Record<string, "mint" | "moss" | "amber" | "ember"> = {
  Active: "mint",
  Settled: "moss",
  Cancelled: "ember",
  PendingConfirmation: "amber",
};

const timeLabel = toJalaliDateTimeDisplay;

export function PolicyFilePage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const openTab = useTabsStore((s) => s.openTab);
  const payload = tab?.payload as PolicyFilePayload | undefined;

  const [file, setFile] = useState<PolicyFileDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editingInstallment, setEditingInstallment] = useState<EditInstallmentTarget | null>(null);
  const [tabSection, setTabSection] = useState<"installments" | "endorsements" | "commissions" | "timeline">("installments");

  const load = useCallback(() => {
    if (!payload?.policyId) return;
    api
      .get<PolicyFileDto>(`/policies/${payload.policyId}/file`)
      .then((data) => {
        setFile(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری پروندهٔ بیمه‌نامه"));
  }, [payload?.policyId]);

  useEffect(() => {
    load();
  }, [load]);

  useLiveReload(load);

  if (!payload) return null;

  async function transitionStatus(action: "mark-pending-confirmation" | "confirm") {
    if (!payload?.policyId) return;
    setBusy(true);
    setError(null);
    try {
      await api.put(`/policies/${payload.policyId}/${action}`, {});
      load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "تغییر وضعیت ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  function openCustomer() {
    if (!file) return;
    openTab({
      navType: "customer-file",
      page: "customer-file",
      kind: "multi-record",
      recordId: file.customerId,
      title: file.customerFullName,
      payload: { customerId: file.customerId },
    });
  }

  return (
    <div>
      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && file === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {file && (
        <>
          <div className="mb-4.5 flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-xl font-extrabold tracking-tight text-(--ice)">
              پروندهٔ <em className="font-extralight not-italic text-(--ice-2)">{fa(file.policyNumber)}</em>
            </h2>
            <div className="flex items-center gap-2">
              <StatusBadge tone={STATUS_TONE[file.status] ?? "neutral"}>
                {STATUS_LABEL[file.status] ?? file.status}
              </StatusBadge>
              {file.status === "Active" && (
                <button
                  type="button"
                  onClick={() => void transitionStatus("mark-pending-confirmation")}
                  disabled={busy}
                  className="rounded-[8px] border border-(--amber)/60 bg-transparent px-2.5 py-1 text-[11.5px] font-semibold text-(--amber) transition-colors hover:bg-(--amber)/10 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  انتقال به «در انتظار تأیید مشتری»
                </button>
              )}
              {file.status === "PendingConfirmation" && (
                <button
                  type="button"
                  onClick={() => void transitionStatus("confirm")}
                  disabled={busy}
                  className="rounded-[8px] border border-(--mint) bg-(--mint) px-2.5 py-1 text-[11.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  تأیید مشتری
                </button>
              )}
            </div>
          </div>
          <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
            {file.insuranceLineNameFa} —{" "}
            <button type="button" onClick={openCustomer} className="text-(--mint) underline underline-offset-2">
              {file.customerFullName}
            </button>
            {file.marketerFullName && <> — بازاریاب: {file.marketerFullName}</>}
          </div>

          <div className="mb-4.5 grid grid-cols-4 gap-3">
            <Fig label="حق‌بیمهٔ خالص" value={money(file.netPremium)} />
            <Fig label="کارمزد خدمات" value={money(file.serviceFee)} />
            <Fig label="جمع دریافتی" value={money(file.totalReceivable)} />
            <Fig label="پیش‌پرداخت" value={money(file.downPayment)} />
          </div>

          <div className="mb-3 flex gap-2">
            {(
              [
                ["installments", `اقساط (${fa(file.installments.length)})`],
                ["endorsements", `الحاقیه‌ها (${fa(file.endorsements.length)})`],
                ["commissions", `پورسانت (${fa(file.commissions.length)})`],
                ["timeline", `تاریخچه (${fa(file.timeline.length)})`],
              ] as const
            ).map(([key, label]) => (
              <button
                key={key}
                type="button"
                onClick={() => setTabSection(key)}
                className={`rounded-full px-3 py-1 text-[11.5px] font-semibold transition-colors ${
                  tabSection === key ? "bg-(--mint) text-(--on-mint)" : "border border-(--edge-2) text-(--ice-3) hover:bg-(--hov)"
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          {tabSection === "installments" &&
            (file.installments.length === 0 ? (
              <EmptyState
                icon="🗓"
                title="قسطی ثبت نشده"
                description="برای این بیمه‌نامه هنوز قسطی ایجاد نشده است — از «زمان‌بندی اقساط» می‌توانید اقساط را بسازید."
              />
            ) : (
              <Table>
                <thead>
                  <tr>
                    {["قسط", "سررسید", "مهلت تسویه", "مبلغ", "پرداخت‌شده", "مانده", "وضعیت", ""].map((h) => (
                      <Th key={h}>{h}</Th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {file.installments.map((i) => (
                    <Tr key={i.id}>
                      <Td className="py-2.5 !text-[12.5px] font-semibold text-(--ice-2)">
                        {fa(i.seqNo)}
                        {i.isManuallyEdited && (
                          <span className="ms-1.5 rounded-full bg-(--amber)/13 px-1.5 py-0.5 text-[10.5px] font-semibold text-(--amber)">
                            ویرایش‌شده
                          </span>
                        )}
                      </Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{toJalaliDisplay(i.dueDate)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{toJalaliDisplay(i.settlementDeadline)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{money(i.amount)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{money(i.paidAmount)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{money(i.balance)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">
                        {INSTALLMENT_STATUS_LABEL[i.status] ?? i.status}
                      </Td>
                      <Td className="py-2.5">
                        {i.status !== "Settled" && (
                          <button
                            type="button"
                            onClick={() =>
                              setEditingInstallment({
                                installmentId: i.id,
                                seqNo: i.seqNo,
                                dueDate: i.dueDate,
                                amount: i.amount,
                                status: i.status,
                              })
                            }
                            className="rounded-[8px] border border-(--edge-2) px-2.5 py-1 text-[11.5px] text-(--ice-2) transition-colors hover:bg-(--hov)"
                          >
                            ویرایش
                          </button>
                        )}
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            ))}

          {tabSection === "endorsements" &&
            (file.endorsements.length === 0 ? (
              <EmptyState
                icon="📎"
                title="الحاقیه‌ای ثبت نشده"
                description="برای این بیمه‌نامه هنوز الحاقیه‌ای صادر نشده است."
              />
            ) : (
              <Table>
                <thead>
                  <tr>
                    {["شمارهٔ الحاقیه", "نوع", "تاریخ صدور", "تغییر حق‌بیمه", "تغییر کارمزد", "توضیح"].map((h) => (
                      <Th key={h}>{h}</Th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {file.endorsements.map((e) => (
                    <Tr key={e.id}>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{e.endorsementNo}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{e.type}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{toJalaliDisplay(e.issueDate)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{money(e.premiumDelta)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{money(e.serviceFeeDelta)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{e.description ?? "—"}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            ))}

          {tabSection === "commissions" &&
            (file.commissions.length === 0 ? (
              <EmptyState
                icon="🪙"
                title="سهم پورسانتی ثبت نشده"
                description="برای این بیمه‌نامه هنوز سهم پورسانتی محاسبه نشده است."
              />
            ) : (
              <Table>
                <thead>
                  <tr>
                    {["قسط", "مبلغ", "وضعیت", "تاریخ قابل‌پرداخت‌شدن", "تاریخ پرداخت"].map((h) => (
                      <Th key={h}>{h}</Th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {file.commissions.map((c) => (
                    <Tr key={c.id}>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">
                        {c.installmentSeqNo ? `قسط ${fa(c.installmentSeqNo)}` : "پیش‌پرداخت"}
                      </Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{money(c.amount)}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{c.status}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{c.eligibleAt ? timeLabel(c.eligibleAt) : "—"}</Td>
                      <Td className="py-2.5 !text-[12.5px] text-(--ice-2)">{c.paidAt ? timeLabel(c.paidAt) : "—"}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            ))}

          {tabSection === "timeline" &&
            (file.timeline.length === 0 ? (
              <EmptyState
                icon="🕘"
                title="تاریخچه‌ای ثبت نشده"
                description="رخدادهای ثبت‌شدهٔ این بیمه‌نامه اینجا نمایش داده می‌شوند."
              />
            ) : (
              <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
                {file.timeline.map((t, idx) => (
                  <div key={idx} className="border-t border-(--edge) px-4 py-2.5 text-[12.5px] first:border-t-0">
                    <span className="font-semibold text-(--ice)">{t.actorDisplayName}</span>
                    <span className="text-(--ice-3)"> — {timeLabel(t.occurredAt)} — </span>
                    <span className="text-(--ice-2)">{t.description}</span>
                  </div>
                ))}
              </div>
            ))}
        </>
      )}

      {editingInstallment && (
        <EditInstallmentDialog
          target={editingInstallment}
          onClose={() => setEditingInstallment(null)}
          onSaved={load}
        />
      )}
    </div>
  );
}

function Fig({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
      <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">{label}</div>
      <div className="text-[20px] font-extrabold tracking-tight text-(--ice)">{value}</div>
    </div>
  );
}
