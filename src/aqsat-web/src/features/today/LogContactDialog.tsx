import * as Dialog from "@radix-ui/react-dialog";
import { useState } from "react";
import { api, ApiError } from "../../lib/api";
import { BTN_PRIMARY, BTN_SECONDARY, INPUT_CLASS, ErrorNote } from "../../components/form";
import { JalaliDateField } from "../../components/JalaliDateField";
import { MoneyInput } from "../../components/MoneyInput";

type Channel = "Call" | "Sms" | "InPerson" | "Portal";
type Outcome = "NoAnswer" | "Promised" | "Paid" | "Refused" | "WrongNumber";

export const CHANNEL_LABELS: Record<Channel, string> = {
  Call: "تماس تلفنی",
  Sms: "پیامک",
  InPerson: "حضوری",
  Portal: "درگاه پرداخت",
};

export const OUTCOME_LABELS: Record<Outcome, string> = {
  NoAnswer: "پاسخ نداد",
  Promised: "قول پرداخت",
  Paid: "پرداخت کرد",
  Refused: "امتناع کرد",
  WrongNumber: "شماره اشتباه",
};

/** Logs one collection contact against a policy. «قول پرداخت» is not a separate feature: it is this
 * form with Outcome = Promised, which is why the promise fields appear exactly there and are
 * required exactly there — the same rule the endpoint enforces. */
export function LogContactDialog({
  policyId,
  installmentId,
  customerFullName,
  suggestedAmount,
  onClose,
  onRecorded,
}: {
  policyId: string;
  installmentId: string | null;
  customerFullName: string;
  suggestedAmount: number;
  onClose: () => void;
  onRecorded: () => void;
}) {
  const [channel, setChannel] = useState<Channel>("Call");
  const [outcome, setOutcome] = useState<Outcome>("NoAnswer");
  const [promisedOn, setPromisedOn] = useState(new Date().toISOString().slice(0, 10));
  const [promisedAmount, setPromisedAmount] = useState(String(Math.round(suggestedAmount)));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    setError(null);

    // Mirrors the endpoint's rule so the agent is told before the round trip, not after.
    if (outcome === "Promised") {
      const amount = Number(promisedAmount);
      if (!Number.isFinite(amount) || amount <= 0) {
        setError("مبلغ قول پرداخت باید مثبت باشد.");
        return;
      }
    }

    setSubmitting(true);
    try {
      await api.post("/collection-contacts", {
        policyId,
        installmentId,
        channel,
        outcome,
        occurredAt: new Date().toISOString(),
        promisedOn: outcome === "Promised" ? promisedOn : null,
        promisedAmount: outcome === "Promised" ? Number(promisedAmount) : null,
      });
      onRecorded();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت تماس ناموفق بود.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog.Root open onOpenChange={(open) => !open && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[60] bg-black/55" />
        <Dialog.Content className="fixed inset-0 z-[60] grid place-items-center p-5">
          <div className="w-full max-w-[360px] rounded-(--r-lg) border border-(--edge-2) bg-(--slate) p-5.5 shadow-[var(--sh)]">
            <Dialog.Title className="mb-1.5 text-[15px] font-bold text-(--ice)">ثبت تماس</Dialog.Title>
            <Dialog.Description className="mb-4.5 text-[13.5px] text-(--ice-2)">{customerFullName}</Dialog.Description>

            {error && (
              <div className="mb-3">
                <ErrorNote>{error}</ErrorNote>
              </div>
            )}

            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">روش تماس</label>
            <select
              value={channel}
              onChange={(e) => setChannel(e.target.value as Channel)}
              className={`mb-3.5 ${INPUT_CLASS}`}
            >
              {(Object.keys(CHANNEL_LABELS) as Channel[]).map((key) => (
                <option key={key} value={key}>
                  {CHANNEL_LABELS[key]}
                </option>
              ))}
            </select>

            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">نتیجه</label>
            <select
              value={outcome}
              onChange={(e) => setOutcome(e.target.value as Outcome)}
              className={`mb-3.5 ${INPUT_CLASS}`}
            >
              {(Object.keys(OUTCOME_LABELS) as Outcome[]).map((key) => (
                <option key={key} value={key}>
                  {OUTCOME_LABELS[key]}
                </option>
              ))}
            </select>

            {outcome === "Promised" && (
              <div className="mb-3.5 rounded-(--r) border border-(--edge-2) bg-(--fld)/50 p-2.5">
                <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">تاریخ قول</label>
                <div className="mb-2.5">
                  <JalaliDateField value={promisedOn} onChange={setPromisedOn} className={`${INPUT_CLASS} tabular-nums`} />
                </div>
                <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">مبلغ قول (تومان)</label>
                <MoneyInput value={promisedAmount} onChange={setPromisedAmount} className={`${INPUT_CLASS} tabular-nums`} />
              </div>
            )}

            <div className="flex gap-2">
              <button type="button" onClick={submit} disabled={submitting} className={BTN_PRIMARY}>
                {submitting ? "در حال ثبت…" : "ثبت تماس"}
              </button>
              <button type="button" onClick={onClose} className={BTN_SECONDARY}>
                انصراف
              </button>
            </div>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
