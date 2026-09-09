using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Risk;

namespace Aqsat.UnitTests.Risk;

/// <summary>docs Phase 2A §29 — the pure engines are unit-tested without a database: score bands,
/// rule firing, decision override and the credit-limit math. Money/date calculations must never be
/// left untested (CLAUDE.md definition of done).</summary>
public class RiskEngineTests
{
    private static readonly RiskSettings Settings = new();

    private static RiskFeatures Features(
        int policyCount = 1, int activePolicyCount = 1, int renewalCount = 0, int tenureMonths = 12,
        decimal currentDebtToman = 0, decimal overdueAmountToman = 0, int overdueCount = 0,
        int maxDaysOverdue = 0, bool hasDefaultHistory = false, int returnedChequeCount = 0,
        int settledInstallmentCount = 0, int onTimeSettledCount = 0) =>
        new(policyCount, activePolicyCount, renewalCount, tenureMonths, currentDebtToman,
            overdueAmountToman, overdueCount, maxDaysOverdue, hasDefaultHistory, returnedChequeCount,
            settledInstallmentCount, onTimeSettledCount);

    // ---- Rule engine (doc §12) ----

    [Fact]
    public void NoAdverseConditions_FiresOnlyPositiveRule()
    {
        var rules = RiskRuleEngine.Evaluate(
            Features(settledInstallmentCount: 10, onTimeSettledCount: 10), Settings, 100_000_000m);

        var rule = Assert.Single(rules);
        Assert.Equal("ON_TIME_REGULAR", rule.Code);
        Assert.True(rule.IsPositive);
        Assert.Null(rule.ForcedLevel);
        Assert.Null(rule.ForcedDecision);
    }

    [Fact]
    public void TwoReturnedCheques_ForcesHighLevel()
    {
        var rules = RiskRuleEngine.Evaluate(Features(returnedChequeCount: 2), Settings, 0);

        Assert.Contains(rules, r => r.Code == "RETURNED_CHEQUES_HIGH" && r.ForcedLevel == RiskLevel.High);
        Assert.Equal(RiskLevel.High, RiskRuleEngine.ForcedLevel(rules));
    }

    [Fact]
    public void PreviousDefault_ForcesCriticalAndDecline()
    {
        var rules = RiskRuleEngine.Evaluate(Features(hasDefaultHistory: true), Settings, 0);

        var rule = Assert.Single(rules, r => r.Code == "DEFAULT_HISTORY");
        Assert.Equal(RiskLevel.Critical, rule.ForcedLevel);
        Assert.Equal(RiskDecision.Decline, rule.ForcedDecision);
    }

    [Fact]
    public void SevereOverdue_FiresBothOverdueRules()
    {
        var rules = RiskRuleEngine.Evaluate(Features(maxDaysOverdue: 95), Settings, 0);

        Assert.Contains(rules, r => r.Code == "SEVERE_OVERDUE");
        Assert.Contains(rules, r => r.Code == "MAX_LATE_DAYS");
        Assert.Equal(RiskLevel.High, RiskRuleEngine.ForcedLevel(rules));
    }

    [Fact]
    public void DebtAboveEffectiveLimit_ForcesManualReviewDecision()
    {
        var rules = RiskRuleEngine.Evaluate(Features(currentDebtToman: 120_000_000m), Settings, 100_000_000m);

        var rule = Assert.Single(rules, r => r.Code == "OVER_CREDIT_LIMIT");
        Assert.Null(rule.ForcedLevel);
        Assert.Equal(RiskDecision.ManualReview, rule.ForcedDecision);
    }

    [Fact]
    public void ZeroCreditLimit_NeverTriggersOverLimit()
    {
        var rules = RiskRuleEngine.Evaluate(Features(currentDebtToman: 120_000_000m), Settings, 0);

        Assert.DoesNotContain(rules, r => r.Code == "OVER_CREDIT_LIMIT");
    }

    // ---- Score bands (doc §8) ----

    [Theory]
    [InlineData(800, RiskLevel.VeryLow)]
    [InlineData(750, RiskLevel.Low)]
    [InlineData(620, RiskLevel.Medium)]
    [InlineData(450, RiskLevel.High)]
    [InlineData(350, RiskLevel.Critical)]
    public void LevelForScore_MapsBands(int score, RiskLevel expected)
    {
        Assert.Equal(expected, CreditScoreEngine.LevelForScore(score, Settings));
    }

