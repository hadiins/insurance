import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { useDraftState } from "../shell/useDraftState";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";
import { MoneyInput } from "../../components/MoneyInput";
import { StatusBadge } from "../../components/StatusBadge";
import { Table, Td, Th, Tr } from "../../components/Table";
import { EmptyState } from "../../components/EmptyState";

interface CollateralFilterPayload {
  type?: string;
  status?: string;
  upcomingDays?: string;
}

interface CollateralDto {
  id: string;
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  type: "ChequeSayadi" | "PromissoryNote" | "None";
  sayadId: string | null;
  bankName: string | null;
  amount: number;
  dueDate: string | null;
  status: "Held" | "AtBank" | "Cleared" | "Bounced";
  colorCode: string | null;
  checkedAt: string | null;
}

interface BankDto {
  id: string;
  name: string;
  isActive: boolean;
}

const TYPE_LABEL: Record<CollateralDto["type"], string> = {
  ChequeSayadi: "چک صیادی",
  PromissoryNote: "سفته",
  None: "سایر",
};

const STATUS_LABEL: Record<CollateralDto["status"], string> = {
  Held: "نزد نماینده",
  AtBank: "نزد بانک",
  Cleared: "پاس‌شده",
  Bounced: "برگشتی",
};

const STATUS_TONE: Record<CollateralDto["status"], "mint" | "amber" | "ember"> = {
  Held: "mint",
  AtBank: "amber",
  Cleared: "mint",
  Bounced: "ember",
};

