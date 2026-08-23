using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aqsat.UnitTests.Sms;

/// <summary>قالب پیامک‌ها — no saved row means the hard-coded default is in effect; saving one
/// overrides it; deleting it resets back to the default.</summary>
[Collection("WebApplicationFactory")]
public class SmsTemplatesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SmsTemplatesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Save_overrides_the_default_and_delete_resets_it()
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

        var before = await client.GetFromJsonAsync<List<SmsTemplateDto>>("/api/sms/templates");
        var installmentTemplate = before!.Single(t => t.Key == "installment-reminder-v1");
        Assert.False(installmentTemplate.IsCustomized);

        var customText = "متن سفارشی برای قسط {SeqNo} — {Balance} تومان";
        var saveResponse = await client.PutAsJsonAsync(
            "/api/sms/templates/installment-reminder-v1", new SaveSmsTemplateRequest(customText));
        saveResponse.EnsureSuccessStatusCode();

        var afterSave = await client.GetFromJsonAsync<List<SmsTemplateDto>>("/api/sms/templates");
        var savedTemplate = afterSave!.Single(t => t.Key == "installment-reminder-v1");
        Assert.True(savedTemplate.IsCustomized);
        Assert.Equal(customText, savedTemplate.Body);

        var deleteResponse = await client.DeleteAsync("/api/sms/templates/installment-reminder-v1");
        deleteResponse.EnsureSuccessStatusCode();

        var afterReset = await client.GetFromJsonAsync<List<SmsTemplateDto>>("/api/sms/templates");
        var resetTemplate = afterReset!.Single(t => t.Key == "installment-reminder-v1");
        Assert.False(resetTemplate.IsCustomized);
        Assert.Equal(installmentTemplate.Body, resetTemplate.Body);
    }
}
