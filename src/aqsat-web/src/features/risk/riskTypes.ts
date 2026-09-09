export type RiskLevelKey = "VeryLow" | "Low" | "Medium" | "High" | "Critical";
export type RiskDecisionKey = "Approve" | "ManualReview" | "Decline";

export interface RiskFactorDto {
  code: string;
  title: string;
  detail: string;
  impact: number;
  severity: "Positive" | "Info" | "Medium" | "High";
}

export interface TriggeredRuleDto {
  code: string;
  name: string;
  description: string;
  forcedLevel: string | null;
  forcedDecision: string | null;
  isPositive: boolean;
}

export interface RiskAssessmentDto {
  id: string;
  customerId: string;
  score: number;
  riskLevel: string;
  riskLevelKey: RiskLevelKey;
  decision: string;
  decisionKey: RiskDecisionKey;
  probabilityOfDefault: number | null;
  currentDebtToman: number;
  overdueAmountToman: number;
  overdueCount: number;
  maxDaysOverdue: number;
  returnedChequeCount: number;
  onTimeRatePercent: number;
  settledInstallmentCount: number;
  tenureMonths: number;
  creditExposureToman: number;
  creditLimitToman: number;
  creditLimitIsOverride: boolean;
  factors: RiskFactorDto[];
  triggeredRules: TriggeredRuleDto[];
  source: string;
  modelVersion: string;
  calculatedAtUtc: string;
}

export interface CustomerRiskDto {
  hasAssessment: boolean;
  insufficientData: boolean;
  latest: RiskAssessmentDto | null;
}

export interface RiskHistoryItemDto {
  id: string;
  calculatedAtUtc: string;
  score: number;
  riskLevel: string;
  decision: string;
  creditLimitToman: number;
  topFactors: string[];
  source: string;
}

export interface CustomerCreditLimitDto {
  recommendedToman: number | null;
  overrideToman: number | null;
  effectiveToman: number;
}

export interface RiskSettingsDto {
  paymentHistoryWeight: number;
  currentDebtWeight: number;
  latePaymentWeight: number;
  returnedChequesWeight: number;
  customerTenureWeight: number;
  insuranceBehaviorWeight: number;
  veryLowMinScore: number;
  lowMinScore: number;
  mediumMinScore: number;
  highMinScore: number;
  approveMinScore: number;
  declineBelowScore: number;
  baseCreditLimitToman: number;
  veryLowMultiplier: number;
  lowMultiplier: number;
  mediumMultiplier: number;
  highMultiplier: number;
  criticalMultiplier: number;
  bouncedChequeHighThreshold: number;
  severeOverdueDays: number;
  maxLateDaysHighThreshold: number;
  onTimeRatePositivePercent: number;
  scoreDropWarningPoints: number;
  debtGrowthWarningPercent: number;
  creditLimitUtilizationWarningPercent: number;
  issuanceGateMode: "Informational" | "SoftBlock" | "HardBlock";
}

export const RISK_LEVEL_STYLES: Record<RiskLevelKey, { chip: string; text: string; label: string }> = {
  VeryLow: { chip: "bg-(--mint)/15 text-(--mint) border-(--mint)/40", text: "text-(--mint)", label: "خیلی پایین" },
  Low: { chip: "bg-(--mint)/15 text-(--mint) border-(--mint)/40", text: "text-(--mint)", label: "پایین" },
  Medium: { chip: "bg-(--amber)/15 text-(--amber) border-(--amber)/40", text: "text-(--amber)", label: "متوسط" },
  High: { chip: "bg-(--ember)/15 text-(--ember) border-(--ember)/40", text: "text-(--ember)", label: "بالا" },
  Critical: { chip: "bg-(--ember)/20 text-(--ember) border-(--ember) font-bold", text: "text-(--ember)", label: "بحرانی" },
};

