using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aqsat.UnitTests.Imports;

/// <summary>تاریخچهٔ ورود داده / رکوردهای ناسازگار — reads what ImportService's commit pipeline
/// already writes (ImportBatch/ImportRow); seeded directly here rather than through a real xlsx
/// commit, since only the read side is under test.</summary>
[Collection("WebApplicationFactory")]
public class ImportHistoryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ImportHistoryEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task History_lists_batches_and_mismatches_lists_only_failed_rows()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        AgencyContext.Current = fixture.AgencyAId;
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];
        var batch = new ImportBatch
        {
            AgencyId = fixture.AgencyAId,
            FileName = $"batch-{uniqueTag}.xlsx",
            FileHash = $"hash-{uniqueTag}",
            NewCount = 1,
            DuplicateCount = 0,
            FailedCount = 2,
        };
        seedContext.ImportBatches.Add(batch);
        await seedContext.SaveChangesAsync();

        seedContext.ImportRows.AddRange(
            new ImportRow { AgencyId = fixture.AgencyAId, ImportBatchId = batch.Id, RowNumber = 2, Status = ImportRowStatus.Failed, ErrorMessage = "شمارهٔ بیمه‌نامه تکراری است." },
            new ImportRow { AgencyId = fixture.AgencyAId, ImportBatchId = batch.Id, RowNumber = 3, Status = ImportRowStatus.Failed, ErrorMessage = "تاریخ نامعتبر است." },
            new ImportRow { AgencyId = fixture.AgencyAId, ImportBatchId = batch.Id, RowNumber = 4, Status = ImportRowStatus.New });
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var history = await client.GetFromJsonAsync<List<ImportBatchDto>>("/api/imports/history");
        Assert.Contains(history!, b => b.Id == batch.Id && b.FailedCount == 2);

        var mismatches = await client.GetFromJsonAsync<List<ImportMismatchRowDto>>($"/api/imports/mismatches?batchId={batch.Id}");
        Assert.Equal(2, mismatches!.Count);
        Assert.All(mismatches!, m => Assert.Equal(batch.Id, m.ImportBatchId));
    }
}