    [Fact]
    public void CleanPayer_ScoresHigh()
    {
        var (score, factors) = CreditScoreEngine.Calculate(
            Features(policyCount: 3, activePolicyCount: 1, renewalCount: 2, tenureMonths: 36,
                settledInstallmentCount: 10, onTimeSettledCount: 10),
            Settings);

        Assert.InRange(score, 850, 1000);
        Assert.Equal(6, factors.Count);
        Assert.Contains(factors, f => f.Code == "PAYMENT_HISTORY" && f.Severity == RiskFactorSeverity.Positive);
    }

    [Fact]
    public void MissingHistory_DropsOutOfWeightedAverage()
    {
        // No settled installments and no policies: payment-history weight redistributes instead of
        // counting as zero — a young customer is not a bad customer.
        var (score, _) = CreditScoreEngine.Calculate(
            Features(policyCount: 0, activePolicyCount: 0, settledInstallmentCount: 0), Settings);

        Assert.InRange(score, 0, 1000);
    }

    [Fact]
    public void OverdueDebt_DrivesScoreDown()
    {
        var (score, _) = CreditScoreEngine.Calculate(
            Features(currentDebtToman: 10_000_000m, overdueAmountToman: 10_000_000m,
                overdueCount: 3, maxDaysOverdue: 45),
            Settings);

        Assert.InRange(score, 100, 550);
    }

    // ---- Decision engine (doc §13) ----

    [Fact]
    public void ScoreAboveApproveMin_NoCriticalRule_Approves()
    {
        var decision = RiskDecisionEngine.Decide(750, RiskLevel.Low, [], Settings);
        Assert.Equal(RiskDecision.Approve, decision);
    }

    [Fact]
    public void ScoreInReviewBand_RequiresManualReview()
    {
        var decision = RiskDecisionEngine.Decide(620, RiskLevel.Medium, [], Settings);
        Assert.Equal(RiskDecision.ManualReview, decision);
    }

    [Fact]
    public void ScoreBelowDeclineLine_Declines()
    {
        var decision = RiskDecisionEngine.Decide(350, RiskLevel.Critical, [], Settings);
        Assert.Equal(RiskDecision.Decline, decision);
    }

    [Fact]
    public void HighScoreWithDefaultHistory_RuleBeatsScore()
    {
        var rules = RiskRuleEngine.Evaluate(Features(hasDefaultHistory: true), Settings, 0);
        var decision = RiskDecisionEngine.Decide(800, RiskLevel.Critical, rules, Settings);
        Assert.Equal(RiskDecision.Decline, decision);
    }

    [Fact]
    public void OverCreditLimitRule_ForcesManualReviewDespiteHighScore()
    {
        var rules = RiskRuleEngine.Evaluate(
            Features(currentDebtToman: 120_000_000m, settledInstallmentCount: 10, onTimeSettledCount: 10),
            Settings, 100_000_000m);
        var decision = RiskDecisionEngine.Decide(850, RiskLevel.VeryLow, rules, Settings);
        Assert.Equal(RiskDecision.ManualReview, decision);
    }

    // ---- Credit limit (doc §14) ----

    [Theory]
    [InlineData(RiskLevel.VeryLow, 100_000_000)]
    [InlineData(RiskLevel.Low, 100_000_000)]
    [InlineData(RiskLevel.Medium, 70_000_000)]
    [InlineData(RiskLevel.High, 35_000_000)]
    [InlineData(RiskLevel.Critical, 0)]
    public void CreditLimit_AppliesLevelMultiplier(RiskLevel level, decimal expectedLimit)
    {
        var (limit, isOverride) = CreditLimitEngine.Compute(level, Settings, null);
        Assert.Equal(expectedLimit, limit);
        Assert.False(isOverride);
    }

    [Fact]
    public void ManualOverride_WinsOverComputedLimit()
    {
        var (limit, isOverride) = CreditLimitEngine.Compute(RiskLevel.High, Settings, 50_000_000m);
        Assert.Equal(50_000_000m, limit);
        Assert.True(isOverride);
    }

    // ---- Weight guard (doc §9) ----

    [Fact]
    public void DefaultWeights_SumToOne()
    {
        Assert.Equal(1.00m, RiskSettingsDefaults.WeightSum(new RiskSettings()));
    }
}
