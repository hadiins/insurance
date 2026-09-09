import { ALERT_STATUS_LABELS, SECURITY_SEVERITY_LABELS } from "./monitoringTypes";

type Tone = "mint" | "amber" | "ember";

function toneClasses(tone: Tone): string {
  return tone === "ember"
    ? "bg-(--ember)/13 text-(--ember)"
    : tone === "amber"
      ? "bg-(--amber)/14 text-(--amber)"
      : "bg-(--mint)/12 text-(--mint)";
}

/** Colored pill for a security severity (Info/Warning/Critical) or alert status
 * (Active/Acknowledged/Resolved). Always text+color, never color alone. */
export function SeverityBadge({ severity }: { severity: string }) {
  const tone: Tone =
    severity === "Critical" || severity === "Active"
      ? "ember"
      : severity === "Warning" || severity === "Acknowledged"
        ? "amber"
        : "mint";
  const label = SECURITY_SEVERITY_LABELS[severity] ?? ALERT_STATUS_LABELS[severity] ?? severity;
  return (
    <span className={`inline-block rounded-full px-2.5 py-0.5 text-[10.5px] font-bold ${toneClasses(tone)}`}>{label}</span>
  );
}
