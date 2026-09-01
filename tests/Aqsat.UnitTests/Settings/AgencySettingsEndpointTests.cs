using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aqsat.UnitTests.Settings;

/// <summary>"مشخصات نمایندگی" — no OrgSettings row exists until the agency's first save (every
/// job/controller reading it already falls back to hard-coded defaults for exactly that reason), so
/// GET must mirror those same defaults, and PUT must create the row on first write, not require it
/// to already exist.</summary>
[Collection("WebApplicationFactory")]
public class AgencySettingsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AgencySettingsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Reading_before_any_save_returns_the_same_defaults_the_rest_of_the_system_assumes()
    {
        var client = await AuthenticatedClientAsync();

        var settings = await client.GetFromJsonAsync<AgencySettingsDto>("/api/settings/agency");

        Assert.Equal(3, settings!.SettlementDeadlineDays);
        Assert.Equal(9, settings.MaxInstallments);
        Assert.Equal("7,3,0", settings.ReminderDaysBefore);
        Assert.Equal(30, settings.DefaultWriteOffDays);
        Assert.Equal(60, settings.RenewalAutoWatchLeadDays);

        // The gateway half moved to its own endpoints («تنظیمات درگاه پرداخت») — the general DTO
        // no longer carries it, so a settings-page save cannot clobber gateway config.
        var gateway = await client.GetFromJsonAsync<AgencyPaymentGatewayDto>("/api/settings/agency/payment-gateway");
        Assert.Equal("Mock", gateway!.PaymentProvider);
        Assert.False(gateway.CustomerPortalEnabled);
        Assert.False(gateway.HasAgentMerchantId);
        Assert.Null(gateway.AgentMerchantIdMasked);
        Assert.Equal(72, gateway.PortalInvitationTtlHours);

        var smsPanel = await client.GetFromJsonAsync<AgencySmsPanelDto>("/api/settings/agency/sms-panel");
        Assert.False(smsPanel!.HasSmsApiKey);
        Assert.Null(smsPanel.SmsApiKeyMasked);
    }

    [Fact]
    public async Task Saving_creates_the_row_and_a_later_read_reflects_the_new_values()
    {
        var client = await AuthenticatedClientAsync();

        var updateResponse = await client.PutAsJsonAsync("/api/settings/agency", new UpdateAgencySettingsRequest(
            "نمایندگی به‌روزشده", "اصفهان", "بیمهٔ نمونه",
            5, "InstallmentOnly", false, 6, "10,5,1", 8, 150_000m, "Percent", 45, 90));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<AgencySettingsDto>();

        Assert.Equal("نمایندگی به‌روزشده", updated!.Name);
        Assert.Equal(5, updated.SettlementDeadlineDays);
        Assert.Equal("InstallmentOnly", updated.LockScope);
        Assert.Equal("10,5,1", updated.ReminderDaysBefore);
        Assert.Equal(45, updated.DefaultWriteOffDays);
        Assert.Equal(90, updated.RenewalAutoWatchLeadDays);

        var reread = await client.GetFromJsonAsync<AgencySettingsDto>("/api/settings/agency");
        Assert.Equal("نمایندگی به‌روزشده", reread!.Name);
        Assert.Equal("اصفهان", reread.City);
        Assert.Equal(90, reread.RenewalAutoWatchLeadDays);
    }

    [Fact]
    public async Task The_gateway_page_saves_only_its_own_fields_and_never_returns_the_merchant_id()
    {
        var client = await AuthenticatedClientAsync();

        var updateResponse = await client.PutAsJsonAsync("/api/settings/agency/payment-gateway",
            new UpdateAgencyPaymentGatewayRequest("Mock", true, "AGT-MERCHANT-12345678", 48));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<AgencyPaymentGatewayDto>();

        Assert.Equal("Mock", updated!.PaymentProvider);
        Assert.True(updated.CustomerPortalEnabled);
        Assert.True(updated.HasAgentMerchantId);
        // Merchant ID is never returned in clear — responses carry the mask only.
        Assert.Equal("AGT-••••5678", updated.AgentMerchantIdMasked);
        Assert.DoesNotContain("AGT-MERCHANT-12345678", await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal(48, updated.PortalInvitationTtlHours);

        // A null merchant ID keeps the stored one — an ordinary save can never wipe a credential.
        var keepResponse = await client.PutAsJsonAsync("/api/settings/agency/payment-gateway",
            new UpdateAgencyPaymentGatewayRequest("Mock", true, null, 24));
        var kept = await keepResponse.Content.ReadFromJsonAsync<AgencyPaymentGatewayDto>();
        Assert.True(kept!.HasAgentMerchantId);
        Assert.Equal("AGT-••••5678", kept.AgentMerchantIdMasked);
        Assert.Equal(24, kept.PortalInvitationTtlHours);

        var reread = await client.GetFromJsonAsync<AgencyPaymentGatewayDto>("/api/settings/agency/payment-gateway");
        Assert.Equal("AGT-••••5678", reread!.AgentMerchantIdMasked);
    }

    [Fact]
    public async Task The_sms_panel_page_stores_the_agency_key_masked_and_null_keeps_the_stored_one()
    {
        var client = await AuthenticatedClientAsync();

        var updateResponse = await client.PutAsJsonAsync("/api/settings/agency/sms-panel",
            new UpdateAgencySmsPanelRequest("AGENCY-SMS-KEY-abcdef123456"));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<AgencySmsPanelDto>();

        Assert.True(updated!.HasSmsApiKey);
        Assert.Equal("AGEN••••3456", updated.SmsApiKeyMasked);
        Assert.DoesNotContain("AGENCY-SMS-KEY-abcdef123456", await updateResponse.Content.ReadAsStringAsync());

        var keepResponse = await client.PutAsJsonAsync("/api/settings/agency/sms-panel",
            new UpdateAgencySmsPanelRequest(null));
        var kept = await keepResponse.Content.ReadFromJsonAsync<AgencySmsPanelDto>();
        Assert.True(kept!.HasSmsApiKey);
        Assert.Equal("AGEN••••3456", kept.SmsApiKeyMasked);
    }

    [Fact]
    public async Task Malformed_reminder_offsets_are_rejected()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/settings/agency", new UpdateAgencySettingsRequest(
            "نمایندگی", null, null, 3, "Full", true, 9, "not,numbers", 12, 0m, "Fixed", 30, 60));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_zero_ttl_on_the_gateway_page_is_rejected()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/settings/agency/payment-gateway",
            new UpdateAgencyPaymentGatewayRequest("Mock", true, null, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return client;
    }
}
