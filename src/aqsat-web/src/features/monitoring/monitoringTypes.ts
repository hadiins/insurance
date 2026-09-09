/** Shared types and label/color maps for the platform-owner monitoring & security pages
 * (api/monitoring/*). Mirrors MonitoringContracts.cs exactly. */

export interface MonitoringOverviewDto {
  totalRequests: number;
  totalErrors: number;
  totalClientErrors: number;
  errorRatePercent: number;
  latencyP95Ms: number;
  latencyP99Ms: number;
  uptimePercent: number;
  healthStatus: string;
  lastSampleUtc: string | null;
  activeAlerts: number;
  latestAlerts: AlertOccurrenceDto[];
  security: SecuritySummaryDto;
}

export interface SecuritySummaryDto {
  failedLogins: number;
  successfulLogins: number;
  rateLimitRejections: number;
  permissionDenied: number;
  sensitiveSettingChanges: number;
  suspiciousActivities: number;
  criticalEvents: number;
  warningEvents: number;
}

export interface TimeseriesPointDto {
  bucketUtc: string;
  requests: number;
  errors: number;
  clientErrors: number;
  latencyP95Ms: number;
  latencyP99Ms: number;
}

export interface EndpointStatDto {
  path: string;
  count: number;
  errors: number;
  avgMs: number;
  slowCount: number;
}

export interface SlowQueryDto {
  text: string;
  count: number;
  avgMs: number;
  maxMs: number;
}

export interface ServerStatusDto {
  cpuPercent: number | null;
  processMemoryMb: number;
  threadCount: number;
  diskFreeGb: number;
  dbProbeMs: number | null;
  healthStatus: string;
  processUpMinutes: number;
  liveMinuteRequests: number;
  liveMinuteErrors: number;
  https: HttpsStatusDto;
}

export interface HttpsStatusDto {
  enabled: boolean;
  url: string;
  status: string;
}

export interface RecurringJobStatusDto {
  id: string;
  cron: string;
  lastExecutionUtc: string | null;
  nextExecutionUtc: string | null;
}

export interface HangfireStatsDto {
  failedCount: number;
  processingCount: number;
  scheduledCount: number;
  serversCount: number;
  jobs: RecurringJobStatusDto[];
}

export interface LogEntryDto {
  timestampUtc: string;
  level: string;
  message: string;
}

export interface LogPageDto {
  entries: LogEntryDto[];
  nextBefore: string | null;
  hasMore: boolean;
}

export interface SecurityEventDto {
  id: string;
  occurredAt: string;
  type: string;
  severity: string;
  ipAddress: string | null;
  mobile: string | null;
  detail: string;
}

export interface SecurityEventPageDto {
  items: SecurityEventDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface SecurityScoreDto {
  score: number;
  factors: { label: string; impact: number; ok: boolean }[];
}

export interface AlertOccurrenceDto {
  id: string;
  ruleName: string;
  severity: string;
  status: string;
  startedAt: string;
  lastSeenAt: string;
  observedValue: number;
  message: string;
}

export interface AlertRuleDto {
  id: string;
  name: string;
  metric: string;
  comparator: string;
  threshold: number;
  windowMinutes: number;
  severity: string;
  isEnabled: boolean;
  smsNotify: boolean;
}

export const TIME_RANGES = [
  { key: "1h", label: "۱ ساعت" },
  { key: "6h", label: "۶ ساعت" },
  { key: "24h", label: "۲۴ ساعت" },
  { key: "7d", label: "۷ روز" },
  { key: "30d", label: "۳۰ روز" },
] as const;

export type TimeRangeKey = (typeof TIME_RANGES)[number]["key"];

export const SECURITY_TYPE_LABELS: Record<string, string> = {
  FailedLogin: "ورود ناموفق",
  SuccessfulLogin: "ورود موفق",
  RateLimitRejection: "رد سقف نرخ",
  PermissionDenied: "دسترسی غیرمجاز",
  SensitiveSettingChanged: "تغییر تنظیم حساس",
  SuspiciousActivity: "فعالیت مشکوک",
};

export const SECURITY_SEVERITY_LABELS: Record<string, string> = {
  Info: "اطلاع",
  Warning: "هشدار",
  Critical: "بحرانی",
};

export const ALERT_STATUS_LABELS: Record<string, string> = {
  Active: "فعال",
  Acknowledged: "دیده‌شده",
  Resolved: "برطرف‌شده",
};

export const ALERT_METRIC_LABELS: Record<string, string> = {
  ErrorRatePercent: "نرخ خطای سرور (٪)",
  LatencyP95Ms: "تأخیر P95 (میلی‌ثانیه)",
  CpuPercent: "پردازش فرایند (٪)",
  ProcessMemoryMb: "حافظهٔ فرایند (مگابایت)",
  DbProbeMs: "زمان پاسخ دیتابیس (میلی‌ثانیه)",
  DiskFreeGb: "فضای آزاد دیسک (گیگابایت)",
  FailedLoginCount: "تعداد ورود ناموفق",
  HealthDown: "از دسترس خارج شدن سرویس",
};

export const ALERT_COMPARATOR_LABELS: Record<string, string> = {
  GreaterThan: "بیش از",
  LessThan: "کمتر از",
};

export const HEALTH_LABELS: Record<string, string> = {
  ok: "سالم",
  degraded: "کند",
  down: "از دسترس خارج",
  unknown: "نامشخص",
};

export function severityTone(severity: string): "mint" | "amber" | "ember" {
  return severity === "Critical" ? "ember" : severity === "Warning" ? "amber" : "mint";
}
