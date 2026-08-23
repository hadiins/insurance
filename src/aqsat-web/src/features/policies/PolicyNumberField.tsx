import { fa } from "../../lib/persian";

export interface PolicyNumberSuggestionDto {
  insurerName: string;
  separator: string;
  lineCode: string | null;
  agencyCode: string | null;
  yearDisplay: string;
  serialLength: number;
  suggestedSerial: string;
  lastSerial: string | null;
  lastIssueDate: string | null;
  composedPreview: string | null;
  canCompose: boolean;
}

interface Props {
  disabled: boolean;
  loading: boolean;
  suggestion: PolicyNumberSuggestionDto | null;
  serialInput: string;
  onSerialChange: (v: string) => void;
  composedNumber: string | null;
  manualEntry: boolean;
  onToggleManual: (v: boolean) => void;
  manualNumberInput: string;
  onManualNumberChange: (v: string) => void;
}

/** docs/TASK-24-POLICY-NUMBER.md §2 — three locked segments (line/agency/year) shown but not
 * editable, one editable serial input, and an escape hatch that swaps the whole control for a
 * single free-text field. */
export function PolicyNumberField({
  disabled,
  loading,
  suggestion,
  serialInput,
  onSerialChange,
  composedNumber,
  manualEntry,
  onToggleManual,
  manualNumberInput,
  onManualNumberChange,
}: Props) {
  return (
    <div className="col-span-2">
      <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">شمارهٔ بیمه‌نامه</label>

      {manualEntry ? (
        <div>
          <input
            value={manualNumberInput}
            onChange={(e) => onManualNumberChange(e.target.value)}
            placeholder="1110/576210/405/000001"
            dir="ltr"
            className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-left text-[13.5px] tabular-nums text-(--ice) outline-none focus:border-(--mint)"
          />
          <button
            type="button"
            onClick={() => onToggleManual(false)}
            className="mt-1.5 text-[11px] text-(--ice-3) underline decoration-dotted hover:text-(--ice-2)"
          >
            بازگشت به حالت خودکار
          </button>
        </div>
      ) : (
        <div className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2.5">
          {disabled ? (
            <div className="text-[12px] text-(--ice-3)">ابتدا نوع بیمه‌نامه و تاریخ صدور را انتخاب کنید.</div>
          ) : (
            <>
              <div className="flex items-center gap-1.5 text-[13.5px] tabular-nums" dir="ltr">
                <LockedSegment value={suggestion?.lineCode ?? "----"} title="رشته" missing={!suggestion?.lineCode} />
                <span className="text-(--ice-3)">{suggestion?.separator ?? "/"}</span>
                <LockedSegment value={suggestion?.agencyCode ?? "----"} title="نمایندگی" missing={!suggestion?.agencyCode} />
                <span className="text-(--ice-3)">{suggestion?.separator ?? "/"}</span>
                <LockedSegment value={suggestion?.yearDisplay ?? "---"} title="سال" />
                <span className="text-(--ice-3)">{suggestion?.separator ?? "/"}</span>
                <input
                  value={serialInput}
                  onChange={(e) => onSerialChange(e.target.value)}
                  onBlur={() => {
                    if (serialInput.trim() && suggestion) {
                      onSerialChange(serialInput.trim().padStart(suggestion.serialLength, "0"));
                    }
                  }}
                  placeholder={suggestion?.suggestedSerial ?? "000001"}
                  className="w-24 rounded-[6px] border border-(--mint)/40 bg-(--pane) px-2 py-1 text-center text-(--mint) outline-none focus:border-(--mint)"
                />
              </div>
              <div className="mt-1 flex gap-1.5 text-[10px] text-(--ice-3)" dir="ltr">
                <span className="w-[52px] text-center">رشته</span>
                <span className="w-[1px]" />
                <span className="w-[68px] text-center">نمایندگی</span>
                <span className="w-[1px]" />
                <span className="w-[44px] text-center">سال</span>
                <span className="w-[1px]" />
                <span className="w-24 text-center">سریال</span>
              </div>

              {loading ? (
                <div className="mt-2 text-[11.5px] text-(--ice-3)">در حال محاسبهٔ پیشنهاد…</div>
              ) : (
                <>
                  {suggestion?.lastSerial && (
                    <div className="mt-2 text-[11.5px] text-(--ice-3)">
                      آخرین ثبت‌شده: <span className="tabular-nums">{fa(suggestion.lastSerial)}</span>
                      {suggestion.lastIssueDate ? ` · ${suggestion.lastIssueDate}` : ""}
                    </div>
                  )}
                  {!suggestion?.canCompose && (
                    <div className="mt-2 text-[11.5px] text-(--ember)">
                      کد رشته یا کد نمایندگی هنوز تنظیم نشده — از «ورود دستی شمارهٔ کامل» استفاده کنید.
                    </div>
                  )}
                  {composedNumber && (
                    <div className="mt-2 text-[12px] text-(--mint)">
                      ✅ <span className="tabular-nums" dir="ltr">{composedNumber}</span>
                    </div>
                  )}
                </>
              )}
            </>
          )}
          <button
            type="button"
            onClick={() => onToggleManual(true)}
            className="mt-2.5 block text-[11px] text-(--ice-3) underline decoration-dotted hover:text-(--ice-2)"
          >
            ورود دستی شمارهٔ کامل
          </button>
        </div>
      )}
    </div>
  );
}

function LockedSegment({ value, title, missing }: { value: string; title: string; missing?: boolean }) {
  return (
    <span
      title={title}
      className={`rounded-[6px] px-2 py-1 ${missing ? "bg-(--ember)/10 text-(--ember)" : "bg-(--pane) text-(--ice-2)"}`}
    >
      {value}
    </span>
  );
}
