import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay, toJalaliDisplay } from "../../lib/jalali";

interface CustomerFilePayload {
  customerId: string;
}

interface CustomerPolicySummaryDto {
  policyId: string;
  policyNumber: string;
  insuranceLineNameFa: string;
  status: string;
  totalReceivable: number;
  balance: number;
}

interface CustomerPaymentDto {
  id: string;
  amount: number;
  paidOn: string;
  method: string;
  referenceNo: string | null;
  allocatedTo: string[];
}

interface CustomerCollateralDto {
  id: string;
  policyNumber: string;
  type: string;
  amount: number;
  dueDate: string | null;
  status: string;
}

interface TimelineEntryDto {
  actorDisplayName: string;
  occurredAt: string;
  description: string;
}

interface PortalInvitationDto {
  id: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  status: "Pending" | "Paid" | "Expired";
  inquiryFeeToman: number;
  token: string;
}

interface CustomerFileDto {
  customerId: string;
  fullName: string;
  mobile: string | null;
  nationalIdMasked: string | null;
  aggregateBalance: number;
  policies: CustomerPolicySummaryDto[];
  payments: CustomerPaymentDto[];
  collateral: CustomerCollateralDto[];
  timeline: TimelineEntryDto[];
}

const timeLabel = toJalaliDateTimeDisplay;

