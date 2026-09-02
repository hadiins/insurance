import { useEffect, useMemo, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { useDraftState } from "../shell/useDraftState";
import { api, ApiError } from "../../lib/api";
import { fa, isValidNationalId, money, toLatinDigits } from "../../lib/persian";
import { PolicyNumberField, type PolicyNumberSuggestionDto } from "./PolicyNumberField";
import { PlateField, EMPTY_PLATE, isPlateFilled, type PlateParts } from "./PlateField";
import { JalaliDateField } from "../../components/JalaliDateField";
import { addOneJalaliYear, toJalaliDisplay } from "../../lib/jalali";
import { MoneyInput } from "../../components/MoneyInput";
import { PolicyVerificationStep } from "./PolicyVerificationStep";

interface InsuranceLineDto {
  id: string;
  parentId: string | null;
  code: string;
  nameFa: string;
  requiresVehicle: boolean;
  requiresProperty: boolean;
  sortOrder: number;
}

interface CreatePolicyResultDto {
  policyId: string;
  policyNumber: string;
  customerId: string;
}

interface MarketerOptionDto {
  id: string;
  fullName: string;
  isActive: boolean;
}

interface MarketerRateLiteDto {
  id: string;
  insuranceLineId: string;
  ratePercent: number;
  effectiveFrom: string;
  effectiveTo: string | null;
}

interface CustomerLookupProfileDto {
  id: string;
  fullName: string;
  firstName: string | null;
  lastName: string | null;
  nationalId: string | null;
  mobile: string | null;
  emergencyMobile: string | null;
  address: string | null;
  postalCode: string | null;
  isProfileComplete: boolean;
}

interface CustomerLookupResultDto {
  found: boolean;
  customer: CustomerLookupProfileDto | null;
  policyCount: number;
}

interface ScheduleInstallmentDto {
  seqNo: number;
  dueDate: string;
  amount: number;
}

interface ScheduleResultDto {
  policyId: string;
  exceedsMaxInstallments: boolean;
  installments: ScheduleInstallmentDto[];
}

interface CashBoxDto {
  id: string;
  name: string;
  isActive: boolean;
}

interface BankAccountDto {
  id: string;
  bankName: string;
  accountNumber: string;
  isActive: boolean;
}

type MethodType = "Cash" | "BankTransfer" | "Cheque" | "PosDirect";

/** Step 4 (اعتبارسنجی) only exists on the installment path; cash jumps 2 → 5. */
type Step = 1 | 2 | 3 | 4 | 5;

type PaymentType = "installment" | "cash";

interface WizardDraft {
  step: Step;
  form: FormState;
  plate: PlateParts;
  nationalIdInput: string;
  serialInput: string;
  manualEntry: boolean;
  manualNumberInput: string;
  paymentType: PaymentType;
}

const TODAY = new Date().toISOString().slice(0, 10);

const STEP_LABELS: Record<Step, string> = {
  1: "مشتری",
  2: "بیمهنامه",
  3: "اقساط",
  4: "اعتبارسنجی",
  5: "دریافت و ثبت نهایی",
};

const METHOD_LABELS: Record<MethodType, string> = {
  Cash: "نقدی",
  BankTransfer: "واریز بانکی",
  Cheque: "چک",
  PosDirect: "پوز مستقیم بیمهگر",
};

const INPUT_CLASS =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)";

const BTN_PRIMARY =
  "rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50";

const BTN_SECONDARY =
  "rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)";
interface FormState {
  customerFirstName: string;
  customerLastName: string;
  customerMobile: string;
  customerEmergencyMobile: string;
  customerNationalId: string;
  customerAddress: string;
  customerPostalCode: string;
  insuranceLineId: string;
  marketerId: string;
  insurerName: string;
  propertyAddress: string;
  propertyPostalCode: string;
  netPremium: string;
  serviceFee: string;
  issueDate: string;
  startDate: string;
  endDate: string;
  vin: string;
  chassis: string;
  engineNumber: string;
  vehicleType: string;
  manufactureYear: string;
}

const EMPTY: FormState = {
  customerFirstName: "",
  customerLastName: "",
  customerMobile: "",
  customerEmergencyMobile: "",
  customerNationalId: "",
  customerAddress: "",
  customerPostalCode: "",
  insuranceLineId: "",
  marketerId: "",
  insurerName: "",
  propertyAddress: "",
  propertyPostalCode: "",
  netPremium: "",
  serviceFee: "",
  issueDate: TODAY,
  startDate: TODAY,
  endDate: addOneJalaliYear(TODAY) ?? "",
  vin: "",
  chassis: "",
  engineNumber: "",
  vehicleType: "",
  manufactureYear: "",
};

/** The issuance wizard (owner decision 2026-08-28): everything starts with the customer's national
 * ID (step 1), then the policy is issued in full (step 2), then the schedule — down payment plus
 * installments — is generated right here (step 3), and the wizard finishes only after the defined
 * down payment is actually received (step 4); with no down payment at all the agent finalizes
 * straight after reviewing the file summary. Replaces the old single-page form and its
 * "issue now, schedule later from a separate page" detour. */
