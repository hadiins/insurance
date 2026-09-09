using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.ApiIr;
using Aqsat.Application.Schedule;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Portal;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aqsat.UnitTests.Verification;

/// <summary>
/// The issuance-verification chain (owner decision 2026-09-01): fee payment on the portal → both
/// api.ir credit inquiries → agency decision → customer contract approval → down payment, with the
/// hard server-side block on receive-down-payment until the chain completes. The api.ir client is
/// stubbed so no test ever depends on the network — the client's own routing/success rules are
/// covered separately in ApiIrClientTests (the established pattern for api.ir-backed actions).
/// </summary>
[Collection("WebApplicationFactory")]
public class PolicyVerificationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const decimal Fee = 40_000m;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubApiIrClient _apiIr = new();

    public PolicyVerificationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApiIrClient>();
                services.AddSingleton<IApiIrClient>(_apiIr);
            });
        });
    }

    private sealed record Harness(
        HttpClient Client, DevSeeder.SeededAuthFixture Fixture,
        Guid PolicyId, Guid CustomerId, string PolicyNumber);

    /// <summary>Seeds an installment policy (RequiresVerification on), schedules it through the real
    /// endpoint, and logs the fixture's dual-agency manager in as agency A's operator.</summary>
    private async Task<Harness> CreateScheduledInstallmentPolicyAsync(
        decimal downPayment = 2_000_000m, string? mobile = "09125556677", string? nationalId = "0072345453")
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var thirdPartyLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        AgencyContext.Current = fixture.AgencyAId;
        var customer = new Customer
        {
            AgencyId = fixture.AgencyAId,
            ExternalCode = $"EXT-{Guid.NewGuid():N}"[..20],
            FullName = "مشتری اعتبارسنجی",
            Mobile = mobile,
            NationalId = nationalId,
        };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = $"55د{Random.Shared.Next(100, 999)}" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "صدور دستی",
            IsInstallment = true,
            RequiresVerification = true,
            IssueDate = new DateOnly(2026, 3, 1),
            StartDate = new DateOnly(2026, 3, 1),
            EndDate = new DateOnly(2027, 3, 1),
            NetPremium = 10_000_000m,
            ServiceFee = 2_000_000m,
            DownPayment = 0,
            InstallmentCount = 0,
        };
        seedContext.Policies.Add(policy);
        seedContext.OrgSettings.Add(new OrgSettings
        {
            OrganizationId = fixture.AgencyAId,
            CustomerPortalEnabled = true,
            PortalInvitationTtlHours = 48,
        });
        await seedContext.SaveChangesAsync();

        // The inquiry fee is the owner's concern (PlatformPaymentSettings): Mock + enabled is the
        // normal dev/panel setup, and the agency's own gateway defaults to Mock as well.
        var platformPayment = await seedContext.PlatformPaymentSettings.FirstOrDefaultAsync();
        if (platformPayment is null)
        {
            platformPayment = new PlatformPaymentSettings();
            seedContext.PlatformPaymentSettings.Add(platformPayment);
        }
        platformPayment.InquiryFeeToman = Fee;
        platformPayment.Provider = PaymentProvider.Mock;
        platformPayment.Enabled = true;
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var schedule = await client.PostAsJsonAsync(
            $"/api/policies/{policy.Id}/schedule", new ScheduleRequest(downPayment, 4));
        schedule.EnsureSuccessStatusCode();

        return new Harness(client, fixture, policy.Id, customer.Id, policy.PolicyNumber);
    }

    private async Task<PolicyVerificationDto> StartChainAsync(Harness h)
    {
        var created = await h.Client.PostAsync($"/api/policies/{h.PolicyId}/verification", null);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<PolicyVerificationDto>())!;
    }

    /// <summary>Pays the inquiry fee on the public portal — the same hop a customer takes — which
    /// fires both credit inquiries through the stubbed client and lands the stage at ReportReady.</summary>
    private async Task PayFeeOnPortalAsync(string token)
    {
        var publicClient = _factory.CreateClient();
        var pay = await publicClient.PostAsync($"/api/portal/{token}/pay", null);
        pay.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_cash_policy_skips_verification_entirely_and_settles_with_one_full_payment()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();

        // Re-issue through the real Create endpoint with PaymentType=cash — the wizard's step-3
        // choice — and prove the two flags land as cash: no chain, no hard block.
        var created = await h.Client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16],
            await GetThirdPartyLineIdAsync(),
            h.CustomerId, null, null, null,
            new VehicleInput("66س666", null, null, null, null, null),
            null,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), new DateOnly(2027, 3, 1),
            10_000_000m, 2_000_000m, null, null, false, PaymentType: "cash"));
        created.EnsureSuccessStatusCode();
        var cashPolicy = (await created.Content.ReadFromJsonAsync<CreatePolicyResultDto>())!;

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = h.Fixture.AgencyAId;
        var row = await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == cashPolicy.PolicyId);
        Assert.False(row.IsInstallment);
        Assert.False(row.RequiresVerification);

        // The wizard's explicit "installment" is what arms the hard block — a caller that omits
        // paymentType keeps the legacy behavior (installment, no chain), which is why the import
        // pipeline and every pre-choice caller stay untouched.
        var installment = await h.Client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16],
            await GetThirdPartyLineIdAsync(),
            h.CustomerId, null, null, null,
            new VehicleInput("77ه777", null, null, null, null, null),
            null,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), new DateOnly(2027, 3, 1),
            10_000_000m, 2_000_000m, null, null, false, PaymentType: "installment"));
        installment.EnsureSuccessStatusCode();
        var installmentPolicy = (await installment.Content.ReadFromJsonAsync<CreatePolicyResultDto>())!;
        AgencyContext.Current = h.Fixture.AgencyAId;
        var installmentRow = await verify.Policies.AsNoTracking()
            .SingleAsync(p => p.Id == installmentPolicy.PolicyId);
        Assert.True(installmentRow.IsInstallment);
        Assert.True(installmentRow.RequiresVerification);

        // No chain exists — the operator's verification view says so without erroring.
        var status = await h.Client.GetFromJsonAsync<PolicyVerificationDto>(
            $"/api/policies/{cashPolicy.PolicyId}/verification");
        Assert.Null(status!.InvitationId);

        var paid = await h.Client.PostAsJsonAsync(
            $"/api/policies/{cashPolicy.PolicyId}/record-full-payment",
            new RecordFullPaymentRequest(12_000_000m, new DateOnly(2026, 3, 2), "نقدی", null));
        paid.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_installment_policy_without_a_completed_chain_is_hard_blocked_from_its_down_payment()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();

        // No verification chain at all.
        var blocked = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/receive-down-payment",
            new ReceiveDownPaymentRequest(new DateOnly(2026, 3, 2), null));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("اعتبارسنجی", await blocked.Content.ReadAsStringAsync());

        // And a chain that is only partway — fee paid, report ready, agency approved — still blocks:
        // only the CUSTOMER's contract approval (or completion) opens the gate.
        var dto = await StartChainAsync(h);
        await PayFeeOnPortalAsync(dto.Token!);
        await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/verification/decision", new AgencyVerificationDecisionRequest(true));

        var stillBlocked = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/receive-down-payment",
            new ReceiveDownPaymentRequest(new DateOnly(2026, 3, 2), null));
        Assert.Equal(HttpStatusCode.BadRequest, stillBlocked.StatusCode);
    }

    [Fact]
    public async Task The_full_chain_happy_path_lands_a_down_payment_payment_and_completes()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();

        // 1. The operator starts the chain: a portal link is created and the fee/down-payment are
        //    snapshotted from the policy's schedule.
        var dto = await StartChainAsync(h);
        Assert.Equal("FeePending", dto.Stage);
        Assert.Equal(Fee, dto.InquiryFeeToman);
        Assert.Equal(2_000_000m, dto.DownPaymentAmountToman);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", dto.Token!);

        // 2. Before approval the public link shows the stage but no contract, no schedule, no number.
        var publicClient = _factory.CreateClient();
        var early = await publicClient.GetFromJsonAsync<PublicPortalInfoDto>($"/api/portal/{dto.Token}");
        Assert.Equal("FeePending", early!.Stage);
        Assert.Null(early.ContractText);
        Assert.Null(early.Installments);
        Assert.Null(early.PolicyNumber);

        // 3. The customer pays the fee — the inquiries fire automatically and the report lands.
        await PayFeeOnPortalAsync(dto.Token!);

        var status = await h.Client.GetFromJsonAsync<PolicyVerificationDto>(
            $"/api/policies/{h.PolicyId}/verification");
        Assert.Equal("ReportReady", status!.Stage);
        var report = status.CreditReport!;
        Assert.NotNull(report);
        Assert.Equal(2, report.ChequeCount);
        Assert.Equal(10_000m, report.ChequeSumAmountToman); // 100,000 rial → toman (rule 19)
        Assert.Equal(4_000m, report.ChequeSumBouncedAmountToman);
        Assert.Equal(1, report.ActiveLoansCount);
        Assert.Equal(2_000m, report.LoanTotalAmountToman);
        Assert.Equal(500m, report.LoanDebtTotalAmountToman);
        Assert.True(report.RawSuccess);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = h.Fixture.AgencyAId;
        var stored = await verify.CreditReports.AsNoTracking().SingleAsync(r => r.PolicyId == h.PolicyId);
        Assert.Equal(h.CustomerId, stored.CustomerId);
        Assert.Equal(h.Fixture.AgencyAId, stored.AgencyId);

        // 4. The agency approves — now the contract, schedule and amounts become visible to the
        //    customer, with the agency-editable text overriding the default.
        var approved = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/verification/decision", new AgencyVerificationDecisionRequest(true));
        approved.EnsureSuccessStatusCode();
        Assert.Equal("AgencyApproved",
            (await approved.Content.ReadFromJsonAsync<PolicyVerificationDto>())!.Stage);

        var agencyView = await publicClient.GetFromJsonAsync<PublicPortalInfoDto>($"/api/portal/{dto.Token}");
        Assert.Equal(h.PolicyNumber, agencyView!.PolicyNumber);
        Assert.Equal(2_000_000m, agencyView.DownPaymentAmountToman);
        Assert.NotNull(agencyView.ContractText);
        Assert.Contains("اقساط", agencyView.ContractText); // the system default text
        Assert.Equal(4, agencyView.Installments!.Count);
        Assert.All(agencyView.Installments, i => Assert.Equal(2_500_000m, i.Amount));
        // The public body never carries the customer's national ID (rules 12/17 in spirit — the
        // anonymous page leaks nothing it doesn't strictly need).
        var body = await publicClient.GetStringAsync($"/api/portal/{dto.Token}");
        Assert.DoesNotContain("0072345453", body);

        // 5. The agency's own edited contract text replaces the default on the portal.
        await using (var editContext = TestDbContextFactory.Create())
        {
            AgencyContext.Current = h.Fixture.AgencyAId;
            var settings = await editContext.OrgSettings.SingleAsync(s => s.OrganizationId == h.Fixture.AgencyAId);
            settings.InstallmentContractText = "متن قرارداد اختصاصی نمایندگی";
            await editContext.SaveChangesAsync();
        }
        var editedView = await publicClient.GetFromJsonAsync<PublicPortalInfoDto>($"/api/portal/{dto.Token}");
        Assert.Equal("متن قرارداد اختصاصی نمایندگی", editedView!.ContractText);

        // 6. The customer accepts the contract, then pays the down payment online.
        var accept = await publicClient.PostAsync($"/api/portal/{dto.Token}/approve-contract", null);
        accept.EnsureSuccessStatusCode();
        Assert.Equal("CustomerApproved",
            (await accept.Content.ReadFromJsonAsync<PublicPortalStageResultDto>())!.Stage);

        var downPayment = await publicClient.PostAsync($"/api/portal/{dto.Token}/pay-down-payment", null);
        downPayment.EnsureSuccessStatusCode();
        var payResult = await downPayment.Content.ReadFromJsonAsync<PublicPortalPayResultDto>();
        Assert.Equal(2_000_000m, payResult!.PaidAmountToman);

        // 7. The same settled Payment a manual receive-down-payment would have written (rule 24):
        //    one row, portal-flavored reference, and the chain is Completed.
        AgencyContext.Current = h.Fixture.AgencyAId;
        var payment = await verify.Payments.AsNoTracking().SingleAsync(p => p.InstallmentIdHint == h.PolicyId);
        Assert.Equal(2_000_000m, payment.Amount);
        Assert.Equal(WellKnownPaymentMethods.DownPayment, payment.Method);
        Assert.StartsWith("PORTAL-", payment.ReferenceNo);
        Assert.Equal(PaymentMethod.Online, payment.MethodType);

        var final = await h.Client.GetFromJsonAsync<PolicyVerificationDto>(
            $"/api/policies/{h.PolicyId}/verification");
        Assert.Equal("Completed", final!.Stage);
        Assert.NotNull(final.DownPaymentPaidAtUtc);

        // 8. A manual re-receipt of the same down payment on the same day is idempotent — no second row.
        var manual = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/receive-down-payment",
            new ReceiveDownPaymentRequest(DateOnly.FromDateTime(DateTime.Now), null));
        manual.EnsureSuccessStatusCode();
        AgencyContext.Current = h.Fixture.AgencyAId;
        Assert.Equal(1, await verify.Payments.AsNoTracking().CountAsync(p => p.InstallmentIdHint == h.PolicyId));
    }

    [Fact]
    public async Task Rejection_is_terminal_cancels_the_policy_and_refuses_every_later_step()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();
        var dto = await StartChainAsync(h);
        await PayFeeOnPortalAsync(dto.Token!);

        var rejected = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/verification/decision", new AgencyVerificationDecisionRequest(false));
        rejected.EnsureSuccessStatusCode();
        Assert.Equal("Rejected", (await rejected.Content.ReadFromJsonAsync<PolicyVerificationDto>())!.Stage);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = h.Fixture.AgencyAId;
        var policy = await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == h.PolicyId);
        Assert.Equal(PolicyStatus.Cancelled, policy.Status);

        // The customer cannot approve a rejected chain, and the operator cannot decide twice.
        var publicClient = _factory.CreateClient();
        var approve = await publicClient.PostAsync($"/api/portal/{dto.Token}/approve-contract", null);
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
        Assert.Contains("رد", await approve.Content.ReadAsStringAsync());

        var decideAgain = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/verification/decision", new AgencyVerificationDecisionRequest(true));
        Assert.Equal(HttpStatusCode.BadRequest, decideAgain.StatusCode);

        // And the down payment stays hard-blocked for a cancelled chain.
        var blocked = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/receive-down-payment",
            new ReceiveDownPaymentRequest(new DateOnly(2026, 3, 2), null));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    }

    [Fact]
    public async Task A_rejected_inquiry_run_keeps_the_stage_at_FeePaid_and_the_retry_endpoint_recovers_it()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();
        _apiIr.RejectCalls = true;

        var dto = await StartChainAsync(h);

        // The fee is charged and the pay call still succeeds — an inquiry failure must not look like
        // a payment failure to the customer; the stage records what actually happened.
        await PayFeeOnPortalAsync(dto.Token!);

        var status = await h.Client.GetFromJsonAsync<PolicyVerificationDto>(
            $"/api/policies/{h.PolicyId}/verification");
        Assert.Equal("FeePaid", status!.Stage);
        Assert.Null(status.CreditReport);
        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = h.Fixture.AgencyAId;
        Assert.False(await verify.CreditReports.AsNoTracking().AnyAsync(r => r.PolicyId == h.PolicyId));

        // Once api.ir answers again, the retry re-fires both inquiries without a second charge.
        _apiIr.RejectCalls = false;
        var retry = await h.Client.PostAsync($"/api/policies/{h.PolicyId}/verification/retry-inquiries", null);
        retry.EnsureSuccessStatusCode();
        var retried = await retry.Content.ReadFromJsonAsync<PolicyVerificationDto>();
        Assert.Equal("ReportReady", retried!.Stage);
        Assert.NotNull(retried.CreditReport);
    }

    [Fact]
    public async Task The_public_portal_only_knows_a_token_it_can_resolve()
    {
        var publicClient = _factory.CreateClient();

        var response = await publicClient.GetAsync("/api/portal/definitely-not-a-real-token-value-xxx");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("لینک", await response.Content.ReadAsStringAsync());
    }

    /// <summary>Seeds a credit report directly — the state a standalone (customer-file portal
    /// link) inquiry leaves behind when PolicyId is null, or a previous policy's report when the
    /// id is passed. Both are reuse candidates (owner decision 2026-09-03).</summary>
    private static async Task SeedReportAsync(
        Guid agencyId, Guid customerId, Guid? policyId, bool rawSuccess, DateTimeOffset retrievedAtUtc,
        int chequeCount = 5)
    {
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = agencyId;
        context.CreditReports.Add(new CreditReport
        {
            AgencyId = agencyId,
            CustomerId = customerId,
            PolicyId = policyId,
            ChequeCount = chequeCount,
            ChequeSumAmountToman = 3_000_000m,
            RawSuccess = rawSuccess,
            RetrievedAtUtc = retrievedAtUtc,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_fresh_standalone_report_is_reused_by_the_wizard_without_fee_or_new_inquiry()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();
        await SeedReportAsync(h.Fixture.AgencyAId, h.CustomerId, policyId: null, rawSuccess: true,
            DateTimeOffset.UtcNow.AddHours(-2));

        var dto = await StartChainAsync(h);
        Assert.Equal("ReportReady", dto.Stage);
        Assert.Equal(0m, dto.InquiryFeeToman);

        // No api.ir call and no report of this policy's own — the seeded row was copied onto it.
        Assert.Equal(0, _apiIr.CreditCalls);
        var status = await h.Client.GetFromJsonAsync<PolicyVerificationDto>(
            $"/api/policies/{h.PolicyId}/verification");
        Assert.NotNull(status!.CreditReport);
        Assert.Equal(5, status.CreditReport.ChequeCount);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = h.Fixture.AgencyAId;
        var copied = await verify.CreditReports.AsNoTracking().SingleAsync(r => r.PolicyId == h.PolicyId);
        Assert.Equal(h.CustomerId, copied.CustomerId);
        // The copy keeps the original retrieval time — an honest snapshot, not a fake "new" one.
        Assert.True(copied.RetrievedAtUtc <= DateTimeOffset.UtcNow.AddHours(-1));
        var standalone = await verify.CreditReports.AsNoTracking().SingleAsync(r => r.PolicyId == null && r.CustomerId == h.CustomerId);
        Assert.NotNull(standalone);
    }

    [Fact]
    public async Task A_reused_chain_still_gates_the_down_payment_and_completes_end_to_end()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();
        await SeedReportAsync(h.Fixture.AgencyAId, h.CustomerId, policyId: null, rawSuccess: true,
            DateTimeOffset.UtcNow.AddHours(-1));

        var dto = await StartChainAsync(h);
        Assert.Equal("ReportReady", dto.Stage);

        var blocked = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/receive-down-payment",
            new ReceiveDownPaymentRequest(new DateOnly(2026, 3, 2), null));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var approved = await h.Client.PostAsJsonAsync(
            $"/api/policies/{h.PolicyId}/verification/decision", new AgencyVerificationDecisionRequest(true));
        approved.EnsureSuccessStatusCode();

        var publicClient = _factory.CreateClient();
        var accept = await publicClient.PostAsync($"/api/portal/{dto.Token}/approve-contract", null);
        accept.EnsureSuccessStatusCode();

        var downPayment = await publicClient.PostAsync($"/api/portal/{dto.Token}/pay-down-payment", null);
        downPayment.EnsureSuccessStatusCode();

        var final = await h.Client.GetFromJsonAsync<PolicyVerificationDto>(
            $"/api/policies/{h.PolicyId}/verification");
        Assert.Equal("Completed", final!.Stage);
    }

    [Fact]
    public async Task A_report_older_than_30_days_is_not_reused()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();
        await SeedReportAsync(h.Fixture.AgencyAId, h.CustomerId, policyId: null, rawSuccess: true,
            DateTimeOffset.UtcNow - PolicyVerificationService.ReportReuseWindow - TimeSpan.FromDays(1));

        var dto = await StartChainAsync(h);
        Assert.Equal("FeePending", dto.Stage);
        Assert.Equal(Fee, dto.InquiryFeeToman);
    }

    [Fact]
    public async Task A_sandboxed_report_is_never_reused()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();
        await SeedReportAsync(h.Fixture.AgencyAId, h.CustomerId, policyId: null, rawSuccess: false,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        var dto = await StartChainAsync(h);
        Assert.Equal("FeePending", dto.Stage);
    }

    [Fact]
    public async Task A_fresh_report_from_a_previous_policy_is_also_reused()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();

        // A real prior policy row — CreditReports.PolicyId is a hard FK (rule 4).
        Guid previousPolicyId;
        await using (var seed = TestDbContextFactory.Create())
        {
            AgencyContext.Current = h.Fixture.AgencyAId;
            var thirdPartyLineId = await seed.InsuranceLines
                .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();
            var previous = new Policy
            {
                AgencyId = h.Fixture.AgencyAId,
                PolicyNumber = $"POL-{Guid.NewGuid():N}"[..16],
                InsuranceLineId = thirdPartyLineId,
                CustomerId = h.CustomerId,
                ContractName = "صدور دستی",
                IsInstallment = true,
                RequiresVerification = true,
                IssueDate = new DateOnly(2026, 2, 1),
                StartDate = new DateOnly(2026, 2, 1),
                EndDate = new DateOnly(2027, 2, 1),
                NetPremium = 10_000_000m,
                ServiceFee = 1_000_000m,
                DownPayment = 0,
                InstallmentCount = 0,
            };
            seed.Policies.Add(previous);
            await seed.SaveChangesAsync();
            previousPolicyId = previous.Id;
        }
        await SeedReportAsync(h.Fixture.AgencyAId, h.CustomerId, policyId: previousPolicyId, rawSuccess: true,
            DateTimeOffset.UtcNow.AddDays(-3));

        var dto = await StartChainAsync(h);
        Assert.Equal("ReportReady", dto.Stage);
        Assert.Equal(0m, dto.InquiryFeeToman);
        Assert.Equal(0, _apiIr.CreditCalls);
    }

    [Fact]
    public async Task Another_agencys_operator_sees_neither_the_chain_nor_the_credit_report()
    {
        var h = await CreateScheduledInstallmentPolicyAsync();
        var dto = await StartChainAsync(h);
        await PayFeeOnPortalAsync(dto.Token!);

        // The fixture's dual-agency user acting in agency B (Staff there) — RLS must hide agency A's
        // invitation and report, and a chain start for A's policy must fail scope resolution, not
        // silently succeed.
        var clientB = _factory.CreateClient();
        var loginB = await clientB.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(h.Fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", h.Fixture.AgencyBId.ToString());

        var status = await clientB.GetFromJsonAsync<PolicyVerificationDto>(
            $"/api/policies/{h.PolicyId}/verification");
        Assert.Null(status!.InvitationId);
        Assert.Null(status.CreditReport);

        var create = await clientB.PostAsync($"/api/policies/{h.PolicyId}/verification", null);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Contains("بیمه‌نامه", await create.Content.ReadAsStringAsync());
    }

    private async Task<Guid> GetThirdPartyLineIdAsync()
    {
        await using var context = TestDbContextFactory.Create();
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        return await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();
    }

    /// <summary>
    /// Deterministic, offline api.ir answers for the two credit services; the other members answer
    /// "no real answer" so nothing in the pipeline mistakes the stub for a live service.
    /// </summary>
    private sealed class StubApiIrClient : IApiIrClient
    {
        // Rial amounts (rule 19) — the service converts to toman on storage.
        public UnpaidChequeResult? UnpaidCheque { get; set; } = new(2, 100_000m, 40_000m);
        public ActiveLoansResult? ActiveLoans { get; set; } = new(1, 20_000m, 5_000m, null, null, null, null);
        public bool RejectCalls { get; set; }

        /// <summary>Counts the two credit inquiries — the reuse tests assert a reused report fires none.</summary>
        public int CreditCalls { get; private set; }

        public Task<UnpaidChequeResult?> UnpaidChequeAsync(string nationalCode, Guid agencyId, CancellationToken ct = default)
        {
            CreditCalls++;
            return Task.FromResult(RejectCalls ? null : UnpaidCheque);
        }

        public Task<ActiveLoansResult?> ActiveLoansAsync(string nationalCode, Guid agencyId, CancellationToken ct = default)
        {
            CreditCalls++;
            return Task.FromResult(RejectCalls ? null : ActiveLoans);
        }

        public Task<bool?> IsHolidayAsync(DateOnly date, Guid agencyId, CancellationToken ct = default) =>
            Task.FromResult<bool?>(null);

        public Task<ShahkarResult?> ShahkarLiteAsync(string nationalId, string mobile, Guid agencyId, CancellationToken ct = default) =>
            Task.FromResult<ShahkarResult?>(null);

        public Task<string?> ChequeColorAsync(string sayadId, Guid agencyId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<bool> SendSmsAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> SmsOtpAsync(string mobile, Guid agencyId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> CallOtpAsync(string mobile, Guid agencyId, CancellationToken ct = default) =>
            Task.FromResult(true);
    }
}