export function CustomerFilePage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const openTab = useTabsStore((s) => s.openTab);
  const payload = tab?.payload as CustomerFilePayload | undefined;

  const [file, setFile] = useState<CustomerFileDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [section, setSection] = useState<"policies" | "payments" | "collateral" | "portal" | "timeline">("policies");

  const [invitations, setInvitations] = useState<PortalInvitationDto[] | null>(null);
  const [invitationsError, setInvitationsError] = useState<string | null>(null);
  const [issueBusy, setIssueBusy] = useState(false);
  const [issueMessage, setIssueMessage] = useState<string | null>(null);
  const [issueError, setIssueError] = useState<string | null>(null);

  useEffect(() => {
    if (!payload?.customerId) return;
    api
      .get<CustomerFileDto>(`/customers/${payload.customerId}/file`)
      .then((data) => {
        setFile(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری پروندهٔ مشتری"));
  }, [payload?.customerId]);

  useEffect(() => {
    if (!payload?.customerId || section !== "portal") return;
    loadInvitations();
  }, [payload?.customerId, section]);

  function loadInvitations() {
    setInvitations(null);
    setInvitationsError(null);
    api
      .get<PortalInvitationDto[]>(`/portal/invitations/customer/${payload!.customerId}`)
      .then(setInvitations)
      .catch((err) => setInvitationsError(err instanceof ApiError ? err.message : "خطا در بارگذاری دعوت‌نامه‌ها"));
  }

  async function issueLink() {
    setIssueBusy(true);
    setIssueMessage(null);
    setIssueError(null);
    try {
      const created = await api.post<PortalInvitationDto>("/portal/invitations", {
        customerId: payload!.customerId,
      });
      setIssueMessage(
        `لینک پورتال ساخته و برای مشتری پیامک شد:\n${window.location.origin}/portal/${created.token}`,
      );
      loadInvitations();
    } catch (err) {
      setIssueError(err instanceof ApiError ? err.message : "خطای غیرمنتظره");
    } finally {
      setIssueBusy(false);
    }
  }

  if (!payload) return null;

  function openPolicy(policyId: string, policyNumber: string) {
    openTab({
      navType: "policy-file",
      page: "policy-file",
      kind: "multi-record",
      recordId: policyId,
      title: policyNumber,
      payload: { policyId },
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
          <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
            پروندهٔ <em className="font-extralight not-italic text-(--ice-2)">{file.fullName}</em>
          </h2>
          <div className="mb-4.5 text-xs text-(--ice-3)">
            {file.mobile ? fa(file.mobile) : "بدون شمارهٔ همراه"}
            {file.nationalIdMasked && <> — کد ملی: {fa(file.nationalIdMasked)}</>}
          </div>

          <div className="mb-4.5 grid grid-cols-4 gap-3">
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">مانده کل</div>
              <div className={`text-[23px] font-extrabold tracking-tight ${file.aggregateBalance > 0 ? "text-(--ember)" : "text-(--mint)"}`}>
                {money(file.aggregateBalance)}
              </div>
            </div>
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">تعداد بیمه‌نامه</div>
              <div className="text-[23px] font-extrabold tracking-tight text-(--ice)">{fa(file.policies.length)}</div>
            </div>
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">پرداخت‌ها</div>
              <div className="text-[23px] font-extrabold tracking-tight text-(--ice)">{fa(file.payments.length)}</div>
            </div>
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">وثیقه</div>
              <div className="text-[23px] font-extrabold tracking-tight text-(--ice)">{fa(file.collateral.length)}</div>
            </div>
          </div>

          <div className="mb-3 flex gap-2">
            {(
              [
                ["policies", `بیمه‌نامه‌ها (${fa(file.policies.length)})`],
                ["payments", `پرداخت‌ها (${fa(file.payments.length)})`],
                ["collateral", `وثیقه (${fa(file.collateral.length)})`],
                ["portal", `پورتال (${fa(invitations?.length ?? 0)})`],
                ["timeline", `تاریخچه (${fa(file.timeline.length)})`],
              ] as const
            ).map(([key, label]) => (
              <button
                key={key}
                type="button"
                onClick={() => setSection(key)}
                className={`rounded-full px-3 py-1 text-[11.5px] font-semibold transition-colors ${
                  section === key ? "bg-(--mint) text-(--on-mint)" : "border border-(--edge-2) text-(--ice-3) hover:bg-(--hov)"
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          {section === "policies" &&
            (file.policies.length === 0 ? (
              <Empty text="بیمه‌نامه‌ای ثبت نشده." />
            ) : (
              <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
                <table className="w-full border-collapse">
                  <thead>
                    <tr>
                      {["بیمه‌نامه", "رشته", "وضعیت", "جمع دریافتی", "مانده"].map((h) => (
                        <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                          {h}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {file.policies.map((p) => (
                      <tr
                        key={p.policyId}
                        onClick={() => openPolicy(p.policyId, p.policyNumber)}
                        className="cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov)"
                      >
                        <td className="px-3 py-2.75 text-[13px] font-semibold">{fa(p.policyNumber)}</td>
                        <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{p.insuranceLineNameFa}</td>
                        <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{p.status}</td>
                        <td className="px-3 py-2.75 text-[13px]">{money(p.totalReceivable)}</td>
                        <td className={`px-3 py-2.75 text-[13px] font-bold ${p.balance > 0 ? "text-(--ember)" : "text-(--mint)"}`}>
                          {money(p.balance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}

          {section === "payments" &&
            (file.payments.length === 0 ? (
              <Empty text="پرداختی ثبت نشده." />
            ) : (
              <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
                <table className="w-full border-collapse">
                  <thead>
                    <tr>
                      {["تاریخ", "مبلغ", "روش", "شمارهٔ پیگیری", "بابت"].map((h) => (
                        <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                          {h}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {file.payments.map((p) => (
                      <tr key={p.id} className="border-t border-(--edge) first:border-t-0">
                        <td className="px-3 py-2.75 text-[13px]">{toJalaliDisplay(p.paidOn)}</td>
                        <td className="px-3 py-2.75 text-[13px] font-bold">{money(p.amount)}</td>
                        <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{p.method}</td>
                        <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{p.referenceNo ? fa(p.referenceNo) : "—"}</td>
                        <td className="px-3 py-2.75 text-[12px] text-(--ice-3)">{p.allocatedTo.join("، ") || "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}

          {section === "collateral" &&
            (file.collateral.length === 0 ? (
              <Empty text="وثیقه‌ای ثبت نشده." />
            ) : (
              <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
                <table className="w-full border-collapse">
                  <thead>
                    <tr>
                      {["بیمه‌نامه", "نوع", "مبلغ", "سررسید", "وضعیت"].map((h) => (
                        <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                          {h}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {file.collateral.map((c) => (
                      <tr key={c.id} className="border-t border-(--edge) first:border-t-0">
                        <td className="px-3 py-2.75 text-[13px]">{fa(c.policyNumber)}</td>
                        <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{c.type}</td>
                        <td className="px-3 py-2.75 text-[13px] font-bold">{money(c.amount)}</td>
                        <td className="px-3 py-2.75 text-[13px]">{toJalaliDisplay(c.dueDate)}</td>
                        <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{c.status}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}

          {section === "portal" && (
            <div>
              {issueError && (
                <div className="mb-3 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] leading-relaxed text-(--ember)">
                  {issueError}
                </div>
              )}
              {issueMessage && (
                <div className="mb-3 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] leading-relaxed text-(--mint)">
                  {issueMessage}
                </div>
              )}
              <button
                type="button"
                onClick={issueLink}
                disabled={issueBusy}
                className="mb-4.5 rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {issueBusy ? "در حال ساخت…" : "ارسال لینک پورتال به مشتری"}
              </button>

              {invitationsError && (
                <div className="rounded-2xl border border-(--ember)/30 bg-(--ember)/10 p-4 text-[13px] text-(--ember)">
                  {invitationsError}
                </div>
              )}
              {!invitationsError && invitations === null && (
                <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
              )}
              {invitations !== null &&
                (invitations.length === 0 ? (
                  <Empty text="هنوز لینکی برای این مشتری ساخته نشده." />
                ) : (
                  <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
                    <table className="w-full border-collapse">
                      <thead>
                        <tr>
                          {["تاریخ ساخت", "انقضا", "کارمزد", "وضعیت"].map((h) => (
                            <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                              {h}
                            </th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {invitations.map((i) => (
                          <tr key={i.id} className="border-t border-(--edge) first:border-t-0">
                            <td className="px-3 py-2.75 text-[13px]">{toJalaliDateTimeDisplay(i.createdAtUtc)}</td>
                            <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{toJalaliDateTimeDisplay(i.expiresAtUtc)}</td>
                            <td className="px-3 py-2.75 text-[13px] font-bold tabular-nums">{money(i.inquiryFeeToman)}</td>
                            <td className="px-3 py-2.75 text-[13px]">
                              <span
                                className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${
                                  i.status === "Paid"
                                    ? "bg-(--mint)/15 text-(--mint)"
                                    : i.status === "Pending"
                                      ? "bg-(--amber)/15 text-(--amber)"
                                      : "bg-(--ice-3)/15 text-(--ice-3)"
                                }`}
                              >
                                {i.status === "Paid" ? "پرداخت‌شده" : i.status === "Pending" ? "در انتظار پرداخت" : "منقضی"}
                              </span>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ))}
            </div>
          )}

          {section === "timeline" && (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              {file.timeline.length === 0 ? (
                <Empty text="تاریخچه‌ای ثبت نشده." />
              ) : (
                file.timeline.map((t, idx) => (
                  <div key={idx} className="border-t border-(--edge) px-4 py-2.5 text-[12.5px] first:border-t-0">
                    <span className="font-semibold text-(--ice)">{t.actorDisplayName}</span>
                    <span className="text-(--ice-3)"> — {timeLabel(t.occurredAt)} — </span>
                    <span className="text-(--ice-2)">{t.description}</span>
                  </div>
                ))
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function Empty({ text }: { text: string }) {
  return <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">{text}</div>;
}
