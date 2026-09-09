import { useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import { useAssignReview, useDecideReview, useManualReviews, useRequestMoreInfo, useReviewers } from "./riskApi";
import { RISK_DECISION_STYLES, RISK_LEVEL_STYLES, type ManualReviewDto } from "./riskTypes";

const BTN_PRIMARY =
  "rounded-[10px] border border-(--mint) bg-(--mint) px-3.5 py-1.5 text-[11.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50";

const BTN_DANGER =
  "rounded-[10px] border border-(--ember)/60 bg-(--ember)/10 px-3.5 py-1.5 text-[11.5px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/20 disabled:cursor-not-allowed disabled:opacity-50";

const BTN_SECONDARY =
  "rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-3.5 py-1.5 text-[11.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice) disabled:cursor-not-allowed disabled:opacity-50";

const INPUT_CLASS =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)";

type StatusFilter = "" | "Pending" | "InReview" | "RequestMoreInfo" | "Approved" | "Rejected";

const STATUS_FILTERS: { value: StatusFilter; label: string }[] = [
  { value: "", label: "صف باز" },
  { value: "Pending", label: "در انتظار بررسی" },
  { value: "InReview", label: "در حال بررسی" },
  { value: "RequestMoreInfo", label: "نیاز به اطلاعات بیشتر" },
  { value: "Approved", label: "تأییدشده" },
  { value: "Rejected", label: "ردشده" },
];

/** «بررسی‌های دستی» (docs Phase 2A §15) — the queue of cases the rule engine could not decide:
 * evidence snapshot, triggered rules, assignment, and the reviewer's final APPROVE/REJECT call. */
