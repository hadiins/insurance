using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Aqsat.UnitTests.Concurrency;

/// <summary>
/// Task 11's own check: "presence appears in both" browsers. Two real SignalR connections (staff +
/// manager) join the same record and observe each other's PresenceChanged broadcasts, including on
/// disconnect.
/// </summary>
[Collection("WebApplicationFactory")]
public class PresenceHubTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PresenceHubTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private sealed record PresenceEntryDto(Guid userId, string userDisplayName, bool isEditing, DateTimeOffset lastSeenAt);

    [Fact]
    public async Task Both_connections_see_each_others_presence_and_the_departure_on_disconnect()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        var entityId = Guid.NewGuid();

        var staffToken = await LoginAsync(fixture.AgencyOnlyStaffMobile);
        var managerToken = await LoginAsync(fixture.DualAgencyManagerMobile);

        await using var staffConnection = BuildConnection(staffToken);
        await using var managerConnection = BuildConnection(managerToken);

        var staffUpdates = new List<List<PresenceEntryDto>>();
        var staffChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        staffConnection.On<List<PresenceEntryDto>>("PresenceChanged", list =>
        {
            staffUpdates.Add(list);
            if (list.Count == 2)
            {
                staffChanged.TrySetResult();
            }
        });

        await staffConnection.StartAsync();
        await managerConnection.StartAsync();

        await staffConnection.InvokeAsync("Enter", fixture.AgencyAId, "Policy", entityId);
        await managerConnection.InvokeAsync("Enter", fixture.AgencyAId, "Policy", entityId);

        var sawBoth = await Task.WhenAny(staffChanged.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(staffChanged.Task, sawBoth);
        Assert.Contains(staffUpdates.Last(), e => e.userDisplayName == "کارمند بدون دسترسی پرداخت");
        Assert.Contains(staffUpdates.Last(), e => e.userDisplayName == "مدیر دونمایندگی");

        var managerLeft = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        staffConnection.On<List<PresenceEntryDto>>("PresenceChanged", list =>
        {
            if (list.Count == 1)
            {
                managerLeft.TrySetResult();
            }
        });

        await managerConnection.StopAsync();

        var sawDeparture = await Task.WhenAny(managerLeft.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(managerLeft.Task, sawDeparture);
    }

    private HubConnection BuildConnection(string token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri("http://localhost/hubs/presence"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

    private async Task<string> LoginAsync(string mobile)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        return (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
    }
}