export function NewPolicyPage() {
  const tabKey = useTabKey();
  const setDirty = useTabsStore((s) => s.setDirty);
  const setTitle = useTabsStore((s) => s.setTitle);
  const openTab = useTabsStore((s) => s.openTab);

  const [step, setStep] = useState<Step>(1);
  const [form, setForm] = useState<FormState>(EMPTY);
  const [lines, setLines] = useState<InsuranceLineDto[] | null>(null);
  const [marketers, setMarketers] = useState<MarketerOptionDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // Step 1 — national-ID lookup. customerMode records which path the agent took: an existing
  // customer found by the lookup, or inline registration for a valid-but-unknown ID.
  const [nationalIdInput, setNationalIdInput] = useState("");
  const [lookupLoading, setLookupLoading] = useState(false);
  const [lookupCustomer, setLookupCustomer] = useState<CustomerLookupProfileDto | null>(null);
  const [lookupPolicyCount, setLookupPolicyCount] = useState(0);
  const [customerMode, setCustomerMode] = useState<"none" | "existing" | "new">("none");

  // docs/TASK-24-POLICY-NUMBER.md §2 — the number is composed from three locked segments plus one
  // editable serial, unless the "ورود دستی شمارهٔ کامل" escape hatch is on.
  const [suggestion, setSuggestion] = useState<PolicyNumberSuggestionDto | null>(null);
  const [suggestionLoading, setSuggestionLoading] = useState(false);
  const [serialInput, setSerialInput] = useState("");
  const [manualEntry, setManualEntry] = useState(false);
  const [manualNumberInput, setManualNumberInput] = useState("");
  const [gapConfirmed, setGapConfirmed] = useState(false);
  const [gapPrompt, setGapPrompt] = useState<string[] | null>(null);

  // docs/TASK-24-POLICY-NUMBER.md §6 — non-blocking cross-checks, relevant only to the manual
  // escape hatch (the structured form's segments can't disagree by construction).
  const [numberWarnings, setNumberWarnings] = useState<string[]>([]);
  const [numberWarningsConfirmed, setNumberWarningsConfirmed] = useState(false);
  const [mismatchPrompt, setMismatchPrompt] = useState<string[] | null>(null);

  const [plate, setPlate] = useState<PlateParts>(EMPTY_PLATE);

  // Step 3 — the schedule. The policy row itself is created at the step-2→3 transition.
  const [created, setCreated] = useState<CreatePolicyResultDto | null>(null);
  const [installmentCount, setInstallmentCount] = useState("");
  const [downPayment, setDownPayment] = useState("");
  const [downPaymentTouched, setDownPaymentTouched] = useState(false);
  const [scheduling, setScheduling] = useState(false);
  const [scheduleResult, setScheduleResult] = useState<ScheduleResultDto | null>(null);
  const [scheduledDownPayment, setScheduledDownPayment] = useState(0);

  // Step 4 — down-payment receipt + finalize, same shape as SchedulePolicyPage's receipt block.
  const [cashBoxes, setCashBoxes] = useState<CashBoxDto[]>([]);
  const [bankAccounts, setBankAccounts] = useState<BankAccountDto[]>([]);
  const [receivePaidOn, setReceivePaidOn] = useState(TODAY);
  const [receiveMethodType, setReceiveMethodType] = useState<MethodType>("Cash");
  const [receiveCashBoxId, setReceiveCashBoxId] = useState("");
  const [receiveBankAccountId, setReceiveBankAccountId] = useState("");
  const [receiveReferenceNo, setReceiveReferenceNo] = useState("");
  const [receiving, setReceiving] = useState(false);
  const [received, setReceived] = useState(false);
  const [chequeNumber, setChequeNumber] = useState("");
  const [chequeBankName, setChequeBankName] = useState("");
  const [chequeDueDate, setChequeDueDate] = useState(TODAY);
  const [chequePresenterName, setChequePresenterName] = useState("");
  const [finalized, setFinalized] = useState(false);

  // Step 2's نقدی/اقساطی choice (owner decision 2026-09-01) — it decides the whole tail of the
  // wizard: cash goes straight to the full-payment receipt, installment goes through scheduling
  // and the portal verification chain.
  const [paymentType, setPaymentType] = useState<PaymentType>("installment");
  const [verificationRejected, setVerificationRejected] = useState(false);

  // Draft survival across refresh — only steps 1-2 are safe to restore: from step 3 on the policy
  // row already exists server-side (`created`), and re-running the wizard would duplicate it.
  const [draft, setDraft, clearDraft] = useDraftState<WizardDraft | null>("wizard", null);
  const [draftRestored, setDraftRestored] = useState(false);

  useEffect(() => {
    if (draft === null || draft.step > 2) {
      clearDraft();
    } else {
      setForm(draft.form);
      setPlate(draft.plate);
      setNationalIdInput(draft.nationalIdInput);
      setSerialInput(draft.serialInput);
      setManualEntry(draft.manualEntry);
      setManualNumberInput(draft.manualNumberInput);
      setPaymentType(draft.paymentType);
      setStep(draft.step);
    }
    setDraftRestored(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!draftRestored) return;
    // Once the policy exists server-side (step > 2) or the wizard is finished, the draft must
    // not survive — restoring it would re-issue an already-issued policy.
    if (finalized || step > 2) {
      clearDraft();
      return;
    }
    setDraft({
      step,
      form,
      plate,
      nationalIdInput,
      serialInput,
      manualEntry,
      manualNumberInput,
      paymentType,
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draftRestored, step, form, plate, nationalIdInput, serialInput, manualEntry, manualNumberInput, paymentType]);

  useEffect(() => {
    api
      .get<InsuranceLineDto[]>("/insurance-lines")
      .then(setLines)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری رشتههای بیمه"));
    // A user without Marketer.Manage legitimately cannot list marketers — the selector simply
    // stays hidden for them rather than erroring the whole wizard.
    api
      .get<MarketerOptionDto[]>("/marketers")
      .then((all) => setMarketers(all.filter((m) => m.isActive)))
      .catch(() => setMarketers(null));
  }, []);

  // The commission rate that will be locked at issuance — read from the marketer's rate history
  // (rows newest-first) and matched against the chosen line and issue date.
  const [marketerRates, setMarketerRates] = useState<MarketerRateLiteDto[] | null>(null);
  useEffect(() => {
    if (!form.marketerId) {
      setMarketerRates(null);
      return;
    }
    let stale = false;
    api
      .get<MarketerRateLiteDto[]>(`/marketers/${form.marketerId}/rates`)
      .then((rates) => {
        if (!stale) setMarketerRates(rates);
      })
      .catch(() => {
        if (!stale) setMarketerRates(null);
      });
    return () => {
      stale = true;
    };
  }, [form.marketerId]);

  const applicableMarketerRate = useMemo(() => {
    if (!marketerRates || !form.insuranceLineId || !form.issueDate) return null;
    return (
      marketerRates.find(
        (r) =>
          r.insuranceLineId === form.insuranceLineId &&
          r.effectiveFrom <= form.issueDate &&
          (!r.effectiveTo || r.effectiveTo >= form.issueDate),
      ) ?? null
    );
  }, [marketerRates, form.insuranceLineId, form.issueDate]);

  const selectedLine = lines?.find((l) => l.id === form.insuranceLineId) ?? null;

  useEffect(() => {
    if (manualEntry || !form.insuranceLineId || !form.issueDate) {
      return;
    }
    setSuggestionLoading(true);
    api
      .get<PolicyNumberSuggestionDto>(
        `/policies/number-suggestion?insuranceLineId=${form.insuranceLineId}&issueDate=${form.issueDate}`,
      )
      .then((s) => {
        setSuggestion(s);
        setSerialInput((prev) => (prev.trim() ? prev : s.suggestedSerial));
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در دریافت پیشنهاد شمارهٔ بیمهنامه"))
      .finally(() => setSuggestionLoading(false));
  }, [form.insuranceLineId, form.issueDate, manualEntry]);

  const composedNumber = useMemo(() => {
    if (!suggestion?.canCompose || !serialInput.trim()) {
      return null;
    }
    const padded = serialInput.trim().padStart(suggestion.serialLength, "0");
    return [suggestion.lineCode, suggestion.agencyCode, suggestion.yearDisplay, padded].join(suggestion.separator);
  }, [suggestion, serialInput]);

  // §3 — a gap between the suggested next serial and what the user actually typed usually means a
  // policy was issued in Fanavaran but never entered here.
  const gapWarning = useMemo(() => {
    if (!suggestion || manualEntry || !serialInput.trim()) {
      return null;
    }
    const entered = Number(serialInput.trim());
    const suggested = Number(suggestion.suggestedSerial);
    if (!Number.isFinite(entered) || !Number.isFinite(suggested) || entered <= suggested) {
      return null;
    }
    const missing: string[] = [];
    for (let n = suggested; n < entered; n++) {
      missing.push(n.toString().padStart(suggestion.serialLength, "0"));
    }
    return missing;
  }, [suggestion, serialInput, manualEntry]);

  const gapWarningKey = gapWarning?.join(",") ?? "";

  useEffect(() => {
    setGapConfirmed(false);
  }, [gapWarningKey]);

  const finalPolicyNumber = manualEntry ? manualNumberInput.trim() : (composedNumber ?? "");

  useEffect(() => {
    setNumberWarnings([]);
    setNumberWarningsConfirmed(false);
    if (!manualEntry || !manualNumberInput.trim() || !form.insuranceLineId || !form.issueDate) {
      return;
    }
    const handle = setTimeout(() => {
      const params = new URLSearchParams({
        policyNumber: manualNumberInput.trim(),
        insuranceLineId: form.insuranceLineId,
        issueDate: form.issueDate,
      });
      api
        .get<{ warnings: string[] }>(`/policies/number-warnings?${params.toString()}`)
        .then((r) => setNumberWarnings(r.warnings))
        .catch(() => setNumberWarnings([]));
    }, 400);
    return () => clearTimeout(handle);
  }, [manualEntry, manualNumberInput, form.insuranceLineId, form.issueDate]);

  useEffect(() => {
    setTitle(tabKey, finalPolicyNumber ? `بیمهنامه — ${finalPolicyNumber}` : "ثبت بیمهنامه");
  }, [finalPolicyNumber, tabKey, setTitle]);

  // Step 3 — suggest the down payment for the entered installment count (the server-side
  // DownPaymentSuggester), until the agent overrides it by typing.
  useEffect(() => {
    if (!created || downPaymentTouched) return;
    const count = Number(installmentCount);
    if (!Number.isFinite(count) || count <= 0) return;
    const handle = setTimeout(() => {
      api
        .get<number>(`/policies/${created.policyId}/suggest-down-payment?installmentCount=${count}`)
        .then((s) => setDownPayment(String(s)))
        .catch(() => {});
    }, 300);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [created, installmentCount, downPaymentTouched]);

  // Step 5 — cash boxes / bank accounts are only needed once the receipt form is reachable.
  useEffect(() => {
    if (step !== 5) return;
    api
      .get<CashBoxDto[]>("/settings/cash-and-bank/cash-boxes")
      .then((list) => {
        setCashBoxes(list);
        const active = list.find((b) => b.isActive);
        if (active) setReceiveCashBoxId((prev) => prev || active.id);
      })
      .catch(() => {});
    api
      .get<BankAccountDto[]>("/settings/cash-and-bank/bank-accounts")
      .then(setBankAccounts)
      .catch(() => {});
  }, [step]);

  function update<K extends keyof FormState>(field: K, value: string) {
    const next = { ...form, [field]: value };
    // تاریخ پایان همیشه یک سال شمسی بعد از تاریخ صدور پیشفرض میشود — کاربر هنوز میتواند بعداً
    // آن را دستی تغییر دهد.
    if (field === "issueDate") {
      next.endDate = addOneJalaliYear(value) ?? next.endDate;
    }
    setForm(next);
    setDirty(tabKey, true);
  }

  // Step 1 — the «بررسی» button: normalize the entered ID, validate the checksum locally, then ask
  // the backend (which normalizes + validates again and searches the plaintext column).
  async function runLookup() {
    const normalized = toLatinDigits(nationalIdInput).trim();
    if (!normalized) {
      setError("کد ملی را وارد کنید.");
      return;
    }
    if (!isValidNationalId(normalized)) {
      setError("کد ملی نامعتبر است.");
      return;
    }

    setLookupLoading(true);
    setError(null);
    try {
      const res = await api.get<CustomerLookupResultDto>(
        `/customers/lookup?nationalId=${encodeURIComponent(normalized)}`,
      );
      if (res.found && res.customer) {
        setLookupCustomer(res.customer);
        setLookupPolicyCount(res.policyCount);
        setCustomerMode("existing");
      } else {
        setLookupCustomer(null);
        setLookupPolicyCount(0);
        setCustomerMode("new");
        setForm((prev) => ({ ...prev, customerNationalId: normalized }));
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "بررسی کد ملی ناموفق بود.");
    } finally {
      setLookupLoading(false);
    }
  }

  function backToLookup() {
    setCustomerMode("none");
    setLookupCustomer(null);
    setLookupPolicyCount(0);
  }

  function customerFieldsError(): string | null {
    const fullName = `${form.customerFirstName.trim()} ${form.customerLastName.trim()}`.trim();
    if (!fullName) {
      return "نام و نام خانوادگی بیمهگذار الزامی است.";
    }
    if (!form.customerNationalId.trim() || !isValidNationalId(form.customerNationalId)) {
      return "کد ملی بیمهگذار الزامی است و باید معتبر باشد.";
    }
    if (
      form.customerEmergencyMobile.trim() &&
      form.customerEmergencyMobile.trim() === form.customerMobile.trim()
    ) {
      return "موبایل اضطراری نباید با موبایل اصلی یکسان باشد.";
    }
    if (form.customerPostalCode.trim() && toLatinDigits(form.customerPostalCode.trim()).length !== 10) {
      return "کد پستی بیمهگذار باید دقیقاً ۱۰ رقم باشد.";
    }
    if (form.customerAddress.trim() && form.customerAddress.trim().length < 10) {
      return "آدرس بیمهگذار باید حداقل ۱۰ کاراکتر باشد.";
    }
    return null;
  }

  function goFromCustomerToPolicy() {
    if (customerMode === "existing" && lookupCustomer) {
      setError(null);
      setStep(2);
      return;
    }
    const problem = customerFieldsError();
    if (problem) {
      setError(problem);
      return;
    }
    setError(null);
    setStep(2);
  }

  async function savePolicy() {
    if (!selectedLine) {
      setError("نوع بیمهنامه را انتخاب کنید.");
      return;
    }
    if (!form.endDate) {
      setError("تاریخ پایان الزامی است.");
      return;
    }
    if (!finalPolicyNumber) {
      setError(
        manualEntry
          ? "شمارهٔ بیمهنامه را وارد کنید."
          : "کد رشته یا کد نمایندگی تنظیم نشده — از «ورود دستی شمارهٔ کامل» استفاده کنید یا سریال را وارد کنید.",
      );
      return;
    }
    if (gapWarning && gapWarning.length > 0 && !gapConfirmed) {
      setGapPrompt(gapWarning);
      return;
    }
    if (numberWarnings.length > 0 && !numberWarningsConfirmed) {
      setMismatchPrompt(numberWarnings);
      return;
    }
    const isNew = customerMode !== "existing";
    if (isNew) {
      const problem = customerFieldsError();
      if (problem) {
        setError(problem);
        return;
      }
    }
    if (selectedLine.requiresVehicle && !isPlateFilled(plate)) {
      setError("شماره پلاک کامل نیست.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const createdResult = await api.post<CreatePolicyResultDto>("/policies", {
        policyNumber: finalPolicyNumber,
        pnManualEntry: manualEntry,
        insuranceLineId: form.insuranceLineId,
        customerId: customerMode === "existing" && lookupCustomer ? lookupCustomer.id : null,
        customerFullName: isNew
          ? `${form.customerFirstName.trim()} ${form.customerLastName.trim()}`.trim()
          : (lookupCustomer?.fullName ?? null),
        customerFirstName: isNew ? (form.customerFirstName.trim() || null) : null,
        customerLastName: isNew ? (form.customerLastName.trim() || null) : null,
        customerMobile: isNew && form.customerMobile.trim() ? form.customerMobile.trim() : null,
        customerEmergencyMobile:
          isNew && form.customerEmergencyMobile.trim() ? form.customerEmergencyMobile.trim() : null,
        customerNationalId: isNew && form.customerNationalId.trim() ? form.customerNationalId.trim() : null,
        customerAddress: isNew && form.customerAddress.trim() ? form.customerAddress.trim() : null,
        customerPostalCode: isNew && form.customerPostalCode.trim() ? form.customerPostalCode.trim() : null,
        vehicle:
          selectedLine.requiresVehicle || isPlateFilled(plate)
            ? {
                plate: null,
                vin: form.vin.trim() || null,
                chassis: form.chassis.trim() || null,
                make: null,
                model: null,
                year: null,
                plateType: plate.plateType,
                plateTwoDigit: plate.twoDigit || null,
                plateLetter: plate.letter || null,
                plateThreeDigit: plate.threeDigit || null,
                plateIranCode: plate.iranCode || null,
                engineNumber: form.engineNumber.trim() || null,
                vehicleType: form.vehicleType.trim() || null,
                manufactureYear: form.manufactureYear.trim()
                  ? Number(toLatinDigits(form.manufactureYear.trim())) || null
                  : null,
              }
            : null,
        property: selectedLine.requiresProperty
          ? { address: form.propertyAddress, postalCode: form.propertyPostalCode || null, type: null, value: null }
          : null,
        issueDate: form.issueDate,
        startDate: form.startDate,
        endDate: form.endDate,
        netPremium: Number(form.netPremium) || 0,
        serviceFee: Number(form.serviceFee) || 0,
        marketerId: form.marketerId || null,
        previousInsurer: null,
        isRenewal: false,
        paymentType,
        insurerName: form.insurerName.trim() || null,
      });
      setCreated(createdResult);
      setStep(paymentType === "cash" ? 5 : 3);
      setDirty(tabKey, true); // the wizard is still mid-flight (schedule + receipt pending)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت بیمهنامه ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  async function generateSchedule() {
    if (!created) return;
    const count = Number(installmentCount);
    if (!Number.isFinite(count) || count <= 0) {
      setError("تعداد اقساط باید مثبت باشد.");
      return;
    }

    setScheduling(true);
    setError(null);
    try {
      const result = await api.post<ScheduleResultDto>(`/policies/${created.policyId}/schedule`, {
        downPayment: Number(downPayment) || 0,
        installmentCount: count,
      });
      setScheduleResult(result);
      setScheduledDownPayment(Number(downPayment) || 0);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "تولید اقساط ناموفق بود.");
    } finally {
      setScheduling(false);
    }
  }

  async function receiveDownPayment() {
    if (!created) return;
    if ((receiveMethodType === "Cash" || receiveMethodType === "Cheque") && !receiveCashBoxId) {
      setError("انتخاب صندوق الزامی است.");
      return;
    }
    if (receiveMethodType === "BankTransfer" && !receiveBankAccountId) {
      setError("انتخاب حساب بانکی الزامی است.");
      return;
    }
    if (receiveMethodType === "Cheque" && (!chequeNumber.trim() || !chequeBankName.trim() || !chequePresenterName.trim())) {
      setError("شمارهٔ چک، بانک عامل و نام تحویلدهنده الزامی است.");
      return;
    }

    setReceiving(true);
    setError(null);
    try {
      await api.post(`/policies/${created.policyId}/receive-down-payment`, {
        paidOn: receivePaidOn,
        referenceNo: receiveReferenceNo.trim() || null,
        methodType: receiveMethodType,
        cashBoxId: receiveMethodType === "Cash" || receiveMethodType === "Cheque" ? receiveCashBoxId : null,
        bankAccountId: receiveMethodType === "BankTransfer" ? receiveBankAccountId : null,
        cheque:
          receiveMethodType === "Cheque"
            ? {
                chequeNumber: chequeNumber.trim(),
                bankName: chequeBankName.trim(),
                dueDate: chequeDueDate,
                presenterName: chequePresenterName.trim(),
                cashBoxId: receiveCashBoxId,
              }
            : null,
      });
      setReceived(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت دریافت پیشپرداخت ناموفق بود.");
    } finally {
      setReceiving(false);
    }
  }

  // Step 5, cash path — the whole policy is settled in one shot (record-full-payment), reusing
  // the same method/cash-box/cheque form the down-payment receipt uses.
  async function recordFullPayment() {
    if (!created) return;
    const total = (Number(form.netPremium) || 0) + (Number(form.serviceFee) || 0);
    if (total <= 0) {
      setError("مبلغ کل باید مثبت باشد.");
      return;
    }
    if ((receiveMethodType === "Cash" || receiveMethodType === "Cheque") && !receiveCashBoxId) {
      setError("انتخاب صندوق الزامی است.");
      return;
    }
    if (receiveMethodType === "BankTransfer" && !receiveBankAccountId) {
      setError("انتخاب حساب بانکی الزامی است.");
      return;
    }
    if (receiveMethodType === "Cheque" && (!chequeNumber.trim() || !chequeBankName.trim() || !chequePresenterName.trim())) {
      setError("شمارهٔ چک، بانک عامل و نام تحویلدهنده الزامی است.");
      return;
    }

    setReceiving(true);
    setError(null);
    try {
      await api.post(`/policies/${created.policyId}/record-full-payment`, {
        amount: total,
        paidOn: receivePaidOn,
        method: METHOD_LABELS[receiveMethodType],
        referenceNo: receiveReferenceNo.trim() || null,
        methodType: receiveMethodType,
        cashBoxId: receiveMethodType === "Cash" || receiveMethodType === "Cheque" ? receiveCashBoxId : null,
        bankAccountId: receiveMethodType === "BankTransfer" ? receiveBankAccountId : null,
        cheque:
          receiveMethodType === "Cheque"
            ? {
                chequeNumber: chequeNumber.trim(),
                bankName: chequeBankName.trim(),
                dueDate: chequeDueDate,
                presenterName: chequePresenterName.trim(),
                cashBoxId: receiveCashBoxId,
              }
            : null,
      });
      setReceived(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت پرداخت کامل ناموفق بود.");
    } finally {
      setReceiving(false);
    }
  }

  // Owner decision 2026-08-28: «ثبت نهایی» only enables once the defined down payment has actually
  // been received; with no down payment at all the agent finalizes right after reviewing the file
  // summary above. The cash path needs its one full payment recorded instead.
  const canFinalize =
    paymentType === "cash"
      ? received
      : scheduleResult !== null && (scheduledDownPayment <= 0 || received);

  const stepOrder: Step[] = paymentType === "cash" ? [1, 2, 5] : [1, 2, 3, 4, 5];
  const stepIndex = stepOrder.indexOf(step);

  function openPolicyFile() {
    if (!created) return;
    openTab({
      navType: "policy-file",
      page: "policy-file",
      kind: "multi-record",
      recordId: created.policyId,
      title: created.policyNumber,
      payload: { policyId: created.policyId },
    });
  }

  function finalize() {
    if (!canFinalize) return;
    setFinalized(true);
    setDirty(tabKey, false);
    openPolicyFile();
  }

  function reset() {
    clearDraft();
    setStep(1);
    setForm(EMPTY);
    setError(null);
    setSaving(false);
    setNationalIdInput("");
    setLookupLoading(false);
    setLookupCustomer(null);
    setLookupPolicyCount(0);
    setCustomerMode("none");
    setSuggestion(null);
    setSerialInput("");
    setManualEntry(false);
    setManualNumberInput("");
    setGapConfirmed(false);
    setGapPrompt(null);
    setNumberWarnings([]);
    setNumberWarningsConfirmed(false);
    setMismatchPrompt(null);
    setPlate(EMPTY_PLATE);
    setCreated(null);
    setInstallmentCount("");
    setDownPayment("");
    setDownPaymentTouched(false);
    setScheduling(false);
    setScheduleResult(null);
    setScheduledDownPayment(0);
    setReceivePaidOn(TODAY);
    setReceiveMethodType("Cash");
    setReceiveCashBoxId("");
    setReceiveBankAccountId("");
    setReceiveReferenceNo("");
    setReceiving(false);
    setReceived(false);
    setChequeNumber("");
    setChequeBankName("");
    setChequeDueDate(TODAY);
    setChequePresenterName("");
    setFinalized(false);
    setPaymentType("installment");
    setVerificationRejected(false);
    setDirty(tabKey, false);
    setTitle(tabKey, "ثبت بیمهنامه");
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        ثبت <em className="font-extralight not-italic text-(--ice-2)">بیمهنامهٔ جدید</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">
        کد ملی ← بیمهنامه ← اقساط ← اعتبارسنجی ← دریافت پیشپرداخت و ثبت نهایی
      </div>

      <div className="mb-5 flex flex-wrap items-center gap-1.5">
        {stepOrder.map((s, i) => (
          <div key={s} className="flex items-center gap-1.5">
            {i > 0 && <div className="h-px w-5 bg-(--edge-2)" />}
            <div
              className={
                s === step
                  ? "rounded-full border border-(--mint) bg-(--mint) px-3 py-1 text-[11.5px] font-bold text-(--on-mint)"
                  : i < stepIndex
                    ? "rounded-full border border-(--mint)/40 bg-(--mint)/10 px-3 py-1 text-[11.5px] font-semibold text-(--mint)"
                    : "rounded-full border border-(--edge-2) bg-(--btn-bg) px-3 py-1 text-[11.5px] font-semibold text-(--ice-3)"
              }
            >
              گام {fa(i + 1)} — {STEP_LABELS[s]}
            </div>
          </div>
        ))}
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {step === 1 && (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-3 text-[13px] font-bold text-(--ice)">مشتری را با کد ملی پیدا کنید</div>
          <div className="grid grid-cols-[1fr_auto] items-end gap-2">
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">کد ملی بیمهگذار</label>
              <input
                value={nationalIdInput}
                onChange={(e) => setNationalIdInput(toLatinDigits(e.target.value).replace(/\D/g, "").slice(0, 10))}
                placeholder="۰۰۷۲۳۴۵۴۵۳"
                dir="ltr"
                className={`${INPUT_CLASS} text-[14px] tabular-nums`}
              />
            </div>
            <button type="button" onClick={runLookup} disabled={lookupLoading} className={BTN_PRIMARY}>
              {lookupLoading ? "در حال بررسی..." : "بررسی"}
            </button>
          </div>

                    {customerMode === "existing" && lookupCustomer && (
            <div className="mt-4 rounded-[12px] border border-(--mint)/30 bg-(--mint)/8 p-4">
              <div className="mb-2 flex items-center justify-between gap-2">
                <div className="text-[13.5px] font-bold text-(--ice)">{lookupCustomer.fullName}</div>
                <div
                  className={
                    lookupCustomer.isProfileComplete
                      ? "text-[11px] font-semibold text-(--mint)"
                      : "text-[11px] font-semibold text-(--ember)"
                  }
                >
                  {lookupCustomer.isProfileComplete ? "پروفایل کامل" : "پروفایل ناقص"}
                </div>
              </div>
              <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-[12.5px] text-(--ice-2)">
                <div>
                  کد ملی: <b className="tabular-nums text-(--ice)" dir="ltr">{fa(lookupCustomer.nationalId ?? "")}</b>
                </div>
                <div>
                  موبایل: <span className="tabular-nums text-(--ice)">{lookupCustomer.mobile ? fa(lookupCustomer.mobile) : "—"}</span>
                </div>
                <div className="col-span-2">آدرس: {lookupCustomer.address ?? "—"}</div>
                <div className="col-span-2 text-(--ice-3)">این مشتری {fa(lookupPolicyCount)} بیمهنامه در سیستم دارد.</div>
              </div>
              <div className="mt-3 flex gap-2">
                <button type="button" onClick={goFromCustomerToPolicy} className={BTN_PRIMARY}>
                  ادامه با این مشتری
                </button>
                <button type="button" onClick={backToLookup} className={BTN_SECONDARY}>
                  جستجوی مجدد
                </button>
              </div>
            </div>
          )}

          {customerMode === "new" && (
            <div className="mt-4 rounded-[12px] border border-(--edge-2) bg-(--fld) p-4">
              <div className="mb-3 text-[12.5px] font-semibold text-(--ice)">
                مشتری با این کد ملی پیدا نشد — اطلاعات بیمهگذار جدید را وارد کنید (هنگام ثبت بیمهنامه ساخته میشود):
              </div>
              <div className="grid grid-cols-2 gap-3.5">
                <Field label="نام" value={form.customerFirstName} onChange={(v) => update("customerFirstName", v)} />
                <Field label="نام خانوادگی" value={form.customerLastName} onChange={(v) => update("customerLastName", v)} />
                <Field
                  label="کد ملی"
                  value={form.customerNationalId}
                  onChange={(v) => update("customerNationalId", toLatinDigits(v).replace(/\D/g, "").slice(0, 10))}
                />
                <Field label="شمارهٔ همراه" value={form.customerMobile} onChange={(v) => update("customerMobile", v)} placeholder="۰۹۱۲۳۴۵۶۷۸۹" />
                <Field label="شمارهٔ همراه اضطراری" value={form.customerEmergencyMobile} onChange={(v) => update("customerEmergencyMobile", v)} placeholder="اختیاری" />
                <Field label="کد پستی" value={form.customerPostalCode} onChange={(v) => update("customerPostalCode", v)} placeholder="۱۲۳۴۵۶۷۸۹۰" />
                <div className="col-span-2">
                  <Field label="آدرس" value={form.customerAddress} onChange={(v) => update("customerAddress", v)} />
                </div>
              </div>
              <div className="mt-3 flex gap-2">
                <button type="button" onClick={goFromCustomerToPolicy} className={BTN_PRIMARY}>
                  ادامه
                </button>
                <button type="button" onClick={backToLookup} className={BTN_SECONDARY}>
                  جستجوی مجدد
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {step === 2 && (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-3 text-[13px] font-bold text-(--ice)">مشخصات بیمهنامه</div>
          {customerMode === "existing" && lookupCustomer && (
            <div className="mb-3.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/8 px-3 py-2 text-[12.5px] text-(--ice-2)">
              بیمهگذار: <b className="text-(--ice)">{lookupCustomer.fullName}</b> — کد ملی{" "}
              <b className="tabular-nums text-(--ice)" dir="ltr">{fa(lookupCustomer.nationalId ?? "")}</b>
            </div>
          )}
          <div className="grid grid-cols-2 gap-3.5">
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">نوع بیمهنامه</label>
              <select value={form.insuranceLineId} onChange={(e) => update("insuranceLineId", e.target.value)} className={INPUT_CLASS}>
                <option value="">انتخاب کنید...</option>
                {lines?.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.nameFa}
                  </option>
                ))}
              </select>
            </div>
            <PolicyNumberField
              disabled={!form.insuranceLineId || !form.issueDate}
              loading={suggestionLoading}
              suggestion={suggestion}
              serialInput={serialInput}
              onSerialChange={(v) => {
                setSerialInput(toLatinDigits(v).replace(/\D/g, ""));
                setDirty(tabKey, true);
              }}
              composedNumber={composedNumber}
              manualEntry={manualEntry}
              onToggleManual={(v) => {
                setManualEntry(v);
                setDirty(tabKey, true);
              }}
              manualNumberInput={manualNumberInput}
              onManualNumberChange={(v) => {
                setManualNumberInput(v);
                setDirty(tabKey, true);
              }}
            />

            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">حق بیمه (تومان)</label>
              <MoneyInput value={form.netPremium} onChange={(v) => update("netPremium", v)} placeholder="۹٬۰۰۰٬۰۰۰" />
            </div>
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">کارمزد خدمات (تومان)</label>
              <MoneyInput value={form.serviceFee} onChange={(v) => update("serviceFee", v)} placeholder="۵۰۰٬۰۰۰" />
            </div>

            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">بیمه‌گر (اختیاری)</label>
              <input
                value={form.insurerName}
                onChange={(e) => update("insurerName", e.target.value)}
                placeholder="مثلاً بیمهٔ آسیا — خالی یعنی بیمه‌گر پیش‌فرض نمایندگی"
                className={INPUT_CLASS}
              />
            </div>

            <div className="col-span-full">
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">نوع پرداخت</label>
              <div className="flex flex-wrap gap-2">
                {(
                  [
                    ["installment", "اقساطی"],
                    ["cash", "نقدی"],
                  ] as [PaymentType, string][]
                ).map(([value, label]) => (
                  <button
                    key={value}
                    type="button"
                    onClick={() => {
                      setPaymentType(value);
                      setDirty(tabKey, true);
                    }}
                    className={
                      paymentType === value
                        ? "rounded-[10px] border border-(--mint) bg-(--mint)/15 px-4 py-2 text-[12.5px] font-semibold text-(--mint)"
                        : "rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:text-(--ice)"
                    }
                  >
                    {label}
                  </button>
                ))}
              </div>
              <div className="mt-1.5 text-[11px] leading-relaxed text-(--ice-3)">
                {paymentType === "cash"
                  ? "پرداخت کامل مبلغ در یک مرحله — بدون اقساط و بدون اعتبارسنجی."
                  : "پیشپرداخت و اقساط — مشتری از طریق لینک پورتال کارمزد استعلام را پرداخت میکند، استعلام اعتباری انجام میشود و پس از تأیید شما و تأیید قرارداد توسط مشتری، پیشپرداخت قابل دریافت است."}
              </div>
            </div>

            {marketers !== null && (
              <div className="col-span-full">
                <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">بازاریاب</label>
                <select
                  value={form.marketerId}
                  onChange={(e) => update("marketerId", e.target.value)}
                  className={INPUT_CLASS}
                >
                  <option value="">بدون بازاریاب</option>
                  {marketers.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.fullName}
                    </option>
                  ))}
                </select>
                {form.marketerId && form.insuranceLineId && marketerRates !== null && (
                  <div className={`mt-1.5 text-[11px] leading-relaxed ${applicableMarketerRate ? "text-(--mint)" : "text-(--ember)"}`}>
                    {applicableMarketerRate
                      ? `نرخ پورسانت: ${fa(applicableMarketerRate.ratePercent)}٪ — هنگام ثبت قفل میشود و مبنای برشهای قسط به قسط خواهد بود.`
                      : "برای این بازاریاب در این رشته نرخ پورسانت ثبت نشده — برای این بیمه‌نامه پورسانتی تولید نخواهد شد."}
                  </div>
                )}
              </div>
            )}

            <DateField label="تاریخ صدور" value={form.issueDate} onChange={(v) => update("issueDate", v)} />
            <DateField label="تاریخ شروع" value={form.startDate} onChange={(v) => update("startDate", v)} />
            <DateField label="تاریخ پایان" value={form.endDate} onChange={(v) => update("endDate", v)} />

            {selectedLine?.requiresVehicle && (
              <>
                <PlateField
                  value={plate}
                  onChange={(v) => {
                    setPlate(v);
                    setDirty(tabKey, true);
                  }}
                />
                <Field label="نوع خودرو" value={form.vehicleType} onChange={(v) => update("vehicleType", v)} placeholder="مثلاً پژو ۲۰۶" />
                <Field label="سال ساخت" value={form.manufactureYear} onChange={(v) => update("manufactureYear", toLatinDigits(v).replace(/\D/g, "").slice(0, 4))} />
                <Field label="شماره شاسی" value={form.chassis} onChange={(v) => update("chassis", v)} />
                <Field label="شماره موتور" value={form.engineNumber} onChange={(v) => update("engineNumber", v)} />
                <Field label="شماره VIN" value={form.vin} onChange={(v) => update("vin", v)} />
              </>
            )}
            {selectedLine?.requiresProperty && (
              <>
                <Field label="نشانی ملک" value={form.propertyAddress} onChange={(v) => update("propertyAddress", v)} />
                <Field label="کد پستی ملک" value={form.propertyPostalCode} onChange={(v) => update("propertyPostalCode", v)} />
              </>
            )}
          </div>

          <div className="mt-4 flex gap-2">
            <button type="button" onClick={savePolicy} disabled={saving || lines === null} className={BTN_PRIMARY}>
              {saving ? "در حال ثبت..." : "ثبت بیمهنامه و ادامه"}
            </button>
            <button type="button" onClick={() => setStep(1)} className={BTN_SECONDARY}>
              مرحلهٔ قبل
            </button>
          </div>
        </div>
      )}

      {step === 3 && created && (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-1 text-[13px] font-bold text-(--ice)">
            بیمهنامهٔ <span className="tabular-nums">{created.policyNumber}</span> ثبت شد — پیشپرداخت و اقساط
          </div>
          <div className="mb-3.5 text-[12px] leading-relaxed text-(--ice-3)">
            مبلغ کل دریافتی: <b className="tabular-nums text-(--ice)">{money((Number(form.netPremium) || 0) + (Number(form.serviceFee) || 0))}</b> تومان —
            تعداد اقساط و پیشپرداخت را مشخص کنید (پیشپرداخت ۰ یعنی فقط اقساط).
          </div>

          <div className="grid grid-cols-3 items-end gap-3.5">
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">تعداد اقساط</label>
              <input
                value={installmentCount}
                onChange={(e) => setInstallmentCount(toLatinDigits(e.target.value).replace(/\D/g, "").slice(0, 2))}
                disabled={scheduleResult !== null}
                className={`${INPUT_CLASS} tabular-nums disabled:opacity-60`}
              />
            </div>
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">پیشپرداخت (تومان)</label>
              <MoneyInput
                value={downPayment}
                onChange={(v) => {
                  setDownPayment(v);
                  setDownPaymentTouched(true);
                  setDirty(tabKey, true);
                }}
              />
            </div>
            {scheduleResult === null ? (
              <button type="button" onClick={generateSchedule} disabled={scheduling} className={BTN_PRIMARY}>
                {scheduling ? "در حال تولید..." : "تولید اقساط"}
              </button>
            ) : (
              <div className="rounded-[10px] border border-(--mint)/30 bg-(--mint)/8 px-3 py-2 text-center text-[12px] font-semibold text-(--mint)">
                اقساط تولید شد
              </div>
            )}
          </div>

          {scheduleResult && (
            <div className="mt-4">
              {scheduleResult.exceedsMaxInstallments && (
                <div className="mb-2 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
                  ⚠️ تعداد اقساط از سقف تنظیمشدهٔ نمایندگی بیشتر است.
                </div>
              )}
              <div className="mb-2 text-[12.5px] font-semibold text-(--mint)">{fa(scheduleResult.installments.length)} قسط ساخته شد</div>
              <div className="max-h-52 overflow-y-auto">
                <table className="w-full border-collapse text-[12px]">
                  <tbody>
                    {scheduleResult.installments.map((i) => (
                      <tr key={i.seqNo} className="border-t border-(--edge)/50 first:border-t-0">
                        <td className="py-1 text-(--ice-3)">قسط {fa(i.seqNo)}</td>
                        <td className="py-1 text-(--ice-3)">{toJalaliDisplay(i.dueDate)}</td>
                        <td className="py-1 text-end font-semibold tabular-nums text-(--ice)">{money(i.amount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          <div className="mt-4 flex items-center gap-2">
            <button type="button" onClick={() => setStep(4)} disabled={scheduleResult === null} className={BTN_PRIMARY}>
              مرحلهٔ بعد
            </button>
            <div className="text-[11.5px] leading-relaxed text-(--ice-3)">
              بیمهنامه ثبت شده و به مرحلهٔ قبل برگشت ندارد — برای اصلاح، از خود پروندهٔ بیمهنامه ادامه دهید.
            </div>
          </div>
        </div>
      )}

      {step === 4 && created && scheduleResult && paymentType === "installment" && (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-1 text-[13px] font-bold text-(--ice)">
            اعتبارسنجی مشتری — بیمهنامهٔ <span className="tabular-nums">{created.policyNumber}</span>
          </div>
          <div className="mb-4 text-[12px] leading-relaxed text-(--ice-3)">
            زنجیرهٔ اعتبارسنجی: پرداخت کارمزد ← استعلام اعتباری ← تأیید شما ← تأیید قرارداد توسط مشتری ← پیشپرداخت.
            سرور هم بدون تکمیل این زنجیره، دریافت پیشپرداخت را نمیپذیرد.
          </div>
          <PolicyVerificationStep
            policyId={created.policyId}
            onChainCompleted={(paidOnline) => {
              if (paidOnline) setReceived(true);
              setStep(5);
            }}
            onRejected={() => setVerificationRejected(true)}
          />
          {!verificationRejected && (
            <div className="mt-4">
              <button type="button" onClick={() => setStep(3)} className={BTN_SECONDARY}>
                مرحلهٔ قبل
              </button>
            </div>
          )}
          {verificationRejected && (
            <div className="mt-4">
              <button type="button" onClick={reset} className={BTN_SECONDARY}>
                ثبت بیمهنامهٔ جدید
              </button>
            </div>
          )}
        </div>
      )}

      {step === 5 && created && (paymentType === "cash" || scheduleResult) && (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="mb-3 text-[13px] font-bold text-(--ice)">
            بررسی پرونده، {paymentType === "cash" ? "ثبت پرداخت کامل" : "دریافت پیشپرداخت"} و ثبت نهایی
          </div>

          <div className="mb-4 grid grid-cols-2 gap-x-4 gap-y-1 rounded-[12px] border border-(--edge-2) bg-(--fld) p-4 text-[12.5px] text-(--ice-2)">
            <div>شمارهٔ بیمهنامه: <b className="tabular-nums text-(--ice)">{created.policyNumber}</b></div>
            <div>
              بیمهگذار:{" "}
              <b className="text-(--ice)">
                {customerMode === "existing" ? lookupCustomer?.fullName : `${form.customerFirstName.trim()} ${form.customerLastName.trim()}`.trim()}
              </b>
            </div>
            <div>نوع: <b className="text-(--ice)">{selectedLine?.nameFa}</b></div>
            <div>
              مبلغ کل: <b className="tabular-nums text-(--ice)">{money((Number(form.netPremium) || 0) + (Number(form.serviceFee) || 0))}</b> تومان
            </div>
            {paymentType === "cash" ? (
              <div className="col-span-2">نوع پرداخت: <b className="text-(--ice)">نقدی — پرداخت کامل در یک مرحله</b></div>
            ) : (
              <>
                <div>پیشپرداخت: <b className="tabular-nums text-(--ice)">{money(scheduledDownPayment)}</b> تومان</div>
                <div>تعداد اقساط: <b className="tabular-nums text-(--ice)">{fa(scheduleResult!.installments.length)}</b></div>
              </>
            )}
          </div>

          {(paymentType === "cash" || scheduledDownPayment > 0) && (
            <div className="mb-4 border-t border-(--edge)/50 pt-3">
              {received ? (
                <div className="text-[12.5px] font-semibold text-(--mint)">
                  {paymentType === "cash" ? "پرداخت کامل با موفقیت ثبت شد." : "پیشپرداخت با موفقیت دریافت و ثبت شد."}
                </div>
              ) : (
                <>
                  <div className="mb-2 text-[12.5px] font-semibold text-(--ice)">
                    {paymentType === "cash"
                      ? `ثبت پرداخت کامل — ${money((Number(form.netPremium) || 0) + (Number(form.serviceFee) || 0))} تومان`
                      : `دریافت پیشپرداخت — ${money(scheduledDownPayment)} تومان`}
                  </div>
                  <div className="mb-2 grid grid-cols-2 gap-2">
                    <div>
                      <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">تاریخ دریافت</label>
                      <JalaliDateField value={receivePaidOn} onChange={setReceivePaidOn} />
                    </div>
                    <div>
                      <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">روش دریافت</label>
                      <select
                        value={receiveMethodType}
                        onChange={(e) => setReceiveMethodType(e.target.value as MethodType)}
                        className={INPUT_CLASS}
                      >
                        <option value="Cash">نقدی</option>
                        <option value="BankTransfer">واریز بانکی</option>
                        <option value="Cheque">چک</option>
                        <option value="PosDirect">پوز مستقیم بیمهگر</option>
                      </select>
                    </div>
                  </div>
                  <div className="mb-2 grid grid-cols-2 gap-2">
                    {receiveMethodType === "BankTransfer" && (
                      <div>
                        <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">حساب بانکی</label>
                        <select value={receiveBankAccountId} onChange={(e) => setReceiveBankAccountId(e.target.value)} className={INPUT_CLASS}>
                          <option value="">انتخاب کنید...</option>
                          {bankAccounts.filter((a) => a.isActive).map((a) => (
                            <option key={a.id} value={a.id}>
                              {a.bankName} — {a.accountNumber}
                            </option>
                          ))}
                        </select>
                      </div>
                    )}
                    {(receiveMethodType === "Cash" || receiveMethodType === "Cheque") && (
                      <div>
                        <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">
                          صندوق {receiveMethodType === "Cheque" && "(محل نگهداری چک)"}
                        </label>
                        <select value={receiveCashBoxId} onChange={(e) => setReceiveCashBoxId(e.target.value)} className={INPUT_CLASS}>
                          <option value="">انتخاب کنید...</option>
                          {cashBoxes.filter((b) => b.isActive).map((b) => (
                            <option key={b.id} value={b.id}>
                              {b.name}
                            </option>
                          ))}
                        </select>
                      </div>
                    )}
                    <div>
                      <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">شمارهٔ پیگیری / مرجع</label>
                      <input value={receiveReferenceNo} onChange={(e) => setReceiveReferenceNo(e.target.value)} className={INPUT_CLASS} />
                    </div>
                  </div>
                  {receiveMethodType === "Cheque" && (
                    <div className="mb-2 grid grid-cols-4 gap-2">
                      <Field label="شماره چک" value={chequeNumber} onChange={setChequeNumber} />
                      <Field label="بانک عامل" value={chequeBankName} onChange={setChequeBankName} />
                      <Field label="نام تحویلدهنده" value={chequePresenterName} onChange={setChequePresenterName} />
                      <div>
                        <label className="mb-1 block text-[11px] tracking-wider text-(--ice-3)">سرسید چک</label>
                        <JalaliDateField value={chequeDueDate} onChange={setChequeDueDate} />
                      </div>
                    </div>
                  )}
                  <button
                    type="button"
                    onClick={paymentType === "cash" ? recordFullPayment : receiveDownPayment}
                    disabled={receiving}
                    className={BTN_PRIMARY}
                  >
                    {receiving ? "در حال ثبت..." : "ثبت دریافت"}
                  </button>
                </>
              )}
            </div>
          )}
          {paymentType === "installment" && scheduledDownPayment <= 0 && (
            <div className="mb-4 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] leading-relaxed text-(--ice-3)">
              برای این بیمهنامه پیشپرداخت تعریف نشده — پس از مرور پرونده در بالا میتوانید ثبت نهایی کنید.
            </div>
          )}

          <div className="flex gap-2">
            <button
              type="button"
              onClick={finalize}
              disabled={!canFinalize}
              title={canFinalize ? undefined : "ابتدا دریافت را ثبت کنید"}
              className={BTN_PRIMARY}
            >
              ثبت نهایی
            </button>
            <button
              type="button"
              onClick={() => setStep(paymentType === "cash" ? 2 : 4)}
              className={BTN_SECONDARY}
            >
              مرحلهٔ قبل
            </button>
          </div>
          {((paymentType === "cash" && !received) ||
            (paymentType === "installment" && scheduledDownPayment > 0 && !received)) && (
            <div className="mt-2 text-[11.5px] text-(--ice-3)">
              «ثبت نهایی» پس از ثبت {paymentType === "cash" ? "پرداخت کامل" : "دریافت پیشپرداخت"} فعال میشود.
            </div>
          )}
        </div>
      )}

      {finalized && created && (
        <div className="mt-4.5 rounded-2xl border border-(--mint)/30 bg-(--mint)/8 p-5">
          <div className="mb-3 text-[14px] font-bold text-(--mint)">بیمهنامه با موفقیت ثبت و نهایی شد</div>
          <div className="mb-4 text-[13px] text-(--ice-2)">
            شمارهٔ بیمهنامه: <b>{created.policyNumber}</b> — پروندهٔ بیمهنامه در تب جدید باز شد.
          </div>
          <div className="flex gap-2">
            <button type="button" onClick={openPolicyFile} className={BTN_PRIMARY}>
              مشاهدهٔ پروندهٔ بیمهنامه
            </button>
            <button type="button" onClick={reset} className={BTN_SECONDARY}>
              ثبت بیمهنامهٔ جدید
            </button>
          </div>
        </div>
      )}

      {gapPrompt && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
          <div className="w-full max-w-sm rounded-2xl border border-(--ember)/30 bg-(--pane) p-5 shadow-xl">
            <div className="mb-2 text-[14px] font-bold text-(--ember)">
              ⚠️ {fa(gapPrompt.length)} شماره جا افتاده
            </div>
            <div className="mb-4 text-[13px] tabular-nums text-(--ice-2)" dir="ltr">
              {gapPrompt.map(fa).join(" و ")}
            </div>
            <div className="mb-4 text-[12px] leading-relaxed text-(--ice-3)">
              اگر عمدی است ادامه دهید. معمولاً این یعنی بیمهنامهای در فناوران هست که هنوز در سیستم ثبت نشده.
            </div>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => {
                  setGapConfirmed(true);
                  setGapPrompt(null);
                }}
                className="rounded-[10px] border border-(--ember) bg-(--ember) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
              >
                ادامه
              </button>
              <button type="button" onClick={() => setGapPrompt(null)} className={BTN_SECONDARY}>
                اصلاح
              </button>
            </div>
          </div>
        </div>
      )}

      {mismatchPrompt && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
          <div className="w-full max-w-sm rounded-2xl border border-(--ember)/30 bg-(--pane) p-5 shadow-xl">
            <div className="mb-2 text-[14px] font-bold text-(--ember)">⚠️ عدم تطابق در شمارهٔ واردشده</div>
            <ul className="mb-4 list-inside list-disc space-y-1 text-[12.5px] leading-relaxed text-(--ice-2)">
              {mismatchPrompt.map((w, i) => (
                <li key={i}>{w}</li>
              ))}
            </ul>
            <div className="mb-4 text-[12px] leading-relaxed text-(--ice-3)">
              موارد مرزی واقعی وجود دارد — صدور آخر اسفند، انتقال پرونده. اگر عمدی است ادامه دهید.
            </div>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => {
                  setNumberWarningsConfirmed(true);
                  setMismatchPrompt(null);
                }}
                className="rounded-[10px] border border-(--ember) bg-(--ember) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
              >
                ادامه
              </button>
              <button type="button" onClick={() => setMismatchPrompt(null)} className={BTN_SECONDARY}>
                اصلاح
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function Field({
  label,
  value,
  onChange,
  placeholder,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
}) {
  return (
    <div>
      <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">{label}</label>
      <input value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} className={INPUT_CLASS} />
    </div>
  );
}

function DateField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div>
      <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">{label}</label>
      <JalaliDateField value={value} onChange={onChange} />
    </div>
  );
}
