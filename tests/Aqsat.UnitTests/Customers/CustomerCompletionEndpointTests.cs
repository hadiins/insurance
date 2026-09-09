using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Customers;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §3 — the completion queue's dashboard summary, list,
/// and per-row save, including the format rules §2 states apply the same way here as on manual
/// entry.</summary>
[Collection("WebApplicationFactory")]
public class CustomerCompletionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CustomerCompletionEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Summary_and_list_surface_an_incomplete_customer()
    {
        var (client, customerId) = await SeedIncompleteCustomerAsync();

        var summary = await client.GetFromJsonAsync<IncompleteProfileSummaryDto>("/api/customers/incomplete-summary");
        Assert.NotNull(summary);
        Assert.True(summary!.Total >= 1);
        Assert.True(summary.WithoutMobile >= 1);
        Assert.True(summary.WithoutNationalId >= 1);
        Assert.True(summary.WithoutAddress >= 1);
        Assert.True(summary.WithoutPostalCode >= 1);
        Assert.True(summary.WithoutName >= 1);

        var byMobileFilter = await client.GetFromJsonAsync<List<CustomerIncompleteRowDto>>("/api/customers/incomplete?filter=no-mobile");
        Assert.Contains(byMobileFilter!, r => r.Id == customerId);

        var byAddressFilter = await client.GetFromJsonAsync<List<CustomerIncompleteRowDto>>("/api/customers/incomplete?filter=no-address");
        Assert.Contains(byAddressFilter!, r => r.Id == customerId);

        var byNameFilter = await client.GetFromJsonAsync<List<CustomerIncompleteRowDto>>("/api/customers/incomplete?filter=no-name");
        Assert.Contains(byNameFilter!, r => r.Id == customerId);
    }

    /// <summary>Production incident 2026-09-07: a national ID (Persian digits) typed into the
    /// lastName field. Digits-only names must be rejected at the door, in both the completion
    /// grid's save and manual creation.</summary>
    [Fact]
    public async Task A_digits_only_name_is_rejected()
    {
        var (client, customerId) = await SeedIncompleteCustomerAsync();

        var response = await client.PutAsJsonAsync($"/api/customers/{customerId}/complete-profile",
            new CompleteCustomerProfileRequest(null, "۰۳۸۶۵۲۹۵۵۸", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Completing_every_field_flips_is_profile_complete_to_true()
    {
        var (client, customerId) = await SeedIncompleteCustomerAsync();

        var response = await client.PutAsJsonAsync($"/api/customers/{customerId}/complete-profile", new CompleteCustomerProfileRequest(
            "نام", "خانوادگی", "0072345454", "09123456789", null, "تهران، خیابان ولیعصر", "1234567890"));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CustomerIncompleteRowDto>();

        Assert.True(result!.IsProfileComplete);
        Assert.True(result.HasNationalId);
    }

    [Fact]
    public async Task Partial_save_only_updates_the_fields_present()
    {
        var (client, customerId) = await SeedIncompleteCustomerAsync();

        var response = await client.PutAsJsonAsync($"/api/customers/{customerId}/complete-profile", new CompleteCustomerProfileRequest(
            null, null, null, "09123456789", null, null, null));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CustomerIncompleteRowDto>();

        Assert.Equal("09123456789", result!.Mobile);
        Assert.False(result.IsProfileComplete);
        Assert.False(result.HasNationalId);
    }

    [Fact]
    public async Task Invalid_mobile_shape_is_rejected()
    {
        var (client, customerId) = await SeedIncompleteCustomerAsync();

        var response = await client.PutAsJsonAsync($"/api/customers/{customerId}/complete-profile", new CompleteCustomerProfileRequest(
            null, null, null, "0912345678", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Emergency_mobile_equal_to_main_mobile_is_rejected()
    {
        var (client, customerId) = await SeedIncompleteCustomerAsync();

        var response = await client.PutAsJsonAsync($"/api/customers/{customerId}/complete-profile", new CompleteCustomerProfileRequest(
            null, null, null, "09123456789", "09123456789", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_national_id_is_rejected()
    {
        var (client, customerId) = await SeedIncompleteCustomerAsync();

        var response = await client.PutAsJsonAsync($"/api/customers/{customerId}/complete-profile", new CompleteCustomerProfileRequest(
            null, null, "1111111111", null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Postal_code_must_be_exactly_ten_digits()
    {
        var (client, customerId) = await SeedIncompleteCustomerAsync();

        var response = await client.PutAsJsonAsync($"/api/customers/{customerId}/complete-profile", new CompleteCustomerProfileRequest(
            null, null, null, null, null, null, "12345"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<(HttpClient Client, Guid CustomerId)> SeedIncompleteCustomerAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        AgencyContext.Current = fixture.AgencyAId;
        var customer = new Customer
        {
            AgencyId = fixture.AgencyAId,
            ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12],
            FullName = "مشتری ناقص",
        };
        seedContext.Customers.Add(customer);
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (client, customer.Id);
    }
}
