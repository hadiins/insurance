import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../lib/api";
import type {
  CustomerCreditLimitDto,
  CustomerRiskDto,
  HighRiskRiskCustomerDto,
  ManualReviewDto,
  NetworkRiskLookupDto,
  RiskDashboardDto,
  RiskHistoryItemDto,
  RiskNetworkSettingsDto,
  RiskSettingsDto,
  RiskWarningDto,
  ReviewerDto,
} from "./riskTypes";

/** Server state for the risk feature runs through TanStack Query (docs Phase 2A §22) — no copied
 * Zustand state; the tab shell keeps UI state as before. */

export function useCustomerRisk(customerId: string | null) {
  return useQuery({
    queryKey: ["risk", "customer", customerId],
    queryFn: () => api.get<CustomerRiskDto>(`/customers/${customerId}/risk`),
    enabled: customerId !== null,
  });
}

export function useRiskHistory(customerId: string | null) {
  return useQuery({
    queryKey: ["risk", "history", customerId],
    queryFn: () => api.get<RiskHistoryItemDto[]>(`/customers/${customerId}/risk/history`),
    enabled: customerId !== null,
  });
}

export function useCreditLimit(customerId: string | null) {
  return useQuery({
    queryKey: ["risk", "credit-limit", customerId],
    queryFn: () => api.get<CustomerCreditLimitDto>(`/customers/${customerId}/credit-limit`),
    enabled: customerId !== null,
  });
}

export function useAssessCustomer(customerId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<CustomerRiskDto>(`/customers/${customerId}/risk/assess`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["risk", "customer", customerId] });
      queryClient.invalidateQueries({ queryKey: ["risk", "history", customerId] });
      queryClient.invalidateQueries({ queryKey: ["risk", "credit-limit", customerId] });
    },
  });
}

export function useSetCreditLimit(customerId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { limitToman: number; reason: string | null }) =>
      api.put<void>(`/customers/${customerId}/credit-limit`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["risk", "credit-limit", customerId] });
      queryClient.invalidateQueries({ queryKey: ["risk", "customer", customerId] });
    },
  });
}

export function useRiskSettings() {
  return useQuery({
    queryKey: ["risk", "settings"],
    queryFn: () => api.get<RiskSettingsDto>("/risk/settings"),
  });
}

export function useUpdateRiskSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (settings: RiskSettingsDto) => api.put<RiskSettingsDto>("/risk/settings", settings),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["risk", "settings"] }),
  });
}

// ---- Stage 3: agency-wide views (docs Phase 2A §6/§15/§19/§20) ----

export function useRiskDashboard() {
  return useQuery({
    queryKey: ["risk", "dashboard"],
    queryFn: () => api.get<RiskDashboardDto>("/risk/dashboard"),
  });
}

export function useHighRiskCustomers() {
  return useQuery({
    queryKey: ["risk", "high-risk"],
    queryFn: () => api.get<HighRiskRiskCustomerDto[]>("/risk/high-risk"),
  });
}

export function useRiskWarnings(unreadOnly: boolean) {
  return useQuery({
    queryKey: ["risk", "warnings", unreadOnly],
    queryFn: () => api.get<RiskWarningDto[]>(`/risk/warnings${unreadOnly ? "?unreadOnly=true" : ""}`),
  });
}

export function useMarkWarningRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (warningId: string) => api.post<void>(`/risk/warnings/${warningId}/read`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["risk", "warnings"] });
      queryClient.invalidateQueries({ queryKey: ["risk", "dashboard"] });
    },
  });
}

export function useManualReviews(status: string | null) {
  return useQuery({
    queryKey: ["risk", "manual-reviews", status],
    queryFn: () =>
      api.get<ManualReviewDto[]>(`/risk/manual-reviews${status ? `?status=${encodeURIComponent(status)}` : ""}`),
  });
}

export function useReviewers() {
  return useQuery({
    queryKey: ["risk", "reviewers"],
    queryFn: () => api.get<ReviewerDto[]>("/risk/manual-reviews/reviewers"),
  });
}

function useInvalidateReviews() {
  const queryClient = useQueryClient();
  return () => {
    queryClient.invalidateQueries({ queryKey: ["risk", "manual-reviews"] });
    queryClient.invalidateQueries({ queryKey: ["risk", "dashboard"] });
  };
}

export function useAssignReview() {
  const invalidate = useInvalidateReviews();
  return useMutation({
    mutationFn: (input: { reviewId: string; assignedToUserId: string | null }) =>
      api.post<void>(`/risk/manual-reviews/${input.reviewId}/assign`, {
        assignedToUserId: input.assignedToUserId,
      }),
    onSuccess: invalidate,
  });
}

export function useDecideReview() {
  const invalidate = useInvalidateReviews();
  return useMutation({
    mutationFn: (input: { reviewId: string; finalDecision: "Approve" | "Decline"; note: string | null }) =>
      api.post<void>(`/risk/manual-reviews/${input.reviewId}/decision`, {
        finalDecision: input.finalDecision,
        note: input.note,
      }),
    onSuccess: invalidate,
  });
}

export function useRequestMoreInfo() {
  const invalidate = useInvalidateReviews();
  return useMutation({
    mutationFn: (input: { reviewId: string; note: string | null }) =>
      api.post<void>(`/risk/manual-reviews/${input.reviewId}/request-info`, { note: input.note }),
    onSuccess: invalidate,
  });
}

// ---- Phase 2B-1: cross-agency network lookup ----

export function useNetworkRiskLookup(query: { nationalId: string } | { plate: string } | null) {
  return useQuery({
    queryKey: ["risk", "network", query],
    queryFn: () => {
      if (query === null) {
        throw new Error("network lookup requires a query");
      }
      const params = "nationalId" in query
        ? `nationalId=${encodeURIComponent(query.nationalId)}`
        : `plate=${encodeURIComponent(query.plate)}`;
      return api.get<NetworkRiskLookupDto>(`/risk/network?${params}`);
    },
    enabled: query !== null,
  });
}

export function useRiskNetworkSettings() {
  return useQuery({
    queryKey: ["risk", "network-settings"],
    queryFn: () => api.get<RiskNetworkSettingsDto>("/risk/network/settings"),
  });
}

export function useSaveRiskNetworkSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (isEnabled: boolean) => api.put<RiskNetworkSettingsDto>("/risk/network/settings", { isEnabled }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["risk", "network-settings"] }),
  });
}