export const RISK_DECISION_STYLES: Record<RiskDecisionKey, { chip: string; label: string }> = {
  Approve: { chip: "bg-(--mint)/15 text-(--mint) border-(--mint)/40", label: "تأیید" },
  ManualReview: { chip: "bg-(--amber)/15 text-(--amber) border-(--amber)/40", label: "بررسی دستی" },
  Decline: { chip: "bg-(--ember)/15 text-(--ember) border-(--ember)/40", label: "رد" },
};

// ---- Stage 3: dashboard / high-risk / warnings / manual reviews (doc §6/§15/§19/§20) ----

export interface TrendPointDto {
  date: string;
  value: number;
}

export interface WarningTypeCountDto {
  type: string;
  typeFa: string;
  count: number;
}

export interface RiskDashboardDto {
  totalCustomers: number;
  assessedCustomers: number;
  veryLowCount: number;
  lowCount: number;
  mediumCount: number;
  highCount: number;
  criticalCount: number;
  currentDebtToman: number;
  totalOverdueToman: number;
  onTimeRatePercent: number | null;
  defaultRatePercent: number;
  openReviewsCount: number;
  unreadWarningsCount: number;
  scoreTrend: TrendPointDto[];
  overdueTrend: TrendPointDto[];
  onTimeTrend: TrendPointDto[];
  highRiskTrend: TrendPointDto[];
  warningsByType: WarningTypeCountDto[];
}

export interface HighRiskRiskCustomerDto {
  customerId: string;
  fullName: string;
  mobile: string | null;
  overdueInstallmentCount: number;
  maxDaysOverdue: number;
  bouncedChequeCount: number;
  overdueAmountToman: number;
  score: number | null;
  riskLevel: string | null;
  riskLevelKey: RiskLevelKey | null;
  decision: string | null;
  decisionKey: RiskDecisionKey | null;
}

export interface RiskWarningDto {
  id: string;
  customerId: string;
  customerName: string;
  type: string;
  typeFa: string;
  message: string;
  isRead: boolean;
  createdAt: string;
}

export type ManualReviewStatusKey = "Pending" | "InReview" | "Approved" | "Rejected" | "RequestMoreInfo";

export interface ManualReviewDto {
  id: string;
  customerId: string;
  customerName: string;
  assessmentId: string;
  score: number;
  riskLevel: string;
  riskLevelKey: RiskLevelKey;
  recommendedDecision: string;
  recommendedDecisionKey: RiskDecisionKey;
  finalDecision: string | null;
  finalDecisionKey: RiskDecisionKey | null;
  status: string;
  statusFa: string;
  assignedToName: string | null;
  currentDebtToman: number;
  overdueAmountToman: number;
  overdueCount: number;
  returnedChequeCount: number;
  triggeredRules: TriggeredRuleDto[];
  note: string | null;
  createdAt: string;
  resolvedAt: string | null;
}

export interface ReviewerDto {
  id: string;
  fullName: string;
}

export const MANUAL_REVIEW_STATUS_LABELS: Record<ManualReviewStatusKey, string> = {
  Pending: "در انتظار بررسی",
  InReview: "در حال بررسی",
  Approved: "تأییدشده",
  Rejected: "ردشده",
  RequestMoreInfo: "نیاز به اطلاعات بیشتر",
};

// ---- Phase 2B-1: cross-agency network lookup (owner decision: status only, no amounts/identity) ----

export interface NetworkRiskResultDto {
  agencyName: string;
  insurerName: string | null;
  isOwnAgency: boolean;
  score: number;
  riskLevel: string;
  riskLevelKey: RiskLevelKey;
  decision: string;
  decisionKey: RiskDecisionKey;
  overdueCount: number;
  maxDaysOverdue: number;
  returnedChequeCount: number;
  onTimeRatePercent: number;
  tenureMonths: number;
  settledInstallmentCount: number;
  calculatedAtUtc: string;
}

export interface NetworkRiskLookupDto {
  isEnabled: boolean;
  queryKind: "nationalId" | "plate";
  results: NetworkRiskResultDto[];
}

export interface RiskNetworkSettingsDto {
  isEnabled: boolean;
  updatedAtUtc: string | null;
}
