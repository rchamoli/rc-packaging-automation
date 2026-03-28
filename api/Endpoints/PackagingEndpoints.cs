using Company.Function.BackgroundServices;
using Company.Function.Models;
using Company.Function.Services;
using Company.Function.Utilities;

namespace Company.Function.Endpoints;

public static class PackagingEndpoints
{
    public static void MapPackagingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/packaging/stats", GetStats);
        app.MapPost("/api/packaging/run", StartRun);
        app.MapGet("/api/packaging/runs", ListRuns);
        app.MapGet("/api/packaging/runs/{runId}", GetRun);
    }

    private static async Task<IResult> GetStats(
        StorageService storageService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PackagingEndpoints");
        logger.LogInformation("GET /api/packaging/stats");
        try
        {
            var stats = await storageService.GetStatsAsync();
            return Results.Ok(new
            {
                totalRuns = stats.TotalRuns,
                succeeded = stats.Succeeded,
                failed = stats.Failed,
                running = stats.Running,
                succeededWithWarnings = stats.SucceededWithWarnings,
                successRate = stats.SuccessRate,
                recentRuns = stats.RecentRuns.Select(r => new
                {
                    id = r.RunId,
                    appName = r.AppName,
                    version = r.Version,
                    status = r.Status,
                    startTime = r.StartTime,
                    endTime = r.EndTime
                })
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in GetStats");
            return Results.Json(new { error = "Failed to load dashboard stats." }, statusCode: 500);
        }
    }

    private static async Task<IResult> StartRun(
        HttpContext context,
        PackagingJobQueue jobQueue,
        StorageService storageService,
        ActivityService activityService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PackagingEndpoints");
        logger.LogInformation("POST /api/packaging/run");

        try
        {
            var principal = AuthHelper.GetClientPrincipal(context.Request);
            if (!AuthHelper.IsPackager(principal))
                return Results.Json(new { error = "You do not have permission to create packaging runs." }, statusCode: 403);

            // Limit request body size (10 KB max for metadata request)
            if (context.Request.ContentLength > 10_240)
                return Results.BadRequest(new { error = "Request body too large (max 10 KB)." });

            PackagingRunRequest? body;
            try
            {
                body = await context.Request.ReadFromJsonAsync<PackagingRunRequest>();
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON in request body." });
            }

            if (body is null)
                return Results.BadRequest(new { error = "Request body is required." });

            var runId = Guid.NewGuid().ToString("N")[..12];
            var userId = AuthHelper.GetUserId(principal);
            var userName = AuthHelper.GetUserDisplayName(principal);

            var queuedRun = new PackagingRunEntity
            {
                PartitionKey = "queued",
                RowKey = runId,
                RunId = runId,
                AppName = "Pending",
                Version = "—",
                SourceType = body.SourceType,
                Status = RunStatus.Queued,
                StartTime = DateTime.UtcNow,
                CreateIntuneApp = body.CreateIntuneApp,
                CreatedBy = userId,
                CreatedByName = userName
            };
            await storageService.UpsertRunAsync(queuedRun);

            await activityService.LogAsync(
                ActivityEventTypes.RunCreated,
                userId, userName,
                $"Queued packaging run {runId}",
                runId);

            // Enqueue to background service (replaces Task.Run)
            await jobQueue.EnqueueAsync(new PackagingJob
            {
                RunId = runId,
                SourceType = body.SourceType,
                ReleaseFolderPath = body.ReleaseFolderPath,
                CreateIntuneApp = body.CreateIntuneApp,
                UploadId = body.UploadId,
                UserId = userId,
                UserName = userName,
                QueuedRun = queuedRun
            });

            return Results.Accepted(value: new { id = runId, status = RunStatus.Queued });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in StartRun");
            return Results.Json(new { error = "An unexpected error occurred while starting the run. Please try again." }, statusCode: 500);
        }
    }

    private static async Task<IResult> ListRuns(
        HttpContext context,
        StorageService storageService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PackagingEndpoints");
        logger.LogInformation("GET /api/packaging/runs");

        try
        {
            var appName = context.Request.Query["appName"].FirstOrDefault();
            var runs = await storageService.GetRunsAsync(appName);

            return Results.Ok(new
            {
                runs = runs.Select(r => new
                {
                    id = r.RunId,
                    appName = r.AppName,
                    version = r.Version,
                    status = r.Status,
                    startTime = r.StartTime,
                    endTime = r.EndTime,
                    sourceType = r.SourceType,
                    logUrl = r.LogUrl,
                    errorSummary = r.ErrorSummary,
                    artifactUrl = !string.IsNullOrEmpty(r.OutputArtifactPath)
                        ? storageService.GenerateBlobSasUrl(BlobContainers.Artifacts, r.OutputArtifactPath)
                        : null,
                    intuneAppId = r.IntuneAppId
                })
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in ListRuns");
            return Results.Json(new { error = "An unexpected error occurred while loading runs. Please try again." }, statusCode: 500);
        }
    }

    private static async Task<IResult> GetRun(
        string runId,
        StorageService storageService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PackagingEndpoints");
        logger.LogInformation("GET /api/packaging/runs/{RunId}", runId);

        try
        {
            if (string.IsNullOrWhiteSpace(runId))
                return Results.BadRequest(new { error = "Run ID is required." });

            var run = await storageService.GetRunByIdAsync(runId);
            if (run is null)
                return Results.NotFound(new { error = "Run not found." });

            string? logSasUrl = null;
            if (!string.IsNullOrEmpty(run.LogUrl))
            {
                var blobName = $"{PackagingRunEntity.NormalizePartitionKey(run.AppName)}/{run.RunId}.log";
                logSasUrl = storageService.GenerateBlobSasUrl(BlobContainers.PackagingLogs, blobName);
            }

            string? artifactSasUrl = null;
            if (!string.IsNullOrEmpty(run.OutputArtifactPath))
                artifactSasUrl = storageService.GenerateBlobSasUrl(BlobContainers.Artifacts, run.OutputArtifactPath);

            return Results.Ok(new
            {
                id = run.RunId,
                appName = run.AppName,
                version = run.Version,
                status = run.Status,
                startTime = run.StartTime,
                endTime = run.EndTime,
                sourceType = run.SourceType,
                sourceLocation = Path.GetFileName(run.SourceLocation) ?? "—",
                logUrl = logSasUrl ?? run.LogUrl,
                outputArtifactPath = run.OutputArtifactPath,
                artifactUrl = artifactSasUrl,
                errorSummary = run.ErrorSummary,
                metadataFileReference = run.MetadataFileReference,
                intuneAppId = run.IntuneAppId,
                intuneAppLink = run.IntuneAppLink,
                createIntuneApp = run.CreateIntuneApp
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in GetRun for {RunId}", runId);
            return Results.Json(new { error = "An unexpected error occurred while loading run details. Please try again." }, statusCode: 500);
        }
    }

    private class PackagingRunRequest
    {
        public string SourceType { get; set; } = string.Empty;
        public string ReleaseFolderPath { get; set; } = string.Empty;
        public bool CreateIntuneApp { get; set; }
        public string? UploadId { get; set; }
    }
}
