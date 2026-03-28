using System.Text.Json;
using Company.Function.Models;
using Company.Function.Services;
using Company.Function.Utilities;

namespace Company.Function.Endpoints;

public static class IntuneAppEndpoints
{
    public static void MapIntuneAppEndpoints(this WebApplication app)
    {
        app.MapPost("/api/intune/create-from-run/{runId}", CreateFromRun);
        app.MapGet("/api/intune/appref/resolve", ResolveAppRef);
    }

    private static async Task<IResult> CreateFromRun(
        string runId,
        HttpContext context,
        IntuneGraphService intuneService,
        StorageService storageService,
        MetadataReader metadataReader,
        ActivityService activityService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("IntuneAppEndpoints");
        logger.LogInformation("POST /api/intune/create-from-run/{RunId}", runId);

        try
        {
            if (string.IsNullOrWhiteSpace(runId))
                return Results.BadRequest(new { error = "Run ID is required." });

            var run = await storageService.GetRunByIdAsync(runId);
            if (run is null)
                return Results.NotFound(new { error = "Run not found." });

            if (run.Status != RunStatus.Succeeded && run.Status != RunStatus.SucceededWithWarnings)
                return Results.Conflict(new { error = $"Run has status '{run.Status}'; Intune app creation requires a succeeded run." });

            // Idempotency: if Intune app was already created, return existing info
            if (!string.IsNullOrEmpty(run.IntuneAppId))
            {
                return Results.Ok(new
                {
                    intuneAppId = run.IntuneAppId,
                    intuneAppLink = run.IntuneAppLink
                        ?? $"https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/{run.IntuneAppId}",
                    runId = run.RunId,
                    appName = run.AppName,
                    version = run.Version,
                    alreadyExisted = true
                });
            }

            // Read metadata: prefer stored snapshot, fall back to disk for pre-snapshot runs
            ReleaseMetadata? metadata;
            if (!string.IsNullOrEmpty(run.MetadataSnapshot))
            {
                metadata = JsonSerializer.Deserialize<ReleaseMetadata>(
                    run.MetadataSnapshot,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (metadata is null)
                    return Results.Json(new { error = "Failed to deserialize stored metadata snapshot." }, statusCode: 500);
            }
            else
            {
                var (readMetadata, metadataError) = await metadataReader.ReadAsync(run.SourceLocation);
                if (readMetadata is null)
                    return Results.BadRequest(new { error = $"Failed to read metadata: {metadataError}" });
                metadata = readMetadata;
            }

            var (intuneAppId, intuneAppLink, createError) = await intuneService.CreateFromRunAsync(run, metadata);

            if (intuneAppId is null)
                return Results.Json(new { error = createError ?? "Failed to create Intune app. Please check Graph API configuration." }, statusCode: 500);

            run.IntuneAppId = intuneAppId;
            run.IntuneAppLink = intuneAppLink;
            await storageService.UpsertRunAsync(run);

            var principal = AuthHelper.GetClientPrincipal(context.Request);
            await activityService.LogAsync(
                ActivityEventTypes.IntuneAppCreated,
                AuthHelper.GetUserId(principal),
                AuthHelper.GetUserDisplayName(principal),
                $"Created Intune app for {run.AppName} v{run.Version}",
                run.RunId,
                run.AppName);

            return Results.Ok(new
            {
                intuneAppId,
                intuneAppLink,
                runId = run.RunId,
                appName = run.AppName,
                version = run.Version
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in CreateFromRun for {RunId}", runId);
            return Results.Json(new { error = "An unexpected error occurred while creating the Intune app. Please try again." }, statusCode: 500);
        }
    }

    private static async Task<IResult> ResolveAppRef(
        HttpContext context,
        StorageService storageService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("IntuneAppEndpoints");
        logger.LogInformation("GET /api/intune/appref/resolve");

        try
        {
            var appName = context.Request.Query["appName"].FirstOrDefault();
            var version = context.Request.Query["version"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(version))
                return Results.BadRequest(new { error = "Both 'appName' and 'version' query parameters are required." });

            var appRef = await storageService.GetIntuneAppRefAsync(appName, version);
            if (appRef is null)
                return Results.NotFound(new { error = $"No Intune app reference found for '{appName}' version '{version}'." });

            return Results.Ok(new
            {
                appName = appRef.AppName,
                version = appRef.Version,
                intuneAppId = appRef.IntuneAppId,
                intuneAppLink = appRef.IntuneAppLink,
                runId = appRef.RunId,
                createdAt = appRef.CreatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in ResolveAppRef");
            return Results.Json(new { error = "An unexpected error occurred while resolving the app reference. Please try again." }, statusCode: 500);
        }
    }
}
