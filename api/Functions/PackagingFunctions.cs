using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Company.Function.Models;
using Company.Function.Services;
using Company.Function.Utilities;

namespace Company.Function;

// Note: AuthorizationLevel.Anonymous is intentional — Azure Static Web Apps
// handles authentication at the route level via staticwebapp.config.json.
// All /api/* routes require the "authenticated" role in the SWA config.

public class PackagingFunctions
{
    private readonly PackagingService _packagingService;
    private readonly StorageService _storageService;
    private readonly ActivityService _activityService;
    private readonly NotificationService _notificationService;
    private readonly ILogger<PackagingFunctions> _logger;

    public PackagingFunctions(PackagingService packagingService, StorageService storageService, ActivityService activityService, NotificationService notificationService, ILogger<PackagingFunctions> logger)
    {
        _packagingService = packagingService;
        _storageService = storageService;
        _activityService = activityService;
        _notificationService = notificationService;
        _logger = logger;
    }

    [Function("GetPackagingStats")]
    public async Task<HttpResponseData> GetStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "packaging/stats")] HttpRequestData req)
    {
        _logger.LogInformation("GET /api/packaging/stats");
        try
        {
            var stats = await _storageService.GetStatsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
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
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in GetStats");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to load dashboard stats." });
            return errorResponse;
        }
    }

    [Function("StartPackagingRun")]
    public async Task<HttpResponseData> StartRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "packaging/run")] HttpRequestData req)
    {
        _logger.LogInformation("POST /api/packaging/run");

        try
        {
            // Auth check: require packager role
            var principal = AuthHelper.GetClientPrincipal(req);
            if (!AuthHelper.IsPackager(principal))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You do not have permission to create packaging runs." });
                return forbidden;
            }

            // Limit request body size (10 KB max for metadata request)
            if (req.Body.Length > 10_240)
            {
                var tooLarge = req.CreateResponse(HttpStatusCode.BadRequest);
                await tooLarge.WriteAsJsonAsync(new { error = "Request body too large (max 10 KB)." });
                return tooLarge;
            }

            PackagingRunRequest? body;
            try
            {
                body = await req.ReadFromJsonAsync<PackagingRunRequest>();
            }
            catch
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid JSON in request body." });
                return badReq;
            }

            if (body is null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Request body is required." });
                return badReq;
            }

            // Generate runId upfront so we can return it immediately
            var runId = Guid.NewGuid().ToString("N")[..12];
            var userId = AuthHelper.GetUserId(principal);
            var userName = AuthHelper.GetUserDisplayName(principal);

            // Create a Queued placeholder entity so the frontend can poll immediately
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
            await _storageService.UpsertRunAsync(queuedRun);

            // Log activity
            await _activityService.LogAsync(
                ActivityEventTypes.RunCreated,
                userId, userName,
                $"Queued packaging run {runId}",
                runId);

            // Fire-and-forget: process the run in the background
            _ = Task.Run(async () =>
            {
                try
                {
                    var (completedRun, error) = await _packagingService.StartRunAsync(
                        body.SourceType, body.ReleaseFolderPath, body.CreateIntuneApp, body.UploadId,
                        userId, userName, runId);

                    // Remove the queued placeholder (real entity has proper PK/RK now)
                    try { await _storageService.DeleteRunEntityAsync("queued", runId); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete queued placeholder for {RunId}", runId); }

                    if (completedRun != null)
                    {
                        // Log completion activity
                        await _activityService.LogAsync(
                            ActivityEventTypes.RunCompleted,
                            userId, userName,
                            $"Packaging run for {completedRun.AppName} v{completedRun.Version} completed: {completedRun.Status}",
                            runId, completedRun.AppName);

                        // Send notifications
                        await _notificationService.NotifyRunCompletedAsync(completedRun);
                    }
                    else
                    {
                        // Validation failed — update placeholder to Failed
                        queuedRun.Status = RunStatus.Failed;
                        queuedRun.ErrorSummary = error ?? "Packaging run failed.";
                        queuedRun.EndTime = DateTime.UtcNow;
                        await _storageService.UpsertRunAsync(queuedRun);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background packaging run {RunId} failed", runId);
                    try
                    {
                        queuedRun.Status = RunStatus.Failed;
                        queuedRun.ErrorSummary = $"Unexpected error: {ex.Message}";
                        queuedRun.EndTime = DateTime.UtcNow;
                        await _storageService.UpsertRunAsync(queuedRun);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Failed to update run {RunId} status after background error", runId);
                    }
                }
            });

            // Return 202 Accepted immediately
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                id = runId,
                status = RunStatus.Queued
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in StartRun");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred while starting the run. Please try again." });
            return errorResponse;
        }
    }

    [Function("ListPackagingRuns")]
    public async Task<HttpResponseData> ListRuns(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "packaging/runs")] HttpRequestData req)
    {
        _logger.LogInformation("GET /api/packaging/runs");

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var appName = query["appName"];

            var runs = await _storageService.GetRunsAsync(appName);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
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
                        ? _storageService.GenerateBlobSasUrl(BlobContainers.Artifacts, r.OutputArtifactPath)
                        : null,
                    intuneAppId = r.IntuneAppId
                })
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in ListRuns");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred while loading runs. Please try again." });
            return errorResponse;
        }
    }

    [Function("GetPackagingRun")]
    public async Task<HttpResponseData> GetRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "packaging/runs/{runId}")] HttpRequestData req,
        string runId)
    {
        _logger.LogInformation("GET /api/packaging/runs/{RunId}", runId);

        try
        {
            if (string.IsNullOrWhiteSpace(runId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Run ID is required." });
                return badReq;
            }

            var run = await _storageService.GetRunByIdAsync(runId);
            if (run is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Run not found." });
                return notFound;
            }

            // Generate SAS URL for the log blob if a log exists
            string? logSasUrl = null;
            if (!string.IsNullOrEmpty(run.LogUrl))
            {
                var blobName = $"{PackagingRunEntity.NormalizePartitionKey(run.AppName)}/{run.RunId}.log";
                logSasUrl = _storageService.GenerateBlobSasUrl(BlobContainers.PackagingLogs, blobName);
            }

            // Generate SAS URL for the artifact blob if an artifact exists
            string? artifactSasUrl = null;
            if (!string.IsNullOrEmpty(run.OutputArtifactPath))
            {
                artifactSasUrl = _storageService.GenerateBlobSasUrl(BlobContainers.Artifacts, run.OutputArtifactPath);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
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
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in GetRun for {RunId}", runId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred while loading run details. Please try again." });
            return errorResponse;
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
