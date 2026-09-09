import { useCallback, useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import { AlertList } from "./MonitoringDashboardPage";
import {
  ALERT_COMPARATOR_LABELS,
  ALERT_METRIC_LABELS,
  type AlertOccurrenceDto,
  type AlertRuleDto,
} from "./monitoringTypes";

const METRICS = Object.keys(ALERT_METRIC_LABELS);
const COMPARATORS = ["GreaterThan", "LessThan"];

interface RuleDraft {
  name: string;
  metric: string;
  comparator: string;
  threshold: string;
  windowMinutes: string;
  severity: string;
  smsNotify: boolean;
}

const emptyDraft: RuleDraft = {
  name: "",
  metric: "ErrorRatePercent",
  comparator: "GreaterThan",
  threshold: "5",
  windowMinutes: "5",
  severity: "Warning",
  smsNotify: false,
};

export function AlertsRulesPage() {
  const [alerts, setAlerts] = useState<AlertOccurrenceDto[] | null>(null);
  const [rules, setRules] = useState<AlertRuleDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [editing, setEditing] = useState<AlertRuleDto | null>(null);
  const [creating, setCreating] = useState(false);
  const [draft, setDraft] = useState<RuleDraft>(emptyDraft);
  const [saving, setSaving] = useState(false);

  const load = useCallback(() => {
    api
      .get<AlertOccurrenceDto[]>("/monitoring/alerts")
      .then(setAlerts)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری هشدارها"));
    api
      .get<AlertRuleDto[]>("/monitoring/alert-rules")
      .then(setRules)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری قوانین"));
  }, []);

  useEffect(load, [load]);

  function startCreate() {
    setDraft(emptyDraft);
    setEditing(null);
    setCreating(true);
  }

  function startEdit(rule: AlertRuleDto) {
    setDraft({
      name: rule.name,
      metric: rule.metric,
      comparator: rule.comparator,
      threshold: String(rule.threshold),
      windowMinutes: String(rule.windowMinutes),
      severity: rule.severity,
      smsNotify: rule.smsNotify,
    });
    setEditing(rule);
    setCreating(true);
  }

  async function save() {
    setSaving(true);
    setActionError(null);
    const body = {
      name: draft.name.trim(),
      metric: draft.metric,
      comparator: draft.comparator,
      threshold: Number(draft.threshold),
      windowMinutes: Number(draft.windowMinutes),
      severity: draft.severity,
      isEnabled: editing ? editing.isEnabled : true,
      smsNotify: draft.smsNotify,
    };
    try {
      if (editing) {
        await api.put(`/monitoring/alert-rules/${editing.id}`, body);
      } else {
        await api.post("/monitoring/alert-rules", body);
      }
      setCreating(false);
      setEditing(null);
      load();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "ذخیرهٔ قانون ناموفق بود");
    } finally {
      setSaving(false);
    }
  }

  async function removeRule(rule: AlertRuleDto) {
    if (!window.confirm(`قانون «${rule.name}» حذف شود؟`)) return;
    setActionError(null);
    try {
      await api.delete(`/monitoring/alert-rules/${rule.id}`);
      load();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "حذف قانون ناموفق بود");
    }
  }

  async function toggleEnabled(rule: AlertRuleDto) {
    setActionError(null);
    try {
      await api.put(`/monitoring/alert-rules/${rule.id}`, {
        name: rule.name,
        metric: rule.metric,
        comparator: rule.comparator,
        threshold: rule.threshold,
        windowMinutes: rule.windowMinutes,
        severity: rule.severity,
        isEnabled: !rule.isEnabled,
        smsNotify: rule.smsNotify,
      });
      load();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "تغییر وضعیت قانون ناموفق بود");
    }
  }

  const formOpen = creating || editing !== null;

  return (
    <div className="mx-auto max-w-[1200px] space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-[17px] font-bold text-(--ice)">هشدارها و قوانین</h1>
          <p className="mt-0.5 text-[12px] text-(--ice-3)">
            قوانین هر دقیقه توسط نمونه‌بردار ارزیابی می‌شوند؛ قوانین بحرانی می‌توانند پیامک به مالک بفرستند
          </p>
        </div>
        <button
          type="button"
          onClick={startCreate}
          className="rounded-[10px] bg-(--mint) px-3.5 py-1.5 text-[12px] font-semibold text-(--on-mint) transition-opacity hover:opacity-90"
        >
          + قانون جدید
        </button>
      </div>

      {actionError && (
        <div className="rounded-[10px] border border-(--ember)/40 bg-(--ember)/8 px-3.5 py-2 text-[12px] text-(--ember)">
          {actionError}
        </div>
      )}

      <section className="space-y-3">
        <h2 className="text-[14px] font-bold text-(--ice)">هشدارهای فعال و اخیر</h2>
        {error && !alerts ? (
          <EmptyState
            icon="⚠️"
            title="بارگذاری هشدارها ناموفق بود"
            description={error}
            action={{ label: "تلاش دوباره", onClick: load }}
          />
        ) : !alerts ? (
          <div className="grid h-32 place-items-center text-(--ice-3)">در حال بارگذاری…</div>
        ) : alerts.length === 0 ? (
          <EmptyState
            icon="🔔"
            title="هشداری ثبت نشده است"
            description="وقتی یکی از قوانین فعال شود، اینجا دیده می‌شود."
          />
        ) : (
          <AlertList alerts={alerts} onChanged={load} />
        )}
      </section>

      <section className="space-y-3">
        <h2 className="text-[14px] font-bold text-(--ice)">قوانین هشدار</h2>
        {error && !rules ? (
          <EmptyState
            icon="⚠️"
            title="بارگذاری قوانین ناموفق بود"
            description={error}
            action={{ label: "تلاش دوباره", onClick: load }}
          />
        ) : !rules ? (
          <div className="grid h-32 place-items-center text-(--ice-3)">در حال بارگذاری…</div>
        ) : rules.length === 0 ? (
          <EmptyState
            icon="📋"
            title="قانونی تعریف نشده است"
            description="قوانین پیش‌فرض هنگام راه‌اندازی ثبت می‌شوند؛ می‌توانید قانون دلخواه بسازید."
            action={{ label: "قانون جدید", onClick: startCreate }}
          />
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>نام</Th>
                <Th>شرط</Th>
                <Th>پنجره</Th>
                <Th>شدت</Th>
                <Th>پیامک</Th>
                <Th>فعال</Th>
                <Th>اقدام</Th>
              </tr>
            </thead>
            <tbody>
              {rules.map((rule) => (
                <Tr key={rule.id}>
                  <Td className="font-medium text-(--ice)">{rule.name}</Td>
                  <Td className="text-(--ice-2)">
                    {ALERT_METRIC_LABELS[rule.metric] ?? rule.metric} {ALERT_COMPARATOR_LABELS[rule.comparator] ?? rule.comparator}{" "}
                    <span className="tabular-nums">{fa(rule.threshold)}</span>
                  </Td>
                  <Td className="tabular-nums text-(--ice-3)">{fa(rule.windowMinutes)} دقیقه</Td>
                  <Td className={rule.severity === "Critical" ? "font-bold text-(--ember)" : "text-(--ice-2)"}>
                    {rule.severity === "Critical" ? "بحرانی" : rule.severity === "Warning" ? "هشدار" : "اطلاع"}
                  </Td>
                  <Td>{rule.smsNotify ? "✅" : "—"}</Td>
                  <Td>
                    <button
                      type="button"
                      onClick={() => toggleEnabled(rule)}
                      className={`rounded-full px-2.5 py-0.5 text-[10.5px] font-bold ${
                        rule.isEnabled ? "bg-(--mint)/12 text-(--mint)" : "bg-(--fld) text-(--ice-3)"
                      }`}
                    >
                      {rule.isEnabled ? "فعال" : "غیرفعال"}
                    </button>
                  </Td>
                  <Td>
                    <button
                      type="button"
                      onClick={() => startEdit(rule)}
                      className="ml-2 rounded-md border border-(--edge) px-2 py-1 text-[11px] text-(--ice-2) transition-colors hover:bg-(--hov)"
                    >
                      ویرایش
                    </button>
                    <button
                      type="button"
                      onClick={() => removeRule(rule)}
                      className="rounded-md border border-(--ember)/50 px-2 py-1 text-[11px] text-(--ember) transition-colors hover:bg-(--ember)/10"
                    >
                      حذف
                    </button>
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        )}
      </section>

      {formOpen && (
        <section className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <h3 className="mb-4 text-[13px] font-bold text-(--ice)">{editing ? `ویرایش «${editing.name}»` : "قانون جدید"}</h3>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="نام قانون">
              <input
                type="text"
                value={draft.name}
                onChange={(e) => setDraft({ ...draft, name: e.target.value })}
                className="w-full rounded-[10px] border border-(--edge) bg-(--void) px-3 py-1.5 text-[12px] text-(--ice)"
              />
            </Field>
            <Field label="سنجه">
              <select
                value={draft.metric}
                onChange={(e) => setDraft({ ...draft, metric: e.target.value })}
                className="w-full rounded-[10px] border border-(--edge) bg-(--void) px-3 py-1.5 text-[12px] text-(--ice)"
              >
                {METRICS.map((m) => (
                  <option key={m} value={m}>
                    {ALERT_METRIC_LABELS[m]}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="شرط">
              <select
                value={draft.comparator}
                onChange={(e) => setDraft({ ...draft, comparator: e.target.value })}
                className="w-full rounded-[10px] border border-(--edge) bg-(--void) px-3 py-1.5 text-[12px] text-(--ice)"
              >
                {COMPARATORS.map((c) => (
                  <option key={c} value={c}>
                    {ALERT_COMPARATOR_LABELS[c]}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="آستانه">
              <input
                type="number"
                step="any"
                value={draft.threshold}
                onChange={(e) => setDraft({ ...draft, threshold: e.target.value })}
                className="w-full rounded-[10px] border border-(--edge) bg-(--void) px-3 py-1.5 text-[12px] text-(--ice)"
              />
            </Field>
            <Field label="پنجرهٔ بررسی (دقیقه)">
              <input
                type="number"
                min={1}
                value={draft.windowMinutes}
                onChange={(e) => setDraft({ ...draft, windowMinutes: e.target.value })}
                className="w-full rounded-[10px] border border-(--edge) bg-(--void) px-3 py-1.5 text-[12px] text-(--ice)"
              />
            </Field>
            <Field label="شدت">
              <select
                value={draft.severity}
                onChange={(e) => setDraft({ ...draft, severity: e.target.value })}
                className="w-full rounded-[10px] border border-(--edge) bg-(--void) px-3 py-1.5 text-[12px] text-(--ice)"
              >
                <option value="Info">اطلاع</option>
                <option value="Warning">هشدار</option>
                <option value="Critical">بحرانی</option>
              </select>
            </Field>
            <label className="flex items-center gap-2 self-end text-[12px] text-(--ice-2)">
              <input
                type="checkbox"
                checked={draft.smsNotify}
                onChange={(e) => setDraft({ ...draft, smsNotify: e.target.checked })}
                className="h-3.5 w-3.5 accent-(--mint)"
              />
              ارسال پیامک به مالک در فعال شدن (فقط بحرانی‌ها توصیه می‌شود)
            </label>
          </div>
          <div className="mt-5 flex gap-2.5">
            <button
              type="button"
              onClick={save}
              disabled={saving || !draft.name.trim() || draft.threshold === "" || draft.windowMinutes === ""}
              className="rounded-[10px] bg-(--mint) px-4 py-1.5 text-[12px] font-semibold text-(--on-mint) transition-opacity hover:opacity-90 disabled:opacity-50"
            >
              {saving ? "در حال ذخیره…" : "ذخیره"}
            </button>
            <button
              type="button"
              onClick={() => {
                setCreating(false);
                setEditing(null);
              }}
              className="rounded-[10px] border border-(--edge) px-4 py-1.5 text-[12px] text-(--ice-2) transition-colors hover:bg-(--hov)"
            >
              انصراف
            </button>
          </div>
        </section>
      )}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <div className="text-[11.5px] text-(--ice-3)">{label}</div>
      {children}
    </div>
  );
}
