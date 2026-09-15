using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Payments;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure;
using Aqsat.Infrastructure.Payments;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Portal;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aqsat.UnitTests.Portal;

/// <summary>
/// The installment-payment link (/pay/{token}): the reminder job mints it (covered in
/// SmsReminderJobTests), the public page lists the customer's open installments, and paying one
/// through the agency's Mock gateway must land in EXACTLY the state an agent-recorded receipt
/// produces — the parity test below locks the two paths together (payment + allocation +
/// installment settlement + commission flip). Every failure answers with a Persian reason, never
/// a silent empty result.
/// </summary>
[Collection("WebApplicationFactory")]
public class InstallmentPaymentLinkEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InstallmentPaymentLinkEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private async Task<(HttpClient Client, DevSeeder.SeededAuthFixture Fixture)> LoginManagerAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return (client, fixture);
    }

    private static async Task<Guid> SeedInstallmentPolicyAsync(
        AppDbContext context, Guid agencyId, string customerName, string mobile,
        int installmentCount = 2, decimal installmentAmount = 1_000_000m,
        DateOnly? firstDueDate = null, bool withCommissionSlices = true)
    {
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        var thirdPartyLineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var customer = new Customer
        {
            AgencyId = agencyId,
            ExternalCode = $"EXT-{Guid.NewGuid():N}"[..20],
            FullName = customerName,
            Mobile = mobile,
            NationalId = $"00{Guid.NewGuid():N}"[..10],
        };
        var vehicle = new Vehicle { AgencyId = agencyId, Plate = $"55{Guid.NewGuid():N}"[..2] } ;
        context.Customers.Add(customer);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = agencyId,
            PolicyNumber = $"POL-PAY-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
            NetPremium = installmentAmount * installmentCount,
            DownPayment = 0,
            InstallmentCount = installmentCount,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        var due = firstDueDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        for (var seq = 1; seq <= installmentCount; seq++)
        {
            var installment = new Installment
            {
                // Client-generated so the commission slice below can reference it in the same
                // SaveChanges — EF cannot fix up a scalar FK without a navigation.
                Id = SequentialGuidGenerator.Next(),
                AgencyId = agencyId,
                PolicyId = policy.Id,
                SeqNo = seq,
                DueDate = due.AddMonths(seq - 1),
                SettlementDeadline = due.AddMonths(seq - 1).AddDays(3),
                Amount = installmentAmount,
                Status = InstallmentStatus.Unpaid,
            };
            context.Installments.Add(installment);

            if (withCommissionSlices)
            {
                context.AgencyCommissionEntries.Add(new AgencyCommissionEntry
                {
                    AgencyId = agencyId,
                    PolicyId = policy.Id,
                    InstallmentId = installment.Id,
                    BasePortion = installmentAmount,
                    RatePercent = 10m,
                    Amount = installmentAmount * 0.1m,
                    Status = CommissionStatus.Pending,
                });
            }
        }
        await context.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task<(Guid LinkId, string Token)> CreateLinkDirectAsync(
        Guid agencyId, Guid customerId, TimeSpan ttl)
    {
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = agencyId;
        var link = new CustomerPaymentLink
        {
            AgencyId = agencyId,
            CustomerId = customerId,
            Token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ttl),
            LastSentAtUtc = DateTimeOffset.UtcNow,
        };
        context.CustomerPaymentLinks.Add(link);
        context.PaymentLinkTokenIndex.Add(new PaymentLinkTokenIndex
        {
            AgencyId = agencyId,
            Token = link.Token,
            LinkId = link.Id,
        });
        await context.SaveChangesAsync();
        return (link.Id, link.Token);
    }

    [Fact]
    public async Task The_public_page_lists_open_installments_across_policies_and_marks_overdue()
    {
        var (_, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری لینک پرداخت", "09123334455");
        // A second policy for the same customer — the page is customer-wide, not policy-wide.
        var secondPolicy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-2ND-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = await context.InsuranceLines.Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync(),
            CustomerId = customerId,
            ContractName = "بیمهٔ عادی",
            IsInstallment = true,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
            NetPremium = 500_000m,
            DownPayment = 0,
            InstallmentCount = 1,
        };
        context.Policies.Add(secondPolicy);
        await context.SaveChangesAsync();
        context.Installments.Add(new Installment
        {
            AgencyId = fixture.AgencyAId,
            PolicyId = secondPolicy.Id,
            SeqNo = 1,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5),
            SettlementDeadline = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2),
            Amount = 500_000m,
            Status = InstallmentStatus.Unpaid,
        });
        // A settled one on the first policy — must NOT appear.
        var settled = await context.Installments
            .Where(i => i.Policy.CustomerId == customerId).OrderBy(i => i.SeqNo).FirstAsync();
        settled.PaidAmount = settled.Amount;
        settled.Status = InstallmentStatus.Settled;
        await context.SaveChangesAsync();

        var (_, token) = await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromDays(30));

        var publicClient = _factory.CreateClient();
        var info = await publicClient.GetFromJsonAsync<PublicPaymentLinkInfoDto>($"/api/portal/pay/{token}");

        Assert.Equal("مشتری لینک پرداخت", info!.CustomerDisplayName);
        Assert.Equal(2, info.Installments.Count);
        Assert.All(info.Installments, i => Assert.True(i.BalanceToman > 0));
        var overdue = info.Installments.Single(i => i.IsOverdue);
        Assert.Equal(500_000m, overdue.BalanceToman);
        Assert.Equal(1, info.Installments.Count(i => !i.IsOverdue));

        // The public page never echoes the mobile or national ID.
        var body = await publicClient.GetStringAsync($"/api/portal/pay/{token}");
        Assert.DoesNotContain("09123334455", body);
    }

    [Fact]
    public async Task Paying_through_the_link_lands_in_the_same_state_as_an_agent_recorded_receipt()
    {
        var (client, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        var onlineCustomerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری آنلاین", "09123334456");
        var manualCustomerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری حضوری", "09123334457");

        var onlineInstallment = await context.Installments.AsNoTracking()
            .Where(i => i.Policy.CustomerId == onlineCustomerId).OrderBy(i => i.SeqNo).FirstAsync();
        var manualInstallment = await context.Installments.AsNoTracking()
            .Where(i => i.Policy.CustomerId == manualCustomerId).OrderBy(i => i.SeqNo).FirstAsync();

        var (_, token) = await CreateLinkDirectAsync(fixture.AgencyAId, onlineCustomerId, TimeSpan.FromDays(30));

        // ONLINE — the customer pays the full balance through the public link (Mock gateway).
        var publicClient = _factory.CreateClient();
        var pay = await publicClient.PostAsync($"/api/portal/pay/{token}/installments/{onlineInstallment.Id}", null);
        pay.EnsureSuccessStatusCode();
        var onlineResult = await pay.Content.ReadFromJsonAsync<PublicPaymentLinkPayResultDto>();
        Assert.Equal(1_000_000m, onlineResult!.PaidAmountToman);

        // MANUAL — the agent records the identical receipt for the other customer.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var record = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            manualInstallment.Id, 1_000_000m, today, "نقدی", null));
        record.EnsureSuccessStatusCode();

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;

        foreach (var (customerId, source) in new[] { (onlineCustomerId, "PORTAL"), (manualCustomerId, "AGENT") })
        {
            var installment = await verify.Installments.AsNoTracking()
                .Where(i => i.Policy.CustomerId == customerId).OrderBy(i => i.SeqNo).FirstAsync();
            Assert.Equal(1_000_000m, installment.PaidAmount, 0); // parity: same settlement state
            Assert.Equal(InstallmentStatus.Settled, installment.Status);

            var payment = await verify.Payments.AsNoTracking()
                .Include(p => p.Allocations.Where(a => !a.IsDeleted))
                .SingleAsync(p => p.InstallmentIdHint == installment.Id);
            Assert.Equal(1_000_000m, payment.Amount);
            var allocation = Assert.Single(payment.Allocations);
            Assert.Equal(installment.Id, allocation.InstallmentId);
            Assert.Equal(1_000_000m, allocation.Amount);

            // The commission slice flipped Payable on full settlement — both paths, identically.
            var commission = await verify.AgencyCommissionEntries.AsNoTracking()
                .SingleAsync(c => c.InstallmentId == installment.Id);
            Assert.Equal(CommissionStatus.Payable, commission.Status);
            Assert.NotNull(commission.EligibleAt);

            // The second installment's slice is still Pending in both.
            var secondSlice = await verify.AgencyCommissionEntries.AsNoTracking()
                .Where(c => c.PolicyId == installment.PolicyId && c.InstallmentId != installment.Id).FirstAsync();
            Assert.Equal(CommissionStatus.Pending, secondSlice.Status);
        }

        // The online payment's own identity: portal method, PORTAL- reference, audit row in the
        // same transaction carrying the installment's policy (rule 28) and the "پورتال مشتری" actor.
        var onlinePayment = await verify.Payments.AsNoTracking()
            .SingleAsync(p => p.InstallmentIdHint == onlineInstallment.Id);
        Assert.Equal(PaymentMethod.Online, onlinePayment.MethodType);
        Assert.Equal(WellKnownPaymentMethods.OnlineInstallment, onlinePayment.Method);
        Assert.StartsWith("PORTAL-", onlinePayment.ReferenceNo);

        var audit = await verify.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.EntityType == nameof(Payment) && a.EntityId == onlinePayment.Id);
        Assert.Equal(onlineInstallment.PolicyId, audit.PolicyId);
        Assert.Equal("پورتال مشتری", audit.UserDisplayName);
        Assert.Contains("لینک پیامکی", audit.Description);
    }

    [Fact]
    public async Task A_duplicate_pay_call_on_the_same_day_is_idempotent()
    {
        var (_, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری تکراری", "09123334458");
        var installment = await context.Installments.AsNoTracking()
            .Where(i => i.Policy.CustomerId == customerId).OrderBy(i => i.SeqNo).FirstAsync();

        var (_, token) = await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromDays(30));
        var publicClient = _factory.CreateClient();

        var first = await publicClient.PostAsync($"/api/portal/pay/{token}/installments/{installment.Id}", null);
        first.EnsureSuccessStatusCode();
        var second = await publicClient.PostAsync($"/api/portal/pay/{token}/installments/{installment.Id}", null);
        second.EnsureSuccessStatusCode(); // rule 24 — the duplicate is a success, not an error

        var secondResult = await second.Content.ReadFromJsonAsync<PublicPaymentLinkPayResultDto>();
        Assert.Equal(1_000_000m, secondResult!.PaidAmountToman);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        Assert.Equal(1, await verify.Payments.AsNoTracking().CountAsync(p => p.InstallmentIdHint == installment.Id));
        Assert.Equal(1, await verify.PaymentAllocations.AsNoTracking().CountAsync(a => a.InstallmentId == installment.Id));
    }

    [Fact]
    public async Task A_settled_installment_and_another_customers_installment_are_refused()
    {
        var (_, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری رد شده", "09123334459");
        var otherCustomerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری دیگر", "09123334460");

        var settled = await context.Installments
            .Where(i => i.Policy.CustomerId == customerId).OrderBy(i => i.SeqNo).FirstAsync();
        settled.PaidAmount = settled.Amount;
        settled.Status = InstallmentStatus.Settled;
        await context.SaveChangesAsync();
        var foreignInstallmentId = await context.Installments.AsNoTracking()
            .Where(i => i.Policy.CustomerId == otherCustomerId).OrderBy(i => i.SeqNo)
            .Select(i => i.Id).FirstAsync();

        var (_, token) = await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromDays(30));
        var publicClient = _factory.CreateClient();

        var settledResponse = await publicClient.PostAsync($"/api/portal/pay/{token}/installments/{settled.Id}", null);
        Assert.Equal(HttpStatusCode.BadRequest, settledResponse.StatusCode);
        Assert.Contains("تسویه", await settledResponse.Content.ReadAsStringAsync());

        // Rule 11 — an in-scope installment of a DIFFERENT customer must not be payable through
        // this customer's link.
        var foreignResponse = await publicClient.PostAsync($"/api/portal/pay/{token}/installments/{foreignInstallmentId}", null);
        Assert.Equal(HttpStatusCode.BadRequest, foreignResponse.StatusCode);
        Assert.Contains("یافت نشد", await foreignResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Expired_revoked_and_unknown_tokens_are_refused_with_a_persian_reason()
    {
        var (_, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری منقضی", "09123334461");

        var (_, expiredToken) = await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromHours(-1));
        var expired = await _factory.CreateClient().GetAsync($"/api/portal/pay/{expiredToken}");
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        Assert.Contains("اعتبار", await expired.Content.ReadAsStringAsync());

        var (_, revokedToken) = await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromDays(30));
        await using var revokeContext = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var link = await revokeContext.CustomerPaymentLinks.SingleAsync(l => l.Token == revokedToken);
        link.Status = PaymentLinkStatus.Revoked;
        await revokeContext.SaveChangesAsync();
        var revoked = await _factory.CreateClient().GetAsync($"/api/portal/pay/{revokedToken}");
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Contains("لغو", await revoked.Content.ReadAsStringAsync());

        var unknown = await _factory.CreateClient().GetAsync("/api/portal/pay/definitely-not-a-real-token-value-xxx");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("لینک", await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_operator_sees_the_link_status_and_can_revoke_it()
    {
        var (client, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری لغو", "09123334462");
        var (linkId, token) = await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromDays(30));

        var status = await client.GetFromJsonAsync<CustomerPaymentLinkDto>($"/api/portal/links/customer/{customerId}");
        Assert.True(status!.HasActiveLink);
        Assert.Equal(token, status.Token);
        Assert.NotNull(status.ExpiresAtUtc);

        var revoke = await client.PostAsync($"/api/portal/links/customer/{customerId}/revoke", null);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        Assert.Equal(PaymentLinkStatus.Revoked,
            (await verify.CustomerPaymentLinks.AsNoTracking().SingleAsync(l => l.Id == linkId)).Status);
        var audit = await verify.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.EntityType == nameof(CustomerPaymentLink) && a.EntityId == linkId);
        Assert.Equal(AuditAction.PaymentLinkRevoked, audit.Action);

        // The revoked token no longer resolves for the anonymous visitor.
        var dead = await _factory.CreateClient().GetAsync($"/api/portal/pay/{token}");
        Assert.Equal(HttpStatusCode.NotFound, dead.StatusCode);

        // Revoking again is a 400, not a silent success.
        var again = await client.PostAsync($"/api/portal/links/customer/{customerId}/revoke", null);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Another_agency_cannot_see_or_revoke_a_foreign_customers_link()
    {
        var (clientA, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری ایزوله", "09123334463");
        await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromDays(30));

        // Operator B — the fixture's dual-agency user acting in agency B (Staff role there).
        var clientB = _factory.CreateClient();
        var loginB = await clientB.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());

        var status = await clientB.GetAsync($"/api/portal/links/customer/{customerId}");
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);

        var revoke = await clientB.PostAsync($"/api/portal/links/customer/{customerId}/revoke", null);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_manage_links()
    {
        var anonymous = _factory.CreateClient();

        var status = await anonymous.GetAsync($"/api/portal/links/customer/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
    }

    [Fact]
    public async Task EnsureLinkAsyncs_token_index_row_points_at_the_real_stored_link_id()
    {
        // Regression: the RLS-exempt PaymentLinkTokenIndex row is added with LinkId = link.Id
        // BEFORE SaveChanges, while link.Id is store-generated (NEWSEQUENTIALID) — the FK can only
        // land correctly through EF's temp-value fixup. Asserted against the DATABASE's two
        // rows, not the in-memory object where a placeholder Guid would hide the bug.
        var (_, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری ایندکس", "09123334464");

        var service = new InstallmentPaymentLinkService(context, [new MockPaymentGateway(NullLogger<MockPaymentGateway>.Instance)]);
        var link = await service.EnsureLinkAsync(customerId, Guid.NewGuid());

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var storedLink = await verify.CustomerPaymentLinks.AsNoTracking().SingleAsync(l => l.Token == link.Token);
        var indexRow = await verify.PaymentLinkTokenIndex.AsNoTracking().SingleAsync(t => t.Token == link.Token);
        Assert.NotEqual(Guid.Empty, storedLink.Id);
        Assert.Equal(storedLink.Id, indexRow.LinkId);
    }

    [Fact]
    public async Task EnsureLinkAsync_on_an_existing_link_persists_the_expiry_refresh()
    {
        // Regression: re-using the active link must PERSIST the rolling expiry and LastSentAtUtc.
        // (The race-path twin of this code in the DbUpdateException catch used to re-fetch with
        // AsNoTracking — a mutation that SaveChanges silently ignores; both paths now mutate a
        // TRACKED entity, which this test proves end-to-end for the shared mechanics.)
        var (_, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری تمدید", "09123334466");
        var (_, token) = await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromDays(1));

        await using var before = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var beforeRow = await before.CustomerPaymentLinks.AsNoTracking().SingleAsync(l => l.Token == token);

        await Task.Delay(50);
        var service = new InstallmentPaymentLinkService(context, [new MockPaymentGateway(NullLogger<MockPaymentGateway>.Instance)]);
        var refreshed = await service.EnsureLinkAsync(customerId, Guid.NewGuid());

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var afterRow = await verify.CustomerPaymentLinks.AsNoTracking().SingleAsync(l => l.Token == token);
        Assert.Equal(refreshed.Id, afterRow.Id);
        Assert.True(afterRow.ExpiresAtUtc > beforeRow.ExpiresAtUtc,
            $"expiry must roll forward: {afterRow.ExpiresAtUtc} vs {beforeRow.ExpiresAtUtc}");
        Assert.True(afterRow.LastSentAtUtc > beforeRow.LastSentAtUtc,
            $"LastSentAtUtc must be re-stamped: {afterRow.LastSentAtUtc} vs {beforeRow.LastSentAtUtc}");
    }

    [Fact]
    public async Task A_gateway_reporting_a_nonpositive_amount_never_records_a_payment()
    {
        // Regression: a Succeeded=true result carrying PaidAmountToman <= 0 is a gateway protocol
        // violation, not an under-capture — it must be refused before any Payment/Allocation row
        // or installment balance math can see it.
        var (_, fixture) = await LoginManagerAsync();
        await using var context = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var customerId = await SeedInstallmentPolicyAsync(context, fixture.AgencyAId, "مشتری مبلغ صفر", "09123334465");
        var installmentId = await context.Installments.AsNoTracking()
            .Where(i => i.Policy.CustomerId == customerId).OrderBy(i => i.SeqNo)
            .Select(i => i.Id).FirstAsync();
        var (_, token) = await CreateLinkDirectAsync(fixture.AgencyAId, customerId, TimeSpan.FromDays(30));

        var service = new InstallmentPaymentLinkService(context, [new ZeroAmountGateway()]);
        var ex = await Assert.ThrowsAsync<PortalInvitationException>(
            () => service.PayInstallmentAsync(token, installmentId, "1.2.3.4"));
        Assert.Contains("نامعتبر", ex.Message);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        Assert.Equal(0, await verify.Payments.AsNoTracking().CountAsync(p => p.InstallmentIdHint == installmentId));
        Assert.Equal(0, await verify.PaymentAllocations.AsNoTracking().CountAsync(a => a.InstallmentId == installmentId));
        var installment = await verify.Installments.AsNoTracking().SingleAsync(i => i.Id == installmentId);
        Assert.Equal(InstallmentStatus.Unpaid, installment.Status);
        Assert.Equal(0m, installment.PaidAmount);
    }

    private sealed class ZeroAmountGateway : IPaymentGateway
    {
        public PaymentProvider Provider => PaymentProvider.Mock;

        public Task<GatewayPaymentResult> ChargeAsync(
            string paymentToken, string merchantId, decimal amountToman, string description,
            string callbackUrl, CancellationToken ct = default)
            => Task.FromResult(new GatewayPaymentResult(Succeeded: true, PaidAmountToman: 0m, FailureReason: null));
    }
}
