import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay, toJalaliDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import { CreditReportCard, type CreditReportDto } from "../../components/CreditReportCard";
import { CustomerRiskPanel } from "../risk/CustomerRiskPanel";

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

interface CustomerCreditReportDto {
  report: CreditReportDto | null;
  isReusableForIssuance: boolean;
  validUntilUtc: string | null;
  failedStandaloneInvitationId: string | null;
}

interface PaymentLinkStatusDto {
  hasActiveLink: boolean;
  token: string | null;
  expiresAtUtc: string | null;
  lastSentAtUtc: string | null;
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
  const [section, setSection] = useState<"policies" | "payments" | "collateral" | "risk" | "portal" | "timeline">("policies");

  const [invitations, setInvitations] = useState<PortalInvitationDto[] | null>(null);
  const [invitationsError, setInvitationsError] = useState<string | null>(null);
  const [issueBusy, setIssueBusy] = useState(false);
  const [issueMessage, setIssueMessage] = useState<string | null>(null);
  const [issueError, setIssueError] = useState<string | null>(null);

  // The latest credit report — standalone (this page's portal link) or from any policy chain.
  const [creditReport, setCreditReport] = useState<CustomerCreditReportDto | null>(null);
  const [creditReportError, setCreditReportError] = useState<string | null>(null);
  const [retryBusy, setRetryBusy] = useState(false);

  // The customer's long-lived installment-payment link (/pay/{token}) — minted and refreshed by
  // the SMS reminder job, revocable here.
  const [paymentLink, setPaymentLink] = useState<PaymentLinkStatusDto | null>(null);
  const [paymentLinkError, setPaymentLinkError] = useState<string | null>(null);
  const [revokeBusy, setRevokeBusy] = useState(false);

