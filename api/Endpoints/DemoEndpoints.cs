using Azure.Data.Tables;
using Company.Function.Services;
using Company.Function.Utilities;

namespace Company.Function.Endpoints;

public static class DemoEndpoints
{
    public static void MapDemoEndpoints(this WebApplication app)
    {
        app.MapPost("/api/demo/seed", SeedData);
        app.MapPost("/api/demo/reset", ResetData);
    }

    private static bool IsDevelopmentEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IResult> SeedData(
        StorageService storageService,
        AppDataSeeder appDataSeeder,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("DemoEndpoints");
        if (!IsDevelopmentEnvironment())
        {
            logger.LogWarning("Demo endpoint called in non-development environment");
            return Results.Json(new { error = "Demo endpoints are only available in development environments." }, statusCode: 403);
        }

        logger.LogInformation("Seeding all demo data (users + application data)...");

        int sampleSeeded;
        try
        {
            sampleSeeded = await SeedSampleEntities();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed sample data");
            return Results.Json(new { error = "Failed to seed sample data: " + ex.Message }, statusCode: 500);
        }

        AppDataSeeder.SeedResult appResult;
        try
        {
            appResult = await appDataSeeder.SeedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed application data");
            return Results.Json(new { error = "Failed to seed application data: " + ex.Message }, statusCode: 500);
        }

        logger.LogInformation("Demo seed complete: {Sample} sample, {Runs} runs, {Refs} app refs",
            sampleSeeded, appResult.RunsSeeded, appResult.AppRefsSeeded);

        return Results.Ok(new
        {
            sampleSeeded,
            runsSeeded = appResult.RunsSeeded,
            appRefsSeeded = appResult.AppRefsSeeded
        });
    }

    private static async Task<IResult> ResetData(
        StorageService storageService,
        AppDataSeeder appDataSeeder,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("DemoEndpoints");
        if (!IsDevelopmentEnvironment())
        {
            logger.LogWarning("Demo endpoint called in non-development environment");
            return Results.Json(new { error = "Demo endpoints are only available in development environments." }, statusCode: 403);
        }

        logger.LogInformation("Resetting all demo data...");

        await storageService.ClearTableAsync(TableNames.PackagingRuns);
        await storageService.ClearTableAsync(TableNames.IntuneAppRefs);
        await storageService.ClearTableAsync("SampleData");

        await storageService.ClearBlobContainerAsync(BlobContainers.Artifacts);
        await storageService.ClearBlobContainerAsync(BlobContainers.PackagingLogs);

        logger.LogInformation("All demo data cleared. Re-seeding...");

        int sampleSeeded = await SeedSampleEntities();
        var appResult = await appDataSeeder.SeedAsync();

        logger.LogInformation("Demo reset complete: {Sample} sample, {Runs} runs, {Refs} app refs",
            sampleSeeded, appResult.RunsSeeded, appResult.AppRefsSeeded);

        return Results.Ok(new
        {
            cleared = true,
            sampleSeeded,
            runsSeeded = appResult.RunsSeeded,
            appRefsSeeded = appResult.AppRefsSeeded
        });
    }

    private static async Task<int> SeedSampleEntities()
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