export function ManualReviewsPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [status, setStatus] = useState<StatusFilter>("");
  const reviews = useManualReviews(status === "" ? null : status);
  const reviewers = useReviewers();

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = reviews.data?.find((r) => r.id === selectedId) ?? null;

  function openCustomer(r: ManualReviewDto) {
    openTab({
      navType: "customer-file",
      page: "customer-file",
      kind: "multi-record",
      recordId: r.customerId,
      title: r.customerName,
      payload: { customerId: r.customerId },
    });
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">بررسی‌های دستی اعتبار</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        پرونده‌هایی که موتور قواعد نتوانسته برایشان تصمیم قطعی بگیرد — با شواهد، قوانین فعال‌شده و تصمیم کارشناس
      </div>

      <div className="mb-3 flex flex-wrap gap-2">
        {STATUS_FILTERS.map((f) => (
          <button
            key={f.value}
            type="button"
            onClick={() => setStatus(f.value)}
            className={`rounded-full px-3 py-1 text-[11.5px] font-semibold transition-colors ${
              status === f.value ? "bg-(--mint) text-(--on-mint)" : "border border-(--edge-2) text-(--ice-3) hover:bg-(--hov)"
            }`}
          >
            {f.label}
          </button>
        ))}
      </div>

      {reviews.isPending && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {reviews.isError && (
        <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {reviews.error instanceof ApiError ? reviews.error.message : "خطا در بارگذاری صف بررسی"}
          <button type="button" onClick={() => void reviews.refetch()} className="ms-2 underline">
            تلاش مجدد
          </button>
        </div>
      )}

      {reviews.data && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(reviews.data.length)} پرونده</div>
          {reviews.data.length === 0 ? (
            <EmptyState
              icon="🗂️"
              title="پرونده‌ای در این وضعیت نیست."
              description="هر ارزیابی‌ای که تصمیم آن «بررسی دستی» باشد، اینجا وارد می‌شود."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["مشتری", "امتیاز", "سطح", "بدهی جاری", "معوق", "چک برگشتی", "وضعیت", "مسئول بررسی", "تاریخ"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {reviews.data.map((r) => (
                  <Tr key={r.id} onClick={() => setSelectedId(selectedId === r.id ? null : r.id)} className="cursor-pointer">
                    <Td className="py-2.75 font-semibold">{r.customerName}</Td>
                    <Td className="py-2.75 font-bold tabular-nums">{fa(r.score)}</Td>
                    <Td className="py-2.75">
                      <span className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${RISK_LEVEL_STYLES[r.riskLevelKey].chip}`}>
                        {RISK_LEVEL_STYLES[r.riskLevelKey].label}
                      </span>
                    </Td>
                    <Td className="py-2.75 tabular-nums">{money(r.currentDebtToman)}</Td>
                    <Td className="py-2.75 tabular-nums text-(--ember)">{money(r.overdueAmountToman)}</Td>
                    <Td className="py-2.75 tabular-nums">{fa(r.returnedChequeCount)}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{r.statusFa}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{r.assignedToName ?? "—"}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{toJalaliDateTimeDisplay(r.createdAt)}</Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </>
      )}

      {selected && (
        <ReviewDetail
          review={selected}
          reviewers={reviewers.data ?? []}
          onOpenCustomer={() => openCustomer(selected)}
        />
      )}
    </div>
  );
}

function ReviewDetail({
  review,
  reviewers,
  onOpenCustomer,
}: {
  review: ManualReviewDto;
  reviewers: { id: string; fullName: string }[];
  onOpenCustomer: () => void;
}) {
  const assign = useAssignReview();
  const decide = useDecideReview();
  const requestInfo = useRequestMoreInfo();
  const [note, setNote] = useState(review.note ?? "");
  const [assignee, setAssignee] = useState("");
  const open = review.status !== "Approved" && review.status !== "Rejected";
  const level = RISK_LEVEL_STYLES[review.riskLevelKey];
  const recommended = RISK_DECISION_STYLES[review.recommendedDecisionKey];

  const error =
    (assign.isError && (assign.error as ApiError).message) ||
    (decide.isError && (decide.error as ApiError).message) ||
    (requestInfo.isError && (requestInfo.error as ApiError).message) ||
    null;

  return (
    <div className="mt-4 rounded-2xl border border-(--edge) bg-(--pane) p-5">
      <div className="flex flex-wrap items-center gap-2.5">
        <b className="text-[14px] text-(--ice)">{review.customerName}</b>
        <span className={`rounded-full border px-2.5 py-0.5 text-[11.5px] font-semibold ${level.chip}`}>
          امتیاز {fa(review.score)} — {level.label}
        </span>
        <span className={`rounded-full border px-2.5 py-0.5 text-[11.5px] font-semibold ${recommended.chip}`}>
          پیشنهاد سیستم: {recommended.label}
        </span>
        {review.finalDecisionKey && (
          <span className={`rounded-full border px-2.5 py-0.5 text-[11.5px] font-semibold ${RISK_DECISION_STYLES[review.finalDecisionKey].chip}`}>
            تصمیم نهایی: {RISK_DECISION_STYLES[review.finalDecisionKey].label}
          </span>
        )}
        <button type="button" onClick={onOpenCustomer} className="ms-auto text-[11.5px] font-semibold text-(--mint) underline underline-offset-2">
          پروندهٔ مشتری
        </button>
      </div>

      <div className="mt-3 grid grid-cols-4 gap-3">
        <MiniStat label="بدهی جاری" value={money(review.currentDebtToman)} />
        <MiniStat label="مبلغ معوق" value={money(review.overdueAmountToman)} warn={review.overdueAmountToman > 0} />
        <MiniStat label="تعداد معوق" value={fa(review.overdueCount)} warn={review.overdueCount > 0} />
        <MiniStat label="چک برگشتی" value={fa(review.returnedChequeCount)} warn={review.returnedChequeCount > 0} />
      </div>

      {review.triggeredRules.length > 0 && (
        <div className="mt-3">
          <div className="mb-1.5 text-[11.5px] tracking-wider text-(--ice-3)">قوانین فعال‌شده</div>
          <div className="flex flex-wrap gap-1.5">
            {review.triggeredRules.map((r) => (
              <span
                key={r.code}
                title={r.description}
                className={`rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${
                  r.isPositive
                    ? "border-(--mint)/40 bg-(--mint)/10 text-(--mint)"
                    : "border-(--ember)/40 bg-(--ember)/10 text-(--ember)"
                }`}
              >
                {r.name}
              </span>
            ))}
          </div>
        </div>
      )}

      {error && (
        <div className="mt-3 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12px] text-(--ember)">{error}</div>
      )}

      <label className="mt-4 block">
        <span className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">یادداشت تصمیم (شواهد و دلیل، نه توصیف مشتری)</span>
        <input value={note} onChange={(e) => setNote(e.target.value)} className={INPUT_CLASS} />
      </label>

      {open && (
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => decide.mutate({ reviewId: review.id, finalDecision: "Approve", note: note.trim() || null })}
            disabled={decide.isPending}
            className={BTN_PRIMARY}
          >
            تأیید صدور
          </button>
          <button
            type="button"
            onClick={() => decide.mutate({ reviewId: review.id, finalDecision: "Decline", note: note.trim() || null })}
            disabled={decide.isPending}
            className={BTN_DANGER}
          >
            رد
          </button>
          <button
            type="button"
            onClick={() => requestInfo.mutate({ reviewId: review.id, note: note.trim() || null })}
            disabled={requestInfo.isPending}
            className={BTN_SECONDARY}
          >
            درخواست اطلاعات بیشتر
          </button>

          <div className="ms-auto flex items-center gap-2">
            <select
              value={assignee}
              onChange={(e) => setAssignee(e.target.value)}
              className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-2.5 py-1.5 text-[11.5px] text-(--ice) outline-none focus:border-(--mint)"
            >
              <option value="">واگذاری به…</option>
              {reviewers.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.fullName}
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={() => {
                assign.mutate({ reviewId: review.id, assignedToUserId: assignee || null });
                setAssignee("");
              }}
              disabled={assign.isPending || assignee === ""}
              className={BTN_SECONDARY}
            >
              واگذاری
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function MiniStat({ label, value, warn }: { label: string; value: string; warn?: boolean }) {
  return (
    <div className="rounded-[12px] border border-(--edge-2) bg-(--fld) p-2.5">
      <div className="mb-0.5 text-[10px] tracking-[0.14em] text-(--ice-3)">{label}</div>
      <div className={`text-[14px] font-extrabold tabular-nums ${warn ? "text-(--ember)" : "text-(--ice)"}`}>{value}</div>
    </div>
  );
}