  const loadFile = () => {
    if (!payload?.customerId) return;
    api
      .get<CustomerFileDto>(`/customers/${payload.customerId}/file`)
      .then((data) => {
        setFile(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری پروندهٔ مشتری"));
  };

  useEffect(loadFile, [payload?.customerId]);

  useLiveReload(loadFile);

  useEffect(() => {
    if (!payload?.customerId || section !== "portal") return;
    loadInvitations();
    loadCreditReport();
    loadPaymentLink();
  }, [payload?.customerId, section]);

  function loadPaymentLink() {
    setPaymentLink(null);
    setPaymentLinkError(null);
    api
      .get<PaymentLinkStatusDto>(`/portal/links/customer/${payload!.customerId}`)
      .then(setPaymentLink)
      .catch((err) => setPaymentLinkError(err instanceof ApiError ? err.message : "خطا در بارگذاری لینک پرداخت"));
  }

  async function revokePaymentLink() {
    setRevokeBusy(true);
    setPaymentLinkError(null);
    try {
      await api.post(`/portal/links/customer/${payload!.customerId}/revoke`);
      loadPaymentLink();
    } catch (err) {
      setPaymentLinkError(err instanceof ApiError ? err.message : "لغو لینک ناموفق بود.");
    } finally {
      setRevokeBusy(false);
    }
  }

  function loadInvitations() {
    setInvitations(null);
    setInvitationsError(null);
    api
      .get<PortalInvitationDto[]>(`/portal/invitations/customer/${payload!.customerId}`)
      .then(setInvitations)
      .catch((err) => setInvitationsError(err instanceof ApiError ? err.message : "خطا در بارگذاری دعوت‌نامه‌ها"));
  }

  function loadCreditReport() {
    setCreditReport(null);
    setCreditReportError(null);
    api
      .get<CustomerCreditReportDto>(`/portal/invitations/customer/${payload!.customerId}/credit-report`)
      .then(setCreditReport)
      .catch((err) => setCreditReportError(err instanceof ApiError ? err.message : "خطا در بارگذاری گزارش اعتباری"));
  }

  /// Retry only re-fires a paid-but-failed standalone inquiry — no second fee is ever charged.
  async function retryStandaloneInquiries(invitationId: string) {
    setRetryBusy(true);
    setCreditReportError(null);
    try {
      setCreditReport(await api.post<CustomerCreditReportDto>(`/portal/invitations/${invitationId}/retry-inquiries`));
    } catch (err) {
      setCreditReportError(err instanceof ApiError ? err.message : "تلاش مجدد استعلام ناموفق بود.");
    } finally {
      setRetryBusy(false);
    }
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
          <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
            {file.mobile ? fa(file.mobile) : "بدون شمارهٔ همراه"}
            {file.nationalIdMasked && <> — کد ملی: {fa(file.nationalIdMasked)}</>}
          </div>

          <div className="mb-4.5 grid grid-cols-4 gap-3">
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">مانده کل</div>
              <div className={`text-[20px] font-extrabold tracking-tight ${file.aggregateBalance > 0 ? "text-(--ember)" : "text-(--mint)"}`}>
                {money(file.aggregateBalance)}
              </div>
            </div>
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">تعداد بیمه‌نامه</div>
              <div className="text-[20px] font-extrabold tracking-tight text-(--ice)">{fa(file.policies.length)}</div>
            </div>
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">پرداخت‌ها</div>
              <div className="text-[20px] font-extrabold tracking-tight text-(--ice)">{fa(file.payments.length)}</div>
            </div>
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">وثیقه</div>
              <div className="text-[20px] font-extrabold tracking-tight text-(--ice)">{fa(file.collateral.length)}</div>
            </div>
          </div>

          <div className="mb-3 flex gap-2">
            {(
              [
                ["policies", `بیمه‌نامه‌ها (${fa(file.policies.length)})`],
                ["payments", `پرداخت‌ها (${fa(file.payments.length)})`],
                ["collateral", `وثیقه (${fa(file.collateral.length)})`],
                ["risk", "اعتبار و ریسک"],
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
              <EmptyState
                icon="📄"
                title="بیمه‌نامه‌ای ثبت نشده."
                description="این مشتری هنوز بیمه‌نامه‌ای در سیستم ندارد."
              />
            ) : (
              <Table>
                <thead>
                  <tr>
                    {["بیمه‌نامه", "رشته", "وضعیت", "جمع دریافتی", "مانده"].map((h) => (
                      <Th key={h}>{h}</Th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {file.policies.map((p) => (
                    <Tr key={p.policyId} onClick={() => openPolicy(p.policyId, p.policyNumber)}>
                      <Td className="py-2.75 font-semibold">{fa(p.policyNumber)}</Td>
                      <Td className="py-2.75 text-(--ice-3)">{p.insuranceLineNameFa}</Td>
                      <Td className="py-2.75 text-(--ice-3)">{p.status}</Td>
                      <Td className="py-2.75">{money(p.totalReceivable)}</Td>
                      <Td className={`py-2.75 font-bold ${p.balance > 0 ? "text-(--ember)" : "text-(--mint)"}`}>
                        {money(p.balance)}
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            ))}

          {section === "payments" &&
            (file.payments.length === 0 ? (
              <EmptyState
                icon="💳"
                title="پرداختی ثبت نشده."
                description="برای این مشتری هنوز پرداختی در سیستم ثبت نشده است."
              />
            ) : (
              <Table>
                <thead>
                  <tr>
                    {["تاریخ", "مبلغ", "روش", "شمارهٔ پیگیری", "بابت"].map((h) => (
                      <Th key={h}>{h}</Th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {file.payments.map((p) => (
                    <Tr key={p.id}>
                      <Td className="py-2.75">{toJalaliDisplay(p.paidOn)}</Td>
                      <Td className="py-2.75 font-bold">{money(p.amount)}</Td>
                      <Td className="py-2.75 text-(--ice-3)">{p.method}</Td>
                      <Td className="py-2.75 text-(--ice-3)">{p.referenceNo ? fa(p.referenceNo) : "—"}</Td>
                      <Td className="py-2.75 !text-[12.5px] text-(--ice-3)">{p.allocatedTo.join("، ") || "—"}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            ))}

          {section === "collateral" &&
            (file.collateral.length === 0 ? (
              <EmptyState
                icon="🔒"
                title="وثیقه‌ای ثبت نشده."
                description="برای بیمه‌نامه‌های این مشتری وثیقه‌ای ثبت نشده است."
              />
            ) : (
              <Table>
                <thead>
                  <tr>
                    {["بیمه‌نامه", "نوع", "مبلغ", "سررسید", "وضعیت"].map((h) => (
                      <Th key={h}>{h}</Th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {file.collateral.map((c) => (
                    <Tr key={c.id}>
                      <Td className="py-2.75">{fa(c.policyNumber)}</Td>
                      <Td className="py-2.75 text-(--ice-3)">{c.type}</Td>
                      <Td className="py-2.75 font-bold">{money(c.amount)}</Td>
                      <Td className="py-2.75">{toJalaliDisplay(c.dueDate)}</Td>
                      <Td className="py-2.75 text-(--ice-3)">{c.status}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            ))}

          {section === "risk" && <CustomerRiskPanel customerId={payload!.customerId} />}

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

              <div className="mb-2.5 text-[13px] font-bold text-(--ice)">گزارش اعتباری</div>
              {creditReportError ? (
                <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] leading-relaxed text-(--ember)">
                  {creditReportError}
                  <button
                    type="button"
                    onClick={loadCreditReport}
                    className="ms-2 rounded-[8px] border border-(--ember)/50 px-2.5 py-1 text-[11.5px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/10"
                  >
                    تلاش مجدد
                  </button>
                </div>
              ) : creditReport === null ? (
                <div className="mb-4.5 text-[12.5px] text-(--ice-3)">در حال بارگذاری گزارش اعتباری…</div>
              ) : creditReport.report ? (
                <div className="mb-4.5">
                  <CreditReportCard report={creditReport.report} />
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    {creditReport.isReusableForIssuance ? (
                      <span className="rounded-full border border-(--mint)/40 bg-(--mint)/10 px-3 py-1 text-[11.5px] font-semibold text-(--mint)">
                        معتبر تا {toJalaliDateTimeDisplay(creditReport.validUntilUtc!)} — در صدور بیمه‌نامهٔ جدید بازیافت میشود
                      </span>
                    ) : (
                      <span className="rounded-full border border-(--amber)/40 bg-(--amber)/10 px-3 py-1 text-[11.5px] font-semibold text-(--amber)">
                        گزارش تازه نیست (بیش از ۳۰ روز) — در صدور جدید دوباره استعلام و کارمزد لازم است
                      </span>
                    )}
                  </div>
                </div>
              ) : (
                <div className="mb-4.5">
                  {creditReport.failedStandaloneInvitationId ? (
                    <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2.5 text-[12.5px] leading-relaxed text-(--ember)">
                      کارمزد پرداخت شد اما استعلام ناموفق بود. کارمزد دوباره گرفته نمیشود.
                      <button
                        type="button"
                        onClick={() => void retryStandaloneInquiries(creditReport.failedStandaloneInvitationId!)}
                        disabled={retryBusy}
                        className="ms-2 rounded-[8px] border border-(--mint) bg-(--mint) px-3 py-1 text-[11.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                      >
                        {retryBusy ? "در حال تلاش…" : "تلاش مجدد استعلام"}
                      </button>
                    </div>
                  ) : (
                    <EmptyState
                      icon="🧾"
                      title="گزارش اعتباری‌ای برای این مشتری ثبت نشده."
                      description="با دکمهٔ «ارسال لینک پورتال به مشتری»، مشتری کارمزد را آنلاین میپردازد و استعلام چک برگشتی و تسهیلات به‌صورت خودکار اجرا و همین‌جا نمایش داده میشود."
                    />
                  )}
                </div>
              )}

              {invitationsError && (
                <div className="rounded-2xl border border-(--ember)/30 bg-(--ember)/10 p-4 text-[13.5px] text-(--ember)">
                  {invitationsError}
                </div>
              )}
              {!invitationsError && invitations === null && (
                <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
              )}
              {invitations !== null &&
                (invitations.length === 0 ? (
                  <EmptyState
                    icon="🔗"
                    title="هنوز لینکی برای این مشتری ساخته نشده."
                    description="با دکمهٔ بالا می‌توانید لینک پورتال بسازید و برای مشتری پیامک کنید."
                  />
                ) : (
                  <Table>
                    <thead>
                      <tr>
                        {["تاریخ ساخت", "انقضا", "کارمزد", "وضعیت"].map((h) => (
                          <Th key={h}>{h}</Th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {invitations.map((i) => (
                        <Tr key={i.id}>
                          <Td className="py-2.75">{toJalaliDateTimeDisplay(i.createdAtUtc)}</Td>
                          <Td className="py-2.75 text-(--ice-3)">{toJalaliDateTimeDisplay(i.expiresAtUtc)}</Td>
                          <Td className="py-2.75 font-bold tabular-nums">{money(i.inquiryFeeToman)}</Td>
                          <Td className="py-2.75">
                            <span
                              className={`rounded-full px-2 py-0.5 text-[11.5px] font-semibold ${
                                i.status === "Paid"
                                  ? "bg-(--mint)/15 text-(--mint)"
                                  : i.status === "Pending"
                                    ? "bg-(--amber)/15 text-(--amber)"
                                    : "bg-(--ice-3)/15 text-(--ice-3)"
                              }`}
                            >
                              {i.status === "Paid" ? "پرداخت‌شده" : i.status === "Pending" ? "در انتظار پرداخت" : "منقضی"}
                            </span>
                          </Td>
                        </Tr>
                      ))}
                    </tbody>
                  </Table>
                ))}

              <div className="mt-4.5 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2.5">
                <div className="mb-1 text-[12.5px] font-semibold text-(--ice)">لینک پرداخت آنلاین اقساط</div>
                {paymentLinkError ? (
                  <div className="text-[12px] leading-relaxed text-(--ember)">
                    {paymentLinkError}
                    <button
                      type="button"
                      onClick={loadPaymentLink}
                      className="ms-2 rounded-[8px] border border-(--ember)/50 px-2.5 py-1 text-[11px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/10"
                    >
                      تلاش مجدد
                    </button>
                  </div>
                ) : paymentLink === null ? (
                  <div className="text-[12px] text-(--ice-3)">در حال بارگذاری…</div>
                ) : paymentLink.hasActiveLink ? (
                  <div className="text-[12px] leading-relaxed text-(--ice-2)">
                    پیامک یادآوری اقساط به‌صورت خودکار لینک پرداخت برای این مشتری می‌سازد و تازه می‌کند.
                    <div className="mt-1 text-(--ice-3)">
                      اعتبار تا {toJalaliDateTimeDisplay(paymentLink.expiresAtUtc!)}
                      {paymentLink.lastSentAtUtc && <> — آخرین ارسال {toJalaliDateTimeDisplay(paymentLink.lastSentAtUtc)}</>}
                    </div>
                    <button
                      type="button"
                      onClick={revokePaymentLink}
                      disabled={revokeBusy}
                      className="mt-2 rounded-[8px] border border-(--ember)/50 px-3 py-1 text-[11.5px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/10 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                      {revokeBusy ? "در حال لغو…" : "لغو لینک پرداخت"}
                    </button>
                  </div>
                ) : (
                  <div className="text-[12px] leading-relaxed text-(--ice-3)">
                    لینک فعالی برای این مشتری وجود ندارد. با فعال بودن پورتال مشتری، اولین پیامک یادآوری قسط
                    به‌صورت خودکار آن را می‌سازد.
                  </div>
                )}
              </div>
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
  return <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13.5px] text-(--ice-3)">{text}</div>;
}
