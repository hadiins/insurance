using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Portal;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Portal;

/// <summary>
/// The customer-portal invitation flow (docs/CUSTOMER-PORTAL-SPEC.md §3): issue a link (gated on
/// the agency's portal switch, one active link per customer), the public token lookup resolves the
/// agency through fn_PortalInvitationAgency and reads through RLS, and payment goes through the
/// Mock gateway behind the platform kill switch. Every failure answers with a Persian reason —
/// never a silent empty result.
/// </summary>
[Collection("WebApplicationFactory")]
public class PortalInvitationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PortalInvitationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private async Task<(HttpClient Client, DevSeeder.SeededAuthFixture Fixture, Guid CustomerId)> SetupAgencyCustomerAsync(
        bool portalEnabled = true, string? mobile = "09123334444")
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        Aqsat.Infrastructure.Persistence.AgencyContext.Current = fixture.AgencyAId;
        var customer = new Aqsat.Domain.Customer
        {
            AgencyId = fixture.AgencyAId,
            ExternalCode = $"EXT-{Guid.NewGuid():N}"[..20],
            FullName = "مشتری پورتال",
            Mobile = mobile,
        };
        seedContext.Customers.Add(customer);

        if (portalEnabled)
        {
            seedContext.OrgSettings.Add(new Aqsat.Domain.OrgSettings
            {
                OrganizationId = fixture.AgencyAId,
                CustomerPortalEnabled = true,
                PortalInvitationTtlHours = 48,
            });
        }

        // The inquiry fee is an owner-account concern — it lives on PlatformPaymentSettings, not
        // OrgSettings (owner decision 2026-09-01), so the fixture seeds it there.
        var platformPayment = await seedContext.PlatformPaymentSettings.FirstOrDefaultAsync();
        if (platformPayment is null)
        {
            platformPayment = new Aqsat.Domain.PlatformPaymentSettings();
            seedContext.PlatformPaymentSettings.Add(platformPayment);
        }
        platformPayment.InquiryFeeToman = 40_000m;
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (client, fixture, customer.Id);
    }

    private static async Task<Guid> CreateInvitationDirectAsync(
        Guid agencyId, Guid customerId, decimal fee, TimeSpan ttl)
    {
        await using var context = TestDbContextFactory.Create();
        Aqsat.Infrastructure.Persistence.AgencyContext.Current = agencyId;
        var invitation = new Aqsat.Domain.CustomerPortalInvitation
        {
            AgencyId = agencyId,
            CustomerId = customerId,
            Token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            InquiryFeeToman = fee,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ttl),
        };
        context.CustomerPortalInvitations.Add(invitation);
        // The service writes this in the same SaveChanges — direct creation must mirror it or the
        // anonymous token lookup cannot resolve the agency.
        context.PortalInvitationTokenIndex.Add(new Aqsat.Domain.PortalInvitationTokenIndex
        {
            AgencyId = agencyId,
            Token = invitation.Token,
            InvitationId = invitation.Id,
        });
        await context.SaveChangesAsync();
        return invitation.Id;
    }

    [Fact]
    public async Task Issuing_a_link_is_refused_while_the_agency_portal_is_disabled()
    {
        var (client, _, customerId) = await SetupAgencyCustomerAsync(portalEnabled: false);

        var response = await client.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(customerId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("پورتال مشتری", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Issuing_creates_a_pending_invitation_with_the_configured_fee_and_ttl()
    {
        var (client, _, customerId) = await SetupAgencyCustomerAsync();

        var response = await client.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(customerId));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<PortalInvitationDto>();

        Assert.Equal("Pending", dto!.Status);
        Assert.Equal(40_000m, dto.InquiryFeeToman);
        Assert.True(dto.ExpiresAtUtc > DateTimeOffset.UtcNow.AddHours(47));
        Assert.Matches("^[A-Za-z0-9_-]{43}$", dto.Token);
    }

    [Fact]
    public async Task A_second_active_link_for_the_same_customer_is_refused()
    {
        var (client, _, customerId) = await SetupAgencyCustomerAsync();

        var first = await client.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(customerId));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(customerId));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task The_public_lookup_returns_fee_and_status_and_never_the_customers_data()
    {
        var (client, fixture, customerId) = await SetupAgencyCustomerAsync();
        var created = await client.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(customerId));
        var dto = await created.Content.ReadFromJsonAsync<PortalInvitationDto>();

        // Anonymous — no token, no org header: the URL token is the only credential.
        var publicClient = _factory.CreateClient();
        var info = await publicClient.GetFromJsonAsync<PublicPortalInfoDto>($"/api/portal/{dto!.Token}");

        Assert.Equal(40_000m, info!.FeeToman);
        Assert.Equal("Pending", info.Status);
        var body = await publicClient.GetStringAsync($"/api/portal/{dto.Token}");
        Assert.DoesNotContain("09123334444", body);
        Assert.DoesNotContain(fixture.AgencyAId.ToString(), body);
    }

    [Fact]
    public async Task An_unknown_token_is_a_404_with_a_persian_reason()
    {
        var publicClient = _factory.CreateClient();

        var response = await publicClient.GetAsync("/api/portal/definitely-not-a-real-token-value-xxx");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("لینک", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Paying_marks_the_invitation_paid_and_a_second_attempt_is_refused()
    {
        var (client, fixture, customerId) = await SetupAgencyCustomerAsync();
        var created = await client.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(customerId));
        var dto = await created.Content.ReadFromJsonAsync<PortalInvitationDto>();

        // Platform kill switch ON with the Mock provider — the owner panel's normal dev setup.
        await using var platformContext = TestDbContextFactory.Create();
        var platformSettings = await platformContext.PlatformPaymentSettings.FirstOrDefaultAsync();
        if (platformSettings is null)
        {
            platformSettings = new Aqsat.Domain.PlatformPaymentSettings();
            platformContext.PlatformPaymentSettings.Add(platformSettings);
        }
        platformSettings.Provider = Aqsat.Domain.Enums.PaymentProvider.Mock;
        platformSettings.Enabled = true;
        await platformContext.SaveChangesAsync();

        var publicClient = _factory.CreateClient();
        var pay = await publicClient.PostAsync($"/api/portal/{dto!.Token}/pay", null);
        pay.EnsureSuccessStatusCode();
        var result = await pay.Content.ReadFromJsonAsync<PublicPortalPayResultDto>();
        Assert.Equal(40_000m, result!.PaidAmountToman);

        await using var verify = TestDbContextFactory.Create();
        Aqsat.Infrastructure.Persistence.AgencyContext.Current = fixture.AgencyAId;
        var row = await verify.CustomerPortalInvitations.AsNoTracking()
            .SingleAsync(i => i.Token == dto.Token);
        Assert.Equal(Aqsat.Domain.Enums.PortalInvitationStatus.Paid, row.Status);
        Assert.NotNull(row.PaidAtUtc);
        Assert.Equal(40_000m, row.PaidAmountToman);

        var second = await publicClient.PostAsync($"/api/portal/{dto.Token}/pay", null);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Paying_is_refused_while_the_platform_payment_switch_is_off()
    {
        var (client, _, customerId) = await SetupAgencyCustomerAsync();
        var created = await client.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(customerId));
        var dto = await created.Content.ReadFromJsonAsync<PortalInvitationDto>();

        await using var platformContext = TestDbContextFactory.Create();
        await platformContext.Database.ExecuteSqlRawAsync(
            "IF EXISTS (SELECT 1 FROM PlatformPaymentSettings) UPDATE PlatformPaymentSettings SET Enabled = 0");

        var publicClient = _factory.CreateClient();
        var pay = await publicClient.PostAsync($"/api/portal/{dto!.Token}/pay", null);

        Assert.Equal(HttpStatusCode.BadRequest, pay.StatusCode);
    }

    [Fact]
    public async Task An_expired_link_flips_to_expired_on_access_and_payment_is_refused()
    {
        var (client, fixture, customerId) = await SetupAgencyCustomerAsync(portalEnabled: true);
        await CreateInvitationDirectAsync(fixture.AgencyAId, customerId, 10_000m, TimeSpan.FromHours(-1));

        await using var context = TestDbContextFactory.Create();
        Aqsat.Infrastructure.Persistence.AgencyContext.Current = fixture.AgencyAId;
        var token = await context.CustomerPortalInvitations.AsNoTracking()
            .Where(i => i.CustomerId == customerId)
            .Select(i => i.Token)
            .FirstAsync();

        var publicClient = _factory.CreateClient();
        var info = await publicClient.GetFromJsonAsync<PublicPortalInfoDto>($"/api/portal/{token}");
        Assert.Equal("Expired", info!.Status);

        var pay = await publicClient.PostAsync($"/api/portal/{token}/pay", null);
        Assert.Equal(HttpStatusCode.BadRequest, pay.StatusCode);
    }

    [Fact]
    public async Task A_customer_without_a_mobile_number_cannot_be_sent_a_link()
    {
        var (client, _, customerId) = await SetupAgencyCustomerAsync(mobile: null);

        var response = await client.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(customerId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("همراه", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_agency_operator_cannot_see_another_agencys_invitations()
    {
        var (clientA, fixture, customerId) = await SetupAgencyCustomerAsync();
        await clientA.PostAsJsonAsync("/api/portal/invitations", new CreatePortalInvitationRequest(customerId));

        // Operator B — the fixture's dual-agency user acting in agency B (Staff role there), an
        // org the invitation's agency A rows must be invisible to.
        var clientB = _factory.CreateClient();
        var loginB = await clientB.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());

        var listB = await clientB.GetFromJsonAsync<List<PortalInvitationDto>>($"/api/portal/invitations/customer/{customerId}");
        Assert.Empty(listB!);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_issue_links()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/portal/invitations",
            new CreatePortalInvitationRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