export function CollateralPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const initial = (tab?.payload as CollateralFilterPayload | undefined) ?? {};

  const [items, setItems] = useState<CollateralDto[] | null>(null);
  const [typeFilter, setTypeFilter] = useState(initial.type ?? "");
  const [statusFilter, setStatusFilter] = useState(initial.status ?? "");
  const [upcomingOnly, setUpcomingOnly] = useState(Boolean(initial.upcomingDays));
  const [error, setError] = useState<string | null>(null);
  const [banks, setBanks] = useState<BankDto[]>([]);

  const [form, setForm] = useDraftState("register-form", {
    policyId: "",
    type: "ChequeSayadi",
    sayadId: "",
    bankName: "",
    amount: "",
    dueDate: "",
  });
  const policyId = form.policyId;
  const type = form.type;
  const sayadId = form.sayadId;
  const bankName = form.bankName;
  const amount = form.amount;
  const dueDate = form.dueDate;

  function reload() {
    const params = new URLSearchParams();
    if (typeFilter) params.set("type", typeFilter);
    if (statusFilter) params.set("status", statusFilter);
    if (upcomingOnly) params.set("upcomingDays", "30");
    api
      .get<CollateralDto[]>(`/collateral?${params.toString()}`)
      .then(setItems)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
  }

  useEffect(reload, [typeFilter, statusFilter, upcomingOnly]);

  useLiveReload(reload);

  useEffect(() => {
    api
      .get<BankDto[]>("/settings/cash-and-bank/banks")
      .then(setBanks)
      .catch(() => {});
  }, []);

  async function register() {
    if (!policyId.trim() || !amount) {
      setError("شناسهٔ بیمه‌نامه و مبلغ الزامی است.");
      return;
    }
    if (type === "ChequeSayadi" && !sayadId.trim()) {
      setError("شناسهٔ صیادی چک الزامی است.");
      return;
    }
    setError(null);
    try {
      await api.post("/collateral", {
        policyId: policyId.trim(),
        type,
        sayadId: sayadId.trim() || null,
        bankName: bankName.trim() || null,
        amount: Number(amount),
        dueDate: dueDate || null,
      });
      setForm({ ...form, policyId: "", sayadId: "", amount: "", dueDate: "" });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت ناموفق بود.");
    }
  }

  async function setStatus(id: string, status: string) {
    setError(null);
    try {
      await api.put(`/collateral/${id}/status`, { status });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی وضعیت ناموفق بود.");
    }
  }

  async function checkColor(id: string) {
    setError(null);
    try {
      await api.post(`/collateral/${id}/check-color`, {});
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "استعلام رنگ چک ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        چک <em className="font-extralight not-italic text-(--ice-2)">و سفته</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">وثیقهٔ اقساط — چک صیادی و سفته</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">ثبت وثیقهٔ جدید</div>
        <div className="mb-3 grid grid-cols-6 gap-3">
          <input
            value={policyId}
            onChange={(e) => setForm({ ...form, policyId: e.target.value })}
            placeholder="شناسهٔ بیمه‌نامه"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
          <select
            value={type}
            onChange={(e) => setForm({ ...form, type: e.target.value })}
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice)"
          >
            <option value="ChequeSayadi">چک صیادی</option>
            <option value="PromissoryNote">سفته</option>
          </select>
          {type === "ChequeSayadi" && (
            <input
              value={sayadId}
              onChange={(e) => setForm({ ...form, sayadId: e.target.value })}
              placeholder="شناسهٔ صیادی"
              className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
            />
          )}
          <select
            value={bankName}
            onChange={(e) => setForm({ ...form, bankName: e.target.value })}
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          >
            <option value="">بانک…</option>
            {banks.filter((b) => b.isActive).map((b) => (
              <option key={b.id} value={b.name}>
                {b.name}
              </option>
            ))}
          </select>
          <MoneyInput
            value={amount}
            onChange={(v) => setForm({ ...form, amount: v })}
            placeholder="مبلغ"
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] tabular-nums text-(--ice) outline-none focus:border-(--mint)"
          />
          <JalaliDateField
            value={dueDate}
            onChange={(iso) => setForm({ ...form, dueDate: iso })}
            className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice)"
          />
        </div>
        <button
          type="button"
          onClick={register}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
        >
          ثبت
        </button>
      </div>

      <div className="mb-3 flex gap-2">
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value)}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice)"
        >
          <option value="">همهٔ انواع</option>
          <option value="ChequeSayadi">چک صیادی</option>
          <option value="PromissoryNote">سفته</option>
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice)"
        >
          <option value="">همهٔ وضعیت‌ها</option>
          {(["Held", "AtBank", "Cleared", "Bounced"] as const).map((s) => (
            <option key={s} value={s}>
              {STATUS_LABEL[s]}
            </option>
          ))}
        </select>
        <button
          type="button"
          onClick={() => setUpcomingOnly((v) => !v)}
          className={`rounded-full px-3 py-1 text-[11.5px] font-semibold transition-colors ${
            upcomingOnly ? "bg-(--mint) text-(--on-mint)" : "border border-(--edge-2) text-(--ice-3) hover:bg-(--hov)"
          }`}
        >
          فقط پیشِ رو (۳۰ روز)
        </button>
        {(typeFilter || statusFilter || upcomingOnly) && (
          <button
            type="button"
            onClick={() => {
              setTypeFilter("");
              setStatusFilter("");
              setUpcomingOnly(false);
            }}
            className="rounded-full px-3 py-1 text-[11.5px] text-(--ice-3) underline underline-offset-2 transition-colors hover:text-(--ice)"
          >
            پاک‌کردن فیلترها
          </button>
        )}
      </div>

      {items !== null && <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(items.length)} مورد</div>}

      {items === null ? (
        <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
      ) : items.length === 0 ? (
        <EmptyState
          icon="🧾"
          title="موردی یافت نشد"
          description={
            typeFilter || statusFilter || upcomingOnly
              ? "هیچ وثیقه‌ای با این فیلترها مطابقت ندارد. فیلترها را پاک کنید یا بازه را تغییر دهید."
              : "هنوز وثیقه‌ای — چک صیادی یا سفته — برای بیمه‌نامه‌ها ثبت نشده است."
          }
          action={
            typeFilter || statusFilter || upcomingOnly
              ? {
                  label: "پاک کردن فیلترها",
                  onClick: () => {
                    setTypeFilter("");
                    setStatusFilter("");
                    setUpcomingOnly(false);
                  },
                }
              : undefined
          }
        />
      ) : (
        <Table>
          <thead>
            <tr>
              {["بیمه‌نامه", "بیمه‌گذار", "نوع", "بانک", "مبلغ", "سررسید", "رنگ چک", "وضعیت", ""].map((h) => (
                <Th key={h}>{h}</Th>
              ))}
            </tr>
          </thead>
          <tbody>
            {items.map((c) => (
              <Tr key={c.id}>
                <Td className="py-2.5 font-semibold">{fa(c.policyNumber)}</Td>
                <Td className="py-2.5 text-(--ice-3)">{c.customerFullName}</Td>
                <Td className="py-2.5 text-(--ice-3)">{TYPE_LABEL[c.type]}</Td>
                <Td className="py-2.5 text-(--ice-3)">{c.bankName ?? "—"}</Td>
                <Td className="py-2.5 font-bold">{money(c.amount)}</Td>
                <Td className="py-2.5">{toJalaliDisplay(c.dueDate)}</Td>
                <Td className="py-2.5 text-(--ice-3)">{c.colorCode ?? "—"}</Td>
                <Td className="py-2.5">
                  <StatusBadge tone={STATUS_TONE[c.status]}>{STATUS_LABEL[c.status]}</StatusBadge>
                </Td>
                <Td className="py-2.5">
                  <div className="flex gap-1.5">
                    {c.status === "Held" && (
                      <button
                        type="button"
                        onClick={() => setStatus(c.id, "AtBank")}
                        className="rounded-[8px] border border-(--edge-2) px-2 py-1 text-[10.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
                      >
                        نزد بانک
                      </button>
                    )}
                    {(c.status === "Held" || c.status === "AtBank") && (
                      <>
                        <button
                          type="button"
                          onClick={() => setStatus(c.id, "Cleared")}
                          className="rounded-[8px] border border-(--mint) px-2 py-1 text-[10.5px] text-(--mint) transition-colors hover:bg-(--mint)/10"
                        >
                          پاس‌شد
                        </button>
                        <button
                          type="button"
                          onClick={() => setStatus(c.id, "Bounced")}
                          className="rounded-[8px] border border-(--ember) px-2 py-1 text-[10.5px] text-(--ember) transition-colors hover:bg-(--ember)/10"
                        >
                          برگشت خورد
                        </button>
                      </>
                    )}
                    {c.type === "ChequeSayadi" && (
                      <button
                        type="button"
                        onClick={() => checkColor(c.id)}
                        className="rounded-[8px] border border-(--edge-2) px-2 py-1 text-[10.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
                      >
                        استعلام رنگ
                      </button>
                    )}
                  </div>
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
    </div>
  );
}
