import { useState } from "react";
import { ApiError } from "../../lib/api";
import { toLatinDigits } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { PlateField, EMPTY_PLATE, isPlateFilled, type PlateParts } from "../policies/PlateField";
import { useNetworkRiskLookup } from "./riskApi";
import { NetworkRiskResults } from "./NetworkRiskResultCard";

type Query = { nationalId: string } | { plate: string } | null;

const MODE_TAB =
  "rounded-[10px] border px-3.5 py-1.5 text-[12.5px] font-semibold transition-colors";

/** «استعلام شبکه‌ای» (Phase 2B-1) — cross-agency status lookup by national ID or vehicle plate.
 * Behind the platform owner's switch; the disabled state is shown explicitly, never as an empty
 * list (rule 17). Plate entry uses the SAME structured PlateField the issuance wizard uses, and
 * the query string is composed exactly the way PoliciesController/PlateParser.Compose builds
 * Vehicle.Plate — so the lookup's Parse() can never fail and its Normalized form matches
 * PlateNormalized byte-for-byte. A free-text plate here would accept formats the wizard never
 * produces, and the user would only find out via a parse error. */
export function NetworkRiskPage() {
  const [mode, setMode] = useState<"nationalId" | "plate">("nationalId");
  const [nationalId, setNationalId] = useState("");
  const [plate, setPlate] = useState<PlateParts>(EMPTY_PLATE);
  const [query, setQuery] = useState<Query>(null);

  const lookup = useNetworkRiskLookup(query);
  const error = lookup.isError ? ((lookup.error as ApiError).message ?? "استعلام ناموفق بود.") : null;

  function search() {
    if (mode === "nationalId") {
      const v = toLatinDigits(nationalId.trim());
      if (!v) return;
      setQuery({ nationalId: v });
    } else {
      if (!isPlateFilled(plate)) return;
      // Same composition as PlateParser.Compose (backend): two + letter + three + "-" + iran,
      // digits Latin. The backend re-parses and re-normalizes it — identical input, identical key.
      setQuery({ plate: `${plate.twoDigit}${plate.letter}${plate.threeDigit}-${plate.iranCode}` });
    }
  }

  const plateReady = mode !== "plate" || isPlateFilled(plate);

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">استعلام شبکه‌ای ریسک</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        بررسی وضعیت اعتبار و ریسک مشتری در سایر نمایندگی‌های شبکه — با کد ملی مالک یا شماره پلاک خودرو
      </div>

      <div className="mb-3 flex gap-1.5">
        <button
          type="button"
          onClick={() => setMode("nationalId")}
          className={`${MODE_TAB} ${
            mode === "nationalId"
              ? "border-(--mint) bg-(--mint)/10 text-(--mint)"
              : "border-(--edge-2) bg-(--fld) text-(--ice-3) hover:text-(--ice)"
          }`}
        >
          با کد ملی
        </button>
        <button
          type="button"
          onClick={() => setMode("plate")}
          className={`${MODE_TAB} ${
            mode === "plate"
              ? "border-(--mint) bg-(--mint)/10 text-(--mint)"
              : "border-(--edge-2) bg-(--fld) text-(--ice-3) hover:text-(--ice)"
          }`}
        >
          با شماره پلاک
        </button>
      </div>

      <div className="mb-4.5 flex items-end gap-2">
        {mode === "nationalId" ? (
          <input
            value={nationalId}
            onChange={(e) => setNationalId(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && search()}
            placeholder="کد ملی مالک…"
            dir="ltr"
            className="flex-1 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
        ) : (
          <PlateField value={plate} onChange={setPlate} inline />
        )}
        <button
          type="button"
          onClick={search}
          disabled={!plateReady}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-40"
        >
          استعلام
        </button>
      </div>

      {query === null ? (
        <EmptyState
          icon="🌐"
          title="کد ملی یا پلاک را وارد کنید."
          description="وضعیت اعتباری مشتری در نمایندگی‌های دیگر شبکه (امتیاز، معوقات، چک برگشتی و سابقهٔ پرداخت) نمایش داده می‌شود."
        />
      ) : (
        <NetworkRiskResults
          data={lookup.data}
          isPending={lookup.isPending}
          error={error}
          onRetry={() => void lookup.refetch()}
        />
      )}
    </div>
  );
}
