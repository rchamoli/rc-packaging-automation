using System.Net;
using Azure.Data.Tables;
using Company.Function.Services;
using Company.Function.Utilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class DemoAdminFunctions
{
    private readonly StorageService _storageService;
    private readonly AppDataSeeder _appDataSeeder;
    private readonly ILogger<DemoAdminFunctions> _logger;

    public DemoAdminFunctions(StorageService storageService, AppDataSeeder appDataSeeder, ILogger<DemoAdminFunctions> logger)
    {
        _storageService = storageService;
        _appDataSeeder = appDataSeeder;
        _logger = logger;
    }

    private bool IsDevelopmentEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT")
               ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseData?> CheckEnvironmentAsync(HttpRequestData req)
    {
        if (!IsDevelopmentEnvironment())
        {
            _logger.LogWarning("Demo endpoint called in non-development environment");
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Demo endpoints are only available in development environments." });
            return forbidden;
        }
        return null;
    }

    [Function("DemoSeed")]
    public async Task<HttpResponseData> SeedData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "demo/seed")] HttpRequestData req)
    {
        var envCheck = await CheckEnvironmentAsync(req);
        if (envCheck is not null) return envCheck;

        _logger.LogInformation("Seeding all demo data (users + application data)...");

        // Step 1: Seed sample data (same as SeedSampleData template)
        int sampleSeeded = 0;
        try
        {
            sampleSeeded = await SeedSampleEntities();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed sample data");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to seed sample data: " + ex.Message });
            return errorResponse;
        }

        // Step 2: Seed application data (packaging runs, Intune app refs, blobs)
        AppDataSeeder.SeedResult appResult;
        try
        {
            appResult = await _appDataSeeder.SeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed application data");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to seed application data: " + ex.Message });
            return errorResponse;
        }

        _logger.LogInformation("Demo seed complete: {Sample} sample, {Runs} runs, {Refs} app refs",
            sampleSeeded, appResult.RunsSeeded, appResult.AppRefsSeeded);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            sampleSeeded,
            runsSeeded = appResult.RunsSeeded,
            appRefsSeeded = appResult.AppRefsSeeded
        });
        return response;
    }

    [Function("DemoReset")]
    public async Task<HttpResponseData> ResetData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "demo/reset")] HttpRequestData req)
    {
        var envCheck = await CheckEnvironmentAsync(req);
        if (envCheck is not null) return envCheck;

        _logger.LogInformation("Resetting all demo data...");

        // Clear tables
        await _storageService.ClearTableAsync(TableNames.PackagingRuns);
        await _storageService.ClearTableAsync(TableNames.IntuneAppRefs);
        await _storageService.ClearTableAsync("SampleData");

        // Clear blob containers
        await _storageService.ClearBlobContainerAsync(BlobContainers.Artifacts);
        await _storageService.ClearBlobContainerAsync(BlobContainers.PackagingLogs);

        _logger.LogInformation("All demo data cleared. Re-seeding...");

        // Re-seed
        int sampleSeeded = await SeedSampleEntities();
        var appResult = await _appDataSeeder.SeedAsync();

        _logger.LogInformation("Demo reset complete: {Sample} sample, {Runs} runs, {Refs} app refs",
            sampleSeeded, appResult.RunsSeeded, appResult.AppRefsSeeded);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            cleared = true,
            sampleSeeded,
            runsSeeded = appResult.RunsSeeded,
            appRefsSeeded = appResult.AppRefsSeeded
        });
        return response;
    }

    private async Task<int> SeedSampleEntities()
    {
        var connectionString = Environment.GetEnvironmentVariable("STORAGE");
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("STORAGE connection string not configured");

        var tableClient = new TableServiceClient(connectionString).GetTableClient("SampleData");
        await tableClient.CreateIfNotExistsAsync();

        var items = new List<TableEntity>
        {
            new("samples", "welcome-note")
            {
                { "Title", "Welcome to Packaging Automation" },
                { "Description", "Nouryon desktop app packaging automation — converts installers to .intunewin format." },
                { "CreatedBy", "user-packager" },
                { "CreatedAt", DateTime.UtcNow }
            },
            new("samples", "getting-started")
            {
                { "Title", "Getting Started" },
                { "Description", "Place your installer and metadata file, then start a new packaging run from the dashboard." },
                { "CreatedBy", "user-packager" },
                { "CreatedAt", DateTime.UtcNow }
            },
            new("samples", "app-owner-review")
            {
                { "Title", "App Owner Review" },
                { "Description", "Review completed packaging runs and approve Intune app configuration before UAT deployment." },
                { "CreatedBy", "user-appowner" },
                { "CreatedAt", DateTime.UtcNow }
            },
            new("samples", "qa-validation")
            {
                { "Title", "QA Validation" },
                { "Description", "Validate packaged apps against detection rules and test UAT group assignment." },
                { "CreatedBy", "user-qa" },
                { "CreatedAt", DateTime.UtcNow }
            }
        };

        int seeded = 0;
        foreach (var item in items)
        {
            await tableClient.UpsertEntityAsync(item, TableUpdateMode.Replace);
            seeded++;
        }
        return seeded;
    }
}
